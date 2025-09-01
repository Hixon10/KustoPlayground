namespace KustoPlayground.Core.Tests;

internal static class TestUtils
{
    internal static string GetColumnNane(Table table)
    {
        Assert.That(table, Is.Not.Null);
        Assert.That(table.Schema, Has.Count.EqualTo(1), "table has more than 1 column");
        return table.Schema.First().Value.Name;
    }

    internal static List<TYpeParameter> ExecuteAndGetDataForOneColumn<TYpeParameter>(
        string columnName,
        KustoDatabase kustoDatabase,
        string query)
    {
        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null,
            $"{results.ExecutionErrors?[0].Code} {results.ExecutionErrors?[0].Description}");

        return results.ResultRows!
            .Select(row => (TYpeParameter)row[columnName]!)
            .ToList();
    }

    internal static Table GenerateTableWithColumn<TYpeParameter>(IEnumerable<TYpeParameter> columnValues,
        string? columnName = null,
        string? tableName = null)
    {
        tableName ??= Guid.NewGuid().ToString();
        columnName ??= Guid.NewGuid().ToString();

        TYpeParameter[] rowsCopy = columnValues.ToArray();

        // we don't expect any particular order
        Random.Shared.Shuffle(rowsCopy);

        var table = new Table(tableName, [
            new Column<TYpeParameter>(columnName, isNullable: false)
        ]);

        foreach (var columnValue in rowsCopy)
        {
            table.AddRow(new Dictionary<string, object?>
            {
                { columnName, columnValue }
            });
        }

        return table;
    }
}