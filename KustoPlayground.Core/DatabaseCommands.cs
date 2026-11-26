using Kusto.Language;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator
{
    private const string ListDatabases = "ShowClusterDatabases";
    private const string DatabaseSchema = "ShowDatabaseSchema";

    private static ExecutionError? ValidateCommand(KustoCode code, CommandBlock block, out string? normalizedText)
    {
        normalizedText = null;
        if (code.GetSyntaxDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } diagnostic)
            return new ExecutionError { Code = diagnostic.Code, Description = diagnostic.Message };
        static ExecutionError Unsupported() => new() { Code = "UnsupportedCommand",
            Description = $"Use .show databases or .show database {DatabaseCatalog.Name} schema, optionally followed by a query pipeline." };
        if (block.Statements is not [{ Element: ExpressionStatement statement }] || block.Directives.Count != 0 || block.SkippedTokens is not null)
            return Unsupported();

        var source = statement.Expression;
        while (source is PipeExpression pipe) source = pipe.Expression;
        if (source is not CustomCommand command) return Unsupported();
        switch (command.CommandKind)
        {
            case ListDatabases when command.Custom.GetTokens() is [{ Text: "show" }, { Text: "databases" }]:
                // Kusto.Language omits the service's trailing SuspensionState column. Bind it for downstream KQL too.
                normalizedText = code.Text.Insert(command.End, " | extend SuspensionState=''");
                return null;
            case DatabaseSchema when command.Custom.GetLastToken().Text == "schema":
                var name = command.Custom.GetFirstDescendant<NameReference>();
                if (name is null || name.SimpleName == DatabaseCatalog.Name) return null;
                if (!string.Equals(name.SimpleName, DatabaseCatalog.Name, StringComparison.OrdinalIgnoreCase))
                    return new ExecutionError { Code = "UnknownDatabase", Description = $"Database '{name.SimpleName}' was not found. The local database is '{DatabaseCatalog.Name}'." };
                // ADX resolves the database name case-insensitively; the binder does not.
                normalizedText = code.Text.Remove(name.TextStart, name.Width).Insert(name.TextStart, DatabaseCatalog.Name);
                return null;
            default:
                return Unsupported();
        }
    }

    private IEnumerable<QueryRow> ReadCatalog(CustomCommand command) => command.CommandKind switch
    {
        ListDatabases => [new QueryRow
        {
            ["DatabaseName"] = DatabaseCatalog.Name, ["PersistentStorage"] = "", ["Version"] = _catalog.Version,
            ["IsCurrent"] = true, ["DatabaseAccessMode"] = "ReadWriteEphemeral", ["PrettyName"] = "",
            ["ReservedSlot1"] = null, ["DatabaseId"] = _catalog.Id, ["InTransitionTo"] = ""
        }],
        DatabaseSchema => ReadDatabaseSchema(),
        _ => throw new NotSupportedException($"Unsupported management command: {command.CommandKind}.")
    };

    private IEnumerable<QueryRow> ReadDatabaseSchema()
    {
        yield return SchemaRow(version: _catalog.Version);
        foreach (var table in _catalog.Tables.Values.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            yield return SchemaRow(table.Name);
            foreach (var column in table.Schema.Values)
                yield return SchemaRow(table.Name, column.Name, SchemaDataType(ColumnType(column.GetColumnType()).Name));
        }

        static QueryRow SchemaRow(string table = "", string column = "", string type = "", string version = "") => new()
        {
            ["DatabaseName"] = DatabaseCatalog.Name, ["TableName"] = table, ["ColumnName"] = column,
            ["ColumnType"] = type, ["IsDefaultTable"] = false, ["IsDefaultColumn"] = false,
            ["PrettyName"] = "", ["Version"] = version, ["Folder"] = "", ["DocString"] = ""
        };
    }
}
