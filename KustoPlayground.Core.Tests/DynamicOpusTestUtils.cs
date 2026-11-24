using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Helpers for the dynamic type test files (DynamicOpus*Tests, MvExpandOperatorOpusTests and MvApplyOperatorOpusTests).
/// <para>
/// A dynamic value can be represented by different CLR objects (object?[], List, Dictionary, JsonElement, JsonNode,
/// boxed primitives ...), so values are converted into a canonical model before they are compared:
/// </para>
/// <list type="bullet">
/// <item>array elements keep their order, property bags are compared without the property order;</item>
/// <item>every integral number (int, long ...) is a Kusto long and every float/double is a Kusto real, so 1 and 1.0
/// are different values (like gettype(dynamic(1)) == 'long' and gettype(dynamic(1.0)) == 'double');</item>
/// <item>decimal, bool, string, datetime (UTC), timespan and guid are separate kinds of values,
/// e.g. a datetime is not equal to its string representation.</item>
/// </list>
/// Expected values are built with <see cref="Arr"/> and <see cref="Bag"/> (C# int literals can be used for longs),
/// table input values with <see cref="Json"/>, <see cref="Arr"/> and <see cref="Bag"/> (use long literals there: 1L).
/// Note that params expands a single array argument, so write <c>Arr((object)Arr(1, 2))</c> for [[1,2]]
/// and <c>Arr((object?)null)</c> for [null].
/// </summary>
internal static class DynamicOpusTestUtils
{
    internal const string GuidText = "6f9619ff-8b86-d011-b42d-00c04fc964ff";

    internal static readonly Guid SampleGuid = Guid.Parse(GuidText);

    private const string KustoDateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    internal static object?[] Arr(params object?[] items) => items;

    internal static Dictionary<string, object?> Bag(params (string Key, object? Value)[] properties)
    {
        var bag = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            bag.Add(key, value);
        }

        return bag;
    }

    internal static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    /// <summary>
    /// Parses JSON text into the representation used for table input values: object?[] arrays, ordinal
    /// Dictionary&lt;string, object?&gt; bags (the property order is kept), long, double, string, bool and null.
    /// </summary>
    internal static object? Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReadInput(document.RootElement);
    }

    /// <summary>
    /// Strict structural comparison of a dynamic value with the expected value built with <see cref="Arr"/>,
    /// <see cref="Bag"/> and CLR scalars (see the class remarks).
    /// </summary>
    internal static void AssertDynamic(object? actual, object? expected, string? context = null)
    {
        var actualValue = Canonicalize(actual);
        var expectedValue = Canonicalize(expected);

        Assert.That(AreEqual(actualValue, expectedValue), Is.True,
            $"{Prefix(context)}expected dynamic value {FormatCanonical(expectedValue)}, but was {FormatCanonical(actualValue)}");
    }

    /// <summary>
    /// Compares a dynamic value with JSON text like tostring() would see it, but ignoring the property order:
    /// datetime, timespan, guid and decimal values are compared with their Kusto JSON strings
    /// ("2020-01-01T00:00:00.0000000Z", "01:00:00", "6f9619ff-...", "1.5").
    /// JSON numbers without '.', 'e' or 'E' are longs and other numbers are reals, so "1" doesn't match 1.0.
    /// </summary>
    internal static void AssertDynamicJson(object? actual, string expectedJson, string? context = null)
    {
        var actualValue = ToJsonModel(Canonicalize(actual));
        object? expectedValue;
        using (var document = JsonDocument.Parse(expectedJson))
        {
            expectedValue = FromJson(document.RootElement);
        }

        Assert.That(AreEqual(actualValue, expectedValue), Is.True,
            $"{Prefix(context)}expected dynamic value {expectedJson}, but was {FormatCanonical(actualValue)}");
    }

    /// <summary>
    /// The value must be an array containing exactly the expected elements in any order
    /// (make_set, set_union, bag_keys ... don't guarantee the order).
    /// </summary>
    internal static void AssertDynamicUnordered(object? actual, params object?[] expectedItems) =>
        AssertUnordered(actual, expectedItems, null);

    /// <summary>
    /// Like <see cref="AssertDynamicUnordered"/> with the expected elements written as a JSON array
    /// (numbers without '.' are longs, so "[1, 1.0]" has a long and a real element).
    /// </summary>
    internal static void AssertDynamicJsonUnordered(object? actual, string expectedJsonArray, string? context = null)
    {
        var expected = Json(expectedJsonArray) as object?[];
        Assert.That(expected, Is.Not.Null, "the expected value must be a JSON array");
        AssertUnordered(actual, expected!, context);
    }

    /// <summary>
    /// Like <see cref="AssertDynamicJsonById"/>, but expected arrays are compared without the element order
    /// (set_union, set_intersect, make_set ... per row or per group); "null" expects null.
    /// </summary>
    internal static void AssertDynamicJsonUnorderedById(
        IReadOnlyDictionary<long, object?> actualById,
        params (long Id, string Json)[] expected)
    {
        Assert.That(actualById.Keys, Is.EquivalentTo(expected.Select(item => item.Id)), "ids");
        foreach (var (id, json) in expected)
        {
            if (json == "null")
            {
                AssertDynamicJson(actualById[id], json, $"id {id}");
            }
            else
            {
                AssertDynamicJsonUnordered(actualById[id], json, $"id {id}");
            }
        }
    }

    private static void AssertUnordered(object? actual, object?[] expectedItems, string? context)
    {
        var actualValue = Canonicalize(actual);
        var expected = expectedItems.Select(Canonicalize).ToList();
        var message = $"{Prefix(context)}expected an array with the elements {FormatCanonical(new CanonicalArray(expected))} "
                      + $"in any order, but was {FormatCanonical(actualValue)}";

        Assert.That(actualValue, Is.InstanceOf<CanonicalArray>(), message);

        var remaining = ((CanonicalArray)actualValue!).Items.ToList();
        var same = remaining.Count == expected.Count && expected.All(item =>
        {
            var index = remaining.FindIndex(candidate => AreEqual(candidate, item));
            if (index < 0)
            {
                return false;
            }

            remaining.RemoveAt(index);
            return true;
        });

        Assert.That(same, Is.True, message);
    }

    /// <summary>
    /// Checks typed scalar values per id (see <see cref="SingleById"/>): the ids must match exactly,
    /// null is expected to be null and other values must have the exact CLR type (6L is not 6.0).
    /// </summary>
    internal static void AssertTypedById<T>(
        IReadOnlyDictionary<long, object?> actualById,
        params (long Id, T? Value)[] expected) where T : struct
    {
        Assert.That(actualById.Keys, Is.EquivalentTo(expected.Select(item => item.Id)), "ids");
        foreach (var (id, value) in expected)
        {
            var actual = actualById[id];
            if (value is null)
            {
                Assert.That(actual, Is.Null, $"id {id}");
                continue;
            }

            Assert.That(actual, Is.TypeOf<T>(), $"id {id}: value '{actual}' has an unexpected type");
            Assert.That(actual, Is.EqualTo(value.Value), $"id {id}");
        }
    }

    /// <summary>
    /// Checks string values per id (see <see cref="SingleById"/>): the ids must match exactly and every value must be
    /// the expected string (a dynamic value holding the same text doesn't match).
    /// </summary>
    internal static void AssertStringsById(
        IReadOnlyDictionary<long, object?> actualById,
        params (long Id, string Value)[] expected)
    {
        Assert.That(actualById.Keys, Is.EquivalentTo(expected.Select(item => item.Id)), "ids");
        foreach (var (id, value) in expected)
        {
            Assert.That(actualById[id], Is.TypeOf<string>().And.EqualTo(value), $"id {id}");
        }
    }

    /// <summary>
    /// Checks a nullable scalar result: null is expected to be null, other values must have the exact CLR type.
    /// </summary>
    internal static void AssertTypedOrNull<T>(object? actual, T? expected) where T : struct
    {
        if (expected is null)
        {
            Assert.That(actual, Is.Null);
            return;
        }

        OpusTestUtils.AssertTypedValue(actual, expected.Value);
    }

    /// <summary>
    /// Checks typed scalar values in order (e.g. the values of one id returned by <see cref="GroupById"/>):
    /// null is expected to be null and other values must have the exact CLR type (6L is not 6.0).
    /// Write <c>(long?)null</c> when the only expected value is null.
    /// </summary>
    internal static void AssertTypedValues<T>(IReadOnlyList<object?> actual, params T?[] expected) where T : struct
    {
        var actualText = "[" + string.Join(", ", actual.Select(value => value?.ToString() ?? "null")) + "]";
        Assert.That(actual, Has.Count.EqualTo(expected.Length), $"expected {expected.Length} values, but was {actualText}");
        for (var index = 0; index < expected.Length; index++)
        {
            var value = expected[index];
            if (value is null)
            {
                Assert.That(actual[index], Is.Null, $"value {index} of {actualText}");
                continue;
            }

            Assert.That(actual[index], Is.TypeOf<T>(), $"value {index} of {actualText} has an unexpected type");
            Assert.That(actual[index], Is.EqualTo(value.Value), $"value {index} of {actualText}");
        }
    }

    /// <summary>
    /// Groups the values of a column by the id column. The values keep the order of the result rows,
    /// so the expansion order of mv-expand / mv-apply can be checked per input row.
    /// </summary>
    internal static Dictionary<long, List<object?>> GroupById(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        string valueColumn,
        string idColumn = "id")
    {
        var groups = new Dictionary<long, List<object?>>();
        foreach (var row in rows)
        {
            Assert.That(row.TryGetValue(idColumn, out var idValue), Is.True, $"column '{idColumn}' is missing in the result");
            Assert.That(row.TryGetValue(valueColumn, out var value), Is.True, $"column '{valueColumn}' is missing in the result");

            var id = Convert.ToInt64(idValue, CultureInfo.InvariantCulture);
            if (!groups.TryGetValue(id, out var values))
            {
                values = [];
                groups[id] = values;
            }

            values.Add(value);
        }

        return groups;
    }

    /// <summary>
    /// Maps every id to the value of a column, every id must be returned exactly once.
    /// </summary>
    internal static Dictionary<long, object?> SingleById(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        string valueColumn,
        string idColumn = "id")
    {
        var groups = GroupById(rows, valueColumn, idColumn);
        foreach (var (id, values) in groups)
        {
            Assert.That(values, Has.Count.EqualTo(1), $"id {id} is expected exactly once in the result");
        }

        return groups.ToDictionary(group => group.Key, group => group.Value[0]);
    }

    /// <summary>
    /// Checks dynamic values per id (see <see cref="SingleById"/>): the ids must match exactly and every value is
    /// compared with <see cref="AssertDynamicJson"/> ("null" for null).
    /// </summary>
    internal static void AssertDynamicJsonById(
        IReadOnlyDictionary<long, object?> actualById,
        params (long Id, string Json)[] expected)
    {
        Assert.That(actualById.Keys, Is.EquivalentTo(expected.Select(item => item.Id)), "ids");
        foreach (var (id, json) in expected)
        {
            AssertDynamicJson(actualById[id], json, $"id {id}");
        }
    }

    /// <summary>
    /// Runs the query and returns the sorted values of the (long) id column.
    /// </summary>
    internal static List<long> ExecuteAndGetIds(KustoDatabase kustoDatabase, string query, string idColumn = "id")
    {
        return OpusTestUtils.ExecuteAndGetValues(idColumn, kustoDatabase, query)
            .Select(value => Convert.ToInt64(value, CultureInfo.InvariantCulture))
            .Order()
            .ToList();
    }

    /// <summary>
    /// Returns the value of a column of a result row (fails if the column doesn't exist).
    /// </summary>
    internal static object? Col(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        Assert.That(row.TryGetValue(columnName, out var value), Is.True, $"column '{columnName}' is missing in the result");
        return value;
    }

    /// <summary>
    /// Runs a query ending with <c>| getschema</c> and returns "ColumnName:ColumnType" items in the column order.
    /// </summary>
    internal static List<string> ExecuteAndGetSchema(KustoDatabase kustoDatabase, string query)
    {
        var schema = OpusTestUtils.ExecuteGetSchema(kustoDatabase, query);
        Assert.That(schema.Select(column => column.ColumnOrdinal), Is.EqualTo(Enumerable.Range(0, schema.Count)),
            "column ordinals");

        return schema.Select(column => $"{column.ColumnName}:{column.ColumnType}").ToList();
    }

    /// <summary>
    /// Evaluates <c>print r = expression</c> after the given let statements.
    /// </summary>
    internal static object? EvaluateWithLets(string letStatements, string expression)
    {
        return OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            $"{letStatements}\nprint {OpusTestUtils.ScalarColumn} = {expression}");
    }

    /// <summary>
    /// The value must be equal (compared like <see cref="AssertDynamicJson"/>) to one of the candidates.
    /// Used where Kusto doesn't define which value is returned (make_bag with a repeated key ...).
    /// </summary>
    internal static void AssertDynamicJsonOneOf(object? actual, params string[] candidateJsons)
    {
        var actualValue = ToJsonModel(Canonicalize(actual));
        var matches = candidateJsons.Any(json => AreEqual(actualValue, ParseExpectedJson(json)));

        Assert.That(matches, Is.True,
            $"expected one of {string.Join(" or ", candidateJsons)}, but was {FormatCanonical(actualValue)}");
    }

    /// <summary>
    /// The value must be an array with exactly <paramref name="expectedCount"/> elements taken from the candidate
    /// elements (every candidate at most once, compared like <see cref="AssertDynamicJson"/>).
    /// Used where Kusto doesn't define which elements are returned (make_list / make_set with a maxSize ...).
    /// </summary>
    internal static void AssertDynamicArraySubsetOf(object? actual, int expectedCount, string candidatesJsonArray)
    {
        var actualValue = ToJsonModel(Canonicalize(actual));
        var candidates = ParseExpectedJson(candidatesJsonArray) as CanonicalArray;
        Assert.That(candidates, Is.Not.Null, "the candidates must be a JSON array");

        var message = $"expected an array with {expectedCount} elements of {candidatesJsonArray}, "
                      + $"but was {FormatCanonical(actualValue)}";
        Assert.That(actualValue, Is.InstanceOf<CanonicalArray>(), message);

        var items = ((CanonicalArray)actualValue!).Items;
        var remaining = candidates!.Items.ToList();
        var allFromCandidates = items.All(item =>
        {
            var index = remaining.FindIndex(candidate => AreEqual(item, candidate));
            if (index < 0)
            {
                return false;
            }

            remaining.RemoveAt(index);
            return true;
        });

        Assert.That(items, Has.Count.EqualTo(expectedCount), message);
        Assert.That(allFromCandidates, Is.True, message);
    }

    /// <summary>
    /// The value must be a property bag with exactly <paramref name="expectedCount"/> properties, every property
    /// (key and value) must be one of the candidate properties. Used for make_bag with a maxSize.
    /// </summary>
    internal static void AssertDynamicBagSubsetOf(object? actual, int expectedCount, string candidatesJsonBag)
    {
        var actualValue = ToJsonModel(Canonicalize(actual));
        var candidates = ParseExpectedJson(candidatesJsonBag) as CanonicalBag;
        Assert.That(candidates, Is.Not.Null, "the candidates must be a JSON object");

        var message = $"expected a property bag with {expectedCount} properties of {candidatesJsonBag}, "
                      + $"but was {FormatCanonical(actualValue)}";
        Assert.That(actualValue, Is.InstanceOf<CanonicalBag>(), message);

        var properties = ((CanonicalBag)actualValue!).Properties;
        Assert.That(properties, Has.Count.EqualTo(expectedCount), message);
        Assert.That(properties.All(property =>
                candidates!.Properties.TryGetValue(property.Key, out var candidate) && AreEqual(property.Value, candidate)),
            Is.True, message);
    }

    /// <summary>
    /// Returns the properties of a property bag value (fails for other values).
    /// The property values can be checked with the AssertDynamic* helpers.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> BagProperties(object? actual)
    {
        var value = Canonicalize(actual);
        Assert.That(value, Is.InstanceOf<CanonicalBag>(), $"expected a property bag, but was {FormatCanonical(value)}");

        return ((CanonicalBag)value!).Properties;
    }

    /// <summary>
    /// Maps every value of a key column (e.g. a string group key) to the value of another column,
    /// every key must be returned exactly once. A null key is mapped to "null", an empty string stays "".
    /// </summary>
    internal static Dictionary<string, object?> SingleByKey(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        string keyColumn,
        string valueColumn)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var keyValue = Col(row, keyColumn);
            var key = keyValue is null ? "null" : Convert.ToString(keyValue, CultureInfo.InvariantCulture)!;
            Assert.That(result.ContainsKey(key), Is.False, $"key '{key}' is expected exactly once in the result");

            result[key] = Col(row, valueColumn);
        }

        return result;
    }

    /// <summary>
    /// Checks dynamic values per key (see <see cref="SingleByKey"/>): the keys must match exactly and every value is
    /// compared with <see cref="AssertDynamicJson"/> ("null" for null).
    /// </summary>
    internal static void AssertDynamicJsonByKey(
        IReadOnlyDictionary<string, object?> actualByKey,
        params (string Key, string Json)[] expected)
    {
        Assert.That(actualByKey.Keys, Is.EquivalentTo(expected.Select(item => item.Key)), "keys");
        foreach (var (key, json) in expected)
        {
            AssertDynamicJson(actualByKey[key], json, $"key '{key}'");
        }
    }

    /// <summary>
    /// Like <see cref="AssertDynamicJsonByKey"/>, but expected arrays are compared without the element order
    /// (make_set per group ...); "null" expects null.
    /// </summary>
    internal static void AssertDynamicJsonUnorderedByKey(
        IReadOnlyDictionary<string, object?> actualByKey,
        params (string Key, string Json)[] expected)
    {
        Assert.That(actualByKey.Keys, Is.EquivalentTo(expected.Select(item => item.Key)), "keys");
        foreach (var (key, json) in expected)
        {
            if (json == "null")
            {
                AssertDynamicJson(actualByKey[key], json, $"key '{key}'");
            }
            else
            {
                AssertDynamicJsonUnordered(actualByKey[key], json, $"key '{key}'");
            }
        }
    }

    /// <summary>
    /// Kusto-like text of a dynamic value for messages: 1 is a long, 1.0 is a real, datetime(...), timespan(...) ...
    /// </summary>
    internal static string Format(object? value) => FormatCanonical(Canonicalize(value));

    private static string Prefix(string? context) => context is null ? string.Empty : $"{context}: ";

    private static object? ParseExpectedJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return FromJson(document.RootElement);
    }

    private static object? ReadInput(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => ReadNumber(element),
        JsonValueKind.Array => element.EnumerateArray().Select(ReadInput).ToArray(),
        JsonValueKind.Object => ReadInputBag(element),
        _ => throw new ArgumentException($"Unexpected JSON value kind {element.ValueKind}")
    };

    private static Dictionary<string, object?> ReadInputBag(JsonElement element)
    {
        var bag = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            bag.Add(property.Name, ReadInput(property.Value));
        }

        return bag;
    }

    private static object ReadNumber(JsonElement element)
    {
        var isInteger = element.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0;
        return isInteger && element.TryGetInt64(out var number) ? (object)number : element.GetDouble();
    }

    private static object? Canonicalize(object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return null;
            case CanonicalArray or CanonicalBag:
                return value;
            case JsonElement element:
                return FromJson(element);
            case JsonDocument document:
                return FromJson(document.RootElement);
            case JsonNode node:
            {
                using var document = JsonDocument.Parse(node.ToJsonString());
                return FromJson(document.RootElement);
            }
            case string text:
                return text;
            case char character:
                return character.ToString();
            case bool flag:
                return flag;
            case sbyte or byte or short or ushort or int or uint or long:
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            case ulong number:
                return number <= long.MaxValue ? (object)(long)number : (double)number;
            case float number:
                return (double)number;
            case double number:
                return number;
            case decimal number:
                return number;
            case System.Data.SqlTypes.SqlDecimal number:
                return number.IsNull ? null : number.Value;
            case DateTime dateTime:
                return dateTime.Kind == DateTimeKind.Local
                    ? dateTime.ToUniversalTime()
                    : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
            case DateTimeOffset dateTimeOffset:
                return dateTimeOffset.UtcDateTime;
            case TimeSpan timeSpan:
                return timeSpan;
            case Guid guid:
                return guid;
            case IDictionary<string, object?> dictionary:
                return ToBag(dictionary.Select(property => (property.Key, property.Value)));
            case IReadOnlyDictionary<string, object?> dictionary:
                return ToBag(dictionary.Select(property => (property.Key, property.Value)));
            case IDictionary dictionary:
                return ToBag(dictionary.Cast<DictionaryEntry>().Select(entry =>
                    (Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty, entry.Value)));
            case IEnumerable enumerable:
                return new CanonicalArray(enumerable.Cast<object?>().Select(Canonicalize).ToList());
            default:
                throw new ArgumentException($"Unsupported dynamic value '{value}' of type {value.GetType()}");
        }
    }

    private static CanonicalBag ToBag(IEnumerable<(string Key, object? Value)> properties)
    {
        var bag = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            bag[key] = Canonicalize(value);
        }

        return new CanonicalBag(bag);
    }

    private static object? FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => ReadNumber(element),
        JsonValueKind.Array => new CanonicalArray(element.EnumerateArray().Select(FromJson).ToList()),
        JsonValueKind.Object => ToBag(element.EnumerateObject().Select(property => (property.Name, FromJson(property.Value)))),
        _ => throw new ArgumentException($"Unexpected JSON value kind {element.ValueKind}")
    };

    /// <summary>
    /// Converts a canonical value into the JSON view of the value (what tostring() would print).
    /// </summary>
    private static object? ToJsonModel(object? value) => value switch
    {
        CanonicalArray array => new CanonicalArray(array.Items.Select(ToJsonModel).ToList()),
        CanonicalBag bag => new CanonicalBag(new SortedDictionary<string, object?>(
            bag.Properties.ToDictionary(property => property.Key, property => ToJsonModel(property.Value)),
            StringComparer.Ordinal)),
        DateTime dateTime => dateTime.ToString(KustoDateTimeFormat, CultureInfo.InvariantCulture),
        TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D"),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        _ => value
    };

    private static bool AreEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return (left, right) switch
        {
            (CanonicalArray x, CanonicalArray y) => x.Items.Count == y.Items.Count
                                                    && x.Items.Zip(y.Items).All(pair => AreEqual(pair.First, pair.Second)),
            (CanonicalBag x, CanonicalBag y) => x.Properties.Count == y.Properties.Count
                                                && x.Properties.All(property =>
                                                    y.Properties.TryGetValue(property.Key, out var other)
                                                    && AreEqual(property.Value, other)),
            (double x, double y) => x.Equals(y) || Math.Abs(x - y) <= 1e-9 * Math.Max(Math.Abs(x), Math.Abs(y)),
            _ => left.GetType() == right.GetType() && left.Equals(right)
        };
    }

    private static string FormatCanonical(object? value) => value switch
    {
        null => "null",
        CanonicalArray array => "[" + string.Join(", ", array.Items.Select(FormatCanonical)) + "]",
        CanonicalBag bag => "{" + string.Join(", ", bag.Properties.Select(property =>
            $"{JsonSerializer.Serialize(property.Key)}: {FormatCanonical(property.Value)}")) + "}",
        string text => JsonSerializer.Serialize(text),
        bool flag => flag ? "true" : "false",
        long number => number.ToString(CultureInfo.InvariantCulture),
        double number => FormatReal(number),
        decimal number => $"decimal({number.ToString(CultureInfo.InvariantCulture)})",
        DateTime dateTime => $"datetime({dateTime.ToString(KustoDateTimeFormat, CultureInfo.InvariantCulture)})",
        TimeSpan timeSpan => $"timespan({timeSpan.ToString("c", CultureInfo.InvariantCulture)})",
        Guid guid => $"guid({guid:D})",
        _ => $"{value} ({value.GetType().Name})"
    };

    private static string FormatReal(double number)
    {
        var text = number.ToString("R", CultureInfo.InvariantCulture);
        return double.IsFinite(number) && text.IndexOfAny(['.', 'E']) < 0 ? text + ".0" : text;
    }

    private sealed class CanonicalArray(List<object?> items)
    {
        internal List<object?> Items { get; } = items;
    }

    private sealed class CanonicalBag(SortedDictionary<string, object?> properties)
    {
        internal SortedDictionary<string, object?> Properties { get; } = properties;
    }
}
