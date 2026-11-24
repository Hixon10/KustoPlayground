using System.Text.Json;

namespace KustoPlayground.Core.Tests;

internal static class NeonTestUtils
{
    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> ExecuteAndAssertNoErrors(
        KustoDatabase kustoDatabase,
        string query)
    {
        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null,
            $"Query failed unexpectedly: {results.ExecutionErrors?[0].Code} {results.ExecutionErrors?[0].Description}");
        Assert.That(results.ResultRows, Is.Not.Null, "Expected ResultRows to be non-null when there are no execution errors.");
        return results.ResultRows!;
    }

    internal static void ExecuteAndExpectError(KustoDatabase kustoDatabase, string query)
    {
        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Not.Null.And.Not.Empty,
            $"Expected execution error for query: {query}");
    }

    internal static object? ExecuteAndGetSingleObject(
        KustoDatabase kustoDatabase,
        string query,
        string columnName = "print_0")
    {
        var rows = ExecuteAndAssertNoErrors(kustoDatabase, query);
        Assert.That(rows, Has.Count.EqualTo(1), $"Expected exactly 1 row for query: {query}");
        Assert.That(rows[0].ContainsKey(columnName), Is.True,
            $"Expected column '{columnName}' in result. Actual columns: {string.Join(", ", rows[0].Keys)}");
        return rows[0][columnName];
    }

    internal static T ExecuteAndGetSingleValue<T>(
        KustoDatabase kustoDatabase,
        string query,
        string columnName = "print_0")
    {
        var value = ExecuteAndGetSingleObject(kustoDatabase, query, columnName);
        Assert.That(value, Is.Not.Null, $"Expected non-null value of type {typeof(T).Name} in column '{columnName}'.");
        Assert.That(value, Is.InstanceOf<T>(),
            $"Expected value of type {typeof(T).Name} in column '{columnName}', but got {value?.GetType().Name}.");
        return (T)value!;
    }

    internal static T? ExecuteAndGetSingleNullableValue<T>(
        KustoDatabase kustoDatabase,
        string query,
        string columnName = "print_0") where T : struct
    {
        var value = ExecuteAndGetSingleObject(kustoDatabase, query, columnName);
        if (value is null)
        {
            return null;
        }

        Assert.That(value, Is.InstanceOf<T>(),
            $"Expected value of type {typeof(T).Name} or null in column '{columnName}', but got {value.GetType().Name}.");
        return (T)value;
    }

    internal static string? ExecuteAndGetSingleNullableString(
        KustoDatabase kustoDatabase,
        string query,
        string columnName = "print_0")
    {
        var value = ExecuteAndGetSingleObject(kustoDatabase, query, columnName);
        if (value is null)
        {
            return null;
        }

        Assert.That(value, Is.InstanceOf<string>(),
            $"Expected string or null in column '{columnName}', but got {value.GetType().Name}.");
        return (string)value;
    }

    internal static List<T?> ExecuteAndGetNullableColumn<T>(
        string columnName,
        KustoDatabase kustoDatabase,
        string query) where T : struct
    {
        var rows = ExecuteAndAssertNoErrors(kustoDatabase, query);
        return rows.Select(row =>
        {
            var value = row[columnName];
            if (value is null)
            {
                return (T?)null;
            }

            Assert.That(value, Is.InstanceOf<T>(),
                $"Expected {typeof(T).Name} or null in column '{columnName}', got {value.GetType().Name}.");
            return (T?)value;
        }).ToList();
    }

    internal static List<string?> ExecuteAndGetNullableStringColumn(
        string columnName,
        KustoDatabase kustoDatabase,
        string query)
    {
        var rows = ExecuteAndAssertNoErrors(kustoDatabase, query);
        return rows.Select(row => (string?)row[columnName]).ToList();
    }

    internal static List<(T1, T2?)> ExecuteAndGet2ColumnsNullable2<T1, T2>(
        string columnName1,
        string columnName2,
        KustoDatabase kustoDatabase,
        string query) where T2 : struct
    {
        var rows = ExecuteAndAssertNoErrors(kustoDatabase, query);
        return rows.Select(row =>
        {
            var value1 = (T1)row[columnName1]!;
            var raw2 = row[columnName2];
            if (raw2 is null)
            {
                return (value1, (T2?)null);
            }

            Assert.That(raw2, Is.InstanceOf<T2>(),
                $"Expected {typeof(T2).Name} or null in column '{columnName2}', got {raw2.GetType().Name}.");
            return (value1, (T2?)raw2);
        }).ToList();
    }

    internal static List<(T1, string?)> ExecuteAndGet2ColumnsNullableString2<T1>(
        string columnName1,
        string columnName2,
        KustoDatabase kustoDatabase,
        string query)
    {
        var rows = ExecuteAndAssertNoErrors(kustoDatabase, query);
        return rows.Select(row => ((T1)row[columnName1]!, (string?)row[columnName2])).ToList();
    }

    internal static Table GenerateTableWithNullableColumn<T>(
        IEnumerable<T?> columnValues,
        string? columnName = null,
        string? tableName = null) where T : struct
    {
        tableName ??= Guid.NewGuid().ToString();
        columnName ??= Guid.NewGuid().ToString();

        var rowsCopy = columnValues.ToArray();
        Random.Shared.Shuffle(rowsCopy);

        var table = new Table(tableName, [
            new Column<T>(columnName, isNullable: true)
        ]);

        foreach (var value in rowsCopy)
        {
            table.AddRow(new Dictionary<string, object?>
            {
                { columnName, value.HasValue ? value.Value : null }
            });
        }

        return table;
    }

    internal static Table GenerateTableWithNullableStringColumn(
        IEnumerable<string?> columnValues,
        string? columnName = null,
        string? tableName = null)
    {
        tableName ??= Guid.NewGuid().ToString();
        columnName ??= Guid.NewGuid().ToString();

        var rowsCopy = columnValues.ToArray();
        Random.Shared.Shuffle(rowsCopy);

        var table = new Table(tableName, [
            new Column<string>(columnName, isNullable: true)
        ]);

        foreach (var value in rowsCopy)
        {
            table.AddRow(new Dictionary<string, object?>
            {
                { columnName, value }
            });
        }

        return table;
    }

    internal static Table GenerateTableWith2ColumnsNullable2<T1, T2>(
        IEnumerable<(T1, T2?)> columnValues,
        string columnName1,
        string columnName2,
        string tableName) where T2 : struct
    {
        var rowsCopy = columnValues.ToArray();
        Random.Shared.Shuffle(rowsCopy);

        var table = new Table(tableName, [
            new Column<T1>(columnName1, isNullable: false),
            new Column<T2>(columnName2, isNullable: true)
        ]);

        foreach (var (v1, v2) in rowsCopy)
        {
            table.AddRow(new Dictionary<string, object?>
            {
                { columnName1, v1 },
                { columnName2, v2.HasValue ? v2.Value : null }
            });
        }

        return table;
    }

    internal static Table GenerateTableWith2ColumnsNullableString2<T1>(
        IEnumerable<(T1, string?)> columnValues,
        string columnName1,
        string columnName2,
        string tableName)
    {
        var rowsCopy = columnValues.ToArray();
        Random.Shared.Shuffle(rowsCopy);

        var table = new Table(tableName, [
            new Column<T1>(columnName1, isNullable: false),
            new Column<string>(columnName2, isNullable: true)
        ]);

        foreach (var (v1, v2) in rowsCopy)
        {
            table.AddRow(new Dictionary<string, object?>
            {
                { columnName1, v1 },
                { columnName2, v2 }
            });
        }

        return table;
    }

    internal static Table CreateTable(
        string tableName,
        ColumnBase[] columns,
        IEnumerable<Dictionary<string, object?>> rows,
        bool shuffle = true)
    {
        var rowsCopy = rows.ToArray();
        if (shuffle && rowsCopy.Length > 1)
        {
            Random.Shared.Shuffle(rowsCopy);
        }

        var table = new Table(tableName, columns);
        foreach (var row in rowsCopy)
        {
            table.AddRow(row);
        }

        return table;
    }

    internal static void AssertNumericEqual(object? actual, double expected, double tolerance = 1e-9)
    {
        Assert.That(actual, Is.Not.Null, $"Expected numeric value {expected}, but got null.");
        double numericActual = actual switch
        {
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            decimal m => (double)m,
            _ => throw new AssertionException($"Expected a numeric value, but got {actual!.GetType().Name} ('{actual}').")
        };

        Assert.That(numericActual, Is.EqualTo(expected).Within(tolerance));
    }

    internal static void AssertNumericEqual(object? actual, long expected)
    {
        Assert.That(actual, Is.Not.Null, $"Expected integer value {expected}, but got null.");
        long numericActual = actual switch
        {
            int i => i,
            long l => l,
            double d when Math.Abs(d - Math.Truncate(d)) < 1e-9 => (long)d,
            _ => throw new AssertionException($"Expected an integer-compatible numeric value, but got {actual!.GetType().Name} ('{actual}').")
        };

        Assert.That(numericActual, Is.EqualTo(expected));
    }

    internal static void AssertJsonEquivalent(string actualJson, string expectedJson)
    {
        using var actualDoc = JsonDocument.Parse(actualJson);
        using var expectedDoc = JsonDocument.Parse(expectedJson);
        AssertJsonElementEqual(actualDoc.RootElement, expectedDoc.RootElement, "$");
    }

    private static void AssertJsonElementEqual(JsonElement actual, JsonElement expected, string path)
    {
        if (expected.ValueKind == JsonValueKind.Number && actual.ValueKind == JsonValueKind.Number)
        {
            Assert.That(actual.GetDouble(), Is.EqualTo(expected.GetDouble()).Within(1e-9),
                $"JSON number mismatch at {path}.");
            return;
        }

        Assert.That(actual.ValueKind, Is.EqualTo(expected.ValueKind),
            $"JSON kind mismatch at {path}: expected {expected.ValueKind} ({expected}), actual {actual.ValueKind} ({actual}).");

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedProps = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var actualProps = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                Assert.That(actualProps.Keys, Is.EquivalentTo(expectedProps.Keys),
                    $"JSON object keys mismatch at {path}.");
                foreach (var (key, expectedVal) in expectedProps)
                {
                    AssertJsonElementEqual(actualProps[key], expectedVal, $"{path}.{key}");
                }

                break;

            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                Assert.That(actualItems, Has.Count.EqualTo(expectedItems.Count),
                    $"JSON array length mismatch at {path}.");
                for (var i = 0; i < expectedItems.Count; i++)
                {
                    AssertJsonElementEqual(actualItems[i], expectedItems[i], $"{path}[{i}]");
                }

                break;

            case JsonValueKind.String:
                Assert.That(actual.GetString(), Is.EqualTo(expected.GetString()),
                    $"JSON string mismatch at {path}.");
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break;

            default:
                Assert.Fail($"Unsupported JSON kind {expected.ValueKind} at {path}.");
                break;
        }
    }
}
