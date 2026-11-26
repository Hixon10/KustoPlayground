using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace KustoPlayground.Core;

internal sealed record DatabaseCatalog(Guid Id, long Revision, ImmutableDictionary<string, Table> Tables)
{
    internal const string Name = "MyDatabase";
    internal string Version => FormattableString.Invariant($"v1.{Revision}");
}

/// <summary>Main interface to interact with a Kusto database.</summary>
public class KustoDatabase
{
    private DatabaseCatalog _catalog = new(Guid.NewGuid(), 0, ImmutableDictionary<string, Table>.Empty);

    public void AddTable(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        // Publish the schema revision and its table map together, including on replacement.
        ImmutableInterlocked.Update(ref _catalog, static (catalog, t) => catalog with
        {
            Revision = catalog.Revision + 1,
            Tables = catalog.Tables.SetItem(t.Name, t)
        }, table);
    }

    public ExecutionResult ExecuteQuery(string query) => ExecuteQuery(query, includeSchema: false);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "The public query boundary returns execution errors to the caller.")]
    public ExecutionResult ExecuteQuery(string query, bool includeSchema)
    {
        try
        {
            ArgumentException.ThrowIfNullOrEmpty(query);
            return new QueryEvaluator(Volatile.Read(ref _catalog)).Execute(query, includeSchema);
        }
        catch (Exception ex)
        {
            return new ExecutionResult
            {
                ExecutionErrors = [new ExecutionError { Code = nameof(ExecutionError.ErrorCodes.InternalError), Description = ex.Message }]
            };
        }
    }
}
