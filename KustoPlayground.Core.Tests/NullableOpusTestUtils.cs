using System.Globalization;
using System.Text.Json;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Shared helpers for the nullable column tests (NullableOpus*Tests).
/// They complement <see cref="SummarizeOperatorOpusTestUtils"/> (nullable input tables, exact CLR type checks)
/// with readers that key every result value by the 'Id' column of the input table, so an expectation can be written
/// per input row: a value (with its exact CLR type), null, or a missing row (filtered out).
/// </summary>
internal static class NullableOpusTestUtils
{
    internal const string IdColumn = "Id";

    /// <summary>
    /// Executes the query and returns the 'Id' column (int) in result order.
    /// </summary>
    internal static List<int> ExecuteAndGetIds(KustoDatabase kustoDatabase, string query)
    {
        return GetValues<int>(ExecuteAndGetRows(kustoDatabase, query), IdColumn);
    }

    /// <summary>
    /// Executes the query and returns the value of a column per 'Id': null stays null, a non-null value must have
    /// the exact CLR type T. Compare with Is.EqualTo(new Dictionary&lt;int, T?&gt; { [1] = ..., [2] = null }).
    /// </summary>
    internal static Dictionary<int, T?> ExecuteAndGetValuesById<T>(KustoDatabase kustoDatabase, string query,
        string columnName)
        where T : struct
    {
        return ToDictionaryById(ExecuteAndGetRows(kustoDatabase, query),
            row => GetNullableValue<T>(row, columnName));
    }

    /// <summary>
    /// Same as <see cref="ExecuteAndGetValuesById{T}"/> for numbers produced by arithmetic operators and functions
    /// whose CLR type is not pinned by these tests (see GetNumber): only the value and its nullness are checked.
    /// </summary>
    internal static Dictionary<int, double?> ExecuteAndGetNumbersById(KustoDatabase kustoDatabase, string query,
        string columnName)
    {
        return ToDictionaryById(ExecuteAndGetRows(kustoDatabase, query), row => GetNumberOrNull(row, columnName));
    }

    /// <summary>
    /// Same as <see cref="ExecuteAndGetValuesById{T}"/> for string columns (a null value stays null).
    /// A string computed by Kusto is never null: a missing string is "".
    /// </summary>
    internal static Dictionary<int, string?> ExecuteAndGetStringsById(KustoDatabase kustoDatabase, string query,
        string columnName)
    {
        return ToDictionaryById(ExecuteAndGetRows(kustoDatabase, query), row => GetStringOrNull(row, columnName));
    }

    /// <summary>
    /// Expected values for the ids 1, 2, 3, ... in this order:
    /// ExpectedById&lt;bool&gt;(true, null, false) is { [1] = true, [2] = null, [3] = false }.
    /// </summary>
    internal static Dictionary<int, T?> ExpectedById<T>(params T?[]? valuesOfIds1ToN)
        where T : struct
    {
        // a single null argument binds to the params array itself, it means one null value
        valuesOfIds1ToN ??= new T?[] { null };
        return valuesOfIds1ToN.Select((value, index) => (Id: index + 1, Value: value))
            .ToDictionary(pair => pair.Id, pair => pair.Value);
    }

    /// <summary>
    /// Expected strings for the ids 1, 2, 3, ... in this order (see <see cref="ExpectedById{T}"/>).
    /// </summary>
    internal static Dictionary<int, string?> ExpectedStringsById(params string?[] valuesOfIds1ToN)
    {
        return valuesOfIds1ToN.Select((value, index) => (Id: index + 1, Value: value))
            .ToDictionary(pair => pair.Id, pair => pair.Value);
    }

    /// <summary>
    /// Executes the query and returns one column in result order: null stays null, a non-null value must have
    /// the exact CLR type T.
    /// </summary>
    internal static List<T?> ExecuteAndGetColumn<T>(KustoDatabase kustoDatabase, string query, string columnName)
        where T : struct
    {
        return ExecuteAndGetRows(kustoDatabase, query).Select(row => GetNullableValue<T>(row, columnName)).ToList();
    }

    /// <summary>
    /// Executes the query and returns one column in result order as a compact text, for example "null, 1, 3"
    /// (bool as true / false, datetime as yyyy-MM-dd, timespan as [d.]hh:mm:ss).
    /// A non-null value must have the exact CLR type T.
    /// </summary>
    internal static string ExecuteAndGetColumnAsText<T>(KustoDatabase kustoDatabase, string query, string columnName)
        where T : struct
    {
        return string.Join(", ", ExecuteAndGetColumn<T>(kustoDatabase, query, columnName)
            .Select(value => FormatValue(value)));
    }

    /// <summary>
    /// Executes the query and returns one string column in result order (a null value stays null).
    /// </summary>
    internal static List<string?> ExecuteAndGetStringColumn(KustoDatabase kustoDatabase, string query,
        string columnName)
    {
        return ExecuteAndGetRows(kustoDatabase, query).Select(row => GetStringOrNull(row, columnName)).ToList();
    }

    /// <summary>
    /// Executes a query that ends with the count operator and returns its single 'Count' value.
    /// </summary>
    internal static long ExecuteAndGetCount(KustoDatabase kustoDatabase, string query)
    {
        return GetValue<long>(ExecuteAndGetSingleRow(kustoDatabase, query), "Count");
    }

    /// <summary>
    /// Reads a string value that may be null (a non-null value must be a string).
    /// </summary>
    internal static string? GetStringOrNull(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        object? value = GetRawValue(row, columnName);
        if (value is null)
        {
            return null;
        }

        Assert.That(value, Is.TypeOf<string>(), $"unexpected CLR type of column '{columnName}'");
        return (string)value;
    }

    /// <summary>
    /// Reads a number that may be null, without pinning its CLR type (see GetNumber).
    /// </summary>
    internal static double? GetNumberOrNull(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        return GetRawValue(row, columnName) is null ? null : GetNumber(row, columnName);
    }

    /// <summary>
    /// Reads a dynamic value (pack_array, bag_pack, ...) as JSON, whatever CLR representation the engine uses for it.
    /// </summary>
    internal static JsonElement GetDynamicAsJson(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        return JsonSerializer.SerializeToElement(GetRawValue(row, columnName));
    }

    /// <summary>
    /// Reads a dynamic array as the raw JSON text of its items, for example ["1", "null", "true"].
    /// </summary>
    internal static List<string> GetArrayItemsAsJson(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        JsonElement array = GetDynamicAsJson(row, columnName);
        Assert.That(array.ValueKind, Is.EqualTo(JsonValueKind.Array), $"column '{columnName}' is not an array");
        return array.EnumerateArray().Select(item => item.GetRawText()).ToList();
    }

    /// <summary>
    /// Reads a dynamic property bag as the raw JSON text of its property values by name, for example
    /// { ["a"] = "1", ["b"] = "null" }; the order of the properties is not checked.
    /// </summary>
    internal static Dictionary<string, string> GetBagPropertiesAsJson(IReadOnlyDictionary<string, object?> row,
        string columnName)
    {
        JsonElement bag = GetDynamicAsJson(row, columnName);
        Assert.That(bag.ValueKind, Is.EqualTo(JsonValueKind.Object), $"column '{columnName}' is not a property bag");
        return bag.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetRawText());
    }

    /// <summary>
    /// Asserts the order of ids when rows with equal sort keys (ties) may come in any order:
    /// every group is a set of ids that must take the next positions of the result, in any order inside the group.
    /// For example AssertIdsInOrderWithTies(ids, [4, 5], [1], [3]) accepts [4, 5, 1, 3] and [5, 4, 1, 3].
    /// </summary>
    internal static void AssertIdsInOrderWithTies(IReadOnlyList<int> actualIds, params int[][] expectedGroups)
    {
        string actualText = $"[{string.Join(", ", actualIds)}]";
        Assert.That(actualIds, Has.Count.EqualTo(expectedGroups.Sum(group => group.Length)),
            $"number of result rows, ids: {actualText}");

        int position = 0;
        foreach (int[] group in expectedGroups)
        {
            Assert.That(actualIds.Skip(position).Take(group.Length), Is.EquivalentTo(group),
                $"ids at positions {position}..{position + group.Length - 1} of {actualText}");
            position += group.Length;
        }
    }

    /// <summary>
    /// Builds a table the same way the UI loads a CSV text (KustoPlayground.UI/Pages/Home.razor): the first line is
    /// the header, cells are split by ',' and trimmed, column types are inferred by <see cref="TableBuilderFromCsv"/>,
    /// and an empty cell is a missing value (null), which also makes its column nullable.
    /// </summary>
    internal static Table BuildTableFromCsv(string tableName, string csvText)
    {
        List<string> lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        List<string> headers = lines[0].Split(',').Select(header => header.Trim()).ToList();
        List<Dictionary<string, object?>> rows = lines.Skip(1)
            .Select(line => line.Split(',')
                .Select((value, index) => new KeyValuePair<string, object?>(headers[index], value.Trim()))
                .ToDictionary(pair => pair.Key, pair => pair.Value))
            .ToList();

        return TableBuilderFromCsv.Build(new TableDef
        {
            Name = tableName,
            Columns = headers.Select(header => new ColumnDef { Name = header }).ToList(),
            Rows = rows
        });
    }

    /// <summary>
    /// Builds a table the same way the UI loads a JSON table definition:
    /// { "Name": ..., "Columns": [ { "Name", "Type", "Nullable" } ], "Rows": [ { column: value or null } ] }.
    /// </summary>
    internal static Table BuildTableFromJson(string json)
    {
        TableDef? tableDef = JsonSerializer.Deserialize(json, TableDefJsonContext.Default.TableDef);
        Assert.That(tableDef, Is.Not.Null, "the JSON table definition could not be parsed");
        return TableBuilderFromJson.Build(tableDef!);
    }

    private static Dictionary<int, TValue> ToDictionaryById<TValue>(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        Func<IReadOnlyDictionary<string, object?>, TValue> valueSelector)
    {
        var valuesById = new Dictionary<int, TValue>();
        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            int id = GetValue<int>(row, IdColumn);
            Assert.That(valuesById, Does.Not.ContainKey(id), $"duplicate {IdColumn} {id} in the result");
            valuesById[id] = valueSelector(row);
        }

        return valuesById;
    }

    private static object? GetRawValue(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        Assert.That(row.Keys, Does.Contain(columnName), $"column '{columnName}' is missing");
        return row[columnName];
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        bool boolValue => boolValue ? "true" : "false",
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
