namespace KustoPlayground.Core.Tests;

internal static class TestUtils
{
    internal static string GetColumnName(Table table)
    {
        Assert.That(table, Is.Not.Null);
        Assert.That(table.Schema, Has.Count.EqualTo(1), "table has more than 1 column");
        return table.Schema.First().Value.Name;
    }

    internal static List<(TYpeParameter1, TYpeParameter2, TYpeParameter3, TYpeParameter4)> ExecuteAndGetDataFor4Columns<
        TYpeParameter1, TYpeParameter2, TYpeParameter3, TYpeParameter4>(
        string columnName1,
        string columnName2,
        string columnName3,
        string columnName4,
        KustoDatabase kustoDatabase,
        string query)
    {
        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null,
            $"{results.ExecutionErrors?[0].Code} {results.ExecutionErrors?[0].Description}");

        return results.ResultRows!
            .Select(row => ((TYpeParameter1)row[columnName1]!, (TYpeParameter2)row[columnName2]!,
                (TYpeParameter3)row[columnName3]!, (TYpeParameter4)row[columnName4]!))
            .ToList();
    }

    internal static List<(TYpeParameter1, TYpeParameter2, TYpeParameter3)> ExecuteAndGetDataFor3Columns<TYpeParameter1,
        TYpeParameter2, TYpeParameter3>(
        string columnName1,
        string columnName2,
        string columnName3,
        KustoDatabase kustoDatabase,
        string query)
    {
        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null,
            $"{results.ExecutionErrors?[0].Code} {results.ExecutionErrors?[0].Description}");

        return results.ResultRows!
            .Select(row => ((TYpeParameter1)row[columnName1]!, (TYpeParameter2)row[columnName2]!,
                (TYpeParameter3)row[columnName3]!))
            .ToList();
    }

    internal static List<(TYpeParameter1, TYpeParameter2)> ExecuteAndGetDataFor2Columns<TYpeParameter1, TYpeParameter2>(
        string columnName1,
        string columnName2,
        KustoDatabase kustoDatabase,
        string query)
    {
        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null,
            $"{results.ExecutionErrors?[0].Code} {results.ExecutionErrors?[0].Description}");

        return results.ResultRows!
            .Select(row => ((TYpeParameter1)row[columnName1]!, (TYpeParameter2)row[columnName2]!))
            .ToList();
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

    internal static Table GenerateTableWith2Columns<TYpeParameter1, TYpeParameter2>(
        IEnumerable<(TYpeParameter1, TYpeParameter2)> columnValues,
        string? columnName1 = null,
        string? columnName2 = null,
        string? tableName = null)
    {
        tableName ??= Guid.NewGuid().ToString();
        columnName1 ??= Guid.NewGuid().ToString();
        columnName2 ??= Guid.NewGuid().ToString();

        var rowsCopy = columnValues.ToArray();

        // we don't expect any particular order
        Random.Shared.Shuffle(rowsCopy);

        var table = new Table(tableName, [
            new Column<TYpeParameter1>(columnName1, isNullable: false),
            new Column<TYpeParameter2>(columnName2, isNullable: false)
        ]);

        foreach (var columnValue in rowsCopy)
        {
            table.AddRow(new Dictionary<string, object?>
            {
                { columnName1, columnValue.Item1 },
                { columnName2, columnValue.Item2 }
            });
        }

        return table;
    }

    internal static Table GenerateTableWith3Columns<TYpeParameter1, TYpeParameter2, TYpeParameter3>(
        IEnumerable<(TYpeParameter1, TYpeParameter2, TYpeParameter3)> columnValues,
        string? columnName1 = null,
        string? columnName2 = null,
        string? columnName3 = null,
        string? tableName = null)
    {
        tableName ??= Guid.NewGuid().ToString();
        columnName1 ??= Guid.NewGuid().ToString();
        columnName2 ??= Guid.NewGuid().ToString();
        columnName3 ??= Guid.NewGuid().ToString();

        var rowsCopy = columnValues.ToArray();

        // we don't expect any particular order
        Random.Shared.Shuffle(rowsCopy);

        var table = new Table(tableName, [
            new Column<TYpeParameter1>(columnName1, isNullable: false),
            new Column<TYpeParameter2>(columnName2, isNullable: false),
            new Column<TYpeParameter3>(columnName3, isNullable: false)
        ]);

        foreach (var columnValue in rowsCopy)
        {
            table.AddRow(new Dictionary<string, object?>
            {
                { columnName1, columnValue.Item1 },
                { columnName2, columnValue.Item2 },
                { columnName3, columnValue.Item3 }
            });
        }

        return table;
    }

    internal static Table GenerateTableWith4Columns<TYpeParameter1, TYpeParameter2, TYpeParameter3, TYpeParameter4>(
        IEnumerable<(TYpeParameter1, TYpeParameter2, TYpeParameter3, TYpeParameter4)> columnValues,
        string? columnName1 = null,
        string? columnName2 = null,
        string? columnName3 = null,
        string? columnName4 = null,
        string? tableName = null)
    {
        tableName ??= Guid.NewGuid().ToString();
        columnName1 ??= Guid.NewGuid().ToString();
        columnName2 ??= Guid.NewGuid().ToString();
        columnName3 ??= Guid.NewGuid().ToString();
        columnName4 ??= Guid.NewGuid().ToString();

        var rowsCopy = columnValues.ToArray();

        // we don't expect any particular order
        Random.Shared.Shuffle(rowsCopy);

        var table = new Table(tableName, [
            new Column<TYpeParameter1>(columnName1, isNullable: false),
            new Column<TYpeParameter2>(columnName2, isNullable: false),
            new Column<TYpeParameter3>(columnName3, isNullable: false),
            new Column<TYpeParameter4>(columnName4, isNullable: false)
        ]);

        foreach (var columnValue in rowsCopy)
        {
            table.AddRow(new Dictionary<string, object?>
            {
                { columnName1, columnValue.Item1 },
                { columnName2, columnValue.Item2 },
                { columnName3, columnValue.Item3 },
                { columnName4, columnValue.Item4 }
            });
        }

        return table;
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

    internal static Table BuildTestTable()
    {
        var startTimeCol = new Column<DateTime>("StartTime", isNullable: false);
        var stateCol = new Column<string>("State", isNullable: false);
        var eventTypeCol = new Column<string>("EventType", isNullable: false);
        var damagePropertyCol = new Column<int>("DamageProperty", isNullable: false);

        var stormEvents = new Table("StormEvents",
            new ColumnBase[] { startTimeCol, stateCol, eventTypeCol, damagePropertyCol });

        stormEvents.AddRow(new Dictionary<string, object?>
        {
            ["StartTime"] = new DateTime(2025, 8, 23, 6, 20, 0),
            ["State"] = "FLORIDA",
            ["EventType"] = "Hurricane",
            ["DamageProperty"] = 20000
        });

        stormEvents.AddRow(new Dictionary<string, object?>
        {
            ["StartTime"] = new DateTime(2023, 3, 28, 10, 30, 0),
            ["State"] = "TEXAS",
            ["EventType"] = "Flood",
            ["DamageProperty"] = 5000
        });

        stormEvents.AddRow(new Dictionary<string, object?>
        {
            ["StartTime"] = new DateTime(2024, 6, 1, 16, 50, 30),
            ["State"] = "FLORIDA",
            ["EventType"] = "Tornado",
            ["DamageProperty"] = 5000
        });

        return stormEvents;
    }
}