using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace KustoPlayground.Core;

/// <summary>Main interface to interact with a Kusto database.</summary>
public class KustoDatabase
{
    private readonly ConcurrentDictionary<string, Table> _tables = new();

    public void AddTable(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        _tables[table.Name] = table;
    }

    public ExecutionResult ExecuteQuery(string query) => ExecuteQuery(query, includeSchema: false);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "The public query boundary returns execution errors to the caller.")]
    public ExecutionResult ExecuteQuery(string query, bool includeSchema)
    {
        try
        {
            ArgumentException.ThrowIfNullOrEmpty(query);
            return new QueryEvaluator(_tables).Execute(query, includeSchema);
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
