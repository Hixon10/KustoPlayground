using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;
using static KustoPlayground.Core.Tests.MakeSeriesOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// make-series - the axis and the bins, https://learn.microsoft.com/en-us/kusto/query/make-series-operator.
/// <list type="bullet">
/// <item>the result has the by columns, one dynamic array per aggregation and the axis column, which keeps the name of
/// the on column and holds the start of every bin; every aggregation array has one value per bin and an empty bin gets
/// the default (0 of the aggregation type);</item>
/// <item>with from and to the bins are from, from + step ... as long as the bin starts before to: from is inclusive and
/// to is exclusive, but a row at or after to is still counted when it falls into the last bin (when the step doesn't
/// divide the range or from is not aligned); rows before from and rows with a null axis value are ignored;</item>
/// <item>without from the first bin is bin(min(axis), step), without to the last bin is the one containing
/// max(axis);</item>
/// <item>with to but without from the bins are aligned to to (to - k * step), and the bin starting at to is added only
/// when to is a multiple of the step (bin(to, step) == to);</item>
/// <item>the legacy syntax <c>in range(start, stop, step)</c> aligns start and stop with bin() and stop is
/// inclusive;</item>
/// <item>an empty range (from >= to) produces no rows;</item>
/// <item>the axis can be datetime, timespan, long, int or real; the bins of a long or int axis are longs;</item>
/// <item>with by and without from / to every group gets the range of its own rows, with from and to all the groups have
/// the same axis.</item>
/// </list>
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class MakeSeriesOperatorOpusAxisAndBinningTests
{
    // from and to

    [Test]
    [Description("make-series - from and to create one bin per step, rows before from, at to and with a null axis value are ignored")]
    public void MakeSeries_FromAndTo_CreatesOneBinPerStepAndIgnoresRowsOutsideTheRange()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series c=count(), s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step 1d");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "c", "s", "Timestamp");
        AssertSeries(row, "c", "[2,1,0,2]");
        AssertSeries(row, "s", "[3,4,0,136]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 4));
    }

    [TestCase("datetime(2026-01-05)", "[3,4,0,136]", 4,
        Description = "the row at to (2026-01-05 00:00, 32) is ignored")]
    [TestCase("datetime(2026-01-04)", "[3,4,0]", 3,
        Description = "to is exclusive, no bin starts at to")]
    [TestCase("datetime(2026-01-04 12:00)", "[3,4,0,136]", 4,
        Description = "the last bin starts before to and still counts its row after to (2026-01-04 18:00, 128)")]
    [Description("make-series - to is exclusive: the last bin is the last one that starts before to")]
    public void MakeSeries_FromAndTo_TheLastBinStartsBeforeTo(string to, string expectedSums, int expectedBinCount)
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to {to} step 1d");

        AssertSeries(row, "s", expectedSums);
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, expectedBinCount));
    }

    [Test]
    [Description("make-series - the bins start at from even when from is not a multiple of the step")]
    public void MakeSeries_FromNotAlignedToStep_BinsStartAtFrom()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01 06:00) to datetime(2026-01-05) step 1d");

        // [01 06:00, 02 06:00) 2, [02 06:00, 03 06:00) 4, [03 06:00, 04 06:00) 8,
        // [04 06:00, 05 06:00) 128 + 32: the row at to (2026-01-05 00:00) falls into the last bin
        AssertSeries(row, "s", "[2,4,8,160]");
        AssertAxis(row, "Timestamp", Bins(Utc(2026, 1, 1, 6), TimeSpan.FromDays(1), 4));
    }

    [Test]
    [Description("make-series - from and to can be expressions")]
    public void MakeSeries_FromAndToExpressions_AreEvaluated()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-05) - 4d to datetime(2026-01-05) step 1d");

        AssertSeries(row, "s", "[3,4,0,136]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 4));
    }

    [Test]
    [Description("make-series - from, to and step can use let variables")]
    public void MakeSeries_LetVariablesInFromToAndStep_AreUsed()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            """
            let start = datetime(2026-01-01);
            let stepSize = 24h;
            Readings | make-series s=sum(V) on Timestamp from start to start + 4d step stepSize
            """);

        AssertSeries(row, "s", "[3,4,0,136]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 4));
    }

    [TestCase("from datetime(2026-01-05) to datetime(2026-01-01)", Description = "from after to")]
    [TestCase("from datetime(2026-01-03) to datetime(2026-01-03)", Description = "from equal to to")]
    [Description("make-series - an empty range (from >= to) returns no rows")]
    public void MakeSeries_EmptyRange_ReturnsNoRows(string range)
    {
        AssertNoSeries(CreateMakeSeriesDatabase(), $"Readings | make-series s=sum(V) on Timestamp {range} step 1d");
    }

    // only from, only to or neither

    [Test]
    [Description("make-series - without to the last bin is the bin of the largest axis value")]
    public void MakeSeries_FromWithoutTo_TheLastBinContainsTheMaximum()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) step 1d");

        AssertSeries(row, "s", "[3,4,0,136,32]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 5));
    }

    [Test]
    [Description("make-series - without to the bins start at an unaligned from and the last bin contains the maximum")]
    public void MakeSeries_UnalignedFromWithoutTo_BinsStartAtFrom()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01 06:00) step 1d");

        AssertSeries(row, "s", "[2,4,8,160]");
        AssertAxis(row, "Timestamp", Bins(Utc(2026, 1, 1, 6), TimeSpan.FromDays(1), 4));
    }

    [Test]
    [Description("make-series - without from and to the axis runs from bin(min, step) to the bin of the maximum, null axis values are ignored")]
    public void MakeSeries_WithoutFromAndTo_TheAxisCoversAllRows()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp step 1d");

        AssertSeries(row, "s", "[16,3,4,0,136,32]");
        AssertAxis(row, "Timestamp", Days(2025, 12, 31, 6));
    }

    [TestCase("datetime(2026-01-04)", "[16,3,4,0,136]", "2025-12-31 00:00:00", 5,
        Description = "to is a multiple of the step: the bin starting at to is added and counts the rows of that day")]
    [TestCase("datetime(2026-01-03)", "[16,3,4,0]", "2025-12-31 00:00:00", 4,
        Description = "to is a multiple of the step: the bin starting at to is added even when it is empty")]
    [TestCase("datetime(2026-01-03 06:00)", "[17,2,4]", "2025-12-31 06:00:00", 3,
        Description = "the bins are aligned to an unaligned to and no bin starts at to")]
    [TestCase("datetime(2026-01-04 12:00)", "[17,6,0,8]", "2025-12-31 12:00:00", 4,
        Description = "the bins are aligned to an unaligned to and the rows after to are ignored")]
    [Description("make-series - with to and without from the bins are aligned to to and start at the bin of the minimum")]
    public void MakeSeries_ToWithoutFrom_BinsAreAlignedToTo(string to, string expectedSums, string expectedFirstBin,
        int expectedBinCount)
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Readings | make-series s=sum(V) on Timestamp to {to} step 1d");

        AssertSeries(row, "s", expectedSums);
        AssertAxis(row, "Timestamp",
            Bins(OpusTestUtils.ParseDateTime(expectedFirstBin), TimeSpan.FromDays(1), expectedBinCount));
    }

    // step

    [TestCase("1d", Description = "days")]
    [TestCase("24h", Description = "hours")]
    [TestCase("1440m", Description = "minutes")]
    [Description("make-series - the same step written in different units creates the same bins")]
    public void MakeSeries_StepInDifferentUnits_CreatesTheSameBins(string step)
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step {step}");

        AssertSeries(row, "s", "[3,4,0,136]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 4));
    }

    [Test]
    [Description("make-series - a step of 12 hours creates two bins per day")]
    public void MakeSeries_TwelveHourStep_CreatesTwoBinsPerDay()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series c=count(), s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-03) step 12h");

        AssertSeries(row, "c", "[1,1,1,0]");
        AssertSeries(row, "s", "[1,2,4,0]");
        AssertAxis(row, "Timestamp", Bins(Utc(2026, 1, 1), TimeSpan.FromHours(12), 4));
    }

    [Test]
    [Description("make-series - an hourly step creates 24 bins per day and every row is counted in its hour")]
    public void MakeSeries_HourlyStep_CreatesOneBinPerHour()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series c=count() on Timestamp from datetime(2026-01-01) to datetime(2026-01-03) step 1h");

        // 2026-01-01 00:00:00 is bin 0, 2026-01-01 23:59:59 is bin 23 and 2026-01-02 06:00 is bin 30
        var counts = Enumerable.Range(0, 48).Select(hour => hour is 0 or 23 or 30 ? 1 : 0);
        AssertSeries(row, "c", "[" + string.Join(",", counts) + "]");
        AssertAxis(row, "Timestamp", Bins(Utc(2026, 1, 1), TimeSpan.FromHours(1), 48));
    }

    [Test]
    [Description("make-series - a step longer than the range creates a single bin, which counts every row of [from, from + step)")]
    public void MakeSeries_StepLongerThanTheRange_CreatesSingleBin()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series c=count(), s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step 7d");

        // the row at to (2026-01-05 00:00, 32) is inside the only bin
        AssertSeries(row, "c", "[6]");
        AssertSeries(row, "s", "[175]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 1));
    }

    [Test]
    [Description("make-series - when the step doesn't divide the range, the last bin extends beyond to and counts its rows")]
    public void MakeSeries_StepNotDividingTheRange_TheLastBinCountsRowsAtAndAfterTo()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step 3d");

        // [01, 04) 1 + 2 + 4, [04, 07) 8 + 128 + 32
        AssertSeries(row, "s", "[7,168]");
        AssertAxis(row, "Timestamp", Bins(Utc(2026, 1, 1), TimeSpan.FromDays(3), 2));
    }

    [Test]
    [Description("make-series - the step can be a timespan expression")]
    public void MakeSeries_StepExpression_IsEvaluated()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step 1d + 12h");

        // [01 00:00, 02 12:00) 1 + 2 + 4, [02 12:00, 04 00:00) nothing, [04 00:00, 05 12:00) 8 + 128 + 32
        AssertSeries(row, "s", "[7,0,168]");
        AssertAxis(row, "Timestamp", Bins(Utc(2026, 1, 1), TimeSpan.FromHours(36), 3));
    }

    // the rows of the input

    [Test]
    [Description("make-series - a single row without from and to creates a single bin")]
    public void MakeSeries_SingleRowWithoutRange_CreatesSingleBin()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | where V == 4 | make-series s=sum(V) on Timestamp step 1d");

        AssertSeries(row, "s", "[4]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 2, 1));
    }

    [Test]
    [Description("make-series - bins without rows at the end of the range get the default")]
    public void MakeSeries_NoRowsAtTheEndOfTheRange_TheTrailingBinsGetTheDefault()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | where V in (1, 2, 4) | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step 1d");

        AssertSeries(row, "s", "[3,4,0,0]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 4));
    }

    // in range (legacy syntax)

    [TestCase("datetime(2026-01-01 06:00)", Description = "an unaligned start is aligned down with bin()")]
    [TestCase("datetime(2026-01-01)", Description = "an aligned start")]
    [Description("make-series in range(start, stop, step) - the legacy syntax aligns start with bin() and includes the bin of stop")]
    public void MakeSeries_InRange_AlignsStartAndIncludesStop(string start)
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Readings | make-series s=sum(V) on Timestamp in range({start}, datetime(2026-01-04), 1d)");

        AssertSeries(row, "s", "[3,4,0,136]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 4));
    }

    // numeric and timespan axes

    [Test]
    [Description("make-series - a long axis creates long bins and ignores null axis values")]
    public void MakeSeries_LongAxis_CreatesLongBins()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Numbers | make-series s=sum(V) on X from 0 to 10 step 2");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "s", "X");
        AssertSeries(row, "s", "[1,2,4,0,8]");
        AssertSeries(row, "X", "[0,2,4,6,8]");
    }

    [Test]
    [Description("make-series - an int axis creates long bins")]
    public void MakeSeries_IntAxis_CreatesLongBins()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Numbers | make-series s=sum(V) on I from 0 to 10 step 2 | extend BinType = gettype(I[0])");

        AssertSeries(row, "s", "[1,2,4,0,8]");
        AssertSeries(row, "I", "[0,2,4,6,8]");
        Assert.That(Col(row, "BinType"), Is.EqualTo("long"));
    }

    [Test]
    [Description("make-series - a real axis creates real bins")]
    public void MakeSeries_RealAxis_CreatesRealBins()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Numbers | make-series s=sum(V) on R from 0.0 to 2.5 step 0.5");

        AssertSeries(row, "s", "[1,2,4,0,8]");
        AssertSeries(row, "R", "[0.0,0.5,1.0,1.5,2.0]");
    }

    [Test]
    [Description("make-series - without from and to a numeric axis starts at bin(min, step), also for a negative minimum")]
    public void MakeSeries_LongAxisWithoutRange_StartsAtTheBinOfTheMinimum()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Numbers | make-series s=sum(V) on X step 3");

        // [-3, 0) -1, [0, 3) 0, [3, 6) 3 and 4, [6, 9) nothing, [9, 12) 9 and 10
        AssertSeries(row, "s", "[32,1,6,0,24]");
        AssertSeries(row, "X", "[-3,0,3,6,9]");
    }

    [Test]
    [Description("make-series - the bins of a numeric axis start at an unaligned from and the last bin counts its row at to")]
    public void MakeSeries_LongAxisWithUnalignedFrom_BinsStartAtFrom()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Numbers | make-series s=sum(V) on X from 1 to 10 step 4");

        // [1, 5) 3 and 4, [5, 9) nothing, [9, 13) 9 and 10
        AssertSeries(row, "s", "[6,0,24]");
        AssertSeries(row, "X", "[1,5,9]");
    }

    [TestCase(1, 9, 4, "[0,4,8]", "[3,4,24]",
        Description = "start and stop are aligned down with bin(), stop is inclusive")]
    [TestCase(0, 10, 5, "[0,5,10]", "[7,8,16]",
        Description = "the bin starting at stop is included")]
    [Description("make-series in range(start, stop, step) - a long axis aligns start and stop with bin() and includes the bin of stop")]
    public void MakeSeries_LongAxisInRange_AlignsStartAndIncludesStop(int start, int stop, int step, string expectedBins,
        string expectedSums)
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Numbers | make-series s=sum(V) on X in range({start}, {stop}, {step})");

        AssertSeries(row, "s", expectedSums);
        AssertSeries(row, "X", expectedBins);
    }

    [Test]
    [Description("make-series - a timespan axis creates timespan bins")]
    public void MakeSeries_TimespanAxis_CreatesTimespanBins()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Durations | make-series s=sum(V) on S from 1h to 9h step 2h");

        // [1h, 3h) 1h, [3h, 5h) 3h and 4h, the row at to (9h) is ignored
        AssertSeries(row, "s", "[1,6,0,0]");
        AssertSeries(row, "S", """["01:00:00","03:00:00","05:00:00","07:00:00"]""");
    }

    // by: the range of every group

    [Test]
    [Description("make-series - with by and without from and to every group gets the range of its own rows")]
    public void MakeSeries_ByWithoutRange_EveryGroupGetsItsOwnAxis()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp step 1d by K", "K");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        AssertSeries(series["a"], "s", "[3,0,0,128,32]");
        AssertAxis(series["a"], "Timestamp", Days(2026, 1, 1, 5));
        AssertSeries(series["b"], "s", "[16,0,4,0,8]");
        AssertAxis(series["b"], "Timestamp", Days(2025, 12, 31, 5));
    }

    [Test]
    [Description("make-series - with by and only from every group ends at the bin of its own maximum")]
    public void MakeSeries_ByWithFromOnly_EveryGroupEndsAtItsOwnMaximum()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) step 1d by K", "K");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        AssertSeries(series["a"], "s", "[3,0,0,128,32]");
        AssertAxis(series["a"], "Timestamp", Days(2026, 1, 1, 5));
        AssertSeries(series["b"], "s", "[0,4,0,8]");
        AssertAxis(series["b"], "Timestamp", Days(2026, 1, 1, 4));
    }

    [Test]
    [Description("make-series - with by and only to every group starts at the bin of its own minimum")]
    public void MakeSeries_ByWithToOnly_EveryGroupStartsAtItsOwnMinimum()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp to datetime(2026-01-03) step 1d by K", "K");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        AssertSeries(series["a"], "s", "[3,0,0]");
        AssertAxis(series["a"], "Timestamp", Days(2026, 1, 1, 3));
        AssertSeries(series["b"], "s", "[16,0,4,0]");
        AssertAxis(series["b"], "Timestamp", Days(2025, 12, 31, 4));
    }

    [Test]
    [Description("make-series - with by, from and to every group has the same axis")]
    public void MakeSeries_ByWithFromAndTo_EveryGroupHasTheSameAxis()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            "Readings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step 1d by K",
            "K");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        AssertSeries(series["a"], "s", "[3,0,0,128]");
        AssertSeries(series["b"], "s", "[0,4,0,8]");
        foreach (var row in series.Values)
        {
            SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "K", "s", "Timestamp");
            AssertAxis(row, "Timestamp", Days(2026, 1, 1, 4));
        }
    }
}
