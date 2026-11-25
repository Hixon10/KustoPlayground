using NullableTable = KustoPlayground.Core.Tests.SummarizeOperatorOpusTableBuilder;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

public partial class NullableAstraTests
{
    [Test]
    public void UnknownComparisonFormsAGroupDistinctFromFalseAndTrue()
    {
        var actual = TestUtils.ExecuteAndGetDataFor4Columns<bool?, long, long, long>(
            "Equal", "Rows", "UniqueA", "Total", NumberPairs(), """
            T | extend Equal=A == B | summarize Rows=count(), UniqueA=dcount(A), Total=sum(A) by Equal
            """);
        Assert.That(actual, Is.EquivalentTo(new (bool?, long, long, long)[]
            { (null, 1, 0, 0), (false, 4, 3, 1), (true, 1, 1, 0) }));
    }

    [Test]
    public void ConditionalAggregatesTestEligibilitySeparatelyFromWhetherTheOperandIsNull()
    {
        var row = ExecuteAndGetSingleRow(NumberPairs(), """
            T | extend Eligible=A != B
              | summarize Qualified=countif(Eligible), Total=sumif(A, Eligible), Mean=avgif(B, Eligible)
            """);
        Assert.That(GetValue<long>(row, "Qualified"), Is.EqualTo(4L));
        Assert.That(GetValue<long>(row, "Total"), Is.EqualTo(1L));
        Assert.That(GetValue<double>(row, "Mean"), Is.EqualTo(2.0 / 3).Within(1e-12));
    }

    [Test]
    public void CollectionsHandleAnUnknownComparisonWithoutLosingItsPositionInListWithNulls()
    {
        var row = ExecuteAndGetSingleRow(NumberPairs(), """
            T | sort by Id asc | extend Equal=A == B
              | summarize Slots=make_list_with_nulls(Equal), Known=make_list(Equal), Kinds=make_set(Equal)
            """);
        Assert.That(GetDynamicArray(row, "Slots"), Is.EqualTo(new bool?[] { null, false, false, true, false, false }));
        Assert.That(GetDynamicArray(row, "Known"), Is.EqualTo(new[] { false, false, true, false, false }));
        Assert.That(GetDynamicArray(row, "Kinds"), Is.EquivalentTo(new[] { true, false }));
    }

    [TestCase("arg_min", -1L)]
    [TestCase("arg_max", 9L)]
    public void RowSelectorPreservesNullPayloadOnTheWinningRow(string selector, long rank)
    {
        var db = CreateDatabase(new NullableTable("T").WithColumn<long>("Score").WithColumn<long>("Payload")
            .WithRow(null, 777L).WithRow(-1L, null).WithRow(1L, 123L).WithRow(9L, null).Build());
        var row = ExecuteAndGetSingleRow(db, $"T | summarize {selector}(Score, Payload)");
        Assert.That(GetValue<long>(row, "Score"), Is.EqualTo(rank));
        AssertIsNull(row, "Payload");
    }

    // Kusto rounds the covariance to 10 decimal places: -16 / 3 is returned as -5.3333333333
    [TestCase("covariance", -8.0)]
    [TestCase("covariancep", -5.3333333333)]
    public void CovarianceDropsIncompletePairsRatherThanDroppingNullsFromEachSeriesIndependently(
        string aggregate, double expected)
    {
        var db = CreateDatabase(new NullableTable("T").WithColumn<long>("X").WithColumn<long>("Y")
            .WithRow(null, 100L).WithRow(500L, null).WithRow(null, null)
            .WithRow(1L, 8L).WithRow(3L, 4L).WithRow(5L, 0L).Build());
        var row = ExecuteAndGetSingleRow(db, $"T | summarize C={aggregate}(X, Y)");
        Assert.That(GetValue<double>(row, "C"), Is.EqualTo(expected).Within(1e-12));
    }

    [Test]
    public void MergingDistinctCountStatesIncludesValuesFromANullKeyGroupButNotNullValues()
    {
        var row = ExecuteAndGetSingleRow(NumberPairs(), """
            T | summarize H=hll(A) by B | summarize M=hll_merge(H) | project N=dcount_hll(M)
            """);
        Assert.That(GetValue<long>(row, "N"), Is.EqualTo(3L));
    }

    [Test]
    public void PercentileSeparatesAMissingDayBucketFromADayWithOnlyMissingMeasurements()
    {
        var day = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var db = CreateDatabase(new NullableTable("T").WithColumn<DateTime>("Stamp").WithColumn<long>("Duration")
            .WithRow(null, null).WithRow(null, 0L).WithRow(null, 0L)
            .WithRow(day, null).WithRow(day.AddHours(4), null)
            .WithRow(day.AddDays(1), 5L).WithRow(day.AddDays(1), null).Build());
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<DateTime?, long, long?>("Day", "Rows", "Median", db,
            "T | summarize Rows=count(), Median=percentile(Duration, 50) by Day=bin(Stamp, 1d)");
        Assert.That(actual, Is.EquivalentTo(new (DateTime?, long, long?)[]
            { (null, 3, 0), (day, 2, null), (day.AddDays(1), 2, 5) }));
    }
}
