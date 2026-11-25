namespace KustoPlayground.Core.Tests;

public partial class SummarizeAstraTests
{
    [TestCase("count()")]
    [TestCase("countif(Keep)")]
    [TestCase("count_distinct(Value)")]
    [TestCase("dcount(Value)")]
    [TestCase("dcountif(Value, Keep)")]
    [TestCase("sum(Value)")]
    [TestCase("sumif(Value, Keep)")]
    public void EmptyInputReturnsOneLongZero(string aggregate)
    {
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<long>("Result", NullableSamples(),
            $"T | summarize Result={aggregate}");
        Assert.That(actual, Is.EqualTo(new[] { 0L }));
    }

    [TestCase("variance(Value)")]
    [TestCase("varianceif(Value, Keep)")]
    [TestCase("stdev(Value)")]
    [TestCase("stdevif(Value, Keep)")]
    public void EmptyInputReturnsOneRealZeroForDispersion(string aggregate)
    {
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<double>("Result", NullableSamples(),
            $"T | summarize Result={aggregate}");
        Assert.That(actual, Is.EqualTo(new[] { 0.0 }));
    }

    [TestCase("min(Value)")]
    [TestCase("max(Value)")]
    [TestCase("percentile(Value, 55)")]
    [TestCase("take_any(Value)")]
    public void EmptyInputReturnsOneNullForValueAggregates(string aggregate)
    {
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<long?>("Result", Numbers(), $"T | summarize Result={aggregate}");
        Assert.That(actual, Is.EqualTo(new long?[] { null }));
    }

    [Test]
    public void AverageOfAnEmptyLongColumnIsNaN()
    {
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<double>("avg_Value", Numbers(), "T | summarize avg(Value)");
        Assert.That(actual, Has.Count.EqualTo(1));
        Assert.That(actual[0], Is.NaN);
    }

    [Test]
    public void CountIncludesNullRowsButSumAndAverageIgnoreNullOperands()
    {
        var db = NullableSamples((1, null, false), (1, 2, true), (2, null, null), (2, 5, true));
        var actual = TestUtils.ExecuteAndGetDataFor4Columns<long, long, long, double>("Rows", "Present", "Total", "Mean", db,
            "T | summarize Rows=count(), Present=countif(isnotnull(Value)), Total=sum(Value), Mean=avg(Value)");
        Assert.That(actual, Is.EqualTo(new[] { (4L, 2L, 7L, 3.5) }));
    }

    [Test]
    public void AllNullGroupHasZeroSumAndNullExtremaButIsNotDropped()
    {
        var db = NullableSamples((1, null, false), (2, -4, true), (1, null, true), (2, 6, null), (2, null, false));
        var actual = TestUtils.ExecuteAndGetDataFor4Columns<long, long, long?, long?>("Key", "Total", "Lo", "Hi", db,
            "T | summarize Total=sum(Value), Lo=min(Value), Hi=max(Value) by Key");
        Assert.That(actual, Is.EquivalentTo(new (long, long, long?, long?)[] { (1, 0, null, null), (2, 2, -4, 6) }));
    }

    [Test]
    public void IntSumWidensBeforeAdditionButExtremaKeepIntType()
    {
        var db = WithTable(TestUtils.GenerateTableWithColumn<int>([int.MaxValue, 1], "I", "T"));
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<long, int, int>("sum_I", "min_I", "max_I", db,
            "T | summarize sum(I), min(I), max(I)");
        Assert.That(actual, Is.EqualTo(new[] { (2_147_483_648L, 1, int.MaxValue) }));
    }

    [Test]
    public void LongSumDoesNotRoundThroughReal()
    {
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<long>("Total", Numbers(9_007_199_254_740_992L, 3),
            "T | summarize Total=sum(Value)");
        Assert.That(actual, Is.EqualTo(new[] { 9_007_199_254_740_995L }));
    }

    [Test]
    public void RealAndDecimalAggregatesPreserveTheirRespectiveTypes()
    {
        var db = WithTable(TestUtils.GenerateTableWith2Columns<double, decimal>(
            [(1.25, 0.1m), (2.5, 0.2m)], "Reading", "Price", "T"));
        var actual = TestUtils.ExecuteAndGetDataFor4Columns<double, double, decimal, decimal>(
            "RealSum", "RealAvg", "DecimalSum", "DecimalAvg", db,
            "T | summarize RealSum=sum(Reading), RealAvg=avg(Reading), DecimalSum=sum(Price), DecimalAvg=avg(Price)");
        Assert.That(actual, Is.EqualTo(new[] { (3.75, 1.875, 0.3m, 0.15m) }));
    }

    [Test]
    public void TimespanAggregatesReturnDurationsRatherThanNumericTicks()
    {
        var minute = TimeSpan.FromMinutes(1);
        var db = WithTable(TestUtils.GenerateTableWithColumn<TimeSpan>([-minute, minute, minute * 3], "Span", "T"));
        var actual = TestUtils.ExecuteAndGetDataFor4Columns<TimeSpan, TimeSpan, TimeSpan, TimeSpan>(
            "Lo", "Hi", "Total", "Mean", db,
            "T | summarize Lo=min(Span), Hi=max(Span), Total=sum(Span), Mean=avg(Span)");
        Assert.That(actual, Is.EqualTo(new[] { (-minute, minute * 3, minute * 3, minute) }));
    }

    [Test]
    public void DatetimeExtremaSelectTheFirstAndLastInstants()
    {
        var midnight = new DateTime(2025, 3, 5, 0, 0, 0, DateTimeKind.Utc);
        var db = WithTable(TestUtils.GenerateTableWithColumn<DateTime>(
            [midnight, midnight.AddTicks(1), midnight.AddTicks(-1)], "Stamp", "T"));
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, DateTime>("First", "Last", db,
            "T | summarize First=min(Stamp), Last=max(Stamp)");
        Assert.That(actual, Is.EqualTo(new[] { (midnight.AddTicks(-1), midnight.AddTicks(1)) }));
    }

    [Test]
    public void ExpressionsInsideAndOutsideAggregatesUseTheCorrectScope()
    {
        var db = WithTable(TestUtils.GenerateTableWith3Columns<string, long, long>(
            [("a", 10, 2), ("a", 6, 3), ("a", 10, 1), ("b", 4, 0), ("b", 2, 2)], "Key", "Price", "Qty", "T"));
        var actual = TestUtils.ExecuteAndGetDataFor4Columns<string, long, long, long>("Key", "Gross", "Spread", "Both", db, """
            T | summarize Gross=sum(Price * Qty), Spread=max(Price) - min(Price), Both=sum(Price) + sum(Qty) by Key
            """);
        Assert.That(actual, Is.EquivalentTo(new[] { ("a", 48L, 4L, 32L), ("b", 4L, 2L, 8L) }));
    }

    [Test]
    public void ConditionalCountCountsQualifyingRowsEvenWhenTheSummedValueIsNull()
    {
        var db = NullableSamples((1, 2, true), (1, 8, true), (1, null, true), (1, -100, false),
            (1, 999, null), (2, -9, false), (2, 77, null));
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<long, long, long>("Key", "Matching", "Total", db,
            "T | summarize Matching=countif(Keep), Total=sumif(Value, Keep) by Key");
        Assert.That(actual, Is.EquivalentTo(new[] { (1L, 3L, 10L), (2L, 0L, 0L) }));
    }

    [Test]
    public void ConditionalExtremaReturnNullForGroupsWithNoMatch()
    {
        var db = NullableSamples((1, 8, true), (1, 2, true), (1, null, true), (1, -90, false),
            (1, 99, null), (2, 10, false));
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<long, long?, long?>("Key", "Lo", "Hi", db,
            "T | summarize Lo=minif(Value, Keep), Hi=maxif(Value, Keep) by Key");
        Assert.That(actual, Is.EquivalentTo(new (long, long?, long?)[] { (1, 2, 8), (2, null, null) }));
    }

    [Test]
    public void ConditionalAverageCountsOnlyNonNullQualifyingValues()
    {
        var db = NullableSamples((1, 1, true), (1, null, true), (1, 4, true), (1, 60, false), (1, 85, null));
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<double>("Mean", db, "T | summarize Mean=avgif(Value, Keep)");
        Assert.That(actual, Is.EqualTo(new[] { 2.5 }));
    }

    [Test]
    public void RepeatedConditionalAggregatesDoNotShareTheirPredicates()
    {
        var db = NullableSamples((1, 4, true), (1, 8, true), (1, 7, false), (1, 99, null));
        var actual = TestUtils.ExecuteAndGetDataFor4Columns<long, long, long, long>("Yes", "No", "Included", "Excluded", db, """
            T | summarize Yes=countif(Keep), No=countif(not(Keep)),
                          Included=sumif(Value, Keep), Excluded=sumif(Value, not(Keep))
            """);
        Assert.That(actual, Is.EqualTo(new[] { (2L, 1L, 12L, 7L) }));
    }

    [TestCase("dcount(Value)", "dcountif(Value, Keep)")]
    [TestCase("count_distinct(Value)", "count_distinctif(Value, Keep)")]
    public void DistinctCountsHandleDuplicatesNullsAndPredicateIndependentlyPerGroup(string all, string matching)
    {
        var db = NullableSamples((1, 7, true), (1, 7, true), (1, 8, false), (1, 9, true), (1, null, true),
            (2, 7, false), (2, 7, true), (2, 8, null));
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<long, long, long>("Key", "AllValues", "Matching", db,
            $"T | summarize AllValues={all}, Matching={matching} by Key");
        Assert.That(actual, Is.EquivalentTo(new[] { (1L, 3L, 2L), (2L, 2L, 1L) }));
    }

    [Test]
    public void MergingPerGroupDistinctCountSketchesCountsSharedValuesOnlyOnce()
    {
        var db = GroupedNumbers(("a", 1), ("a", 2), ("a", 1), ("b", 2), ("b", 3));
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<long>("N", db, """
            T | summarize State=hll(Value, 2) by Key
              | summarize Combined=hll_merge(State)
              | project N=dcount_hll(Combined)
            """);
        Assert.That(actual, Is.EqualTo(new[] { 3L }));
    }

    [Test]
    public void SampleAndPopulationDispersionUseDifferentDenominatorsAndIgnoreNulls()
    {
        var db = NullableSamples((1, 1, true), (1, 3, true), (1, null, true), (1, 5, true));
        var actual = TestUtils.ExecuteAndGetDataFor4Columns<double, double, double, double>("SV", "PV", "SD", "PD", db,
            "T | summarize SV=variance(Value), PV=variancep(Value), SD=stdev(Value), PD=stdevp(Value)");
        Assert.That(actual, Has.Count.EqualTo(1));
        var (sampleVariance, populationVariance, sampleDeviation, populationDeviation) = actual[0];
        Assert.That(sampleVariance, Is.EqualTo(4.0).Within(1e-12));
        // Kusto rounds variance results to 10 decimal places; stdevp is the square root of that rounded variance.
        Assert.That(populationVariance, Is.EqualTo(2.6666666667).Within(1e-12));
        Assert.That(sampleDeviation, Is.EqualTo(2.0).Within(1e-12));
        Assert.That(populationDeviation, Is.EqualTo(Math.Sqrt(2.6666666667)).Within(1e-12));
    }

    [Test]
    public void ConditionalDispersionExcludesFalseNullAndMissingValues()
    {
        var db = NullableSamples((1, 2, true), (1, 4, true), (1, 6, true), (1, null, true),
            (1, 99, false), (1, -99, null));
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<double, double>("V", "S", db,
            "T | summarize V=varianceif(Value, Keep), S=stdevif(Value, Keep)");
        Assert.That(actual, Is.EqualTo(new[] { (4.0, 2.0) }));
    }

    [TestCase("percentiles(Value, 0, 100)", "percentile_Value_0", "percentile_Value_100")]
    [TestCase("(Low, High)=percentiles(Value, 0, 100)", "Low", "High")]
    public void MultiplePercentilesExpandIntoColumnsAlongsideOtherAggregates(string expression, string low, string high)
    {
        var db = NullableSamples((1, -3, true), (1, 6, true), (1, 15, true), (1, null, true), (2, 4, false));
        var actual = TestUtils.ExecuteAndGetDataFor4Columns<long, long, long, long>("Key", low, high, "N", db,
            $"T | summarize {expression}, N=count() by Key");
        Assert.That(actual, Is.EquivalentTo(new[] { (1L, -3L, 15L, 4L), (2L, 4L, 4L, 1L) }));
    }
}
