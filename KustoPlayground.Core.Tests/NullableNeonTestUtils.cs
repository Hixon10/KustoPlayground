using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KustoPlayground.Core.Tests;

internal sealed class NullableTableBuilder
{
    private readonly string _tableName;
    private readonly List<ColumnBase> _columns = [];
    private readonly List<Dictionary<string, object?>> _rows = [];

    internal NullableTableBuilder(string tableName)
    {
        _tableName = tableName;
    }

    internal NullableTableBuilder WithColumn<T>(string columnName, bool isNullable = true)
    {
        _columns.Add(NullableNeonTestUtils.CreateColumn(columnName, typeof(T), isNullable));
        return this;
    }

    internal NullableTableBuilder WithRow(params object?[] values)
    {
        if (values.Length != _columns.Count)
        {
            throw new ArgumentException(
                $"Row for '{_tableName}' has {values.Length} values, but {_columns.Count} columns were defined.");
        }

        var row = new Dictionary<string, object?>(_columns.Count);
        for (int index = 0; index < _columns.Count; index++)
        {
            row[_columns[index].Name] = values[index];
        }

        _rows.Add(row);
        return this;
    }

    internal Table Build(bool shuffle = true)
    {
        var table = new Table(_tableName, _columns);
        var copy = _rows.ToArray();
        if (shuffle && copy.Length > 1)
        {
            Random.Shared.Shuffle(copy);
        }

        table.AddRows(copy);
        return table;
    }
}

internal static class NullableNeonTestUtils
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

    internal static KustoDatabase CreateDatabase(params Table[] tables)
    {
        var kustoDatabase = new KustoDatabase();
        foreach (Table table in tables)
        {
            kustoDatabase.AddTable(table);
        }

        return kustoDatabase;
    }

    internal static Table CreateEmptyNullableTable(string tableName, params (string Name, Type Type)[] columns)
    {
        var columnDefs = columns
            .Select(column => CreateColumn(column.Name, column.Type, isNullable: true))
            .ToArray();
        return new Table(tableName, columnDefs);
    }

    internal static Table CreateNullableTable<T>(
        IReadOnlyList<T> values,
        string tableName = "table1",
        string columnName = "column1",
        bool shuffle = true)
    {
        var builder = new NullableTableBuilder(tableName).WithColumn<T>(columnName, isNullable: true);
        foreach (T value in values)
        {
            builder.WithRow(value);
        }

        return builder.Build(shuffle);
    }

    internal static Table CreateNullableTable2<T1, T2>(
        IReadOnlyList<(T1 Col1, T2 Col2)> values,
        string tableName = "table1",
        string columnName1 = "col1",
        string columnName2 = "col2",
        bool shuffle = true)
    {
        var builder = new NullableTableBuilder(tableName)
            .WithColumn<T1>(columnName1, isNullable: true)
            .WithColumn<T2>(columnName2, isNullable: true);
        foreach (var (col1, col2) in values)
        {
            builder.WithRow(col1, col2);
        }

        return builder.Build(shuffle);
    }

    internal static Table CreateNullableTable3<T1, T2, T3>(
        IReadOnlyList<(T1 Col1, T2 Col2, T3 Col3)> values,
        string tableName = "table1",
        string columnName1 = "col1",
        string columnName2 = "col2",
        string columnName3 = "col3",
        bool shuffle = true)
    {
        var builder = new NullableTableBuilder(tableName)
            .WithColumn<T1>(columnName1, isNullable: true)
            .WithColumn<T2>(columnName2, isNullable: true)
            .WithColumn<T3>(columnName3, isNullable: true);
        foreach (var (col1, col2, col3) in values)
        {
            builder.WithRow(col1, col2, col3);
        }

        return builder.Build(shuffle);
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

    internal static IReadOnlyDictionary<string, object?> ExecuteAndGetSingleRow(
        KustoDatabase kustoDatabase,
        string query)
    {
        var rows = ExecuteAndAssertSuccess(kustoDatabase, query);
        Assert.That(rows, Has.Count.EqualTo(1), () => $"Expected 1 row for query '{query}', got {rows.Count}.");
        return rows[0];
    }

    internal static object? ExecutePrintSingleValue(string query, string columnName = "print_0")
    {
        var kustoDatabase = new KustoDatabase();
        var row = ExecuteAndGetSingleRow(kustoDatabase, query);
        Assert.That(row, Does.ContainKey(columnName));
        return row[columnName];
    }

    internal static void AssertPrintNull(string query, string columnName = "print_0")
    {
        object? actual = ExecutePrintSingleValue(query, columnName);
        Assert.That(actual, Is.Null, () => $"Expected null for '{query}' column '{columnName}', got '{actual}' ({actual?.GetType().Name}).");
    }

    internal static void AssertPrintBool(string query, bool? expected, string columnName = "print_0")
    {
        object? actual = ExecutePrintSingleValue(query, columnName);
        if (expected is null)
        {
            Assert.That(actual, Is.Null, () => $"Expected bool(null) for '{query}', got '{actual}'.");
        }
        else
        {
            Assert.That(actual, Is.EqualTo(expected.Value), () => $"Unexpected boolean result for '{query}'.");
        }
    }

    internal static List<T> ExecuteAndGetNullableColumn<T>(
        string columnName,
        KustoDatabase kustoDatabase,
        string query)
    {
        var rows = ExecuteAndAssertSuccess(kustoDatabase, query);
        return rows.Select(row =>
        {
            Assert.That(row, Does.ContainKey(columnName));
            return ConvertNullable<T>(row[columnName]);
        }).ToList();
    }

    internal static List<(T1, T2)> ExecuteAndGetNullable2Columns<T1, T2>(
        string columnName1,
        string columnName2,
        KustoDatabase kustoDatabase,
        string query)
    {
        var rows = ExecuteAndAssertSuccess(kustoDatabase, query);
        return rows.Select(row =>
        {
            Assert.That(row, Does.ContainKey(columnName1));
            Assert.That(row, Does.ContainKey(columnName2));
            return (ConvertNullable<T1>(row[columnName1]), ConvertNullable<T2>(row[columnName2]));
        }).ToList();
    }

    internal static List<(T1, T2, T3)> ExecuteAndGetNullable3Columns<T1, T2, T3>(
        string columnName1,
        string columnName2,
        string columnName3,
        KustoDatabase kustoDatabase,
        string query)
    {
        var rows = ExecuteAndAssertSuccess(kustoDatabase, query);
        return rows.Select(row =>
        {
            Assert.That(row, Does.ContainKey(columnName1));
            Assert.That(row, Does.ContainKey(columnName2));
            Assert.That(row, Does.ContainKey(columnName3));
            return (
                ConvertNullable<T1>(row[columnName1]),
                ConvertNullable<T2>(row[columnName2]),
                ConvertNullable<T3>(row[columnName3]));
        }).ToList();
    }

    internal static double? AsNullableDouble(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            short s => s,
            byte b => b,
            _ => throw new AssertionException($"Expected nullable numeric value, got {value.GetType().Name}: '{value}'.")
        };
    }

    internal static long? AsNullableLong(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double d when Math.Abs(d - Math.Truncate(d)) < 1e-12 => (long)d,
            decimal m when m == Math.Truncate(m) => (long)m,
            _ => throw new AssertionException($"Expected nullable integer value, got {value.GetType().Name}: '{value}'.")
        };
    }

    internal static List<object?> ExtractDynamicArray(object? value)
    {
        Assert.That(value, Is.Not.Null, "Expected dynamic array, got null.");

        if (value is JsonElement jsonElement)
        {
            Assert.That(jsonElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
            return jsonElement.EnumerateArray().Select(UnwrapJsonElement).ToList();
        }

        if (value is JsonArray jsonArray)
        {
            return jsonArray.Select(UnwrapJsonNode).ToList();
        }

        if (value is string jsonText)
        {
            using JsonDocument doc = JsonDocument.Parse(jsonText);
            Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
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

        throw new AssertionException($"Expected dynamic array representation, got {value!.GetType().Name}: '{value}'.");
    }

    internal static Dictionary<string, object?> ExtractDynamicBag(object? value)
    {
        Assert.That(value, Is.Not.Null, "Expected dynamic property bag, got null.");

        if (value is IReadOnlyDictionary<string, object?> dict)
        {
            return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
        }

        if (value is JsonElement element)
        {
            Assert.That(element.ValueKind, Is.EqualTo(JsonValueKind.Object));
            return element.EnumerateObject()
                .ToDictionary(p => p.Name, p => UnwrapJsonElement(p.Value), StringComparer.Ordinal);
        }

        if (value is string jsonText)
        {
            using JsonDocument doc = JsonDocument.Parse(jsonText);
            Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
            return doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => UnwrapJsonElement(p.Value), StringComparer.Ordinal);
        }

        throw new AssertionException($"Expected dynamic bag representation, got {value!.GetType().Name}: '{value}'.");
    }

    private static T ConvertNullable<T>(object? raw)
    {
        if (raw is null)
        {
            if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) is null)
            {
                throw new AssertionException(
                    $"Column value was null, which cannot be represented as non-nullable type '{typeof(T).Name}'. Use '{typeof(T).Name}?' instead.");
            }

            return default!;
        }

        if (raw is T typed)
        {
            return typed;
        }

        Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType == typeof(double))
        {
            return (T)(object)Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(long))
        {
            return (T)(object)Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(int))
        {
            return (T)(object)Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(decimal))
        {
            return (T)(object)Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
        }

        return (T)raw;
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
            if (val.TryGetValue<string>(out string? s)) return s;
            if (val.TryGetValue<bool>(out bool b)) return b;
            if (val.TryGetValue<long>(out long l)) return l;
            if (val.TryGetValue<int>(out int i)) return (long)i;
            if (val.TryGetValue<double>(out double d)) return d;
        }

        if (node is JsonArray arr)
        {
            return arr.Select(UnwrapJsonNode).ToList();
        }

        return node.ToJsonString();
    }
}
