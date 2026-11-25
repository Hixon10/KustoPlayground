using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;
using static KustoPlayground.Core.Tests.MakeSeriesOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// make-series - by, the output schema and an input without rows,
/// https://learn.microsoft.com/en-us/kusto/query/make-series-operator.
/// <list type="bullet">
/// <item>with by every distinct combination of the group keys produces one row; the keys are compared exactly (case
/// sensitive), the empty string and null are keys of their own and a group without rows in [from, to) produces no
/// row; the order of the rows isn't defined;</item>
/// <item>a by column keeps its name and type, Name=expression is named Name, bin(column, size) keeps the name of the
/// column and other expressions are named Column1, Column2 ...;</item>
/// <item>the result columns are the by columns, the aggregations in the order of the query and the axis last; the
/// aggregations and the axis are dynamic;</item>
/// <item>an aggregation without a name is named like in summarize (count_, sum_V, percentile_V_50, sum_ for an
/// expression, I for take_any(I)), a repeated name gets the suffix 1, 2 ... and an aggregation named like the axis
/// is renamed (Timestamp1), the axis keeps its name;</item>
/// <item>without rows to aggregate (an empty input, no rows in [from, to) or only null axis values) make-series
/// returns no rows, but kind=nonempty returns a single row with the default of every aggregation in every bin of
/// [from, to).</item>
/// </list>
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class MakeSeriesOperatorOpusGroupingSchemaAndEmptyInputTests
{
    /// <summary>
    /// Five daily bins 2026-01-01 .. 2026-01-05 over Fleet; the row of the group 'old' (2025-12-01) is before from.
    /// </summary>
    private const string FleetAxis = "on Timestamp from datetime(2026-01-01) to datetime(2026-01-06) step 1d";

    /// <summary>
    /// Three daily bins over Metrics: 2026-01-01 (three rows), 2026-01-02 (one row) and 2026-01-03 (no rows).
    /// </summary>
    private const string MetricsAxis = "on Timestamp from datetime(2026-01-01) to datetime(2026-01-04) step 1d";

    // by

    [Test]
    [Description("make-series by - one row per distinct key: the keys are case sensitive, the empty string is a key and a group without rows in the range produces no row")]
    public void MakeSeries_ByStringColumn_ProducesOneRowPerDistinctKey()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            $"Fleet | make-series s=sum(V) {FleetAxis} by K", "K");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "", "A", "a", "b" }));
        AssertSeries(series["a"], "s", "[1,2,0,0,0]");
        AssertSeries(series["A"], "s", "[0,0,32,0,0]");
        AssertSeries(series["b"], "s", "[0,0,0,4,8]");
        AssertSeries(series[""], "s", "[0,0,16,0,0]");
        foreach (var row in series.Values)
        {
            SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "K", "s", "Timestamp");
            AssertAxis(row, "Timestamp", Days(2026, 1, 1, 5));
        }
    }

    [Test]
    [Description("make-series by - a null key is a group of its own and a long key stays a long")]
    public void MakeSeries_ByLongColumnWithNull_NullIsAGroup()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            $"Fleet | make-series s=sum(V) {FleetAxis} by N", "N");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "null", "1", "2", "3" }));
        AssertSeries(series["null"], "s", "[0,0,16,0,0]");
        AssertSeries(series["1"], "s", "[1,2,0,0,0]");
        AssertSeries(series["2"], "s", "[0,0,0,4,8]");
        AssertSeries(series["3"], "s", "[0,0,32,0,0]");
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<long>(series["2"], "N"), Is.EqualTo(2L));
        SummarizeOperatorOpusTestUtils.AssertIsNull(series["null"], "N");
    }

    [Test]
    [Description("make-series by - several keys produce one row per distinct combination")]
    public void MakeSeries_ByTwoColumns_ProducesOneRowPerCombination()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            $"Fleet | make-series s=sum(V) {FleetAxis} by K, N", "K", "N");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "|null", "A|3", "a|1", "b|2" }));
        AssertSeries(series["|null"], "s", "[0,0,16,0,0]");
        AssertSeries(series["A|3"], "s", "[0,0,32,0,0]");
        AssertSeries(series["a|1"], "s", "[1,2,0,0,0]");
        AssertSeries(series["b|2"], "s", "[0,0,0,4,8]");
        foreach (var row in series.Values)
        {
            SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "K", "N", "s", "Timestamp");
        }
    }

    [Test]
    [Description("make-series by - an expression without a name is named Column1 and merges the rows with the same value")]
    public void MakeSeries_ByExpression_IsNamedColumn1()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            $"Fleet | make-series s=sum(V) {FleetAxis} by tolower(K)", "Column1");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "", "a", "b" }));
        // 'a' and 'A' are one group
        AssertSeries(series["a"], "s", "[1,2,32,0,0]");
        AssertSeries(series["b"], "s", "[0,0,0,4,8]");
        AssertSeries(series[""], "s", "[0,0,16,0,0]");
        foreach (var row in series.Values)
        {
            SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "Column1", "s", "Timestamp");
        }
    }

    [Test]
    [Description("make-series by - Name=expression names the key column")]
    public void MakeSeries_ByNamedExpression_UsesTheName()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            $"Fleet | make-series s=sum(V) {FleetAxis} by Kind=K", "Kind");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "", "A", "a", "b" }));
        AssertSeries(series["a"], "s", "[1,2,0,0,0]");
        AssertSeries(series["A"], "s", "[0,0,32,0,0]");
        foreach (var row in series.Values)
        {
            SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "Kind", "s", "Timestamp");
        }
    }

    [Test]
    [Description("make-series by - a bool expression groups the rows into true, false and null")]
    public void MakeSeries_ByBoolExpression_GroupsByTrueFalseAndNull()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            $"Fleet | make-series s=sum(V) {FleetAxis} by Big = N > 1", "Big");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "false", "true", "null" }));
        AssertSeries(series["false"], "s", "[1,2,0,0,0]");
        AssertSeries(series["true"], "s", "[0,0,32,4,8]");
        // N > 1 is null for the null N
        AssertSeries(series["null"], "s", "[0,0,16,0,0]");
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<bool>(series["true"], "Big"), Is.True);
    }

    [Test]
    [Description("make-series by - bin(column, size) keeps the name of the column")]
    public void MakeSeries_ByBin_KeepsTheColumnName()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            $"Fleet | make-series s=sum(V) {FleetAxis} by bin(N, 2)", "N");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "0", "2", "null" }));
        AssertSeries(series["0"], "s", "[1,2,0,0,0]");
        // N = 2 and N = 3 are one group
        AssertSeries(series["2"], "s", "[0,0,32,4,8]");
        AssertSeries(series["null"], "s", "[0,0,16,0,0]");
        Assert.That(SummarizeOperatorOpusTestUtils.GetValue<long>(series["2"], "N"), Is.EqualTo(2L));
    }

    [Test]
    [Description("make-series by - without from and to no group is dropped and a group with a single row gets a single bin")]
    public void MakeSeries_ByWithoutRange_KeepsEveryGroup()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            "Fleet | make-series s=sum(V) on Timestamp step 1d by K", "K");

        // 'old' (2025-12-01) is kept, FleetAxis drops it
        Assert.That(series.Keys, Is.EquivalentTo(new[] { "", "A", "a", "b", "old" }));
        AssertSeries(series["old"], "s", "[64]");
        AssertAxis(series["old"], "Timestamp", Days(2025, 12, 1, 1));
        AssertSeries(series[""], "s", "[16]");
        AssertAxis(series[""], "Timestamp", Days(2026, 1, 3, 1));
        AssertSeries(series["A"], "s", "[32]");
        AssertAxis(series["A"], "Timestamp", Days(2026, 1, 3, 1));
    }

    [Test]
    [Description("make-series by - only the groups with rows in [from, to) produce a row")]
    public void MakeSeries_ByGroupsWithoutRowsInTheRange_ProduceNoRows()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            "Fleet | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-03) step 1d by K",
            "K");

        // to is exclusive: the rows of '' and 'A' (2026-01-03) and of 'b' (2026-01-04, 2026-01-05) are after the
        // range and the row of 'old' (2025-12-01) is before it
        Assert.That(series.Keys, Is.EquivalentTo(new[] { "a" }));
        AssertSeries(series["a"], "s", "[1,2]");
        AssertAxis(series["a"], "Timestamp", Days(2026, 1, 1, 2));
    }

    [Test]
    [Description("make-series by - every group has a series per aggregation and its empty bins get the default")]
    public void MakeSeries_BySeveralAggregations_EveryGroupHasItsOwnSeries()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            $"Metrics | make-series c=count(), a=avg(R), s=sum(V) {MetricsAxis} by K", "K");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        AssertSeries(series["a"], "c", "[2,0,0]");
        AssertSeries(series["a"], "a", "[2.0,0.0,0.0]");
        AssertSeries(series["a"], "s", "[4,0,0]");
        AssertSeries(series["b"], "c", "[1,1,0]");
        AssertSeries(series["b"], "a", "[4.0,0.5,0.0]");
        AssertSeries(series["b"], "s", "[8,5,0]");
        foreach (var row in series.Values)
        {
            SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "K", "c", "a", "s", "Timestamp");
            AssertAxis(row, "Timestamp", Days(2026, 1, 1, 3));
        }
    }

    [Test]
    [Description("make-series hint.shufflekey - the hint doesn't change the result")]
    public void MakeSeries_ShuffleKeyHint_DoesNotChangeTheResult()
    {
        var series = ExecuteAndGetSeriesByKey(CreateMakeSeriesDatabase(),
            "Readings | make-series hint.shufflekey=K s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-03) step 1d by K",
            "K");

        Assert.That(series.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        AssertSeries(series["a"], "s", "[3,0]");
        AssertSeries(series["b"], "s", "[0,4]");
    }

    // the output schema

    [TestCase("c=count(), s=sum(V) " + MetricsAxis + " by K",
        new[] { "K:string", "c:dynamic", "s:dynamic", "Timestamp:dynamic" },
        Description = "the by column, the aggregations and the axis")]
    [TestCase("c=count(), s=sum(V) " + MetricsAxis,
        new[] { "c:dynamic", "s:dynamic", "Timestamp:dynamic" },
        Description = "without by")]
    [TestCase("c=count() " + MetricsAxis + " by K, I",
        new[] { "K:string", "I:int", "c:dynamic", "Timestamp:dynamic" },
        Description = "the by columns keep their type")]
    [TestCase("c=count() " + MetricsAxis + " by R, Kind=K",
        new[] { "R:real", "Kind:string", "c:dynamic", "Timestamp:dynamic" },
        Description = "a real by column and a named by expression")]
    [TestCase("c=count() " + MetricsAxis + " by K, tolower(K), toupper(K)",
        new[] { "K:string", "Column1:string", "Column2:string", "c:dynamic", "Timestamp:dynamic" },
        Description = "by expressions without a name are numbered")]
    [TestCase("c=count() " + MetricsAxis + " by tolower(K), V",
        new[] { "Column1:string", "V:long", "c:dynamic", "Timestamp:dynamic" },
        Description = "an expression without a name before a column")]
    [TestCase("c=count() " + MetricsAxis + " by bin(V, 2)",
        new[] { "V:long", "c:dynamic", "Timestamp:dynamic" },
        Description = "bin() keeps the name of the column")]
    [TestCase("sum(V) " + MetricsAxis + " by V",
        new[] { "V:long", "sum_V:dynamic", "Timestamp:dynamic" },
        Description = "an aggregation of the by column")]
    [TestCase("sum(V * 2), max(V + 1), Flag=7 " + MetricsAxis + " by Kind=K",
        new[] { "Kind:string", "sum_:dynamic", "max_:dynamic", "Flag:dynamic", "Timestamp:dynamic" },
        Description = "aggregations of expressions and a constant")]
    [TestCase("s=sum(V) on I from 0 to 30 step 10",
        new[] { "s:dynamic", "I:dynamic" },
        Description = "an int axis keeps its name")]
    [TestCase("s=sum(V) on R from 0.0 to 5.0 step 1.0",
        new[] { "s:dynamic", "R:dynamic" },
        Description = "a real axis keeps its name")]
    [Description("make-series - the result columns are the by columns, the aggregations and the axis, the aggregations and the axis are dynamic")]
    public void MakeSeries_Schema_ByColumnsThenAggregationsThenAxis(string makeSeries, string[] expectedSchema)
    {
        var schema = ExecuteAndGetSchema(CreateMakeSeriesDatabase(), $"Metrics | make-series {makeSeries} | getschema");

        Assert.That(schema, Is.EqualTo(expectedSchema));
    }

    [TestCase("count()", "count_")]
    [TestCase("sum(V)", "sum_V")]
    [TestCase("sum(R)", "sum_R")]
    [TestCase("avg(V)", "avg_V")]
    [TestCase("min(I)", "min_I")]
    [TestCase("max(R)", "max_R")]
    [TestCase("dcount(K)", "dcount_K")]
    [TestCase("stdev(V)", "stdev_V")]
    [TestCase("variance(V)", "variance_V")]
    [TestCase("percentile(V, 50)", "percentile_V_50")]
    [TestCase("countif(V > 2)", "countif_")]
    [TestCase("sumif(R, V > 2)", "sumif_R")]
    [TestCase("avgif(V, V > 2)", "avgif_V")]
    [TestCase("minif(V, V > 2)", "minif_V")]
    [TestCase("maxif(V, V > 2)", "maxif_V")]
    [TestCase("dcountif(K, V > 2)", "dcountif_K")]
    [TestCase("sum(V * 2)", "sum_", Description = "the aggregation of an expression")]
    [TestCase("max(V + 1)", "max_", Description = "the aggregation of an expression")]
    [TestCase("take_any(I)", "I", Description = "take_any() keeps the name of the column")]
    [Description("make-series - an aggregation without a name gets the generated name of summarize")]
    public void MakeSeries_AggregationWithoutName_GetsTheGeneratedName(string aggregation, string expectedName)
    {
        var schema = ExecuteAndGetSchema(CreateMakeSeriesDatabase(),
            $"Metrics | make-series {aggregation} {MetricsAxis} | getschema");

        Assert.That(schema, Is.EqualTo(new[] { $"{expectedName}:dynamic", "Timestamp:dynamic" }));
    }

    [Test]
    [Description("make-series - repeated generated names get the suffix 1 and every aggregation keeps its own series")]
    public void MakeSeries_RepeatedAggregations_GetNumberedNames()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Metrics | make-series sum(V), sum(V), count(), count() {MetricsAxis}");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "sum_V", "sum_V1", "count_", "count_1", "Timestamp");
        AssertSeries(row, "sum_V", "[12,5,0]");
        AssertSeries(row, "sum_V1", "[12,5,0]");
        AssertSeries(row, "count_", "[3,1,0]");
        AssertSeries(row, "count_1", "[3,1,0]");
    }

    [Test]
    [Description("make-series - an aggregation named like the axis is renamed and the axis keeps its name")]
    public void MakeSeries_AggregationNamedLikeTheAxis_IsRenamed()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Metrics | make-series Timestamp=sum(V) {MetricsAxis}");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "Timestamp1", "Timestamp");
        AssertSeries(row, "Timestamp1", "[12,5,0]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 3));
    }

    [Test]
    [Description("make-series - an input without rows still has the schema of make-series")]
    public void MakeSeries_EmptyInput_HasTheSchema()
    {
        var schema = ExecuteAndGetSchema(CreateMakeSeriesDatabase(),
            "EmptyReadings | make-series s=sum(V) on Timestamp step 1d by K | getschema");

        Assert.That(schema, Is.EqualTo(new[] { "K:string", "s:dynamic", "Timestamp:dynamic" }));
    }

    // no rows to aggregate

    [TestCase("EmptyReadings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-04) step 1d",
        Description = "an empty table with from and to")]
    [TestCase("EmptyReadings | make-series s=sum(V) on Timestamp step 1d",
        Description = "an empty table without from and to")]
    [TestCase("EmptyReadings | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-04) step 1d by K",
        Description = "an empty table with by")]
    [TestCase("EmptyReadings | make-series s=sum(V) on Timestamp step 1d by K",
        Description = "an empty table with by and without from and to")]
    [TestCase("Readings | make-series s=sum(V) on Timestamp from datetime(2027-01-01) to datetime(2027-01-04) step 1d",
        Description = "no rows in [from, to)")]
    [TestCase("Readings | where isnull(Timestamp) | make-series s=sum(V) on Timestamp step 1d",
        Description = "only null axis values without from and to")]
    [TestCase("Readings | where isnull(Timestamp) | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-04) step 1d",
        Description = "only null axis values with from and to")]
    [TestCase("Readings | where V > 1000 | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-04) step 1d",
        Description = "a filter that removes every row")]
    [TestCase("Readings | take 0 | make-series s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-03) step 1d",
        Description = "take 0")]
    [Description("make-series - without rows to aggregate make-series returns no rows")]
    public void MakeSeries_NoRowsToAggregate_ReturnsNoRows(string query)
    {
        AssertNoSeries(CreateMakeSeriesDatabase(), query);
    }

    // kind=nonempty

    [Test]
    [Description("make-series kind=nonempty - an empty input returns one row with the default of every aggregation in every bin")]
    public void MakeSeries_KindNonEmpty_EmptyInputReturnsTheDefaults()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "EmptyReadings | make-series kind=nonempty s=sum(V), c=count() default=-1, a=avg(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-04) step 1d");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "s", "c", "a", "Timestamp");
        AssertSeries(row, "s", "[0,0,0]");
        AssertSeries(row, "c", "[-1,-1,-1]");
        AssertSeries(row, "a", "[0.0,0.0,0.0]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 3));
    }

    [Test]
    [Description("make-series kind=nonempty - no rows in [from, to) return one row with the defaults")]
    public void MakeSeries_KindNonEmpty_NoRowsInTheRangeReturnsTheDefaults()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series kind=nonempty s=sum(V) on Timestamp from datetime(2027-01-01) to datetime(2027-01-04) step 1d");

        AssertSeries(row, "s", "[0,0,0]");
        AssertAxis(row, "Timestamp", Days(2027, 1, 1, 3));
    }

    [Test]
    [Description("make-series kind=nonempty - the default row has a bin for every step of [from, to)")]
    public void MakeSeries_KindNonEmpty_DefaultRowHasEveryBin()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series kind=nonempty s=sum(V), m=max(V) default=-1 on Timestamp from datetime(2027-01-01) to datetime(2027-01-03) step 12h");

        AssertSeries(row, "s", "[0,0,0,0]");
        AssertSeries(row, "m", "[-1,-1,-1,-1]");
        AssertAxis(row, "Timestamp", Bins(Utc(2027, 1, 1), TimeSpan.FromHours(12), 4));
    }

    [Test]
    [Description("make-series kind=nonempty - an input filtered to no rows returns one row with the defaults")]
    public void MakeSeries_KindNonEmpty_FilteredInputReturnsTheDefaults()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | take 0 | make-series kind=nonempty s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-03) step 1d");

        AssertSeries(row, "s", "[0,0]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 2));
    }

    [Test]
    [Description("make-series kind=nonempty - with rows in the range the result is the same as without kind")]
    public void MakeSeries_KindNonEmpty_WithRowsIsLikeTheDefaultKind()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Readings | make-series kind=nonempty s=sum(V) on Timestamp from datetime(2026-01-01) to datetime(2026-01-04) step 1d");

        AssertSeries(row, "s", "[3,4,0]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 3));
    }

    [TestCase("", Description = "without from and to")]
    [TestCase("from datetime(2026-01-01)", Description = "only from")]
    [TestCase("to datetime(2026-01-03)", Description = "only to")]
    [Description("make-series kind=nonempty - without from and to an empty input returns one row with a single default value and an empty axis, like Azure Data Explorer does")]
    public void MakeSeries_KindNonEmptyWithoutFullRange_EmptyInputReturnsSingleDefaultAndEmptyAxis(string range)
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"EmptyReadings | make-series kind=nonempty s=sum(V) on Timestamp {range} step 1d");

        AssertSeries(row, "s", "[0]");
        AssertSeries(row, "Timestamp", "[]");
    }
}
