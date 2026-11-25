using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;
using static KustoPlayground.Core.Tests.MakeSeriesOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// make-series - the aggregation functions and the default value of the bins,
/// https://learn.microsoft.com/en-us/kusto/query/make-series-operator.
/// <list type="bullet">
/// <item>every aggregation produces a dynamic array with one value per bin, the value of a bin is the aggregation of
/// the rows of the bin; the elements of count(), dcount() and of the long and int aggregations are longs, the elements
/// of avg(), stdev(), variance() and of the real aggregations are reals, the elements of max() and min() of a datetime
/// column are datetimes;</item>
/// <item>the aggregations are avg, avgif, count, countif, dcount, dcountif, max, maxif, min, minif, percentile, stdev,
/// sum, sumif, take_any and variance; an aggregation can be applied to an expression and a constant is a valid
/// aggregation too;</item>
/// <item>a bin without rows gets the default: 0 of the aggregation type without default=, the zero datetime
/// (0001-01-01) for a datetime aggregation, or the value of default=;</item>
/// <item>default= applies only to the aggregation it follows; it is an int, long or real constant or a typed null and
/// it is converted to the aggregation type (a real default of a long aggregation is truncated toward zero);</item>
/// <item>the default also replaces the null result of a bin whose rows have only null values for avg, avgif, min,
/// minif, max, maxif and percentile, while sum, sumif, count, countif, dcount, stdev and variance return 0 for such a
/// bin; null values are ignored by all the aggregations except count().</item>
/// </list>
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class MakeSeriesOperatorOpusAggregationsAndDefaultsTests
{
    /// <summary>
    /// Three daily bins over Metrics: 2026-01-01 (three rows), 2026-01-02 (one row) and 2026-01-03 (no rows).
    /// </summary>
    private const string MetricsAxis = "on Timestamp from datetime(2026-01-01) to datetime(2026-01-04) step 1d";

    /// <summary>
    /// Four daily bins over Sensors: 2026-01-01 (two values and a null row), 2026-01-02 (only a null row),
    /// 2026-01-03 (no rows) and 2026-01-04 (one row).
    /// </summary>
    private const string SensorsAxis = "on Timestamp from datetime(2026-01-01) to datetime(2026-01-05) step 1d";

    /// <summary>
    /// Three daily bins over Meters: 2026-01-01 (two values), 2026-01-02 (only null rows) and 2026-01-03 (no rows).
    /// </summary>
    private const string MetersAxis = "on Timestamp from datetime(2026-01-01) to datetime(2026-01-04) step 1d";

    // the aggregation functions

    [TestCase("count()", "[3,1,0]", Description = "count() counts the rows of the bin")]
    [TestCase("sum(V)", "[12,5,0]", Description = "sum() of a long column is a long series")]
    [TestCase("sum(I)", "[90,30,0]", Description = "sum() of an int column is a long series")]
    [TestCase("sum(R)", "[8.0,0.5,0.0]", Description = "sum() of a real column is a real series")]
    [TestCase("avg(V)", "[4.0,5.0,0.0]", Description = "avg() of a long column is a real series")]
    [TestCase("avg(I)", "[30.0,30.0,0.0]", Description = "avg() of an int column is a real series")]
    [TestCase("avg(R)", "[2.6666666666666667,0.5,0.0]", Description = "avg() of a real column")]
    [TestCase("min(I)", "[10,30,0]", Description = "min() of an int column")]
    [TestCase("min(R)", "[1.5,0.5,0.0]", Description = "min() of a real column")]
    [TestCase("max(V)", "[8,5,0]", Description = "max() of a long column")]
    [TestCase("max(I)", "[60,30,0]", Description = "max() of an int column")]
    [TestCase("max(R)", "[4.0,0.5,0.0]", Description = "max() of a real column")]
    [TestCase("dcount(K)", "[2,1,0]", Description = "dcount() counts the distinct values of the bin")]
    [TestCase("percentile(V, 50)", "[3,5,0]", Description = "percentile() of a long column is a long series")]
    [TestCase("percentile(I, 50)", "[20,30,0]", Description = "percentile() of an int column")]
    [TestCase("percentile(R, 50)", "[2.5,0.5,0.0]", Description = "percentile() of a real column is a real series")]
    [TestCase("stdev(V)", "[3.605551275463989,0.0,0.0]", Description = "stdev() is the sample deviation and 0 for a single row")]
    [TestCase("stdev(R)", "[1.2583057391985462,0.0,0.0]", Description = "stdev() of a real column")]
    [TestCase("variance(V)", "[13.0,0.0,0.0]", Description = "variance() is the sample variance and 0 for a single row")]
    [TestCase("variance(R)", "[1.5833333333333333,0.0,0.0]", Description = "variance() of a real column")]
    [TestCase("countif(V > 2)", "[2,1,0]", Description = "countif() counts the matching rows of the bin")]
    [TestCase("sumif(V, V > 2)", "[11,5,0]", Description = "sumif() of a long column")]
    [TestCase("sumif(R, V > 2)", "[6.5,0.5,0.0]", Description = "sumif() of a real column with a predicate on another column")]
    [TestCase("avgif(R, V > 2)", "[3.25,0.5,0.0]", Description = "avgif() averages the matching rows of the bin")]
    [TestCase("minif(V, V > 2)", "[3,5,0]", Description = "minif() takes the minimum of the matching rows")]
    [TestCase("maxif(V, V < 5)", "[3,0,0]", Description = "maxif() without matching rows in a bin with rows gets the default 0")]
    [TestCase("dcountif(K, V > 2)", "[2,1,0]", Description = "dcountif() counts the distinct values of the matching rows")]
    [TestCase("sum(V * 2)", "[24,10,0]", Description = "an aggregation of an arithmetic expression")]
    [TestCase("max(V + 1)", "[9,6,0]", Description = "max() of an arithmetic expression")]
    [Description("make-series - every aggregation function is computed per bin and an empty bin gets 0 of the aggregation type")]
    public void MakeSeries_AggregationFunction_IsComputedPerBin(string aggregation, string expectedSeries)
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Metrics | make-series r={aggregation} {MetricsAxis}");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "r", "Timestamp");
        AssertSeries(row, "r", expectedSeries);
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 3));
    }

    [Test]
    [Description("make-series - several aggregations produce one series each, in the order of the query, followed by the axis")]
    public void MakeSeries_SeveralAggregations_ProduceOneSeriesEach()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Metrics | make-series c=count(), s=sum(V), a=avg(R), d=dcount(K) {MetricsAxis}");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "c", "s", "a", "d", "Timestamp");
        AssertSeries(row, "c", "[3,1,0]");
        AssertSeries(row, "s", "[12,5,0]");
        AssertSeries(row, "a", "[2.6666666666666667,0.5,0.0]");
        AssertSeries(row, "d", "[2,1,0]");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 3));
    }

    [Test]
    [Description("make-series - the series and the axis are dynamic arrays and their elements keep the type of the aggregation and of the axis")]
    public void MakeSeries_SeriesElements_HaveTheTypeOfTheAggregation()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Metrics | make-series c=count(), s=sum(V), r=sum(R), m=min(I) {MetricsAxis} "
            + "| project SeriesType=gettype(c), AxisType=gettype(Timestamp), CountType=gettype(c[0]), "
            + "LongSumType=gettype(s[0]), RealSumType=gettype(r[0]), IntMinType=gettype(m[0]), "
            + "AxisElementType=gettype(Timestamp[0]), EmptyRealBinType=gettype(r[2])");

        var expectedTypes = new Dictionary<string, string>
        {
            ["SeriesType"] = "array",
            ["AxisType"] = "array",
            ["CountType"] = "long",
            ["LongSumType"] = "long",
            ["RealSumType"] = "double",
            // the elements of an int aggregation are longs
            ["IntMinType"] = "long",
            ["AxisElementType"] = "datetime",
            // the default of an empty bin has the type of the aggregation
            ["EmptyRealBinType"] = "double"
        };
        foreach (var (column, expectedType) in expectedTypes)
        {
            Assert.That(Col(row, column), Is.EqualTo(expectedType), column);
        }
    }

    [Test]
    [Description("make-series - max() and min() of a datetime column are datetime series and an empty bin gets the zero datetime")]
    public void MakeSeries_DatetimeAggregations_EmptyBinGetsTheZeroDatetime()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Metrics | make-series Last=max(Timestamp), First=min(Timestamp) {MetricsAxis}");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "Last", "First", "Timestamp");
        AssertDynamic(Col(row, "Last"), Arr(Utc(2026, 1, 1, 20), Utc(2026, 1, 2, 12), ZeroDateTime), "series 'Last'");
        AssertDynamic(Col(row, "First"), Arr(Utc(2026, 1, 1, 1), Utc(2026, 1, 2, 12), ZeroDateTime), "series 'First'");
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 3));
    }

    [Test]
    [Description("make-series - take_any() returns any value of the bin")]
    public void MakeSeries_TakeAny_ReturnsAnyValueOfTheBin()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Metrics | make-series t=take_any(I) {MetricsAxis}");

        // 2026-01-01 has the values 10, 20 and 60, 2026-01-02 only 30
        AssertDynamicJsonOneOf(Col(row, "t"), "[10,30,0]", "[20,30,0]", "[60,30,0]");
    }

    [Test]
    [Description("make-series - a constant is a valid aggregation: the bins with rows get the constant and the empty bins the default")]
    public void MakeSeries_ConstantAggregation_FillsTheBinsWithRows()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Metrics | make-series c=count(), Flag=7 {MetricsAxis}");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "c", "Flag", "Timestamp");
        AssertSeries(row, "c", "[3,1,0]");
        AssertSeries(row, "Flag", "[7,7,0]");
    }

    [Test]
    [Description("make-series - weekly bins with an aggregation named like its input column and a constant aggregation with its own default")]
    public void MakeSeries_ConstantAggregationWithDefault_WeeklyBins()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            "Prices | make-series Value=sum(Value), other=-1 default=-2 on Time from datetime(2025-01-06) to datetime(2025-02-09) step 7d");

        // the bins start on 2025-01-06, 01-13, 01-20, 01-27 and 02-03; the rows are on 01-07, 01-16 and 02-01
        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "Value", "other", "Time");
        AssertSeries(row, "Value", "[10,20,0,30,0]");
        AssertSeries(row, "other", "[-1,-1,-2,-1,-2]");
        AssertAxis(row, "Time", Bins(Utc(2025, 1, 6), TimeSpan.FromDays(7), 5));
    }

    // null values

    [TestCase("count()", "[3,1,0,1]", Description = "count() counts the rows with null values too")]
    [TestCase("sum(V)", "[4,0,0,7]", Description = "sum() ignores null values and is 0 for only null values")]
    [TestCase("dcount(V)", "[2,0,0,1]", Description = "dcount() ignores null values")]
    [TestCase("avg(V)", "[2.0,0.0,0.0,7.0]", Description = "avg() ignores null values and its null result for only null values gets the default 0")]
    [TestCase("min(R)", "[1.5,0.0,0.0,3.0]", Description = "min() ignores null values")]
    [TestCase("max(V)", "[3,0,0,7]", Description = "max() ignores null values")]
    [TestCase("percentile(R, 50)", "[1.5,0.0,0.0,3.0]", Description = "percentile() ignores null values")]
    [TestCase("stdev(V)", "[1.4142135623730952,0.0,0.0,0.0]", Description = "stdev() ignores null values and is 0 for a single value")]
    [TestCase("sumif(V, V > 1)", "[3,0,0,7]", Description = "sumif() with a predicate that is null for null values")]
    [Description("make-series - the aggregations ignore null values and a bin with only null values gets 0")]
    public void MakeSeries_NullValues_AreIgnoredByTheAggregations(string aggregation, string expectedSeries)
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Sensors | make-series r={aggregation} {SensorsAxis}");

        AssertSeries(row, "r", expectedSeries);
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 4));
    }

    // default=

    [TestCase("count() default=-1", "[3,1,-1,1]", Description = "only the empty bin gets the default, a bin with a null row counts it")]
    [TestCase("sum(V) default=long(null)", "[4,0,null,7]", Description = "a typed null default, sum() of only null values is still 0")]
    [TestCase("sum(R) default=2", "[4.0,0.0,2.0,3.0]", Description = "a long default of a real aggregation becomes a real")]
    [TestCase("avg(R) default=real(null)", "[2.0,null,null,3.0]", Description = "the null result of avg() for only null values gets the default too")]
    [TestCase("max(V) default=-5", "[3,-5,-5,7]", Description = "the null result of max() for only null values gets the default too")]
    [TestCase("count() default=1.5", "[3,1,1,1]", Description = "a real default of a long aggregation is truncated")]
    [TestCase("countif(isnull(V)) default=9", "[1,1,9,0]", Description = "countif() is 0 when no row of the bin matches and only the empty bin gets the default")]
    [Description("make-series - default= fills the empty bins and the null results of the bins with rows")]
    public void MakeSeries_Default_FillsTheEmptyBins(string aggregation, string expectedSeries)
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Sensors | make-series r={aggregation} {SensorsAxis}");

        AssertSeries(row, "r", expectedSeries);
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 4));
    }

    [TestCase("avg(R) default=-1.0", "[2.0,-1.0,-1.0]", Description = "avg()")]
    [TestCase("min(R) default=99.0", "[1.5,99.0,99.0]", Description = "min()")]
    [TestCase("max(R) default=-12", "[2.5,-12.0,-12.0]", Description = "max() with a long default of a real aggregation")]
    [TestCase("percentile(V, 50) default=-6", "[1,-6,-6]", Description = "percentile()")]
    [TestCase("avgif(R, R > 0) default=-8.0", "[2.0,-8.0,-8.0]", Description = "avgif()")]
    [TestCase("stdev(R) default=-3.0", "[0.7071067811865476,0.0,-3.0]", Description = "stdev() of only null values is 0")]
    [TestCase("variance(V) default=-4", "[4.5,0.0,-4.0]", Description = "variance() of only null values is 0")]
    [TestCase("sum(V) default=-9", "[5,0,-9]", Description = "sum() of only null values is 0")]
    [TestCase("sumif(V, V > 0) default=-10", "[5,0,-10]", Description = "sumif() without matching rows is 0")]
    [TestCase("dcount(V) default=-11", "[2,0,-11]", Description = "dcount() of only null values is 0")]
    [TestCase("count() default=2.9", "[2,2,2]", Description = "a positive real default of a long aggregation is truncated toward zero")]
    [TestCase("count() default=-2.9", "[2,2,-2]", Description = "a negative real default of a long aggregation is truncated toward zero")]
    [Description("make-series - a bin with only null values gets the default for avg, min, max and percentile, but 0 for sum, count, dcount, stdev and variance")]
    public void MakeSeries_BinWithOnlyNullValues_GetsTheDefaultOrZero(string aggregation, string expectedSeries)
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Meters | make-series r={aggregation} {MetersAxis}");

        AssertSeries(row, "r", expectedSeries);
        AssertAxis(row, "Timestamp", Days(2026, 1, 1, 3));
    }

    [Test]
    [Description("make-series - default= applies only to the aggregation it follows")]
    public void MakeSeries_Default_AppliesOnlyToItsAggregation()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Meters | make-series c=count() default=-1, s=sum(V) {MetersAxis}");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "c", "s", "Timestamp");
        AssertSeries(row, "c", "[2,2,-1]");
        AssertSeries(row, "s", "[5,0,0]");
    }

    [Test]
    [Description("make-series - default= after an aggregation without a column name keeps the generated name")]
    public void MakeSeries_DefaultWithoutColumnName_KeepsTheGeneratedName()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Meters | make-series sum(V) default=-1 {MetersAxis}");

        SummarizeOperatorOpusTestUtils.AssertColumnsInOrder(row, "sum_V", "Timestamp");
        AssertSeries(row, "sum_V", "[5,0,-1]");
    }

    [Test]
    [Description("make-series - a typed null default leaves null elements in the series")]
    public void MakeSeries_NullDefault_LeavesNullElements()
    {
        var row = ExecuteAndGetSeriesRow(CreateMakeSeriesDatabase(),
            $"Meters | make-series s=sum(V) default=long(null), a=avg(R) default=real(null) {MetersAxis}");

        AssertSeries(row, "s", "[5,0,null]");
        AssertSeries(row, "a", "[2.0,null,null]");
    }

    [Test]
    [Description("make-series - the null elements of a typed null default have no type after mv-expand, the other elements keep theirs")]
    public void MakeSeries_NullDefault_ExpandsToNullElements()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateMakeSeriesDatabase(),
            $"Meters | make-series s=sum(V) default=long(null), a=avg(R) default=real(null) {MetersAxis} "
            + "| mv-expand with_itemindex=i s, a | project i, SType=gettype(s), AType=gettype(a) | order by i asc");

        Assert.That(SummarizeOperatorOpusTestUtils.GetValues<long>(rows, "i"), Is.EqualTo(new[] { 0L, 1L, 2L }));
        Assert.That(rows.Select(row => $"{Col(row, "SType")} {Col(row, "AType")}"),
            Is.EqualTo(new[] { "long double", "long null", "null null" }));
    }
}
