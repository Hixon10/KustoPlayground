using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KustoPlayground.Core.Tests;

internal static class MakeSeriesNeonTestUtils
{
    internal static DateTime Utc(
        int year,
        int month,
        int day,
        int hour = 0,
        int minute = 0,
        int second = 0,
        int millisecond = 0)
    {
        return new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
    }

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
        var db = new KustoDatabase();
        foreach (Table table in tables)
        {
            db.AddTable(table);
        }

        return db;
    }

    internal static Table CreateEmptyTable(string tableName, params ColumnBase[] columns)
    {
        return new Table(tableName, columns);
    }

    internal static Table CreateTable(
        string tableName,
        IReadOnlyList<ColumnBase> columns,
        IReadOnlyList<Dictionary<string, object?>> rows,
        bool shuffle = true)
    {
        var table = new Table(tableName, columns);
        Dictionary<string, object?>[] copy = rows.ToArray();
        if (shuffle && copy.Length > 1)
        {
            Random.Shared.Shuffle(copy);
        }

        table.AddRows(copy);
        return table;
    }

    internal static Table CreateDateTimeValueTable(
        IReadOnlyList<(DateTime? Ts, double? Val)> rows,
        string tableName = "Events",
        string tsColumn = "Ts",
        string valColumn = "Val",
        bool shuffle = true)
    {
        ColumnBase[] columns =
        [
            CreateColumn(tsColumn, typeof(DateTime), isNullable: true),
            CreateColumn(valColumn, typeof(double), isNullable: true)
        ];

        Dictionary<string, object?>[] dictRows = rows.Select(r => new Dictionary<string, object?>
        {
            { tsColumn, r.Ts },
            { valColumn, r.Val }
        }).ToArray();

        return CreateTable(tableName, columns, dictRows, shuffle);
    }

    internal static Table CreateDateTimeIntTable(
        IReadOnlyList<(DateTime? Ts, int? Val)> rows,
        string tableName = "Events",
        string tsColumn = "Ts",
        string valColumn = "Val",
        bool shuffle = true)
    {
        ColumnBase[] columns =
        [
            CreateColumn(tsColumn, typeof(DateTime), isNullable: true),
            CreateColumn(valColumn, typeof(int), isNullable: true)
        ];

        Dictionary<string, object?>[] dictRows = rows.Select(r => new Dictionary<string, object?>
        {
            { tsColumn, r.Ts },
            { valColumn, r.Val }
        }).ToArray();

        return CreateTable(tableName, columns, dictRows, shuffle);
    }

    internal static Table CreateLongValueTable(
        IReadOnlyList<(long? Axis, long? Val)> rows,
        string tableName = "Events",
        string axisColumn = "X",
        string valColumn = "Val",
        bool shuffle = true)
    {
        ColumnBase[] columns =
        [
            CreateColumn(axisColumn, typeof(long), isNullable: true),
            CreateColumn(valColumn, typeof(long), isNullable: true)
        ];

        Dictionary<string, object?>[] dictRows = rows.Select(r => new Dictionary<string, object?>
        {
            { axisColumn, r.Axis },
            { valColumn, r.Val }
        }).ToArray();

        return CreateTable(tableName, columns, dictRows, shuffle);
    }

    internal static Table CreateKeyedDateTimeTable(
        IReadOnlyList<(DateTime? Ts, string? Grp, int? Val)> rows,
        string tableName = "Events",
        string tsColumn = "Ts",
        string grpColumn = "Grp",
        string valColumn = "Val",
        bool shuffle = true)
    {
        ColumnBase[] columns =
        [
            CreateColumn(tsColumn, typeof(DateTime), isNullable: true),
            CreateColumn(grpColumn, typeof(string), isNullable: true),
            CreateColumn(valColumn, typeof(int), isNullable: true)
        ];

        Dictionary<string, object?>[] dictRows = rows.Select(r => new Dictionary<string, object?>
        {
            { tsColumn, r.Ts },
            { grpColumn, r.Grp },
            { valColumn, r.Val }
        }).ToArray();

        return CreateTable(tableName, columns, dictRows, shuffle);
    }

    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> ExecuteAndAssertSuccess(
        KustoDatabase db,
        string query)
    {
        ExecutionResult results = db.ExecuteQuery(query);
        Assert.That(results, Is.Not.Null);
        Assert.That(
            results.ExecutionErrors,
            Is.Null,
            () => $"Unexpected execution errors for query:\n{query}\nErrors: " +
                  string.Join("; ", results.ExecutionErrors?.Select(e => $"{e.Code}: {e.Description}") ?? []));
        Assert.That(results.ResultRows, Is.Not.Null, "Expected ResultRows to be non-null when query succeeds.");
        return results.ResultRows!;
    }

    internal static IReadOnlyDictionary<string, object?> ExecuteAndGetSingleRow(
        KustoDatabase db,
        string query)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndAssertSuccess(db, query);
        Assert.That(
            rows,
            Has.Count.EqualTo(1),
            () => $"Expected exactly 1 row for query:\n{query}\nActual row count: {rows.Count}.");
        return rows[0];
    }

    internal static void ExecuteAndAssertEmpty(
        KustoDatabase db,
        string query)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndAssertSuccess(db, query);
        Assert.That(
            rows,
            Is.Empty,
            () => $"Expected 0 rows for query:\n{query}\nActual row count: {rows.Count}.");
    }

    internal static IReadOnlyList<ExecutionError> ExecuteAndAssertError(
        KustoDatabase db,
        string query)
    {
        ExecutionResult results = db.ExecuteQuery(query);
        Assert.That(results, Is.Not.Null);
        Assert.That(
            results.ExecutionErrors,
            Is.Not.Null.And.Not.Empty,
            () => $"Expected query to fail with execution error(s), but it succeeded:\n{query}");
        Assert.That(
            results.ExecutionErrors!.Any(e =>
                e.Description?.Contains("Unsupported operator: MakeSeriesOperator", StringComparison.OrdinalIgnoreCase) == true),
            Is.False,
            () => $"Query failed only because MakeSeriesOperator is not yet implemented, not due to the expected query error:\n{query}");
        return results.ExecutionErrors!;
    }

    internal static IReadOnlyDictionary<string, object?> FindRowByKey(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string keyColumn,
        object? expectedKey)
    {
        List<IReadOnlyDictionary<string, object?>> matches = rows
            .Where(row =>
            {
                Assert.That(
                    row.ContainsKey(keyColumn),
                    Is.True,
                    () => $"Expected key column '{keyColumn}' in row columns [{string.Join(", ", row.Keys)}].");
                return ScalarKeysEqual(row[keyColumn], expectedKey);
            })
            .ToList();

        Assert.That(
            matches,
            Has.Count.EqualTo(1),
            () => $"Expected exactly 1 row where {keyColumn} == {FormatValue(expectedKey)}, found {matches.Count}. " +
                  $"Available keys: [{string.Join(", ", rows.Select(r => FormatValue(r.GetValueOrDefault(keyColumn))))}].");
        return matches[0];
    }

    internal static IReadOnlyDictionary<string, object?> FindRowByKeys(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        params (string Column, object? ExpectedValue)[] keys)
    {
        List<IReadOnlyDictionary<string, object?>> matches = rows
            .Where(row => keys.All(k =>
            {
                Assert.That(
                    row.ContainsKey(k.Column),
                    Is.True,
                    () => $"Expected key column '{k.Column}' in row columns [{string.Join(", ", row.Keys)}].");
                return ScalarKeysEqual(row[k.Column], k.ExpectedValue);
            }))
            .ToList();

        Assert.That(
            matches,
            Has.Count.EqualTo(1),
            () => $"Expected exactly 1 row matching ({string.Join(", ", keys.Select(k => $"{k.Column}={FormatValue(k.ExpectedValue)}"))}), found {matches.Count}.");
        return matches[0];
    }

    internal static void AssertColumnOrder(
        IReadOnlyDictionary<string, object?> row,
        params string[] expectedColumns)
    {
        Assert.That(
            row.Keys.ToList(),
            Is.EqualTo(expectedColumns.ToList()),
            () => $"Column order mismatch. Expected [{string.Join(", ", expectedColumns)}], actual [{string.Join(", ", row.Keys)}].");
    }

    internal static void AssertSchemaRow(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> schemaRows,
        int expectedOrdinal,
        string expectedColumnName,
        string expectedColumnType,
        string expectedDataType)
    {
        Assert.That(
            expectedOrdinal,
            Is.LessThan(schemaRows.Count),
            () => $"Expected schema row at ordinal {expectedOrdinal} ('{expectedColumnName}'), but getschema returned only {schemaRows.Count} columns.");

        IReadOnlyDictionary<string, object?> row = schemaRows[expectedOrdinal];
        Assert.That(row["ColumnName"]?.ToString(), Is.EqualTo(expectedColumnName),
            () => $"ColumnName mismatch at ordinal {expectedOrdinal}.");
        Assert.That(Convert.ToInt32(row["ColumnOrdinal"], CultureInfo.InvariantCulture), Is.EqualTo(expectedOrdinal),
            () => $"ColumnOrdinal mismatch for '{expectedColumnName}'.");
        Assert.That(row["ColumnType"]?.ToString(), Is.EqualTo(expectedColumnType),
            () => $"ColumnType mismatch for '{expectedColumnName}'.");
        Assert.That(row["DataType"]?.ToString(), Is.EqualTo(expectedDataType),
            () => $"DataType mismatch for '{expectedColumnName}'.");
    }

    internal static List<object?> ExtractSeries(object? value, string context)
    {
        Assert.That(value, Is.Not.Null, () => $"Expected dynamic series array for '{context}', got null.");

        if (value is JsonElement je)
        {
            Assert.That(je.ValueKind, Is.EqualTo(JsonValueKind.Array),
                () => $"Expected JSON array for '{context}', got {je.ValueKind} ({je}).");
            return je.EnumerateArray().Select(UnwrapJsonElement).ToList();
        }

        if (value is JsonArray jArr)
        {
            return jArr.Select(UnwrapJsonNode).ToList();
        }

        if (value is string jsonText)
        {
            using JsonDocument doc = JsonDocument.Parse(jsonText);
            return ExtractSeries(doc.RootElement, context);
        }

        if (value is IEnumerable enumerable && value is not IDictionary && value is not IReadOnlyDictionary<string, object?>)
        {
            var items = new List<object?>();
            foreach (object? item in enumerable)
            {
                items.Add(UnwrapValue(item));
            }

            return items;
        }

        throw new AssertionException($"Expected dynamic series array for '{context}', got {value!.GetType().Name} ('{value}').");
    }

    internal static void AssertLongSeries(
        IReadOnlyDictionary<string, object?> row,
        string columnName,
        params long?[] expected)
    {
        Assert.That(row.ContainsKey(columnName), Is.True,
            () => $"Column '{columnName}' missing. Available: [{string.Join(", ", row.Keys)}].");

        List<object?> actual = ExtractSeries(row[columnName], columnName);
        Assert.That(actual, Has.Count.EqualTo(expected.Length),
            () => $"Length mismatch for series '{columnName}'. Expected [{string.Join(", ", expected.Select(v => FormatValue(v)))}], actual [{string.Join(", ", actual.Select(FormatValue))}].");

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] is null)
            {
                Assert.That(actual[i], Is.Null, () => $"Expected null at {columnName}[{i}], got {FormatValue(actual[i])}.");
            }
            else
            {
                Assert.That(actual[i], Is.Not.Null, () => $"Expected {expected[i]} at {columnName}[{i}], got null.");
                long actualLong = ToInt64(actual[i]!, $"{columnName}[{i}]");
                Assert.That(actualLong, Is.EqualTo(expected[i]!.Value),
                    () => $"Value mismatch at {columnName}[{i}]. Expected {expected[i]}, got {actualLong}. Full series: [{string.Join(", ", actual.Select(FormatValue))}].");
            }
        }
    }

    internal static void AssertDoubleSeriesWithin(
        IReadOnlyDictionary<string, object?> row,
        string columnName,
        double tolerance,
        params double?[] expected)
    {
        Assert.That(row.ContainsKey(columnName), Is.True,
            () => $"Column '{columnName}' missing. Available: [{string.Join(", ", row.Keys)}].");

        List<object?> actual = ExtractSeries(row[columnName], columnName);
        Assert.That(actual, Has.Count.EqualTo(expected.Length),
            () => $"Length mismatch for series '{columnName}'. Expected [{string.Join(", ", expected.Select(v => FormatValue(v)))}], actual [{string.Join(", ", actual.Select(FormatValue))}].");

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] is null)
            {
                Assert.That(actual[i], Is.Null, () => $"Expected null at {columnName}[{i}], got {FormatValue(actual[i])}.");
            }
            else
            {
                Assert.That(actual[i], Is.Not.Null, () => $"Expected {expected[i]} at {columnName}[{i}], got null.");
                double actualDouble = ToDouble(actual[i]!, $"{columnName}[{i}]");
                double expectedDouble = expected[i]!.Value;

                if (double.IsNaN(expectedDouble))
                {
                    Assert.That(double.IsNaN(actualDouble), Is.True, () => $"Expected NaN at {columnName}[{i}], got {actualDouble}.");
                }
                else if (double.IsPositiveInfinity(expectedDouble))
                {
                    Assert.That(double.IsPositiveInfinity(actualDouble), Is.True, () => $"Expected +Infinity at {columnName}[{i}], got {actualDouble}.");
                }
                else if (double.IsNegativeInfinity(expectedDouble))
                {
                    Assert.That(double.IsNegativeInfinity(actualDouble), Is.True, () => $"Expected -Infinity at {columnName}[{i}], got {actualDouble}.");
                }
                else
                {
                    Assert.That(actualDouble, Is.EqualTo(expectedDouble).Within(tolerance),
                        () => $"Value mismatch at {columnName}[{i}]. Expected {expectedDouble}, got {actualDouble}. Full series: [{string.Join(", ", actual.Select(FormatValue))}].");
                }
            }
        }
    }

    internal static void AssertDoubleSeries(
        IReadOnlyDictionary<string, object?> row,
        string columnName,
        params double?[] expected)
    {
        AssertDoubleSeriesWithin(row, columnName, 1e-9, expected);
    }

    internal static void AssertDecimalSeries(
        IReadOnlyDictionary<string, object?> row,
        string columnName,
        params decimal?[] expected)
    {
        Assert.That(row.ContainsKey(columnName), Is.True,
            () => $"Column '{columnName}' missing. Available: [{string.Join(", ", row.Keys)}].");

        List<object?> actual = ExtractSeries(row[columnName], columnName);
        Assert.That(actual, Has.Count.EqualTo(expected.Length),
            () => $"Length mismatch for series '{columnName}'. Expected [{string.Join(", ", expected.Select(v => FormatValue(v)))}], actual [{string.Join(", ", actual.Select(FormatValue))}].");

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] is null)
            {
                Assert.That(actual[i], Is.Null, () => $"Expected null at {columnName}[{i}], got {FormatValue(actual[i])}.");
            }
            else
            {
                Assert.That(actual[i], Is.Not.Null, () => $"Expected {expected[i]} at {columnName}[{i}], got null.");
                decimal actualDecimal = ToDecimal(actual[i]!, $"{columnName}[{i}]");
                Assert.That(actualDecimal, Is.EqualTo(expected[i]!.Value),
                    () => $"Value mismatch at {columnName}[{i}]. Expected {expected[i]}, got {actualDecimal}. Full series: [{string.Join(", ", actual.Select(FormatValue))}].");
            }
        }
    }

    internal static void AssertDateTimeSeries(
        IReadOnlyDictionary<string, object?> row,
        string columnName,
        params DateTime?[] expectedUtc)
    {
        Assert.That(row.ContainsKey(columnName), Is.True,
            () => $"Column '{columnName}' missing. Available: [{string.Join(", ", row.Keys)}].");

        List<object?> actual = ExtractSeries(row[columnName], columnName);
        Assert.That(actual, Has.Count.EqualTo(expectedUtc.Length),
            () => $"Length mismatch for DateTime series '{columnName}'. Expected {expectedUtc.Length} items, got {actual.Count}: [{string.Join(", ", actual.Select(FormatValue))}].");

        for (int i = 0; i < expectedUtc.Length; i++)
        {
            if (expectedUtc[i] is null)
            {
                Assert.That(actual[i], Is.Null, () => $"Expected null DateTime at {columnName}[{i}], got {FormatValue(actual[i])}.");
            }
            else
            {
                Assert.That(actual[i], Is.Not.Null, () => $"Expected {expectedUtc[i]:O} at {columnName}[{i}], got null.");
                DateTime actualDt = ToUtcDateTime(actual[i]!, $"{columnName}[{i}]");
                DateTime expectedDt = NormalizeUtc(expectedUtc[i]!.Value);
                Assert.That(actualDt, Is.EqualTo(expectedDt),
                    () => $"DateTime mismatch at {columnName}[{i}]. Expected {expectedDt:O}, got {actualDt:O}.");
            }
        }
    }

    internal static void AssertTimeSpanSeries(
        IReadOnlyDictionary<string, object?> row,
        string columnName,
        params TimeSpan?[] expected)
    {
        Assert.That(row.ContainsKey(columnName), Is.True,
            () => $"Column '{columnName}' missing. Available: [{string.Join(", ", row.Keys)}].");

        List<object?> actual = ExtractSeries(row[columnName], columnName);
        Assert.That(actual, Has.Count.EqualTo(expected.Length),
            () => $"Length mismatch for TimeSpan series '{columnName}'. Expected {expected.Length} items, got {actual.Count}: [{string.Join(", ", actual.Select(FormatValue))}].");

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] is null)
            {
                Assert.That(actual[i], Is.Null, () => $"Expected null TimeSpan at {columnName}[{i}], got {FormatValue(actual[i])}.");
            }
            else
            {
                Assert.That(actual[i], Is.Not.Null, () => $"Expected {expected[i]} at {columnName}[{i}], got null.");
                TimeSpan actualTs = ToTimeSpan(actual[i]!, $"{columnName}[{i}]");
                Assert.That(actualTs, Is.EqualTo(expected[i]!.Value),
                    () => $"TimeSpan mismatch at {columnName}[{i}]. Expected {expected[i]}, got {actualTs}.");
            }
        }
    }

    private static bool ScalarKeysEqual(object? actual, object? expected)
    {
        if (actual is null && expected is null)
        {
            return true;
        }

        if (actual is null || expected is null)
        {
            return false;
        }

        if (expected is DateTime expectedDt)
        {
            return ToUtcDateTime(actual, "key") == NormalizeUtc(expectedDt);
        }

        if (expected is TimeSpan expectedTs)
        {
            return ToTimeSpan(actual, "key") == expectedTs;
        }

        if (expected is Guid expectedGuid)
        {
            Guid actualGuid = actual is Guid g ? g : Guid.Parse(actual.ToString()!);
            return actualGuid == expectedGuid;
        }

        if (expected is decimal expectedDec)
        {
            return ToDecimal(actual, "key") == expectedDec;
        }

        if (expected is double expectedDouble)
        {
            double actualDouble = ToDouble(actual, "key");
            return double.IsNaN(expectedDouble)
                ? double.IsNaN(actualDouble)
                : Math.Abs(actualDouble - expectedDouble) < 1e-9;
        }

        if (expected is long or int or short or byte)
        {
            return ToInt64(actual, "key") == Convert.ToInt64(expected, CultureInfo.InvariantCulture);
        }

        return Equals(actual, expected);
    }

    private static long ToInt64(object value, string context)
    {
        return value switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double d when Math.Abs(d - Math.Truncate(d)) < 1e-9 => (long)d,
            decimal m when m == Math.Truncate(m) => (long)m,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) => parsed,
            _ => throw new AssertionException($"Expected integer value at {context}, got {value.GetType().Name} ('{value}').")
        };
    }

    private static double ToDouble(object value, string context)
    {
        return value switch
        {
            double d => d,
            float f => f,
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            decimal m => (double)m,
            string s when string.Equals(s, "NaN", StringComparison.OrdinalIgnoreCase) => double.NaN,
            string s when string.Equals(s, "Infinity", StringComparison.OrdinalIgnoreCase) => double.PositiveInfinity,
            string s when string.Equals(s, "-Infinity", StringComparison.OrdinalIgnoreCase) => double.NegativeInfinity,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => throw new AssertionException($"Expected numeric value at {context}, got {value.GetType().Name} ('{value}').")
        };
    }

    private static decimal ToDecimal(object value, string context)
    {
        return value switch
        {
            decimal m => m,
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double d => Convert.ToDecimal(d, CultureInfo.InvariantCulture),
            string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
            _ => throw new AssertionException($"Expected decimal value at {context}, got {value.GetType().Name} ('{value}').")
        };
    }

    private static DateTime ToUtcDateTime(object value, string context)
    {
        DateTime dt = value switch
        {
            DateTime d => d,
            DateTimeOffset dto => dto.UtcDateTime,
            string s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            _ => throw new AssertionException($"Expected DateTime value at {context}, got {value.GetType().Name} ('{value}').")
        };

        return NormalizeUtc(dt);
    }

    private static DateTime NormalizeUtc(DateTime dt)
    {
        return dt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : dt.ToUniversalTime();
    }

    private static TimeSpan ToTimeSpan(object value, string context)
    {
        return value switch
        {
            TimeSpan ts => ts,
            string s => TimeSpan.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new AssertionException($"Expected TimeSpan value at {context}, got {value.GetType().Name} ('{value}').")
        };
    }

    private static object? UnwrapValue(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement je => UnwrapJsonElement(je),
            JsonNode jn => UnwrapJsonNode(jn),
            _ => value
        };
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

            if (val.TryGetValue<decimal>(out decimal m))
            {
                return m;
            }
        }

        if (node is JsonArray arr)
        {
            return arr.Select(UnwrapJsonNode).ToList();
        }

        return node.ToJsonString();
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
