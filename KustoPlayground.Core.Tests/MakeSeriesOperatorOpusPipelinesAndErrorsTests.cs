using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;
using static KustoPlayground.Core.Tests.MakeSeriesOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// make-series - the user scenario, make-series in a pipeline and invalid queries,
/// https://learn.microsoft.com/en-us/kusto/query/make-series-operator.
/// <list type="bullet">
/// <item>the result of make-series is a table like any other: it can be filtered, sorted, counted, joined, assigned to
/// a let and piped into the next operator, and the input of make-series can be any tabular expression (where, union,
/// extend, summarize or the mv-expand of another make-series);</item>
/// <item>mv-expand with types (Timestamp to typeof(datetime) ...) turns the series back into one typed row per bin,
/// the series of a row are expanded side by side; without the types the expanded values stay dynamic (a datetime, a
/// long and a real), so order by such a column is an error and the value has to be converted first
/// (todatetime() ...);</item>
/// <item>the series are dynamic arrays: indexing, array_length(), array_sum(), array_slice() and mv-apply work on
/// them;</item>
/// <item>an invalid make-series is rejected: a missing on, step or aggregation, by before on, a zero or negative step,
/// a step or a range that doesn't match the axis type, an axis that is a string or an expression, an aggregation that
/// make-series doesn't support (make_list(), make_set(), take_any() of a string), an expression that isn't an
/// aggregation, a default that isn't a numeric constant, two columns with the same name, a dynamic by column,
/// kind=nonempty with by and unknown columns.</item>
/// </list>
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class MakeSeriesOperatorOpusPipelinesAndErrorsTests
{
    /// <summary>
    /// Expands the series of <see cref="MakeSeriesOperatorOpusTestUtils.EventsSeries"/> back into one typed row per
    /// event type and day.
    /// </summary>
    private const string TypedExpand =
        " | mv-expand Timestamp to typeof(datetime), EventCount to typeof(long), Revenue to typeof(real)";

    /// <summary>
    /// The beginning of the query of the user: the events in a datatable of a let statement and the make-series of the
    /// daily event count and revenue by event type (the same series as <see cref="MakeSeriesOperatorOpusTestUtils.EventsSeries"/>).
    /// </summary>
    private const string UserEventsSeries = """
        let Events = datatable(
            Timestamp:datetime,
            EventType:string,
            Amount:real
        )
        [
            datetime(2026-01-01 09:00:00), "view", 0.0,
            datetime(2026-01-01 10:00:00), "purchase", 120.0,
            datetime(2026-01-02 09:30:00), "purchase", 85.0,
            datetime(2026-01-02 12:00:00), "refund", 20.0,
            datetime(2026-01-03 14:00:00), "purchase", 200.0,
            datetime(2026-01-03 15:00:00), "view", 0.0,
            datetime(2026-01-04 08:00:00), "purchase", 150.0,
            datetime(2026-01-05 16:00:00), "purchase", 95.0,
            datetime(2026-01-05 17:00:00), "refund", 10.0,
            datetime(2026-01-06 13:00:00), "purchase", 175.0
        ];
        Events
        | make-series EventCount=count(), Revenue=sum(Amount)
            default=0
            on Timestamp
            from datetime(2026-01-01) to datetime(2026-01-07) step 1d
            by EventType
        """;

    /// <summary>
    /// Four daily bins 2026-01-01 .. 2026-01-04 over Readings, the range of the invalid queries.
    /// </summary>
    private const string ReadingsRange = "on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step 1d";

    /// <summary>
    /// The 18 rows of the user scenario after the typed mv-expand (Timestamp, EventType, EventCount, Revenue): every
    /// event type has a row for every day, a day without events of the type has the count 0 and the revenue 0.0.
    /// </summary>
    private static readonly (DateTime Timestamp, string EventType, long EventCount, double Revenue)[] DailyRows =
    {
        (Utc(2026, 1, 1), "view", 1L, 0.0),
        (Utc(2026, 1, 1), "purchase", 1L, 120.0),
        (Utc(2026, 1, 1), "refund", 0L, 0.0),
        (Utc(2026, 1, 2), "view", 0L, 0.0),
        (Utc(2026, 1, 2), "purchase", 1L, 85.0),
        (Utc(2026, 1, 2), "refund", 1L, 20.0),
        (Utc(2026, 1, 3), "view", 1L, 0.0),
        (Utc(2026, 1, 3), "purchase", 1L, 200.0),
        (Utc(2026, 1, 3), "refund", 0L, 0.0),
        (Utc(2026, 1, 4), "view", 0L, 0.0),
        (Utc(2026, 1, 4), "purchase", 1L, 150.0),
        (Utc(2026, 1, 4), "refund", 0L, 0.0),
        (Utc(2026, 1, 5), "view", 0L, 0.0),
        (Utc(2026, 1, 5), "purchase", 1L, 95.0),
        (Utc(2026, 1, 5), "refund", 1L, 10.0),
        (Utc(2026, 1, 6), "view", 0L, 0.0),
        (Utc(2026, 1, 6), "purchase", 1L, 175.0),
        (Utc(2026, 1, 6), "refund", 0L, 0.0)
    };

    // the user scenario

    [Test]
    [Description("make-series - the user scenario: one row per event type with the daily event count and revenue, default=0 fills the days without events")]
    public void MakeSeries_UserScenario_ReturnsTheDailySeriesOfEveryEventType()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(), EventsSeries, "EventType");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "view", "purchase", "refund" }));
        AssertSeries(series["view"], "EventCount", "[1,0,1,0,0,0]");
        AssertSeries(series["view"], "Revenue", "[0.0,0.0,0.0,0.0,0.0,0.0]");
        AssertSeries(series["purchase"], "EventCount", "[1,1,1,1,1,1]");
        AssertSeries(series["purchase"], "Revenue", "[120.0,85.0,200.0,150.0,95.0,175.0]");
        AssertSeries(series["refund"], "EventCount", "[0,1,0,0,1,0]");
        AssertSeries(series["refund"], "Revenue", "[0.0,20.0,0.0,0.0,10.0,0.0]");
        foreach (var row in series.Values)
        {
            SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "EventType", "EventCount", "Revenue", "Timestamp");
            AssertAxis(row, "Timestamp", Days(2026, 1, 1, 6));
        }
    }

    [Test]
    [Description("make-series | mv-expand to typeof() - the series are expanded back into one typed row per event type and day")]
    public void MakeSeries_TypedMvExpand_ReturnsOneRowPerEventTypeAndDay()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateMakeSeriesDatabase(),
            EventsSeries + TypedExpand + " | project Timestamp, EventType, EventCount, Revenue | order by Timestamp asc");

        AssertDailyRows(rows);
    }

    [Test]
    [Description("make-series | mv-expand to typeof() - the query of the user with the events in a datatable of a let statement")]
    public void MakeSeries_UserQueryWithTypedMvExpand_ReturnsOneRowPerEventTypeAndDay()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(new KustoDatabase(),
            UserEventsSeries + """

            | mv-expand Timestamp to typeof(datetime), EventCount to typeof(long), Revenue to typeof(real)
            | project Timestamp, EventType, EventCount, Revenue
            | order by Timestamp asc
            """);

        AssertDailyRows(rows);
    }

    [Test]
    [Description("make-series | mv-expand to typeof() - the expanded columns get the types of typeof()")]
    public void MakeSeries_TypedMvExpand_HasTheTypedSchema()
    {
        var schema = ExecuteAndGetSchema(CreateMakeSeriesDatabase(),
            EventsSeries + TypedExpand + " | project Timestamp, EventType, EventCount, Revenue | getschema");

        Assert.That(schema,
            Is.EqualTo(new[] { "Timestamp:datetime", "EventType:string", "EventCount:long", "Revenue:real" }));
    }

    [Test]
    [Description("make-series | mv-expand - without typeof() the expanded Timestamp is dynamic, so the order by of the original user query is rejected, like Azure Data Explorer does")]
    public void MakeSeries_UserQueryWithUntypedMvExpand_OrderByDynamicColumnFails()
    {
        AssertMakeSeriesFails(new KustoDatabase(),
            UserEventsSeries + """

            | mv-expand Timestamp, EventCount, Revenue
            | project Timestamp, EventType, EventCount, Revenue
            | order by Timestamp asc
            """);
    }

    [Test]
    [Description("make-series | mv-expand - without typeof() the expanded values keep their dynamic types (datetime, long, double) and can be converted and sorted")]
    public void MakeSeries_UntypedMvExpand_ValuesKeepTheirDynamicTypes()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateMakeSeriesDatabase(),
            EventsSeries + " | mv-expand Timestamp, EventCount, Revenue"
            + " | extend Ts = todatetime(Timestamp), TimestampType = gettype(Timestamp), EventCountType = gettype(EventCount), RevenueType = gettype(Revenue)"
            + " | order by Ts asc");

        Assert.That(rows, Has.Count.EqualTo(18));
        var timestamps = SummarizeOperatorOpusTestUtils.GetValues<DateTime>(rows, "Ts");
        Assert.That(timestamps, Is.Ordered);
        Assert.That(timestamps[0], Is.EqualTo(Utc(2026, 1, 1)));
        Assert.That(timestamps[^1], Is.EqualTo(Utc(2026, 1, 6)));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValues<string>(rows, "TimestampType"), Is.All.EqualTo("datetime"));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValues<string>(rows, "EventCountType"), Is.All.EqualTo("long"));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValues<string>(rows, "RevenueType"), Is.All.EqualTo("double"));
    }

    [Test]
    [Description("make-series | mv-expand | summarize - the expanded rows can be aggregated again, here the totals of every day")]
    public void MakeSeries_TypedMvExpandThenSummarize_ReturnsTheDailyTotals()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateMakeSeriesDatabase(),
            EventsSeries + TypedExpand
            + " | summarize TotalEvents=sum(EventCount), TotalRevenue=sum(Revenue) by Timestamp");

        var totals = rows.Select(row => (
                SummarizeOperatorOpusTestUtils.GetValue<DateTime>(row, "Timestamp"),
                SummarizeOperatorOpusTestUtils.GetValue<long>(row, "TotalEvents"),
                SummarizeOperatorOpusTestUtils.GetValue<double>(row, "TotalRevenue")))
            .ToList();
        Assert.That(totals, Is.EquivalentTo(new[]
        {
            (Utc(2026, 1, 1), 2L, 120.0),
            (Utc(2026, 1, 2), 2L, 105.0),
            (Utc(2026, 1, 3), 2L, 200.0),
            (Utc(2026, 1, 4), 1L, 150.0),
            (Utc(2026, 1, 5), 2L, 105.0),
            (Utc(2026, 1, 6), 1L, 175.0)
        }));
    }

    [Test]
    [Description("make-series | mv-expand with_itemindex - the index of a bin is its position in the series")]
    public void MakeSeries_MvExpandWithItemIndex_NumbersTheBins()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateMakeSeriesDatabase(),
            "Events | make-series Revenue=sum(Amount) default=0 on Timestamp from datetime(2026-01-01) to datetime(2026-01-07) step 1d by EventType"
            + " | where EventType == 'refund'"
            + " | mv-expand with_itemindex=Day Timestamp to typeof(datetime), Revenue to typeof(real)");

        var bins = rows.Select(row => (
                SummarizeOperatorOpusTestUtils.GetValue<long>(row, "Day"),
                SummarizeOperatorOpusTestUtils.GetValue<DateTime>(row, "Timestamp"),
                SummarizeOperatorOpusTestUtils.GetValue<double>(row, "Revenue")))
            .ToList();
        Assert.That(bins, Is.EquivalentTo(new[]
        {
            (0L, Utc(2026, 1, 1), 0.0),
            (1L, Utc(2026, 1, 2), 20.0),
            (2L, Utc(2026, 1, 3), 0.0),
            (3L, Utc(2026, 1, 4), 0.0),
            (4L, Utc(2026, 1, 5), 10.0),
            (5L, Utc(2026, 1, 6), 0.0)
        }));
    }

    [Test]
    [Description("make-series | mv-expand | where | count - the bins with at least one event")]
    public void MakeSeries_TypedMvExpandThenWhere_CountsTheNonEmptyBins()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            EventsSeries + " | mv-expand EventCount to typeof(long) | where EventCount > 0 | count");

        // view 2 + purchase 6 + refund 2
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<long>(row, "Count"), Is.EqualTo(10L));
    }

    // the series are dynamic arrays

    [Test]
    [Description("make-series | array_length() and array_sum() - the number of bins and the total of every series")]
    public void MakeSeries_ArrayLengthAndArraySum_ComputeTheBinsAndTheTotals()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            EventsSeries
            + " | project EventType, Days=array_length(Timestamp), TotalEvents=array_sum(EventCount), TotalRevenue=array_sum(Revenue)",
            "EventType");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "purchase", "refund", "view" }));
        foreach (var row in series.Values)
        {
            Assert.That(SummarizeOperatorOpusTestUtils.GetValue<long>(row, "Days"), Is.EqualTo(6L));
        }

        // array_sum() is a real
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(series["purchase"], "TotalEvents"), Is.EqualTo(6.0));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(series["purchase"], "TotalRevenue"), Is.EqualTo(825.0));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(series["refund"], "TotalEvents"), Is.EqualTo(2.0));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(series["refund"], "TotalRevenue"), Is.EqualTo(30.0));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(series["view"], "TotalEvents"), Is.EqualTo(2.0));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(series["view"], "TotalRevenue"), Is.EqualTo(0.0));
    }

    [Test]
    [Description("make-series | indexing - an element of a series is the value of a bin and an element of the axis is the start of the bin")]
    public void MakeSeries_Indexing_ReturnsTheValueOfABin()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            EventsSeries + " | where EventType == 'purchase'"
            + " | project First=todatetime(Timestamp[0]), Last=todatetime(Timestamp[5]), SecondDayRevenue=toreal(Revenue[1]), ThirdDayCount=tolong(EventCount[2])");

        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<DateTime>(row, "First"), Is.EqualTo(Utc(2026, 1, 1)));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<DateTime>(row, "Last"), Is.EqualTo(Utc(2026, 1, 6)));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(row, "SecondDayRevenue"), Is.EqualTo(85.0));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<long>(row, "ThirdDayCount"), Is.EqualTo(1L));
    }

    [Test]
    [Description("make-series | array_slice() - the first three bins of every series")]
    public void MakeSeries_ArraySlice_ReturnsTheFirstBins()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            EventsSeries + " | project EventType, FirstThree=array_slice(Revenue, 0, 2)", "EventType");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "purchase", "refund", "view" }));
        AssertSeries(series["purchase"], "FirstThree", "[120.0,85.0,200.0]");
        AssertSeries(series["refund"], "FirstThree", "[0.0,20.0,0.0]");
        AssertSeries(series["view"], "FirstThree", "[0.0,0.0,0.0]");
    }

    [Test]
    [Description("make-series | mv-apply - a subquery over the bins of every series, here the peak revenue of every event type")]
    public void MakeSeries_MvApply_AggregatesTheBinsOfEverySeries()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            EventsSeries + " | mv-apply r=Revenue to typeof(real) on (summarize Peak=max(r)) | project EventType, Peak",
            "EventType");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "purchase", "refund", "view" }));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(series["purchase"], "Peak"), Is.EqualTo(200.0));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(series["refund"], "Peak"), Is.EqualTo(20.0));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(series["view"], "Peak"), Is.EqualTo(0.0));
    }

    // operators after make-series

    [Test]
    [Description("make-series | where - the series can be filtered by a function of a series")]
    public void MakeSeries_WhereOnASeries_FiltersTheSeries()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            EventsSeries + " | where array_sum(EventCount) > 2", "EventType");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "purchase" }));
        AssertSeries(series["purchase"], "EventCount", "[1,1,1,1,1,1]");
    }

    [Test]
    [Description("make-series | order by - the series can be sorted by the group key")]
    public void MakeSeries_OrderByTheGroupKey_SortsTheSeries()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateMakeSeriesDatabase(), EventsSeries + " | order by EventType asc");

        Assert.That(SummarizeOperatorOpusTestUtils.GetValues<string>(rows, "EventType"),
            Is.EqualTo(new[] { "purchase", "refund", "view" }));
    }

    [Test]
    [Description("make-series | extend | top - the series with the highest total revenue")]
    public void MakeSeries_ExtendThenTop_ReturnsTheSeriesWithTheHighestTotal()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            EventsSeries + " | extend Total=array_sum(Revenue) | top 1 by Total desc");

        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<string>(row, "EventType"), Is.EqualTo("purchase"));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<double>(row, "Total"), Is.EqualTo(825.0));
        AssertSeries(row, "Revenue", "[120.0,85.0,200.0,150.0,95.0,175.0]");
    }

    [Test]
    [Description("make-series | count - make-series returns one row per group")]
    public void MakeSeries_Count_CountsTheGroups()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(), EventsSeries + " | count");

        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<long>(row, "Count"), Is.EqualTo(3L));
    }

    [Test]
    [Description("make-series | join - the series can be joined with another table on the group key")]
    public void MakeSeries_JoinOnTheGroupKey_EnrichesTheSeries()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            EventsSeries + " | join kind=inner EventCategories on EventType | project EventType, Category, Revenue",
            "EventType");

        // view has no category and signup has no events
        Assert.That(series.Keys, Is.EquivalentTo(new[] { "purchase", "refund" }));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<string>(series["purchase"], "Category"), Is.EqualTo("sales"));
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<string>(series["refund"], "Category"), Is.EqualTo("sales"));
        AssertSeries(series["purchase"], "Revenue", "[120.0,85.0,200.0,150.0,95.0,175.0]");
        AssertSeries(series["refund"], "Revenue", "[0.0,20.0,0.0,0.0,10.0,0.0]");
        foreach (var row in series.Values)
        {
            SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "EventType", "Category", "Revenue");
        }
    }

    [Test]
    [Description("let - the result of make-series can be assigned to a let and queried like a table")]
    public void MakeSeries_InALetStatement_CanBeQueriedLikeATable()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            $"let Series = {EventsSeries}; Series | where EventType == 'refund'", "EventType");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "refund" }));
        AssertSeries(series["refund"], "EventCount", "[0,1,0,0,1,0]");
        AssertSeries(series["refund"], "Revenue", "[0.0,20.0,0.0,0.0,10.0,0.0]");
        AssertAxis(series["refund"], "Timestamp", Days(2026, 1, 1, 6));
    }

    // the input of make-series

    [Test]
    [Description("where | make-series - only the rows that pass the filter are counted")]
    public void MakeSeries_AfterWhere_CountsOnlyTheFilteredRows()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Events | where EventType == 'purchase' and Amount > 100"
            + " | make-series Purchases=count() on Timestamp from datetime(2026-01-01) to datetime(2026-01-07) step 1d");

        // the purchases of 85.0 (2026-01-02) and 95.0 (2026-01-05) are filtered out
        AssertSeries(row, "Purchases", "[1,0,1,1,0,1]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 6));
    }

    [Test]
    [Description("union | make-series - the input can be a union of two filtered tables")]
    public void MakeSeries_OfAUnion_CountsTheRowsOfBothInputs()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "union (Events | where EventType == 'view'), (Events | where EventType == 'refund')"
            + " | make-series c=count() on Timestamp from datetime(2026-01-01) to datetime(2026-01-07) step 1d");

        AssertSeries(row, "c", "[1,1,1,0,1,0]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 6));
    }

    [Test]
    [Description("extend | make-series - the axis can be a column computed by extend and keeps its name")]
    public void MakeSeries_OnAnExtendedColumn_UsesItAsTheAxis()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Events | extend Day=bin(Timestamp, 1d)"
            + " | make-series c=count() on Day from datetime(2026-01-01) to datetime(2026-01-07) step 1d");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "c", "Day");
        AssertSeries(row, "c", "[2,2,2,1,2,1]");
        AssertAxis(row, "Day", Days(2026, 1, 1, 6));
    }

    [Test]
    [Description("summarize | make-series - the input can be the result of summarize and the axis a column of summarize")]
    public void MakeSeries_OfASummarize_SumsTheDailyAmounts()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Events | summarize Amount=sum(Amount) by EventType, Day=bin(Timestamp, 1d)"
            + " | make-series Revenue=sum(Amount) on Day from datetime(2026-01-01) to datetime(2026-01-07) step 1d");

        AssertSeries(row, "Revenue", "[120.0,105.0,200.0,150.0,105.0,175.0]");
        AssertAxis(row, "Day", Days(2026, 1, 1, 6));
    }

    [Test]
    [Description("make-series | mv-expand | make-series - the expanded daily rows are binned again into two-day bins")]
    public void MakeSeries_OfAnExpandedSeries_RebinsTheValues()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            EventsSeries + " | mv-expand Timestamp to typeof(datetime), EventCount to typeof(long)"
            + " | make-series Total=sum(EventCount) on Timestamp from datetime(2026-01-01) to datetime(2026-01-07) step 2d");

        AssertSeries(row, "Total", "[4,3,3]");
        AssertAxis(row, "Timestamp", Bins(Utc(2026, 1, 1), TimeSpan.FromDays(2), 3));
    }

    // invalid queries

    [TestCase("Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05)",
        Description = "step is missing")]
    [TestCase("Readings | make-series s=sum(V) step 1d",
        Description = "on is missing")]
    [TestCase("Readings | make-series on Timestamp step 1d",
        Description = "the aggregation is missing")]
    [TestCase("Readings | make-series s=sum(V) by K on Timestamp step 1d",
        Description = "by before on")]
    [TestCase("Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step 0d",
        Description = "a zero step")]
    [TestCase("Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step -1d",
        Description = "a negative step")]
    [TestCase("Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step 1",
        Description = "a datetime axis with a numeric step")]
    [TestCase("Readings | make-series s=sum(V) on Timestamp from 0 to 10 step 1d",
        Description = "a datetime axis with a numeric range")]
    [TestCase("Numbers | make-series s=sum(V) on X from datetime(2026-01-01) to datetime(2026-01-05) step 1",
        Description = "a long axis with a datetime range")]
    [TestCase("Numbers | make-series s=sum(V) on X from 0 to 10 step 1d",
        Description = "a long axis with a timespan step")]
    [TestCase("Readings | make-series s=sum(V) on K step 1",
        Description = "a string axis")]
    [TestCase("Readings | make-series s=sum(V) on Timestamp + 2h from datetime(2026-01-01) to datetime(2026-01-05) step 1d",
        Description = "an expression as the axis")]
    [TestCase("Readings | make-series s=make_list(V) " + ReadingsRange,
        Description = "make_list() isn't a make-series aggregation")]
    [TestCase("Readings | make-series s=make_set(V) " + ReadingsRange,
        Description = "make_set() isn't a make-series aggregation")]
    [TestCase("Readings | make-series s=take_any(K) " + ReadingsRange,
        Description = "take_any() of a string column")]
    [TestCase("Readings | make-series x=V " + ReadingsRange,
        Description = "an expression that isn't an aggregation")]
    [TestCase("Readings | make-series s=sum(V) default=V " + ReadingsRange,
        Description = "a default that isn't a constant")]
    [TestCase("Readings | make-series c=count() default='x' " + ReadingsRange,
        Description = "a string default")]
    [TestCase("Metrics | make-series Last=max(Timestamp) default=datetime(2000-01-01) on Timestamp from datetime(2026-01-01) to datetime(2026-01-04) step 1d",
        Description = "a datetime default")]
    [TestCase("Readings | make-series s=sum(V), s=count() " + ReadingsRange,
        Description = "two aggregations with the same name")]
    [TestCase("Readings | make-series K=sum(V) " + ReadingsRange + " by K",
        Description = "an aggregation named like a by column")]
    [TestCase("DynamicKeys | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-03) step 1d by D",
        Description = "a dynamic by column")]
    [TestCase("Readings | make-series kind=nonempty s=sum(V) " + ReadingsRange + " by K",
        Description = "kind=nonempty with by")]
    [TestCase("Readings | make-series s=sum(Missing) " + ReadingsRange,
        Description = "an unknown aggregated column")]
    [TestCase("Readings | make-series s=sum(V) on Missing step 1d",
        Description = "an unknown axis column")]
    [TestCase("Readings | make-series s=sum(V) " + ReadingsRange + " by Missing",
        Description = "an unknown by column")]
    [Description("make-series - an invalid make-series is rejected with an error that describes the problem")]
    public void MakeSeries_InvalidQuery_Fails(string query)
    {
        AssertMakeSeriesFails(CreateMakeSeriesDatabase(), query);
    }

    /// <summary>
    /// The rows must be the <see cref="DailyRows"/> ordered by Timestamp; the order of the event types of a day isn't
    /// defined.
    /// </summary>
    private static void AssertDailyRows(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        Assert.That(rows, Has.Count.EqualTo(DailyRows.Length));
        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(rows[0], "Timestamp", "EventType", "EventCount", "Revenue");

        var actual = rows.Select(row => (
                SummarizeOperatorOpusTestUtils.GetValue<DateTime>(row, "Timestamp"),
                SummarizeOperatorOpusTestUtils.GetValue<string>(row, "EventType"),
                SummarizeOperatorOpusTestUtils.GetValue<long>(row, "EventCount"),
                SummarizeOperatorOpusTestUtils.GetValue<double>(row, "Revenue")))
            .ToList();
        Assert.That(actual, Is.EquivalentTo(DailyRows));
        Assert.That(actual.Select(row => row.Item1), Is.Ordered, "the rows are ordered by Timestamp");
    }
}
