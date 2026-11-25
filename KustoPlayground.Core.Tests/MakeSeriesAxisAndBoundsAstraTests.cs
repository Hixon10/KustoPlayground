using static KustoPlayground.Core.Tests.AstraTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

public partial class MakeSeriesAstraTests
{
    private static KustoDatabase Points(params long[] points) =>
        CreateDatabase(TestUtils.GenerateTableWithColumn(points, "X", "T"));

    private static void AssertCounts(IReadOnlyDictionary<string, object?> row, string axis, string counts)
    {
        AssertJsonValue(row["X"], axis);
        AssertJsonValue(row["N"], counts);
    }

    [Test]
    public void MainSyntaxAnchorsBinsAtFromAndExcludesAnAlignedStop()
    {
        var db = Points(24, 3, 22, 7, 3, 8, 2, 12, 23, 18);
        var row = ExecuteAndGetSingleRow(db, "T | make-series N=count() on X from 3 to 23 step 5");
        AssertCounts(row, "[3,8,13,18]", "[3,2,0,2]");
    }

    [Test]
    public void NegativeStartCrossesZeroAndKeepsTheLastPartialBin()
    {
        var db = Points(-6, -5, -3, -2, 0, 1, 3, 4, 5, 6, 7);
        var row = ExecuteAndGetSingleRow(db, "T | make-series N=count() on X from -5 to 5 step 3");
        // The last bin starts below 5; points 5 and 6 still belong to that bin.
        AssertCounts(row, "[-5,-2,1,4]", "[2,2,2,3]");
    }

    [Test]
    public void StepWiderThanTheIntervalStillProducesOneBinAtFrom()
    {
        var row = ExecuteAndGetSingleRow(Points(1, 8, 8, 19),
            "T | make-series N=count() on X from 7 to 9 step 10");
        AssertCounts(row, "[7]", "[2]");
    }

    [Test]
    public void OneOccupiedBinPadsBothEndsOfAnExplicitAxis()
    {
        var row = ExecuteAndGetSingleRow(Points(4, 5),
            "T | make-series N=count() on X from 0 to 8 step 2");
        AssertCounts(row, "[0,2,4,6]", "[0,0,2,0]");
    }

    [Test]
    public void InferringBothBoundsFromOneOccupiedBinDoesNotAddAnotherBin()
    {
        var row = ExecuteAndGetSingleRow(Points(12, 12),
            "T | make-series N=count() on X step 3");
        AssertCounts(row, "[12]", "[2]");
    }

    [TestCase("step 2", "[0,2,4]", "[2,0,1]", "[6,8,10]", "[1,2,1]")]
    [TestCase("from 2 step 2", "[2,4]", "[0,1]", "[2,4,6,8,10]", "[0,0,1,2,1]")]
    // With no explicit from, Azure includes the bin at an aligned to.
    [TestCase("to 10 step 2", "[0,2,4,6,8,10]", "[2,0,1,0,0,0]", "[6,8,10]", "[1,2,1]")]
    public void OnlyOmittedBoundsAreInferredSeparatelyForEachGroup(
        string bounds, string earlyAxis, string earlyCounts, string lateAxis, string lateCounts)
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith2Columns<string, long>(
            [("early", 1), ("late", 8), ("early", 1), ("late", 7), ("late", 8), ("early", 4), ("late", 10)],
            "Site", "X", "T"));
        var rows = ExecuteAndGetRows(db, $"T | make-series N=count() on X {bounds} by Site");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertCounts(GetSingleRowWhere(rows, "Site", "early"), earlyAxis, earlyCounts);
        AssertCounts(GetSingleRowWhere(rows, "Site", "late"), lateAxis, lateCounts);
    }

    [TestCase("from 0 to 6 step 2", "[0,2,4]", "[2,1,0]")]
    [TestCase("in range(0, 6, 2)", "[0,2,4,6]", "[2,1,0,2]")]
    public void AlternateSyntaxIncludesAnAlignedStopThatMainSyntaxExcludes(
        string bounds, string axis, string counts)
    {
        var row = ExecuteAndGetSingleRow(Points(-1, 0, 1, 3, 6, 6, 8),
            $"T | make-series N=count() on X {bounds}");
        AssertCounts(row, axis, counts);
    }

    [Test]
    public void AlternateSyntaxOnAnUnalignedRangeUsesBinInsteadOfBinAtStart()
    {
        var db = Points(-1, 0, 2, 3, 4, 6, 9, 10, 14, 20, 21, 24, 25);
        var row = ExecuteAndGetSingleRow(db, "T | make-series N=count() on X in range(3, 20, 5)");
        AssertCounts(row, "[0,5,10,15,20]", "[4,2,2,0,3]");
    }

    [TestCase("from 2 to 2 step 1")]
    [TestCase("from 3 to 1 step 1")]
    public void EmptyOrReversedMainIntervalProducesNoSeries(string bounds)
    {
        Assert.That(ExecuteAndGetRows(Points(1, 2, 3), $"T | make-series N=count() on X {bounds}"), Is.Empty);
    }

    [Test]
    public void EqualLegacyEndpointsSelectOneBinRatherThanAnEmptyInterval()
    {
        var row = ExecuteAndGetSingleRow(Points(1, 2, 2, 3), "T | make-series N=count() on X in range(2, 2, 1)");
        AssertCounts(row, "[2]", "[2]");
    }

    [Test]
    public void IntAxisAcceptsLongBoundsAndStep()
    {
        var db = CreateDatabase(TestUtils.GenerateTableWithColumn<int>([-2, 0, 1, 4, 6], "X", "T"));
        var row = ExecuteAndGetSingleRow(db,
            "T | make-series N=count() on X from -2 to 6 step 2");
        AssertCounts(row, "[-2,0,2,4]", "[1,2,0,1]");
    }

    [Test]
    public void AdjacentLongAxisValuesAboveDoublePrecisionStayInDistinctBins()
    {
        const long start = 9_007_199_254_740_992L;
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<long, long>("X", "N",
            Points(start, start + 1, start + 1, start + 3), """
            T | make-series N=count() on X from 9007199254740992 to 9007199254740996 step 1
              | mv-expand X to typeof(long), N to typeof(long)
            """);
        Assert.That(rows, Is.EqualTo(new[] { (start, 1L), (start + 1, 2L), (start + 2, 0L), (start + 3, 1L) }));
    }

    [Test]
    public void FractionalRealBinsUseTheSpecifiedNegativeAnchor()
    {
        var db = CreateDatabase(TestUtils.GenerateTableWithColumn<double>(
            [-0.5, -0.375, -0.25, -0.125, 0, 0.375, 0.5, 0.625], "X", "T"));
        var row = ExecuteAndGetSingleRow(db,
            "T | make-series N=count() on X from -0.375 to 0.625 step 0.25");
        AssertCounts(row, "[-0.375,-0.125,0.125,0.375]", "[2,2,0,2]");
    }

    [Test]
    public void DecimalAxisAcceptsDecimalBoundsAndStep()
    {
        var db = CreateDatabase(TestUtils.GenerateTableWithColumn<decimal>(
            [0.125m, 0.25m, 0.5m, 0.625m], "X", "T"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<decimal, long>("X", "N", db, """
            T | make-series N=count() on X from decimal(0.125) to decimal(0.625) step decimal(0.125)
              | mv-expand X to typeof(decimal), N to typeof(long)
            """);
        Assert.That(rows, Is.EqualTo(new[] { (0.125m, 1L), (0.25m, 1L), (0.375m, 0L), (0.5m, 1L) }));
    }

    [Test]
    public void DatetimeAxisPreservesAnOffGridTickAcrossLeapDayMidnight()
    {
        var start = new DateTime(2024, 2, 29, 23, 59, 30, DateTimeKind.Utc).AddTicks(1);
        var db = CreateDatabase(TestUtils.GenerateTableWithColumn<DateTime>(
            [start.AddTicks(-1), start, start.AddSeconds(30).AddTicks(-1), start.AddSeconds(30),
                start.AddSeconds(90), start.AddMinutes(2).AddTicks(-1), start.AddMinutes(2)], "Stamp", "T"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, long>("Stamp", "N", db, """
            let start = datetime(2024-02-29T23:59:30.0000001Z);
            T | make-series N=count() on Stamp from start to (start + 2m) step 30s
              | mv-expand Stamp to typeof(datetime), N to typeof(long)
            """);
        Assert.That(rows, Is.EqualTo(new[]
            { (start, 2L), (start.AddSeconds(30), 1L), (start.AddSeconds(60), 0L), (start.AddSeconds(90), 2L) }));
    }

    [Test]
    public void TimespanAxisExpandsToDurationsMeasuredFromTheExplicitAnchor()
    {
        var seconds = new[] { -1, 15, 44, 45, 60, 105, 135 };
        var db = CreateDatabase(TestUtils.GenerateTableWithColumn(
            seconds.Select(s => TimeSpan.FromSeconds(s)), "Elapsed", "T"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<TimeSpan, long>("Elapsed", "N", db, """
            T | make-series N=count() on Elapsed from 15s to 135s step 30s
              | mv-expand Elapsed to typeof(timespan), N to typeof(long)
            """);
        Assert.That(rows, Is.EqualTo(new[]
        {
            (TimeSpan.FromSeconds(15), 2L), (TimeSpan.FromSeconds(45), 2L),
            (TimeSpan.FromSeconds(75), 0L), (TimeSpan.FromSeconds(105), 1L)
        }));
    }

    [Test]
    public void InferredTimespanAxisFloorsNegativeDurationsAndCrossesZero()
    {
        var db = CreateDatabase(TestUtils.GenerateTableWithColumn(
            new[] { -16, -15, 5, 35 }.Select(s => TimeSpan.FromSeconds(s)), "Elapsed", "T"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<TimeSpan, long>("Elapsed", "N", db, """
            T | make-series N=count() on Elapsed step 15s
              | mv-expand Elapsed to typeof(timespan), N to typeof(long)
            """);
        Assert.That(rows, Is.EqualTo(new[]
        {
            (TimeSpan.FromSeconds(-30), 1L), (TimeSpan.FromSeconds(-15), 1L), (TimeSpan.Zero, 1L),
            (TimeSpan.FromSeconds(15), 0L), (TimeSpan.FromSeconds(30), 1L)
        }));
    }
}
