using System.Globalization;
using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Shared scenario tables and helpers for the make-series operator tests (MakeSeriesOperatorOpus*Tests).
/// <list type="bullet">
/// <item>every table is built with <see cref="OpusTableBuilder"/>, so the input rows are shuffled and a test cannot
/// depend on the input order; all the tables are registered by <see cref="CreateMakeSeriesDatabase"/>;</item>
/// <item>an aggregation series is a dynamic array and is compared with <see cref="AssertSeries"/> as JSON: numbers
/// without '.' are longs and numbers with '.' are reals, so <c>[3,4,0]</c> doesn't match <c>[3.0,4.0,0.0]</c> and the
/// element type of the series is checked too;</item>
/// <item>a datetime axis is compared with <see cref="AssertAxis"/> against UTC <see cref="DateTime"/> values
/// (<see cref="Days"/>, <see cref="Bins"/>), because the axis holds datetime values, not strings;</item>
/// <item>make-series doesn't define the order of the series of different groups, so the rows of a grouped make-series
/// are looked up by their group key (<see cref="ExecuteAndGetSeriesByKey"/>);</item>
/// <item>an invalid query must be rejected with an error that describes the problem, the generic
/// "Unsupported operator" error doesn't count (<see cref="AssertMakeSeriesFails"/>).</item>
/// </list>
/// </summary>
internal static class MakeSeriesOperatorOpusTestUtils
{
    /// <summary>
    /// The make-series of the user scenario: the daily event count and revenue of every event type for the days
    /// 2026-01-01 .. 2026-01-06 (to is exclusive) over <see cref="BuildEventsTable"/>.
    /// </summary>
    internal const string EventsSeries =
        "Events | make-series EventCount=count(), Revenue=sum(Amount) default=0 on Timestamp "
        + "from datetime(2026-01-01) to datetime(2026-01-07) step 1d by EventType";

    /// <summary>
    /// The value of an empty bin of a datetime aggregation (max(Timestamp) ...) without an explicit default:
    /// the zero datetime 0001-01-01, not null.
    /// </summary>
    internal static readonly DateTime ZeroDateTime = Utc(1, 1, 1);

    internal static KustoDatabase CreateMakeSeriesDatabase()
    {
        return SummarizeOperatorOpusTestUtils.CreateDatabase(
            BuildEventsTable(),
            BuildEventCategoriesTable(),
            BuildReadingsTable(),
            BuildEmptyReadingsTable(),
            BuildNumbersTable(),
            BuildDurationsTable(),
            BuildMetricsTable(),
            BuildSensorsTable(),
            BuildMetersTable(),
            BuildFleetTable(),
            BuildPricesTable(),
            BuildDynamicKeysTable());
    }

    #region helpers

    /// <summary>
    /// The expected axis of <paramref name="count"/> bins: first, first + step, first + 2 * step ... (UTC datetimes).
    /// </summary>
    internal static object?[] Bins(DateTime first, TimeSpan step, int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => (object?)first.AddTicks(step.Ticks * index))
            .ToArray();
    }

    /// <summary>
    /// The expected axis of <paramref name="count"/> daily bins starting at midnight of the given day.
    /// </summary>
    internal static object?[] Days(int year, int month, int day, int count)
    {
        return Bins(Utc(year, month, day), TimeSpan.FromDays(1), count);
    }

    /// <summary>
    /// Executes a make-series without by (or with a single group) and returns its only row.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> ExecuteAndGetSeriesRow(KustoDatabase kustoDatabase,
        string query)
    {
        return SummarizeOperatorOpusTestUtils.ExecuteAndGetSingleRow(kustoDatabase, query);
    }

    /// <summary>
    /// Executes the query and maps every row to the text of its group key: the values of the key columns joined by '|',
    /// null is "null", a bool is "true" or "false" and the empty string stays "", e.g. "a|1" for K = 'a', N = 1 and
    /// "|null" for K = '', N = null. Every group must be returned exactly once.
    /// </summary>
    internal static Dictionary<string, IReadOnlyDictionary<string, object?>> ExecuteAndGetSeriesByKey(
        KustoDatabase kustoDatabase,
        string query,
        params string[] keyColumns)
    {
        var rowsByKey = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var row in OpusTestUtils.ExecuteAndGetRows(kustoDatabase, query))
        {
            var key = string.Join("|", keyColumns.Select(column => FormatKey(Col(row, column))));
            Assert.That(rowsByKey.ContainsKey(key), Is.False, $"the group '{key}' is expected exactly once");
            rowsByKey[key] = row;
        }

        return rowsByKey;
    }

    /// <summary>
    /// Compares a series (a dynamic array column) with JSON text, see <see cref="DynamicOpusTestUtils.AssertDynamicJson"/>:
    /// "[3,4,0]" is an array of longs, "[2.0,null,0.5]" an array of reals and nulls.
    /// </summary>
    internal static void AssertSeries(IReadOnlyDictionary<string, object?> row, string column, string expectedJson)
    {
        AssertDynamicJson(Col(row, column), expectedJson, $"series '{column}'");
    }

    /// <summary>
    /// Compares the axis column (a dynamic array of datetime values) with the expected UTC datetimes.
    /// </summary>
    internal static void AssertAxis(IReadOnlyDictionary<string, object?> row, string column, object?[] expected)
    {
        AssertDynamic(Col(row, column), expected, $"axis '{column}'");
    }

    /// <summary>
    /// The query must succeed and return no rows.
    /// </summary>
    internal static void AssertNoSeries(KustoDatabase kustoDatabase, string query)
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, query);
        Assert.That(rows, Is.Empty, "make-series is expected to return no rows");
    }

    /// <summary>
    /// The query must be rejected with at least one error, and the error must not be the generic
    /// "Unsupported operator" error of an operator that isn't implemented yet.
    /// </summary>
    internal static void AssertMakeSeriesFails(KustoDatabase kustoDatabase, string query)
    {
        var result = kustoDatabase.ExecuteQuery(query);
        Assert.That(result.ResultRows, Is.Null, $"the query is expected to fail: {query}");
        Assert.That(result.ExecutionErrors, Is.Not.Null.And.Not.Empty, "the query is expected to report an error");

        var descriptions = result.ExecutionErrors!.Select(error => error.Description ?? string.Empty).ToList();
        Assert.That(descriptions.Any(description => description.Contains("Unsupported operator", StringComparison.Ordinal)),
            Is.False,
            $"the error must describe the invalid query, but was: {string.Join("; ", descriptions)}");
    }

    private static string FormatKey(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    #endregion

    #region scenario tables

    /// <summary>
    /// Events(Timestamp: datetime, EventType: string, Amount: real) - the user scenario, all in 2026-01:
    /// 01 09:00 view 0.0, 01 10:00 purchase 120.0, 02 09:30 purchase 85.0, 02 12:00 refund 20.0,
    /// 03 14:00 purchase 200.0, 03 15:00 view 0.0, 04 08:00 purchase 150.0, 05 16:00 purchase 95.0,
    /// 05 17:00 refund 10.0, 06 13:00 purchase 175.0.
    /// </summary>
    internal static Table BuildEventsTable()
    {
        return OpusTestUtils.NewTable("Events")
            .WithColumn<DateTime>("Timestamp")
            .WithColumn<string>("EventType")
            .WithColumn<double>("Amount")
            .WithRow(Utc(2026, 1, 1, 9), "view", 0.0)
            .WithRow(Utc(2026, 1, 1, 10), "purchase", 120.0)
            .WithRow(Utc(2026, 1, 2, 9, 30), "purchase", 85.0)
            .WithRow(Utc(2026, 1, 2, 12), "refund", 20.0)
            .WithRow(Utc(2026, 1, 3, 14), "purchase", 200.0)
            .WithRow(Utc(2026, 1, 3, 15), "view", 0.0)
            .WithRow(Utc(2026, 1, 4, 8), "purchase", 150.0)
            .WithRow(Utc(2026, 1, 5, 16), "purchase", 95.0)
            .WithRow(Utc(2026, 1, 5, 17), "refund", 10.0)
            .WithRow(Utc(2026, 1, 6, 13), "purchase", 175.0)
            .Build();
    }

    /// <summary>
    /// EventCategories(EventType: string, Category: string): purchase sales, refund sales, signup accounts.
    /// </summary>
    internal static Table BuildEventCategoriesTable()
    {
        return OpusTestUtils.NewTable("EventCategories")
            .WithColumn<string>("EventType")
            .WithColumn<string>("Category")
            .WithRow("purchase", "sales")
            .WithRow("refund", "sales")
            .WithRow("signup", "accounts")
            .Build();
    }

    /// <summary>
    /// Readings(Timestamp: datetime, K: string, V: long) - the values are powers of two, so every sum tells which rows
    /// fell into a bin: 2026-01-01 00:00:00 a 1, 2026-01-01 23:59:59 a 2, 2026-01-02 06:00 b 4, 2026-01-04 00:00 b 8,
    /// 2026-01-04 18:00 a 128, 2025-12-31 23:00 b 16, 2026-01-05 00:00 a 32 and a row with a null Timestamp b 64.
    /// </summary>
    internal static Table BuildReadingsTable()
    {
        return OpusTestUtils.NewTable("Readings")
            .WithColumn<DateTime>("Timestamp")
            .WithColumn<string>("K")
            .WithColumn<long>("V")
            .WithRow(Utc(2026, 1, 1), "a", 1L)
            .WithRow(Utc(2026, 1, 1, 23, 59, 59), "a", 2L)
            .WithRow(Utc(2026, 1, 2, 6), "b", 4L)
            .WithRow(Utc(2026, 1, 4), "b", 8L)
            .WithRow(Utc(2026, 1, 4, 18), "a", 128L)
            .WithRow(Utc(2025, 12, 31, 23), "b", 16L)
            .WithRow(Utc(2026, 1, 5), "a", 32L)
            .WithRow(null, "b", 64L)
            .Build();
    }

    /// <summary>
    /// EmptyReadings(Timestamp: datetime, K: string, V: long) without rows.
    /// </summary>
    internal static Table BuildEmptyReadingsTable()
    {
        return OpusTestUtils.NewTable("EmptyReadings")
            .WithColumn<DateTime>("Timestamp")
            .WithColumn<string>("K")
            .WithColumn<long>("V")
            .Build();
    }

    /// <summary>
    /// Numbers(X: long, I: int, R: real, V: long) - numeric axes, X and I hold the same values and R = X / 4:
    /// 0 0 0.0 1, 3 3 0.75 2, 4 4 1.0 4, 9 9 2.25 8, 10 10 2.5 16, -1 -1 -0.25 32, null null null 64.
    /// </summary>
    internal static Table BuildNumbersTable()
    {
        return OpusTestUtils.NewTable("Numbers")
            .WithColumn<long>("X")
            .WithColumn<int>("I")
            .WithColumn<double>("R")
            .WithColumn<long>("V")
            .WithRow(0L, 0, 0.0, 1L)
            .WithRow(3L, 3, 0.75, 2L)
            .WithRow(4L, 4, 1.0, 4L)
            .WithRow(9L, 9, 2.25, 8L)
            .WithRow(10L, 10, 2.5, 16L)
            .WithRow(-1L, -1, -0.25, 32L)
            .WithRow(null, null, null, 64L)
            .Build();
    }

    /// <summary>
    /// Durations(S: timespan, V: long) - a timespan axis: 1h 1, 3h 2, 4h 4, 9h 8.
    /// </summary>
    internal static Table BuildDurationsTable()
    {
        return OpusTestUtils.NewTable("Durations")
            .WithColumn<TimeSpan>("S")
            .WithColumn<long>("V")
            .WithRow(TimeSpan.FromHours(1), 1L)
            .WithRow(TimeSpan.FromHours(3), 2L)
            .WithRow(TimeSpan.FromHours(4), 4L)
            .WithRow(TimeSpan.FromHours(9), 8L)
            .Build();
    }

    /// <summary>
    /// Metrics(Timestamp: datetime, K: string, V: long, I: int, R: real) - the aggregation functions, three rows on
    /// 2026-01-01 and one on 2026-01-02: 01 01:00 a 1 10 1.5, 01 08:00 a 3 20 2.5, 01 20:00 b 8 60 4.0,
    /// 02 12:00 b 5 30 0.5.
    /// </summary>
    internal static Table BuildMetricsTable()
    {
        return OpusTestUtils.NewTable("Metrics")
            .WithColumn<DateTime>("Timestamp")
            .WithColumn<string>("K")
            .WithColumn<long>("V")
            .WithColumn<int>("I")
            .WithColumn<double>("R")
            .WithRow(Utc(2026, 1, 1, 1), "a", 1L, 10, 1.5)
            .WithRow(Utc(2026, 1, 1, 8), "a", 3L, 20, 2.5)
            .WithRow(Utc(2026, 1, 1, 20), "b", 8L, 60, 4.0)
            .WithRow(Utc(2026, 1, 2, 12), "b", 5L, 30, 0.5)
            .Build();
    }

    /// <summary>
    /// Sensors(Timestamp: datetime, V: long, R: real) - null values: 2026-01-01 01:00 1 1.5, 01 02:00 null null,
    /// 01 03:00 3 2.5, 02 01:00 null null, 04 01:00 7 3.0 (2026-01-02 has only null values, 2026-01-03 has no rows).
    /// </summary>
    internal static Table BuildSensorsTable()
    {
        return OpusTestUtils.NewTable("Sensors")
            .WithColumn<DateTime>("Timestamp")
            .WithColumn<long>("V")
            .WithColumn<double>("R")
            .WithRow(Utc(2026, 1, 1, 1), 1L, 1.5)
            .WithRow(Utc(2026, 1, 1, 2), null, null)
            .WithRow(Utc(2026, 1, 1, 3), 3L, 2.5)
            .WithRow(Utc(2026, 1, 2, 1), null, null)
            .WithRow(Utc(2026, 1, 4, 1), 7L, 3.0)
            .Build();
    }

    /// <summary>
    /// Meters(Timestamp: datetime, V: long, R: real) - defaults: 2026-01-01 01:00 1 1.5, 01 02:00 4 2.5,
    /// 02 01:00 null null, 02 02:00 null null (2026-01-02 has only null values, 2026-01-03 has no rows).
    /// </summary>
    internal static Table BuildMetersTable()
    {
        return OpusTestUtils.NewTable("Meters")
            .WithColumn<DateTime>("Timestamp")
            .WithColumn<long>("V")
            .WithColumn<double>("R")
            .WithRow(Utc(2026, 1, 1, 1), 1L, 1.5)
            .WithRow(Utc(2026, 1, 1, 2), 4L, 2.5)
            .WithRow(Utc(2026, 1, 2, 1), null, null)
            .WithRow(Utc(2026, 1, 2, 2), null, null)
            .Build();
    }

    /// <summary>
    /// Fleet(Timestamp: datetime, K: string, N: long, V: long) - grouping: 2026-01-01 10:00 a 1 1, 2026-01-02 10:00 a 1 2,
    /// 2026-01-04 10:00 b 2 4, 2026-01-05 10:00 b 2 8, 2026-01-03 10:00 '' null 16, 2026-01-03 11:00 A 3 32,
    /// 2025-12-01 10:00 old 4 64 (an empty key, a null key, keys that differ only in case and a group before the range).
    /// </summary>
    internal static Table BuildFleetTable()
    {
        return OpusTestUtils.NewTable("Fleet")
            .WithColumn<DateTime>("Timestamp")
            .WithColumn<string>("K")
            .WithColumn<long>("N")
            .WithColumn<long>("V")
            .WithRow(Utc(2026, 1, 1, 10), "a", 1L, 1L)
            .WithRow(Utc(2026, 1, 2, 10), "a", 1L, 2L)
            .WithRow(Utc(2026, 1, 4, 10), "b", 2L, 4L)
            .WithRow(Utc(2026, 1, 5, 10), "b", 2L, 8L)
            .WithRow(Utc(2026, 1, 3, 10), "", null, 16L)
            .WithRow(Utc(2026, 1, 3, 11), "A", 3L, 32L)
            .WithRow(Utc(2025, 12, 1, 10), "old", 4L, 64L)
            .Build();
    }

    /// <summary>
    /// Prices(Time: datetime, Value: long, Volume: long) - the default example of the documentation:
    /// 2025-01-07 10 11, 2025-01-16 20 21, 2025-02-01 30 5.
    /// </summary>
    internal static Table BuildPricesTable()
    {
        return OpusTestUtils.NewTable("Prices")
            .WithColumn<DateTime>("Time")
            .WithColumn<long>("Value")
            .WithColumn<long>("Volume")
            .WithRow(Utc(2025, 1, 7), 10L, 11L)
            .WithRow(Utc(2025, 1, 16), 20L, 21L)
            .WithRow(Utc(2025, 2, 1), 30L, 5L)
            .Build();
    }

    /// <summary>
    /// DynamicKeys(Timestamp: datetime, D: dynamic, V: long) - a dynamic column, which can't be a make-series group key:
    /// 2026-01-01 [1] 1.
    /// </summary>
    internal static Table BuildDynamicKeysTable()
    {
        return OpusTestUtils.NewTable("DynamicKeys")
            .WithColumn<DateTime>("Timestamp")
            .WithColumn<object>("D")
            .WithColumn<long>("V")
            .WithRow(Utc(2026, 1, 1), Arr(1L), 1L)
            .Build();
    }

    #endregion
}
