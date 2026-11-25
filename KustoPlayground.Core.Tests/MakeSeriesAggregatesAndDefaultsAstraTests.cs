using static KustoPlayground.Core.Tests.AstraTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

public partial class MakeSeriesAstraTests
{
    private static KustoDatabase Measurements(params (long X, long V)[] rows) =>
        CreateDatabase(TestUtils.GenerateTableWith2Columns(rows, "X", "V", "T"));

    private static KustoDatabase PairedMeasurements() =>
        CreateDatabase(TestUtils.GenerateTableWith4Columns<long, long, long, bool>(
            [(0, 1, 6, true), (0, 4, 3, false), (0, 7, 0, true),
                (2, 10, 2, true), (2, 10, 2, false), (2, 10, 2, true)],
            "X", "V", "W", "Keep", "T"));

    [Test]
    public void EachAggregateFillsOnlyItsOwnGapsAndRetainsAnObservedZero()
    {
        var row = ExecuteAndGetSingleRow(Measurements((1, -2), (1, 5), (3, 0)), """
            T | make-series N=count(), Total=sum(V) default=99, Mean=avg(V) default=double(null),
                            Low=min(V) default=-11, High=max(V) default=42
                on X from 0 to 5 step 1
            """);
        AssertCounts(row, "[0,1,2,3,4]", "[0,2,0,1,0]");
        AssertJsonValue(row["Total"], "[99,3,99,0,99]");
        AssertJsonValue(row["Mean"], "[null,1.5,null,0,null]");
        AssertJsonValue(row["Low"], "[-11,-2,-11,0,-11]");
        AssertJsonValue(row["High"], "[42,5,42,0,42]");
    }

    [Test]
    public void ImplicitFillIsZeroEvenForAverageAndExtrema()
    {
        var row = ExecuteAndGetSingleRow(Measurements((1, -7), (1, -4)), """
            T | make-series avg(V), min(V), max(V) on X from 0 to 3 step 1
            """);
        AssertJsonValue(row["avg_V"], "[0,-5.5,0]");
        AssertJsonValue(row["min_V"], "[0,-7,0]");
        AssertJsonValue(row["max_V"], "[0,-4,0]");
    }

    [Test]
    public void NullAxisIsIgnoredButAnOccupiedBinOfNullMeasurementsIsNotAGap()
    {
        var db = CreateDatabase(OpusTestUtils.NewTable("T").WithColumn<long>("X").WithColumn<long>("V")
            .WithRow(null, 400L).WithRow(0L, null).WithRow(0L, 5L).WithRow(1L, null).WithRow(3L, -1L)
            .Build());
        var row = ExecuteAndGetSingleRow(db, """
            T | make-series N=count(), Known=countif(isnotnull(V)) default=7,
                            Total=sum(V) default=9, Low=min(V) default=long(null), FilledLow=min(V) default=-11
                on X from 0 to 4 step 1
            """);
        AssertCounts(row, "[0,1,2,3]", "[2,1,0,1]");
        AssertJsonValue(row["Known"], "[1,0,7,1]");
        AssertJsonValue(row["Total"], "[5,0,9,-1]");
        AssertJsonValue(row["Low"], "[5,null,null,-1]");
        AssertJsonValue(row["FilledLow"], "[5,-11,-11,-1]");
    }

    [Test]
    public void FalseAndNullPredicatesGiveAnOccupiedBinZeroMatchesRatherThanItsFillValue()
    {
        var db = CreateDatabase(OpusTestUtils.NewTable("T")
            .WithColumn<long>("X").WithColumn<long>("V").WithColumn<bool>("Keep")
            .WithRow(0L, 20L, false).WithRow(0L, 80L, null)
            .WithRow(2L, null, true).WithRow(2L, 5L, true).WithRow(2L, 30L, false).Build());
        var row = ExecuteAndGetSingleRow(db, """
            T | make-series Hits=countif(Keep) default=-1, Total=sumif(V, Keep) default=-2
                on X from 0 to 3 step 1
            """);
        AssertJsonValue(row["Hits"], "[0,-1,2]");
        AssertJsonValue(row["Total"], "[0,-2,5]");
    }

    [Test]
    public void ConditionalExtremaMeanAndDistinctCountApplyTheirPredicateInsideEachBin()
    {
        var row = ExecuteAndGetSingleRow(PairedMeasurements(), """
            T | make-series Lo=minif(V, not(Keep)), Hi=maxif(V, not(Keep)), Mean=avgif(W, V > 3),
                            Unique=dcountif(V, Keep)
                on X from 0 to 3 step 1
            """);
        AssertJsonValue(row["Lo"], "[4,0,10]");
        AssertJsonValue(row["Hi"], "[4,0,10]");
        AssertJsonValue(row["Mean"], "[1.5,0,2]");
        AssertJsonValue(row["Unique"], "[2,0,1]");
    }

    [Test]
    public void PercentileAndDistinctCountDoNotCombineSamplesFromDifferentBins()
    {
        var row = ExecuteAndGetSingleRow(PairedMeasurements(), """
            T | make-series Median=percentile(V, 50) default=-1, DistinctValues=dcount(V)
                on X from 0 to 3 step 1
            """);
        AssertJsonValue(row["Median"], "[4,-1,10]");
        AssertJsonValue(row["DistinctValues"], "[3,0,1]");
    }

    [Test]
    public void TakeAnyMayChooseOnlyFromTheCurrentBin()
    {
        var row = ExecuteAndGetSingleRow(PairedMeasurements(),
            "T | make-series Pick=take_any(V) default=-1 on X from 0 to 4 step 1");
        var picks = GetDynamicArray(row, "Pick");
        Assert.That(picks, Has.Count.EqualTo(4));
        Assert.That(picks[0], Is.AnyOf(1L, 4L, 7L));
        Assert.That(picks.Skip(1), Is.EqualTo(new[] { -1L, 10L, -1L }));
    }

    [TestCase("variance(V)", 9.0)]
    [TestCase("variancep(V)", 6.0)]
    [TestCase("stdev(V)", 3.0)]
    [TestCase("varianceif(V, Keep)", 18.0)]
    [TestCase("variancepif(V, Keep)", 9.0)]
    [TestCase("covariance(V, W)", -9.0)]
    [TestCase("covariancep(V, W)", -6.0)]
    [TestCase("covarianceif(V, W, Keep)", -18.0)]
    [TestCase("covariancepif(V, W, Keep)", -9.0)]
    public void StatisticalAggregatesDistinguishVaryingSamplesAGapAndConstantSamples(
        string aggregate, double firstBin)
    {
        var row = ExecuteAndGetSingleRow(PairedMeasurements(),
            $"T | make-series S={aggregate} default=-1.0 on X from 0 to 3 step 1");
        Assert.That(GetDynamicArray(row, "S"), Is.EqualTo(new[] { firstBin, -1.0, 0.0 }));
    }

    [Test]
    public void RowExpressionsAggregateArithmeticAndConstantMarkersUseDifferentScopes()
    {
        var row = ExecuteAndGetSingleRow(Measurements((0, -3), (0, 7), (2, 5)), """
            let fillValue = -11;
            T | make-series Total=sum(V * 2), Spread=max(V) - min(V) default=fillValue,
                            Marker=-1 default=-2 on X from 0 to 3 step 1
            """);
        AssertJsonValue(row["Total"], "[8,0,10]");
        AssertJsonValue(row["Spread"], "[10,-11,0]");
        AssertJsonValue(row["Marker"], "[-1,-2,-1]");
    }

    [Test]
    public void LongAggregateDoesNotLoseLowBitsWhenPlacedInADynamicArray()
    {
        var row = ExecuteAndGetSingleRow(Measurements((0, 9_007_199_254_740_992L), (0, 1), (2, -3)), """
            T | make-series Total=sum(V) default=long(null) on X from 0 to 3 step 1
            """);
        AssertJsonValue(row["Total"], "[9007199254740993,null,-3]");
    }
}
