using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KustoPlayground.Core.Tests;

internal static class DynamicNeonTestUtils
{
    internal static Dictionary<string, object?> Bag(params (string Key, object? Value)[] entries)
    {
        var bag = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            bag[key] = value;
        }

        return bag;
    }

    internal static object?[] Arr(params object?[] items) => items;

    internal static Table CreateDynamicTable(
        string tableName,
        IEnumerable<object?> values,
        string columnName = "Payload",
        bool shuffle = false)
    {
        var table = new Table(tableName, [new Column<object>(columnName, isNullable: true)]);
        var rows = values.Select(v => new Dictionary<string, object?> { { columnName, v } }).ToArray();
        if (shuffle && rows.Length > 1)
        {
            Random.Shared.Shuffle(rows);
        }

        table.AddRows(rows);
        return table;
    }

    internal static Table CreateKeyedDynamicTable(
        string tableName,
        IEnumerable<(int Id, object? Payload)> rows,
        string idColumn = "Id",
        string dynamicColumn = "Payload",
        bool shuffle = true)
    {
        var table = new Table(tableName, [
            new Column<int>(idColumn, isNullable: false),
            new Column<object>(dynamicColumn, isNullable: true)
        ]);

        var dictRows = rows.Select(r => new Dictionary<string, object?>
        {
            { idColumn, r.Id },
            { dynamicColumn, r.Payload }
        }).ToArray();

        if (shuffle && dictRows.Length > 1)
        {
            Random.Shared.Shuffle(dictRows);
        }

        table.AddRows(dictRows);
        return table;
    }

    internal static Table CreateTable(
        string tableName,
        ColumnBase[] columns,
        IEnumerable<Dictionary<string, object?>> rows,
        bool shuffle = true)
    {
        var table = new Table(tableName, columns);
        var copy = rows.ToArray();
        if (shuffle && copy.Length > 1)
        {
            Random.Shared.Shuffle(copy);
        }

        table.AddRows(copy);
        return table;
    }

    internal static KustoDatabase CreateDatabase(params Table[] tables)
    {
        var db = new KustoDatabase();
        foreach (var table in tables)
        {
            db.AddTable(table);
        }

        return db;
    }

    internal static Table BuildTableFromJson(string json)
    {
        TableDef? tableDef = JsonSerializer.Deserialize<TableDef>(json);
        Assert.That(tableDef, Is.Not.Null, "Failed to deserialize TableDef JSON.");
        return TableBuilderFromJson.Build(tableDef!);
    }

    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> Execute(
        KustoDatabase db,
        string query)
    {
        ExecutionResult results = db.ExecuteQuery(query);
        Assert.That(results, Is.Not.Null);
        Assert.That(
            results.ExecutionErrors,
            Is.Null,
            () => $"Query failed unexpectedly:\n{query}\nErrors: " +
                  string.Join("; ", results.ExecutionErrors?.Select(e => $"{e.Code}: {e.Description}") ?? []));
        Assert.That(results.ResultRows, Is.Not.Null, "Expected ResultRows to be non-null when query succeeds.");
        return results.ResultRows!;
    }

    internal static IReadOnlyDictionary<string, object?> ExecuteSingleRow(KustoDatabase db, string query)
    {
        var rows = Execute(db, query);
        Assert.That(rows, Has.Count.EqualTo(1), () => $"Expected exactly 1 row for query:\n{query}\nActual row count: {rows.Count}");
        return rows[0];
    }

    internal static object? ExecuteSingleObject(
        KustoDatabase db,
        string query,
        string columnName = "print_0")
    {
        var row = ExecuteSingleRow(db, query);
        Assert.That(row.ContainsKey(columnName), Is.True,
            () => $"Expected column '{columnName}' in result. Actual columns: [{string.Join(", ", row.Keys)}].");
        return row[columnName];
    }

    internal static T ExecuteSingle<T>(
        KustoDatabase db,
        string query,
        string columnName = "print_0")
    {
        object? value = ExecuteSingleObject(db, query, columnName);
        Assert.That(value, Is.Not.Null, () => $"Expected non-null {typeof(T).Name} in column '{columnName}'.");
        Assert.That(value, Is.InstanceOf<T>(),
            () => $"Expected {typeof(T).Name} in column '{columnName}', got {value?.GetType().Name} ('{value}').");
        return (T)value!;
    }

    internal static T? ExecuteSingleNullable<T>(
        KustoDatabase db,
        string query,
        string columnName = "print_0") where T : struct
    {
        object? value = ExecuteSingleObject(db, query, columnName);
        if (value is null)
        {
            return null;
        }

        Assert.That(value, Is.InstanceOf<T>(),
            () => $"Expected {typeof(T).Name} or null in column '{columnName}', got {value.GetType().Name} ('{value}').");
        return (T)value;
    }

    internal static string? ExecuteSingleNullableString(
        KustoDatabase db,
        string query,
        string columnName = "print_0")
    {
        object? value = ExecuteSingleObject(db, query, columnName);
        if (value is null)
        {
            return null;
        }

        Assert.That(value, Is.InstanceOf<string>(),
            () => $"Expected string or null in column '{columnName}', got {value.GetType().Name} ('{value}').");
        return (string)value;
    }

    internal static IReadOnlyList<ExecutionError> ExecuteAndExpectError(KustoDatabase db, string query)
    {
        ExecutionResult results = db.ExecuteQuery(query);
        Assert.That(results, Is.Not.Null);
        Assert.That(
            results.ExecutionErrors,
            Is.Not.Null.And.Not.Empty,
            () => $"Expected execution error for query:\n{query}");
        return results.ExecutionErrors!;
    }

    internal static void AssertSchemaColumn(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> schemaRows,
        string columnName,
        string expectedColumnType,
        string? expectedDataType = null)
    {
        var match = schemaRows.FirstOrDefault(r => string.Equals(r["ColumnName"]?.ToString(), columnName, StringComparison.Ordinal));
        Assert.That(match, Is.Not.Null,
            () => $"Column '{columnName}' not found in getschema output. Columns: [{string.Join(", ", schemaRows.Select(r => r["ColumnName"]))}].");
        Assert.That(match!["ColumnType"]?.ToString(), Is.EqualTo(expectedColumnType),
            () => $"ColumnType mismatch for '{columnName}'.");
        if (expectedDataType is not null)
        {
            Assert.That(match["DataType"]?.ToString(), Is.EqualTo(expectedDataType),
                () => $"DataType mismatch for '{columnName}'.");
        }
    }

    internal static void AssertNumericEqual(object? actual, double expected, double tolerance = 1e-9, string? context = null)
    {
        Assert.That(actual, Is.Not.Null, () => $"Expected numeric value {expected}{(context is null ? "" : $" ({context})")}, got null.");
        double numericActual = ToDouble(actual!);
        Assert.That(numericActual, Is.EqualTo(expected).Within(tolerance), () => context ?? $"Expected {expected}, got {actual}.");
    }

    internal static void AssertLongEqual(object? actual, long expected, string? context = null)
    {
        Assert.That(actual, Is.Not.Null, () => $"Expected integer value {expected}{(context is null ? "" : $" ({context})")}, got null.");
        long numericActual = actual switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double d when Math.Abs(d - Math.Truncate(d)) < 1e-9 => (long)d,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out long jl) => jl,
            _ => throw new AssertionException($"Expected integer value {expected}, got {actual!.GetType().Name} ('{actual}').")
        };

        Assert.That(numericActual, Is.EqualTo(expected), () => context ?? $"Expected {expected}, got {numericActual}.");
    }

    internal static void AssertDateTimeEqual(object? actual, DateTime expectedUtc, string? context = null)
    {
        Assert.That(actual, Is.Not.Null, () => $"Expected DateTime {expectedUtc:O}{(context is null ? "" : $" ({context})")}, got null.");
        DateTime actualDt = actual switch
        {
            DateTime dt => dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime(),
            DateTimeOffset dto => dto.UtcDateTime,
            string s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            JsonElement je when je.ValueKind == JsonValueKind.String =>
                DateTime.Parse(je.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            _ => throw new AssertionException($"Expected DateTime value, got {actual!.GetType().Name} ('{actual}').")
        };

        DateTime normalizedExpected = expectedUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(expectedUtc, DateTimeKind.Utc)
            : expectedUtc.ToUniversalTime();

        Assert.That(actualDt, Is.EqualTo(normalizedExpected), () => context ?? $"Expected DateTime {normalizedExpected:O}, got {actualDt:O}.");
    }

    internal static void AssertTimeSpanEqual(object? actual, TimeSpan expected, string? context = null)
    {
        Assert.That(actual, Is.Not.Null, () => $"Expected TimeSpan {expected}{(context is null ? "" : $" ({context})")}, got null.");
        TimeSpan actualTs = actual switch
        {
            TimeSpan ts => ts,
            string s => TimeSpan.Parse(s, CultureInfo.InvariantCulture),
            JsonElement je when je.ValueKind == JsonValueKind.String => TimeSpan.Parse(je.GetString()!, CultureInfo.InvariantCulture),
            _ => throw new AssertionException($"Expected TimeSpan value, got {actual!.GetType().Name} ('{actual}').")
        };

        Assert.That(actualTs, Is.EqualTo(expected), () => context ?? $"Expected TimeSpan {expected}, got {actualTs}.");
    }

    internal static void AssertGuidEqual(object? actual, Guid expected, string? context = null)
    {
        Assert.That(actual, Is.Not.Null, () => $"Expected Guid {expected}{(context is null ? "" : $" ({context})")}, got null.");
        Guid actualGuid = actual switch
        {
            Guid g => g,
            string s => Guid.Parse(s),
            JsonElement je when je.ValueKind == JsonValueKind.String => Guid.Parse(je.GetString()!),
            _ => throw new AssertionException($"Expected Guid value, got {actual!.GetType().Name} ('{actual}').")
        };

        Assert.That(actualGuid, Is.EqualTo(expected), () => context ?? $"Expected Guid {expected}, got {actualGuid}.");
    }

    internal static Dictionary<string, object?> ExtractBag(object? value, string path = "$")
    {
        Assert.That(value, Is.Not.Null, () => $"Expected dynamic property bag at {path}, got null.");

        if (value is Dictionary<string, object?> dict)
        {
            return dict.ToDictionary(kv => kv.Key, kv => UnwrapNode(kv.Value), StringComparer.Ordinal);
        }

        if (value is IReadOnlyDictionary<string, object?> roDict)
        {
            return roDict.ToDictionary(kv => kv.Key, kv => UnwrapNode(kv.Value), StringComparer.Ordinal);
        }

        if (value is IDictionary nonGenericDict)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                result[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = UnwrapNode(entry.Value);
            }

            return result;
        }

        if (value is JsonElement je)
        {
            Assert.That(je.ValueKind, Is.EqualTo(JsonValueKind.Object),
                () => $"Expected JSON object at {path}, got {je.ValueKind} ({je}).");
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (JsonProperty prop in je.EnumerateObject())
            {
                result[prop.Name] = UnwrapJsonElement(prop.Value);
            }

            return result;
        }

        if (value is JsonObject jObj)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in jObj)
            {
                result[kv.Key] = UnwrapJsonNode(kv.Value);
            }

            return result;
        }

        if (value is string jsonText)
        {
            using JsonDocument doc = JsonDocument.Parse(jsonText);
            return ExtractBag(doc.RootElement, path);
        }

        throw new AssertionException($"Expected dynamic property bag at {path}, got {value!.GetType().Name} ('{value}').");
    }

    internal static List<object?> ExtractArray(object? value, string path = "$")
    {
        Assert.That(value, Is.Not.Null, () => $"Expected dynamic array at {path}, got null.");

        if (value is JsonElement je)
        {
            Assert.That(je.ValueKind, Is.EqualTo(JsonValueKind.Array),
                () => $"Expected JSON array at {path}, got {je.ValueKind} ({je}).");
            return je.EnumerateArray().Select(UnwrapJsonElement).ToList();
        }

        if (value is JsonArray jArr)
        {
            return jArr.Select(UnwrapJsonNode).ToList();
        }

        if (value is string jsonText)
        {
            using JsonDocument doc = JsonDocument.Parse(jsonText);
            return ExtractArray(doc.RootElement, path);
        }

        if (value is IEnumerable enumerable && value is not IDictionary && value is not IReadOnlyDictionary<string, object?>)
        {
            var items = new List<object?>();
            foreach (object? item in enumerable)
            {
                items.Add(UnwrapNode(item));
            }

            return items;
        }

        throw new AssertionException($"Expected dynamic array at {path}, got {value!.GetType().Name} ('{value}').");
    }

    internal static void AssertDynamicBagEqual(object? actual, params (string Key, object? Value)[] expectedEntries)
    {
        AssertDynamicEqual(actual, Bag(expectedEntries));
    }

    internal static void AssertDynamicArrayEqual(object? actual, params object?[] expectedItems)
    {
        AssertDynamicEqual(actual, expectedItems);
    }

    internal static void AssertDynamicSetEquivalent(object? actual, params object?[] expectedItems)
    {
        List<object?> actualItems = ExtractArray(actual);
        Assert.That(actualItems, Has.Count.EqualTo(expectedItems.Length),
            () => $"Dynamic set length mismatch. Expected {expectedItems.Length} elements, got {actualItems.Count}: [{string.Join(", ", actualItems.Select(FormatValue))}].");

        var used = new bool[actualItems.Count];
        foreach (object? expectedItem in expectedItems)
        {
            int matchIndex = -1;
            for (int i = 0; i < actualItems.Count; i++)
            {
                if (!used[i] && AreDynamicEqual(actualItems[i], expectedItem))
                {
                    matchIndex = i;
                    break;
                }
            }

            Assert.That(matchIndex, Is.GreaterThanOrEqualTo(0),
                () => $"Expected element {FormatValue(expectedItem)} not found in set [{string.Join(", ", actualItems.Select(FormatValue))}].");
            used[matchIndex] = true;
        }
    }

    internal static void AssertDynamicEqual(object? actual, object? expected, string path = "$")
    {
        if (expected is null)
        {
            Assert.That(UnwrapNode(actual), Is.Null, () => $"Expected null at {path}, got {FormatValue(actual)}.");
            return;
        }

        if (expected is IDictionary<string, object?> expectedBag)
        {
            Dictionary<string, object?> actualBag = ExtractBag(actual, path);
            Assert.That(actualBag.Keys, Is.EquivalentTo(expectedBag.Keys),
                () => $"Property bag keys mismatch at {path}. Expected: [{string.Join(", ", expectedBag.Keys)}], Actual: [{string.Join(", ", actualBag.Keys)}].");
            foreach (var (key, expectedVal) in expectedBag)
            {
                AssertDynamicEqual(actualBag[key], expectedVal, $"{path}.{key}");
            }

            return;
        }

        if (expected is object?[] expectedArr)
        {
            List<object?> actualArr = ExtractArray(actual, path);
            Assert.That(actualArr, Has.Count.EqualTo(expectedArr.Length),
                () => $"Array length mismatch at {path}. Expected {expectedArr.Length}, got {actualArr.Count}.");
            for (int i = 0; i < expectedArr.Length; i++)
            {
                AssertDynamicEqual(actualArr[i], expectedArr[i], $"{path}[{i}]");
            }

            return;
        }

        object? unwrappedActual = UnwrapNode(actual);
        Assert.That(unwrappedActual, Is.Not.Null, () => $"Expected {FormatValue(expected)} at {path}, got null.");

        switch (expected)
        {
            case bool expectedBool:
                Assert.That(unwrappedActual, Is.InstanceOf<bool>(), () => $"Expected bool at {path}, got {unwrappedActual!.GetType().Name}.");
                Assert.That((bool)unwrappedActual!, Is.EqualTo(expectedBool), () => $"Bool mismatch at {path}.");
                break;

            case string expectedStr:
                Assert.That(unwrappedActual, Is.InstanceOf<string>(), () => $"Expected string at {path}, got {unwrappedActual!.GetType().Name} ('{unwrappedActual}').");
                Assert.That((string)unwrappedActual!, Is.EqualTo(expectedStr), () => $"String mismatch at {path}.");
                break;

            case int expectedInt:
                AssertNumericEqual(unwrappedActual, expectedInt, context: path);
                break;

            case long expectedLong:
                if (unwrappedActual is long actualLong)
                {
                    Assert.That(actualLong, Is.EqualTo(expectedLong), () => $"Long mismatch at {path}.");
                }
                else
                {
                    AssertLongEqual(unwrappedActual, expectedLong, path);
                }
                break;

            case double expectedDouble:
                AssertNumericEqual(unwrappedActual, expectedDouble, context: path);
                break;

            case decimal expectedDecimal:
                AssertNumericEqual(unwrappedActual, (double)expectedDecimal, context: path);
                break;

            case DateTime expectedDt:
                AssertDateTimeEqual(unwrappedActual, expectedDt, path);
                break;

            case TimeSpan expectedTs:
                AssertTimeSpanEqual(unwrappedActual, expectedTs, path);
                break;

            case Guid expectedGuid:
                AssertGuidEqual(unwrappedActual, expectedGuid, path);
                break;

            default:
                Assert.That(unwrappedActual, Is.EqualTo(expected), () => $"Value mismatch at {path}.");
                break;
        }
    }

    internal static bool AreDynamicEqual(object? actual, object? expected)
    {
        object? unwrappedActual = UnwrapNode(actual);

        if (expected is null)
        {
            return unwrappedActual is null;
        }

        if (unwrappedActual is null)
        {
            return false;
        }

        if (expected is IDictionary<string, object?> expectedBag)
        {
            if (!TryExtractBag(unwrappedActual, out Dictionary<string, object?>? actualBag) ||
                actualBag!.Count != expectedBag.Count)
            {
                return false;
            }

            foreach (var (key, expectedVal) in expectedBag)
            {
                if (!actualBag.TryGetValue(key, out object? actualVal) || !AreDynamicEqual(actualVal, expectedVal))
                {
                    return false;
                }
            }

            return true;
        }

        if (expected is object?[] expectedArr)
        {
            if (!TryExtractArray(unwrappedActual, out List<object?>? actualArr) ||
                actualArr!.Count != expectedArr.Length)
            {
                return false;
            }

            for (int i = 0; i < expectedArr.Length; i++)
            {
                if (!AreDynamicEqual(actualArr[i], expectedArr[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return expected switch
        {
            bool expectedBool => unwrappedActual is bool actualBool && actualBool == expectedBool,
            string expectedStr => unwrappedActual is string actualStr && string.Equals(actualStr, expectedStr, StringComparison.Ordinal),
            int expectedInt => TryToDouble(unwrappedActual, out double d) && Math.Abs(d - expectedInt) <= 1e-9,
            long expectedLong => TryToInt64(unwrappedActual, out long actualLong) && actualLong == expectedLong,
            double expectedDouble => TryToDouble(unwrappedActual, out double d) &&
                                     (double.IsNaN(expectedDouble) ? double.IsNaN(d) : Math.Abs(d - expectedDouble) <= 1e-9),
            decimal expectedDecimal => TryToDouble(unwrappedActual, out double d) && Math.Abs(d - (double)expectedDecimal) <= 1e-9,
            DateTime expectedDt => TryToDateTime(unwrappedActual, out DateTime actualDt) &&
                                   actualDt.ToUniversalTime() == expectedDt.ToUniversalTime(),
            TimeSpan expectedTs => TryToTimeSpan(unwrappedActual, out TimeSpan actualTs) && actualTs == expectedTs,
            Guid expectedGuid => TryToGuid(unwrappedActual, out Guid actualGuid) && actualGuid == expectedGuid,
            _ => Equals(unwrappedActual, expected)
        };
    }

    private static bool TryExtractBag(object? value, out Dictionary<string, object?>? bag)
    {
        bag = null;
        if (value is null)
        {
            return false;
        }

        if (value is Dictionary<string, object?> dict)
        {
            bag = dict.ToDictionary(kv => kv.Key, kv => UnwrapNode(kv.Value), StringComparer.Ordinal);
            return true;
        }

        if (value is IReadOnlyDictionary<string, object?> roDict)
        {
            bag = roDict.ToDictionary(kv => kv.Key, kv => UnwrapNode(kv.Value), StringComparer.Ordinal);
            return true;
        }

        if (value is IDictionary nonGenericDict)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                result[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = UnwrapNode(entry.Value);
            }

            bag = result;
            return true;
        }

        if (value is string jsonText && jsonText.TrimStart().StartsWith('{'))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonText);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                bag = doc.RootElement.EnumerateObject()
                    .ToDictionary(p => p.Name, p => UnwrapJsonElement(p.Value), StringComparer.Ordinal);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryExtractArray(object? value, out List<object?>? array)
    {
        array = null;
        if (value is null)
        {
            return false;
        }

        if (value is string jsonText)
        {
            if (!jsonText.TrimStart().StartsWith('['))
            {
                return false;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonText);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                array = doc.RootElement.EnumerateArray().Select(UnwrapJsonElement).ToList();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        if (value is IEnumerable enumerable && value is not IDictionary && value is not IReadOnlyDictionary<string, object?>)
        {
            var items = new List<object?>();
            foreach (object? item in enumerable)
            {
                items.Add(UnwrapNode(item));
            }

            array = items;
            return true;
        }

        return false;
    }

    private static bool TryToDouble(object value, out double result)
    {
        switch (value)
        {
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case short s:
                result = s;
                return true;
            case byte b:
                result = b;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryToInt64(object value, out long result)
    {
        switch (value)
        {
            case long l:
                result = l;
                return true;
            case int i:
                result = i;
                return true;
            case short s:
                result = s;
                return true;
            case byte b:
                result = b;
                return true;
            case double d when Math.Abs(d - Math.Truncate(d)) <= 1e-9 && d is >= long.MinValue and <= long.MaxValue:
                result = (long)d;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryToDateTime(object value, out DateTime result)
    {
        switch (value)
        {
            case DateTime dt:
                result = dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime();
                return true;
            case DateTimeOffset dto:
                result = dto.UtcDateTime;
                return true;
            case string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed):
                result = parsed.UtcDateTime;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryToTimeSpan(object value, out TimeSpan result)
    {
        switch (value)
        {
            case TimeSpan ts:
                result = ts;
                return true;
            case string s when TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out TimeSpan parsed):
                result = parsed;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryToGuid(object value, out Guid result)
    {
        switch (value)
        {
            case Guid g:
                result = g;
                return true;
            case string s when Guid.TryParse(s, out Guid parsed):
                result = parsed;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static double ToDouble(object value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        short s => s,
        byte b => b,
        decimal m => (double)m,
        JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
        _ => throw new AssertionException($"Expected numeric value, got {value.GetType().Name} ('{value}').")
    };

    private static object? UnwrapNode(object? value) => value switch
    {
        null => null,
        JsonElement je => UnwrapJsonElement(je),
        JsonNode jn => UnwrapJsonNode(jn),
        _ => value
    };

    private static object? UnwrapJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt64(out long l) => l,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.Array => element.EnumerateArray().Select(UnwrapJsonElement).ToArray(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => UnwrapJsonElement(p.Value), StringComparer.Ordinal),
        _ => element.GetRawText()
    };

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
            return arr.Select(UnwrapJsonNode).ToArray();
        }

        if (node is JsonObject obj)
        {
            return obj.ToDictionary(kv => kv.Key, kv => UnwrapJsonNode(kv.Value), StringComparer.Ordinal);
        }

        return node.ToJsonString();
    }

    private static string FormatValue(object? value)
    {
        if (value is null) return "null";
        if (value is string s) return $"\"{s}\"";
        if (value is IDictionary<string, object?> dict)
        {
            return "{" + string.Join(", ", dict.Select(kv => $"\"{kv.Key}\": {FormatValue(kv.Value)}")) + "}";
        }

        if (value is IEnumerable enumerable)
        {
            return "[" + string.Join(", ", enumerable.Cast<object?>().Select(FormatValue)) + "]";
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty;
    }
}
