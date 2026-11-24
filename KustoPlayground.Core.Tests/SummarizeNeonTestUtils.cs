using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KustoPlayground.Core.Tests;

internal static class SummarizeNeonTestUtils
{
    internal static ColumnBase CreateColumn(string name, Type clrType, bool isNullable)
    {
        Type underlyingType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        Type columnType = typeof(Column<>).MakeGenericType(underlyingType);
        object? instance = Activator.CreateInstance(
            columnType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [name, isNullable],
            culture: null);

        return instance as ColumnBase
            ?? throw new InvalidOperationException($"Failed to create Column<{underlyingType.Name}> for '{name}'.");
    }

    internal static Table GenerateEmptyTable(string tableName, params ColumnBase[] columns)
    {
        return new Table(tableName, columns);
    }

    internal static Table GenerateNullableTableWithColumn<T>(
        IReadOnlyList<T?> values,
        string tableName = "table1",
        string columnName = "column1",
        bool shuffle = true)
    {
        ColumnBase column = CreateColumn(columnName, typeof(T), isNullable: true);
        var table = new Table(tableName, [column]);

        var rows = values.Select(v => new Dictionary<string, object?>
        {
            { columnName, v }
        }).ToArray();

        if (shuffle)
        {
            Random.Shared.Shuffle(rows);
        }

        table.AddRows(rows);
        return table;
    }

    internal static Table GenerateNullableTableWith2Columns<T1, T2>(
        IReadOnlyList<(T1? Col1, T2? Col2)> values,
        string tableName = "table1",
        string columnName1 = "col1",
        string columnName2 = "col2",
        bool shuffle = true)
    {
        ColumnBase col1 = CreateColumn(columnName1, typeof(T1), isNullable: true);
        ColumnBase col2 = CreateColumn(columnName2, typeof(T2), isNullable: true);
        var table = new Table(tableName, [col1, col2]);

        var rows = values.Select(v => new Dictionary<string, object?>
        {
            { columnName1, v.Col1 },
            { columnName2, v.Col2 }
        }).ToArray();

        if (shuffle)
        {
            Random.Shared.Shuffle(rows);
        }

        table.AddRows(rows);
        return table;
    }

    internal static Table GenerateNullableTableWith3Columns<T1, T2, T3>(
        IReadOnlyList<(T1? Col1, T2? Col2, T3? Col3)> values,
        string tableName = "table1",
        string columnName1 = "col1",
        string columnName2 = "col2",
        string columnName3 = "col3",
        bool shuffle = true)
    {
        ColumnBase col1 = CreateColumn(columnName1, typeof(T1), isNullable: true);
        ColumnBase col2 = CreateColumn(columnName2, typeof(T2), isNullable: true);
        ColumnBase col3 = CreateColumn(columnName3, typeof(T3), isNullable: true);
        var table = new Table(tableName, [col1, col2, col3]);

        var rows = values.Select(v => new Dictionary<string, object?>
        {
            { columnName1, v.Col1 },
            { columnName2, v.Col2 },
            { columnName3, v.Col3 }
        }).ToArray();

        if (shuffle)
        {
            Random.Shared.Shuffle(rows);
        }

        table.AddRows(rows);
        return table;
    }

    internal static Table GenerateTableWithSchema(
        string tableName,
        IReadOnlyList<ColumnBase> columns,
        IReadOnlyList<Dictionary<string, object?>> rows,
        bool shuffle = true)
    {
        var table = new Table(tableName, columns);
        var copy = rows.ToArray();
        if (shuffle)
        {
            Random.Shared.Shuffle(copy);
        }

        table.AddRows(copy);
        return table;
    }

    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> ExecuteAndAssertSuccess(
        KustoDatabase kustoDatabase,
        string query)
    {
        ExecutionResult results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results, Is.Not.Null);
        Assert.That(
            results.ExecutionErrors,
            Is.Null,
            () => $"Unexpected execution errors for query '{query}': " +
                  string.Join("; ", results.ExecutionErrors?.Select(e => $"{e.Code}: {e.Description}") ?? []));
        Assert.That(results.ResultRows, Is.Not.Null);
        return results.ResultRows!;
    }

    internal static IReadOnlyList<ExecutionError> ExecuteAndAssertError(
        KustoDatabase kustoDatabase,
        string query)
    {
        ExecutionResult results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results, Is.Not.Null);
        Assert.That(
            results.ExecutionErrors,
            Is.Not.Null.And.Not.Empty,
            () => $"Expected query '{query}' to fail with execution error(s), but it succeeded.");
        Assert.That(
            results.ExecutionErrors!.Any(e => e.Description?.Contains("Unsupported operator: SummarizeOperator", StringComparison.OrdinalIgnoreCase) == true),
            Is.False,
            () => $"Query '{query}' failed only because SummarizeOperator is not yet implemented, not due to the expected query error.");
        return results.ExecutionErrors!;
    }

    internal static IReadOnlyDictionary<string, object?> ExecuteAndGetSingleRow(
        KustoDatabase kustoDatabase,
        string query)
    {
        var rows = ExecuteAndAssertSuccess(kustoDatabase, query);
        Assert.That(rows, Has.Count.EqualTo(1), () => $"Expected exactly 1 row for query '{query}', got {rows.Count}.");
        return rows[0];
    }

    internal static List<string> GetColumnNames(IReadOnlyDictionary<string, object?> row)
    {
        return row.Keys.ToList();
    }

    internal static List<T?> ExecuteAndGetNullableDataForOneColumn<T>(
        string columnName,
        KustoDatabase kustoDatabase,
        string query)
    {
        var rows = ExecuteAndAssertSuccess(kustoDatabase, query);
        return rows.Select(row =>
        {
            Assert.That(row.ContainsKey(columnName), Is.True, () => $"Expected column '{columnName}' in result columns [{string.Join(", ", row.Keys)}].");
            return ConvertNullable<T>(row[columnName]);
        }).ToList();
    }

    internal static List<(T1?, T2?)> ExecuteAndGetNullableDataFor2Columns<T1, T2>(
        string columnName1,
        string columnName2,
        KustoDatabase kustoDatabase,
        string query)
    {
        var rows = ExecuteAndAssertSuccess(kustoDatabase, query);
        return rows.Select(row =>
        {
            Assert.That(row.ContainsKey(columnName1), Is.True, () => $"Expected column '{columnName1}' in result columns [{string.Join(", ", row.Keys)}].");
            Assert.That(row.ContainsKey(columnName2), Is.True, () => $"Expected column '{columnName2}' in result columns [{string.Join(", ", row.Keys)}].");
            return (ConvertNullable<T1>(row[columnName1]), ConvertNullable<T2>(row[columnName2]));
        }).ToList();
    }

    internal static List<(T1?, T2?, T3?)> ExecuteAndGetNullableDataFor3Columns<T1, T2, T3>(
        string columnName1,
        string columnName2,
        string columnName3,
        KustoDatabase kustoDatabase,
        string query)
    {
        var rows = ExecuteAndAssertSuccess(kustoDatabase, query);
        return rows.Select(row =>
        {
            Assert.That(row.ContainsKey(columnName1), Is.True, () => $"Expected column '{columnName1}' in result columns [{string.Join(", ", row.Keys)}].");
            Assert.That(row.ContainsKey(columnName2), Is.True, () => $"Expected column '{columnName2}' in result columns [{string.Join(", ", row.Keys)}].");
            Assert.That(row.ContainsKey(columnName3), Is.True, () => $"Expected column '{columnName3}' in result columns [{string.Join(", ", row.Keys)}].");
            return (
                ConvertNullable<T1>(row[columnName1]),
                ConvertNullable<T2>(row[columnName2]),
                ConvertNullable<T3>(row[columnName3]));
        }).ToList();
    }

    internal static double AsDouble(object? value)
    {
        Assert.That(value, Is.Not.Null, "Expected a numeric value, got null.");
        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            short s => s,
            byte b => b,
            _ => throw new AssertionException($"Expected numeric value, but got {value!.GetType().Name}: '{value}'.")
        };
    }

    internal static long AsLong(object? value)
    {
        Assert.That(value, Is.Not.Null, "Expected an integer value, got null.");
        return value switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double d when Math.Abs(d - Math.Truncate(d)) < 1e-12 => (long)d,
            _ => throw new AssertionException($"Expected integer value, but got {value!.GetType().Name}: '{value}'.")
        };
    }

    internal static List<object?> ExtractDynamicArray(object? value)
    {
        Assert.That(value, Is.Not.Null, "Expected dynamic array (possibly empty []), got null.");

        if (value is JsonElement jsonElement)
        {
            Assert.That(jsonElement.ValueKind, Is.EqualTo(JsonValueKind.Array), $"Expected JSON array, got {jsonElement.ValueKind}.");
            return jsonElement.EnumerateArray().Select(UnwrapJsonElement).ToList();
        }

        if (value is JsonArray jsonArray)
        {
            return jsonArray.Select(UnwrapJsonNode).ToList();
        }

        if (value is string jsonText)
        {
            using JsonDocument doc = JsonDocument.Parse(jsonText);
            Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array), $"Expected JSON array string, got {doc.RootElement.ValueKind}.");
            return doc.RootElement.EnumerateArray().Select(UnwrapJsonElement).ToList();
        }

        if (value is IEnumerable enumerable)
        {
            var items = new List<object?>();
            foreach (object? item in enumerable)
            {
                items.Add(item is JsonElement je ? UnwrapJsonElement(je) : item is JsonNode jn ? UnwrapJsonNode(jn) : item);
            }

            return items;
        }

        throw new AssertionException($"Expected dynamic array representation, but got {value!.GetType().Name}: '{value}'.");
    }

    internal static List<T?> ExtractTypedDynamicArray<T>(object? value)
    {
        return ExtractDynamicArray(value).Select(ConvertDynamicElement<T>).ToList();
    }

    private static T? ConvertNullable<T>(object? raw)
    {
        if (raw is null)
        {
            return default;
        }

        return (T)raw;
    }

    private static T? ConvertDynamicElement<T>(object? raw)
    {
        if (raw is null)
        {
            return default;
        }

        if (raw is T typed)
        {
            return typed;
        }

        Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType == typeof(int))
        {
            return (T)(object)Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(long))
        {
            return (T)(object)Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(double))
        {
            return (T)(object)Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(decimal))
        {
            return (T)(object)Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(bool))
        {
            return (T)(object)Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(string))
        {
            return (T)(object)Convert.ToString(raw, CultureInfo.InvariantCulture)!;
        }

        if (targetType == typeof(DateTime) && raw is string dtText)
        {
            return (T)(object)DateTime.Parse(dtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (targetType == typeof(TimeSpan) && raw is string tsText)
        {
            return (T)(object)TimeSpan.Parse(tsText, CultureInfo.InvariantCulture);
        }

        return (T)Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
    }

    private static object? UnwrapJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out long l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Array => element.EnumerateArray().Select(UnwrapJsonElement).ToList(),
            _ => element.GetRawText()
        };
    }

    private static object? UnwrapJsonNode(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue val)
        {
            if (val.TryGetValue<string>(out string? s))
            {
                return s;
            }

            if (val.TryGetValue<bool>(out bool b))
            {
                return b;
            }

            if (val.TryGetValue<long>(out long l))
            {
                return l;
            }

            if (val.TryGetValue<int>(out int i))
            {
                return (long)i;
            }

            if (val.TryGetValue<double>(out double d))
            {
                return d;
            }
        }

        if (node is JsonArray arr)
        {
            return arr.Select(UnwrapJsonNode).ToList();
        }

        return node.ToJsonString();
    }
}
