using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Helpers used by the *OpusTests files. They complement <see cref="TestUtils"/> with
/// tables that can contain nulls, single value / exact type / schema / error assertions
/// and structural comparison of dynamic values.
/// </summary>
internal static class OpusTestUtils
{
    internal const string ScalarColumn = "r";

    internal static OpusTableBuilder NewTable(string tableName) => new(tableName);

    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> ExecuteAndGetRows(
        KustoDatabase kustoDatabase,
        string query)
    {
        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null,
            $"{results.ExecutionErrors?[0].Code} {results.ExecutionErrors?[0].Description}");
        Assert.That(results.ResultRows, Is.Not.Null);

        return results.ResultRows!;
    }

    internal static List<object?> ExecuteAndGetValues(string columnName, KustoDatabase kustoDatabase, string query)
    {
        return ExecuteAndGetRows(kustoDatabase, query)
            .Select(row => GetColumnValue(row, columnName))
            .ToList();
    }

    internal static object? ExecuteAndGetSingleValue(string columnName, KustoDatabase kustoDatabase, string query)
    {
        var rows = ExecuteAndGetRows(kustoDatabase, query);
        Assert.That(rows, Has.Count.EqualTo(1), "the query is expected to return exactly one row");

        return GetColumnValue(rows[0], columnName);
    }

    /// <summary>
    /// Evaluates <c>print r = expression</c> on an empty database and returns the boxed value of <c>r</c>.
    /// </summary>
    internal static object? EvaluateScalar(string expression)
    {
        return ExecuteAndGetSingleValue(ScalarColumn, new KustoDatabase(), $"print {ScalarColumn} = {expression}");
    }

    /// <summary>
    /// Checks the value and its exact CLR type
    /// (Kusto int is int, long is long, real is double, decimal is decimal, bool is bool ...).
    /// </summary>
    internal static void AssertTypedValue<T>(object? actual, T expected) where T : notnull
    {
        Assert.That(actual, Is.TypeOf<T>(),
            $"value '{actual}' has unexpected type {actual?.GetType().Name ?? "null"}");
        Assert.That(actual, Is.EqualTo(expected));
    }

    /// <summary>
    /// Only the numeric value is checked, the CLR type is not
    /// (used for results of arithmetic expressions).
    /// </summary>
    internal static void AssertNumericValue(object? actual, double expected)
    {
        Assert.That(actual, Is.Not.Null);
        Assert.That(Convert.ToDouble(actual, CultureInfo.InvariantCulture), Is.EqualTo(expected));
    }

    internal static void AssertQueryFails(KustoDatabase kustoDatabase, string query)
    {
        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ResultRows, Is.Null, "the query is expected to fail");
        Assert.That(results.ExecutionErrors, Is.Not.Null);
        Assert.That(results.ExecutionErrors!, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Runs a query ending with <c>| getschema</c> and returns its rows.
    /// </summary>
    internal static List<(string ColumnName, int ColumnOrdinal, string DataType, string ColumnType)> ExecuteGetSchema(
        KustoDatabase kustoDatabase,
        string query)
    {
        return TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            kustoDatabase,
            query);
    }

    /// <summary>
    /// Dynamic values have no fixed CLR representation (JsonNode, JsonElement, dictionaries, lists, primitives ...),
    /// so the value is converted to JSON and compared structurally (property order is ignored) with the expected JSON.
    /// Use <c>tostring(...)</c> in the query when the exact JSON text (including property order) matters.
    /// </summary>
    internal static void AssertDynamicEquals(object? actual, string expectedJson)
    {
        var actualNode = ToJsonNode(actual);
        var expectedNode = JsonNode.Parse(expectedJson);

        Assert.That(JsonNode.DeepEquals(actualNode, expectedNode), Is.True,
            $"expected dynamic value {expectedJson}, but was {actualNode?.ToJsonString() ?? "null"}");
    }

    /// <summary>
    /// Parses a UTC datetime like "2025-08-23 06:00:00" or "2025-08-23 06:00:00.123".
    /// </summary>
    internal static DateTime ParseDateTime(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    /// <summary>
    /// Parses a timespan like "1.02:03:04", "-01:00:00" or "00:00:00.090".
    /// </summary>
    internal static TimeSpan ParseTimeSpan(string value)
    {
        return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
    }

    private static object? GetColumnValue(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        Assert.That(row.TryGetValue(columnName, out var value), Is.True,
            $"column '{columnName}' is missing in the result");

        return value;
    }

    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        JsonDocument document => JsonNode.Parse(document.RootElement.GetRawText()),
        _ => JsonSerializer.SerializeToNode(value, value.GetType())
    };
}

/// <summary>
/// Fluent builder for input tables with explicitly typed columns which (unlike <see cref="TestUtils"/> tables)
/// can contain nulls and keep the declared column order, e.g.
/// <code>
/// var table = OpusTestUtils.NewTable("T")
///     .WithColumn&lt;long&gt;("id")
///     .WithColumn&lt;double&gt;("r")
///     .WithRow(1L, 1.5)
///     .WithRow(2L, null)
///     .Build();
/// </code>
/// Values must have the exact column CLR type (long: 1L, int: 1, real: 1.5, decimal: 1.5m).
/// Rows are shuffled, because we don't expect any particular order.
/// </summary>
internal sealed class OpusTableBuilder
{
    private readonly string _tableName;
    private readonly List<ColumnBase> _columns = [];
    private readonly List<object?[]> _rows = [];

    internal OpusTableBuilder(string tableName)
    {
        _tableName = tableName;
    }

    internal OpusTableBuilder WithColumn<T>(string columnName, bool isNullable = true)
    {
        _columns.Add(new Column<T>(columnName, isNullable));
        return this;
    }

    internal OpusTableBuilder WithRow(params object?[] values)
    {
        if (values.Length != _columns.Count)
        {
            throw new ArgumentException($"Expected {_columns.Count} values, but got {values.Length}.");
        }

        _rows.Add(values);
        return this;
    }

    internal Table Build()
    {
        var table = new Table(_tableName, _columns);

        var rowsCopy = _rows.ToArray();

        // we don't expect any particular order
        Random.Shared.Shuffle(rowsCopy);

        foreach (var values in rowsCopy)
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < _columns.Count; i++)
            {
                row[_columns[i].Name] = values[i];
            }

            table.AddRow(row);
        }

        return table;
    }
}
