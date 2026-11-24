using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Shared helpers for the summarize operator tests (SummarizeOperatorOpus*Tests).
/// They complement <see cref="TestUtils"/> with nullable input tables, assertions on the result schema
/// (column names and order), exact CLR type checks, and reading of dynamic arrays (make_list, make_set, ...).
/// </summary>
internal static class SummarizeOperatorOpusTestUtils
{
    /// <summary>
    /// Sales(Region: string, Product: string, Amount: long) with 7 rows:
    /// East/Apple/10, East/Apple/20, East/Pear/5, West/Apple/7, West/Pear/1, West/Pear/2, West/Pear/3.
    /// </summary>
    internal static Table BuildSalesTable()
    {
        var rows = new List<(string, string, long)>
        {
            ("East", "Apple", 10L),
            ("East", "Apple", 20L),
            ("East", "Pear", 5L),
            ("West", "Apple", 7L),
            ("West", "Pear", 1L),
            ("West", "Pear", 2L),
            ("West", "Pear", 3L)
        };

        return TestUtils.GenerateTableWith3Columns(rows, "Region", "Product", "Amount", "Sales");
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

    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> ExecuteAndGetRows(
        KustoDatabase kustoDatabase,
        string query)
    {
        ExecutionResult result = kustoDatabase.ExecuteQuery(query);
        Assert.That(result.ExecutionErrors, Is.Null,
            $"{result.ExecutionErrors?[0].Code} {result.ExecutionErrors?[0].Description}");
        Assert.That(result.ResultRows, Is.Not.Null);
        return result.ResultRows!;
    }

    internal static IReadOnlyDictionary<string, object?> ExecuteAndGetSingleRow(
        KustoDatabase kustoDatabase,
        string query)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndGetRows(kustoDatabase, query);
        Assert.That(rows, Has.Count.EqualTo(1), "expected exactly one result row");
        return rows[0];
    }

    internal static void AssertQueryFails(KustoDatabase kustoDatabase, string query)
    {
        ExecutionResult result = kustoDatabase.ExecuteQuery(query);
        Assert.That(result.ResultRows, Is.Null, $"query was expected to fail: {query}");
        Assert.That(result.ExecutionErrors, Is.Not.Null.And.Not.Empty);
    }

    internal static void AssertColumnsInOrder(IReadOnlyDictionary<string, object?> row,
        params string[] expectedColumns)
    {
        Assert.That(row.Keys, Is.EqualTo(expectedColumns), "result columns (names and order)");
    }

    internal static void AssertColumnSet(IReadOnlyDictionary<string, object?> row, params string[] expectedColumns)
    {
        Assert.That(row.Keys, Is.EquivalentTo(expectedColumns), "result columns (names, any order)");
    }

    /// <summary>
    /// Asserts only the leading columns. Used for '*' expansions (take_any(*), arg_max(x, *)), because the engine
    /// does not keep the input schema order for the columns that the star expands to.
    /// </summary>
    internal static void AssertColumnsStartWith(IReadOnlyDictionary<string, object?> row,
        params string[] expectedLeadingColumns)
    {
        Assert.That(row.Keys.Take(expectedLeadingColumns.Length), Is.EqualTo(expectedLeadingColumns),
            "leading result columns (names and order)");
    }

    /// <summary>
    /// Reads a non-null value and asserts its exact CLR type
    /// (NUnit's Is.EqualTo treats 5 and 5L as equal, so the type has to be checked separately).
    /// </summary>
    internal static T GetValue<T>(IReadOnlyDictionary<string, object?> row, string columnName)
        where T : notnull
    {
        object? value = GetRawValue(row, columnName);
        Assert.That(value, Is.Not.Null, $"column '{columnName}' is null");
        Assert.That(value, Is.TypeOf<T>(), $"unexpected CLR type of column '{columnName}'");
        return (T)value!;
    }

    internal static List<T> GetValues<T>(IEnumerable<IReadOnlyDictionary<string, object?>> rows, string columnName)
        where T : notnull
    {
        return rows.Select(row => GetValue<T>(row, columnName)).ToList();
    }

    /// <summary>
    /// Reads a value that may be null; a non-null value must have the exact CLR type.
    /// </summary>
    internal static T? GetNullableValue<T>(IReadOnlyDictionary<string, object?> row, string columnName)
        where T : struct
    {
        object? value = GetRawValue(row, columnName);
        if (value is null)
        {
            return null;
        }

        Assert.That(value, Is.TypeOf<T>(), $"unexpected CLR type of column '{columnName}'");
        return (T)value;
    }

    internal static void AssertIsNull(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        Assert.That(GetRawValue(row, columnName), Is.Null, $"column '{columnName}' was expected to be null");
    }

    /// <summary>
    /// Reads a number without pinning its CLR type. Used only for values produced by arithmetic operators,
    /// because the engine evaluates + - * / % as double, while Kusto keeps long for long operands.
    /// </summary>
    internal static double GetNumber(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        object? value = GetRawValue(row, columnName);
        Assert.That(value, Is.Not.Null, $"column '{columnName}' is null");
        Assert.That(value is sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal,
            Is.True, $"column '{columnName}' is not numeric: {value!.GetType().Name}");
        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    internal static IReadOnlyDictionary<string, object?> GetSingleRowWhere(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        string columnName,
        object? expectedValue)
    {
        List<IReadOnlyDictionary<string, object?>> matches = rows
            .Where(row => row.TryGetValue(columnName, out object? value) && Equals(value, expectedValue))
            .ToList();
        Assert.That(matches, Has.Count.EqualTo(1),
            $"expected exactly one row where {columnName} == {expectedValue ?? "null"}");
        return matches[0];
    }

    /// <summary>
    /// Reads a dynamic array (make_list, make_set, percentiles_array, ...).
    /// Any non-string IEnumerable (List, array, JsonArray) or a JsonElement array is accepted.
    /// Elements are normalized: integers to long, float/decimal to double, JSON values to CLR values.
    /// </summary>
    internal static List<object?> GetDynamicArray(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        object? value = GetRawValue(row, columnName);
        Assert.That(value, Is.Not.Null, $"dynamic array column '{columnName}' is null");

        if (value is JsonElement { ValueKind: JsonValueKind.Array } jsonArray)
        {
            return jsonArray.EnumerateArray().Select(element => NormalizeJsonElement(element)).ToList();
        }

        Assert.That(value, Is.Not.InstanceOf<string>(), $"column '{columnName}' must hold an array, not a string");
        Assert.That(value, Is.InstanceOf<IEnumerable>(), $"column '{columnName}' must hold an array");
        return ((IEnumerable)value!).Cast<object?>().Select(NormalizeDynamicElement).ToList();
    }

    private static object? GetRawValue(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        Assert.That(row.Keys, Does.Contain(columnName), $"column '{columnName}' is missing");
        return row[columnName];
    }

    private static object? NormalizeDynamicElement(object? element) => element switch
    {
        null => null,
        JsonElement json => NormalizeJsonElement(json),
        JsonNode node => NormalizeJsonText(node.ToJsonString()),
        sbyte or byte or short or ushort or int or uint or long =>
            (object)Convert.ToInt64(element, CultureInfo.InvariantCulture),
        float or decimal => (object)Convert.ToDouble(element, CultureInfo.InvariantCulture),
        _ => element
    };

    private static object? NormalizeJsonText(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return NormalizeJsonElement(document.RootElement);
    }

    private static object? NormalizeJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => (object)true,
        JsonValueKind.False => (object)false,
        JsonValueKind.Number when element.TryGetInt64(out long longValue) => (object)longValue,
        JsonValueKind.Number => (object)element.GetDouble(),
        _ => element.GetRawText()
    };
}

/// <summary>
/// Fluent builder for input tables whose columns may contain nulls (TestUtils only creates non-nullable columns).
/// Values must have the exact CLR type of the column (for example 1L for a long column).
/// Rows are shuffled, because the result of summarize must not depend on the input order.
/// </summary>
internal sealed class SummarizeOperatorOpusTableBuilder
{
    private readonly string _tableName;
    private readonly List<ColumnBase> _columns = new();
    private readonly List<object?[]> _rows = new();

    internal SummarizeOperatorOpusTableBuilder(string tableName)
    {
        _tableName = tableName;
    }

    internal SummarizeOperatorOpusTableBuilder WithColumn<T>(string columnName)
    {
        _columns.Add(new Column<T>(columnName, isNullable: true));
        return this;
    }

    internal SummarizeOperatorOpusTableBuilder WithRow(params object?[]? values)
    {
        // WithRow(null) on a single-column table passes a null array instead of one null value
        values ??= new object?[] { null };
        if (values.Length != _columns.Count)
        {
            throw new ArgumentException($"Expected {_columns.Count} values, but got {values.Length}.");
        }

        _rows.Add(values);
        return this;
    }

    internal Table Build()
    {
        object?[][] rows = _rows.ToArray();
        Random.Shared.Shuffle(rows);

        var table = new Table(_tableName, _columns);
        foreach (object?[] values in rows)
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < _columns.Count; i++)
            {
                row[_columns[i].Name] = values[i];
            }

            table.AddRow(row);
        }

        return table;
    }
}
