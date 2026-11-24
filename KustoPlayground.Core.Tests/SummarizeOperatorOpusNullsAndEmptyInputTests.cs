using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// summarize over empty input and over null values (https://learn.microsoft.com/en-us/kusto/query/summarize-operator):
/// - without 'by', empty input still produces one row with the default value of every aggregate
///   (0 for counting/sum/stdev/variance, NaN for avg, [] for collections, null for the others, "" for strings);
/// - with 'by', empty input produces no rows;
/// - aggregates ignore null values (an all-null group behaves like an empty group, but count() counts its rows);
/// - null is a group key value of its own.
/// </summary>
public class SummarizeOperatorOpusNullsAndEmptyInputTests
{
    // Readings(Sensor: string, Value: long?) = a: 1, null, 3; b: null, null; c: 5
    private static Table BuildReadingsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Readings")
            .WithColumn<string>("Sensor")
            .WithColumn<long>("Value")
            .WithRow("a", 1L)
            .WithRow("a", null)
            .WithRow("a", 3L)
            .WithRow("b", null)
            .WithRow("b", null)
            .WithRow("c", 5L)
            .Build();
    }

    // Tagged(Sensor: string, Value: long?, Tag: string) = Readings with a unique Tag per row (t1..t6)
    private static Table BuildTaggedTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Tagged")
            .WithColumn<string>("Sensor")
            .WithColumn<long>("Value")
            .WithColumn<string>("Tag")
            .WithRow("a", 1L, "t1")
            .WithRow("a", null, "t2")
            .WithRow("a", 3L, "t3")
            .WithRow("b", null, "t4")
            .WithRow("b", null, "t5")
            .WithRow("c", 5L, "t6")
            .Build();
    }

    // Empty(x: long) without rows
    private static Table BuildEmptyTable()
    {
        return TestUtils.GenerateTableWithColumn(Array.Empty<long>(), "x", "Empty");
    }

    #region empty input without 'by': one row with default values

    [Test]
    [Description("Empty input without 'by' returns one row: count() is 0")]
    public void EmptyInput_NoBy_CountIsZero()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | where Amount > 100 | summarize count()");

        AssertColumnsInOrder(row, "count_");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(0L));
    }

    [Test]
    [Description("Empty input without 'by': count, countif, dcount, dcountif and count_distinct are 0")]
    public void EmptyInput_NoBy_CountingAggregatesAreZero()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | where Amount > 100 | summarize count(), countif(Amount > 1), dcount(Product), " +
            "dcountif(Product, Amount > 1), count_distinct(Product)");

        AssertColumnsInOrder(row, "count_", "countif_", "dcount_Product", "dcountif_Product", "count_distinct_Product");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "countif_"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "dcount_Product"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "dcountif_Product"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "count_distinct_Product"), Is.EqualTo(0L));
    }

    [Test]
    [Description("Empty input without 'by': sum and sumif are 0, avg and avgif are NaN")]
    public void EmptyInput_NoBy_SumIsZeroAndAvgIsNaN()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | where Amount > 100 | summarize sum(Amount), sumif(Amount, Amount > 1), avg(Amount), " +
            "avgif(Amount, Amount > 1)");

        AssertColumnsInOrder(row, "sum_Amount", "sumif_Amount", "avg_Amount", "avgif_Amount");
        Assert.That(GetValue<long>(row, "sum_Amount"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "sumif_Amount"), Is.EqualTo(0L));
        Assert.That(GetValue<double>(row, "avg_Amount"), Is.NaN);
        Assert.That(GetValue<double>(row, "avgif_Amount"), Is.NaN);
    }

    [Test]
    [Description("Empty input without 'by': min, max, minif, maxif and percentile are null")]
    public void EmptyInput_NoBy_MinMaxAndPercentileAreNull()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | where Amount > 100 | summarize min(Amount), max(Amount), minif(Amount, Amount > 1), " +
            "maxif(Amount, Amount > 1), percentile(Amount, 50)");

        AssertColumnsInOrder(row, "min_Amount", "max_Amount", "minif_Amount", "maxif_Amount", "percentile_Amount_50");
        AssertIsNull(row, "min_Amount");
        AssertIsNull(row, "max_Amount");
        AssertIsNull(row, "minif_Amount");
        AssertIsNull(row, "maxif_Amount");
        AssertIsNull(row, "percentile_Amount_50");
    }

    [Test]
    [Description("Empty input without 'by': string results are empty strings (a Kusto string is never null)")]
    public void EmptyInput_NoBy_StringResultsAreEmptyStrings()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | where Amount > 100 | summarize min(Product), max(Product), take_any(Product)");

        AssertColumnsInOrder(row, "min_Product", "max_Product", "Product");
        Assert.That(GetValue<string>(row, "min_Product"), Is.Empty);
        Assert.That(GetValue<string>(row, "max_Product"), Is.Empty);
        Assert.That(GetValue<string>(row, "Product"), Is.Empty);
    }

    [Test]
    [Description("Empty input without 'by': stdev, stdevp, variance and variancep are 0")]
    public void EmptyInput_NoBy_StdevAndVarianceAreZero()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | where Amount > 100 | summarize stdev(Amount), stdevp(Amount), variance(Amount), variancep(Amount)");

        AssertColumnsInOrder(row, "stdev_Amount", "stdevp_Amount", "variance_Amount", "variancep_Amount");
        Assert.That(GetValue<double>(row, "stdev_Amount"), Is.EqualTo(0.0));
        Assert.That(GetValue<double>(row, "stdevp_Amount"), Is.EqualTo(0.0));
        Assert.That(GetValue<double>(row, "variance_Amount"), Is.EqualTo(0.0));
        Assert.That(GetValue<double>(row, "variancep_Amount"), Is.EqualTo(0.0));
    }

    [Test]
    [Description("Empty input without 'by': collection aggregates are empty arrays; duplicate list/set names get suffixes")]
    public void EmptyInput_NoBy_CollectionsAreEmptyArrays()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | where Amount > 100 | summarize make_list(Amount), make_set(Product), " +
            "make_list_if(Amount, Amount > 1), make_set_if(Product, Amount > 1), make_list_with_nulls(Amount)");

        AssertColumnsInOrder(row, "list_Amount", "set_Product", "list_Amount1", "set_Product1", "list_Amount2");
        Assert.That(GetDynamicArray(row, "list_Amount"), Is.Empty);
        Assert.That(GetDynamicArray(row, "set_Product"), Is.Empty);
        Assert.That(GetDynamicArray(row, "list_Amount1"), Is.Empty);
        Assert.That(GetDynamicArray(row, "set_Product1"), Is.Empty);
        Assert.That(GetDynamicArray(row, "list_Amount2"), Is.Empty);
    }

    [Test]
    [Description("Empty input without 'by': arg_max, arg_min and take_any return null (\"\" for strings)")]
    public void EmptyInput_NoBy_ArgMaxArgMinAndTakeAnyReturnNullValues()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | where Amount > 100 | summarize arg_max(Amount, Product), arg_min(Amount, Product), " +
            "take_any(Amount, Product), c = count()");

        AssertColumnsInOrder(row, "Amount", "Product", "Amount1", "Product1", "Amount2", "Product2", "c");
        AssertIsNull(row, "Amount");
        Assert.That(GetValue<string>(row, "Product"), Is.Empty);
        AssertIsNull(row, "Amount1");
        Assert.That(GetValue<string>(row, "Product1"), Is.Empty);
        AssertIsNull(row, "Amount2");
        Assert.That(GetValue<string>(row, "Product2"), Is.Empty);
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(0L));
    }

    [Test]
    [Description("Empty input without 'by' for real, decimal, datetime and timespan columns: sums are zero of the column type, avg of real is NaN, the rest is null")]
    public void EmptyInput_NoBy_DefaultsPerType()
    {
        var tableRows = new List<(double, decimal, DateTime, TimeSpan)>
        {
            (1.5, 1.5m, new DateTime(2024, 1, 1), TimeSpan.FromHours(1))
        };
        Table table = TestUtils.GenerateTableWith4Columns(tableRows, "d", "dec", "dt", "ts", "Measures");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Measures | take 0 | summarize sum(d), sum(dec), sum(ts), avg(d), avg(dec), avg(ts), avg(dt), " +
            "min(dt), max(dt), min(ts)");

        AssertColumnsInOrder(row, "sum_d", "sum_dec", "sum_ts", "avg_d", "avg_dec", "avg_ts", "avg_dt", "min_dt",
            "max_dt", "min_ts");
        Assert.That(GetValue<double>(row, "sum_d"), Is.EqualTo(0.0));
        Assert.That(GetValue<decimal>(row, "sum_dec"), Is.EqualTo(0m));
        Assert.That(GetValue<TimeSpan>(row, "sum_ts"), Is.EqualTo(TimeSpan.Zero));
        Assert.That(GetValue<double>(row, "avg_d"), Is.NaN);
        AssertIsNull(row, "avg_dec");
        AssertIsNull(row, "avg_ts");
        AssertIsNull(row, "avg_dt");
        AssertIsNull(row, "min_dt");
        AssertIsNull(row, "max_dt");
        AssertIsNull(row, "min_ts");
    }

    [Test]
    [Description("Doc example: datatable(x:long)[] | summarize count(x), countif(x > 0), dcount(x), dcountif(x, x > 0) gives zeros")]
    public void EmptyTable_NoBy_DocExampleCountingAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildEmptyTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Empty | summarize count(x), countif(x > 0), dcount(x), dcountif(x, x > 0)");

        AssertColumnsInOrder(row, "count_x", "countif_", "dcount_x", "dcountif_x");
        Assert.That(GetValue<long>(row, "count_x"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "countif_"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "dcount_x"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "dcountif_x"), Is.EqualTo(0L));
    }

    [Test]
    [Description("Doc example: default values of take_any, avg, max, min, percentile, stdev, sum, sumif and variance over an empty table")]
    public void EmptyTable_NoBy_DocExampleOtherAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildEmptyTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Empty | summarize any_x = take_any(x), avg(x), max(x), min(x), percentile(x, 55), stdev(x), sum(x), " +
            "sumif(x, x > 0), variance(x)");

        AssertColumnsInOrder(row, "any_x", "avg_x", "max_x", "min_x", "percentile_x_55", "stdev_x", "sum_x", "sumif_x",
            "variance_x");
        AssertIsNull(row, "any_x");
        Assert.That(GetValue<double>(row, "avg_x"), Is.NaN);
        AssertIsNull(row, "max_x");
        AssertIsNull(row, "min_x");
        AssertIsNull(row, "percentile_x_55");
        Assert.That(GetValue<double>(row, "stdev_x"), Is.EqualTo(0.0));
        Assert.That(GetValue<long>(row, "sum_x"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "sumif_x"), Is.EqualTo(0L));
        Assert.That(GetValue<double>(row, "variance_x"), Is.EqualTo(0.0));
    }

    [Test]
    [Description("Doc example: make_set(x) and make_list(x) over an empty table are empty arrays")]
    public void EmptyTable_NoBy_DocExampleCollections()
    {
        var kustoDatabase = CreateDatabase(BuildEmptyTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Empty | summarize make_set(x), make_list(x)");

        AssertColumnsInOrder(row, "set_x", "list_x");
        Assert.That(GetDynamicArray(row, "set_x"), Is.Empty);
        Assert.That(GetDynamicArray(row, "list_x"), Is.Empty);
    }

    [Test]
    [Description("'take 0' before summarize without 'by' still returns one row of defaults")]
    public void Take0_NoBy_ReturnsDefaults()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | take 0 | summarize count(), sum(Amount), make_list(Amount)");

        AssertColumnsInOrder(row, "count_", "sum_Amount", "list_Amount");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "sum_Amount"), Is.EqualTo(0L));
        Assert.That(GetDynamicArray(row, "list_Amount"), Is.Empty);
    }

    #endregion

    #region empty input with 'by': no rows

    [Test]
    [Description("Empty input with 'by' returns no rows")]
    public void EmptyInput_WithBy_ReturnsNoRows()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | where Amount > 100 | summarize count(), sum(Amount) by Region");

        Assert.That(rows, Is.Empty);
    }

    [Test]
    [Description("Empty input with 'by' and no aggregates returns no rows")]
    public void EmptyInput_ByOnly_ReturnsNoRows()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | where Amount > 100 | summarize by Region, Product");

        Assert.That(rows, Is.Empty);
    }

    [Test]
    [Description("Empty input grouped by bin() returns no rows")]
    public void EmptyInput_WithBinKey_ReturnsNoRows()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | where Amount > 100 | summarize count() by bin(Amount, 10)");

        Assert.That(rows, Is.Empty);
    }

    [Test]
    [Description("A table without rows grouped by a column returns no rows")]
    public void EmptyTable_WithBy_ReturnsNoRows()
    {
        var kustoDatabase = CreateDatabase(BuildEmptyTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Empty | summarize count() by x");

        Assert.That(rows, Is.Empty);
    }

    [Test]
    [Description("Empty input with 'by' followed by the count operator gives Count = 0")]
    public void EmptyInput_WithBy_ThenCountOperator()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | where Amount > 100 | summarize count() by Region | count");

        Assert.That(GetValue<long>(row, "Count"), Is.EqualTo(0L));
    }

    [Test]
    [Description("A summarize without 'by' after an empty grouped result returns one row of defaults")]
    public void EmptyInput_WithBy_ThenSummarizeWithoutBy()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | where Amount > 100 | summarize c = count() by Region | summarize sum(c), count()");

        AssertColumnsInOrder(row, "sum_c", "count_");
        Assert.That(GetValue<long>(row, "sum_c"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(0L));
    }

    #endregion

    #region conditional aggregates without matching rows

    [Test]
    [Description("Conditional aggregates with a predicate that matches no row return the empty-input defaults")]
    public void IfAggregates_NoMatchingRows()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize countif(Amount > 100), sumif(Amount, Amount > 100), avgif(Amount, Amount > 100), " +
            "minif(Amount, Amount > 100), maxif(Amount, Amount > 100), dcountif(Product, Amount > 100), " +
            "make_list_if(Amount, Amount > 100), make_set_if(Product, Amount > 100)");

        AssertColumnsInOrder(row, "countif_", "sumif_Amount", "avgif_Amount", "minif_Amount", "maxif_Amount",
            "dcountif_Product", "list_Amount", "set_Product");
        Assert.That(GetValue<long>(row, "countif_"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "sumif_Amount"), Is.EqualTo(0L));
        Assert.That(GetValue<double>(row, "avgif_Amount"), Is.NaN);
        AssertIsNull(row, "minif_Amount");
        AssertIsNull(row, "maxif_Amount");
        Assert.That(GetValue<long>(row, "dcountif_Product"), Is.EqualTo(0L));
        Assert.That(GetDynamicArray(row, "list_Amount"), Is.Empty);
        Assert.That(GetDynamicArray(row, "set_Product"), Is.Empty);
    }

    [Test]
    [Description("A group in which the predicate matches no row gets 0 / null / [] for the conditional aggregates")]
    public void IfAggregates_GroupWithoutMatchingRows()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize countif(Amount > 9), sumif(Amount, Amount > 9), minif(Amount, Amount > 9), " +
            "maxif(Amount, Amount > 9), make_list_if(Amount, Amount > 9) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "countif_", "sumif_Amount", "minif_Amount", "maxif_Amount",
            "list_Amount");

        var east = GetSingleRowWhere(rows, "Region", "East");
        Assert.That(GetValue<long>(east, "countif_"), Is.EqualTo(2L));
        Assert.That(GetValue<long>(east, "sumif_Amount"), Is.EqualTo(30L));
        Assert.That(GetValue<long>(east, "minif_Amount"), Is.EqualTo(10L));
        Assert.That(GetValue<long>(east, "maxif_Amount"), Is.EqualTo(20L));
        Assert.That(GetDynamicArray(east, "list_Amount"), Is.EquivalentTo(new object?[] { 10L, 20L }));

        var west = GetSingleRowWhere(rows, "Region", "West");
        Assert.That(GetValue<long>(west, "countif_"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(west, "sumif_Amount"), Is.EqualTo(0L));
        AssertIsNull(west, "minif_Amount");
        AssertIsNull(west, "maxif_Amount");
        Assert.That(GetDynamicArray(west, "list_Amount"), Is.Empty);
    }

    #endregion

    #region null values in aggregated columns

    [Test]
    [Description("count() counts rows, including rows whose values are null")]
    public void NullValues_CountCountsRowsWithNulls()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Sensor",
            columnName2: "count_",
            kustoDatabase,
            query: "Readings | summarize count() by Sensor");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("a", 3L), ("b", 2L), ("c", 1L) }));
    }

    [Test]
    [Description("count(Value) is named count_Value and still counts all rows, including rows where Value is null")]
    public void NullValues_CountWithArgumentCountsAllRows()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Readings | summarize count(Value), count()");

        AssertColumnsInOrder(row, "count_Value", "count_");
        Assert.That(GetValue<long>(row, "count_Value"), Is.EqualTo(6L));
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(6L));
    }

    [Test]
    [Description("sum() ignores nulls; the sum of an all-null group is 0")]
    public void NullValues_SumIgnoresNulls()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Sensor",
            columnName2: "sum_Value",
            kustoDatabase,
            query: "Readings | summarize sum(Value) by Sensor");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("a", 4L), ("b", 0L), ("c", 5L) }));
    }

    [Test]
    [Description("avg() ignores nulls (they are not treated as 0); the avg of an all-null group is NaN")]
    public void NullValues_AvgIgnoresNulls()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Readings | summarize avg(Value) by Sensor");

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(GetValue<double>(GetSingleRowWhere(rows, "Sensor", "a"), "avg_Value"), Is.EqualTo(2.0));
        Assert.That(GetValue<double>(GetSingleRowWhere(rows, "Sensor", "b"), "avg_Value"), Is.NaN);
        Assert.That(GetValue<double>(GetSingleRowWhere(rows, "Sensor", "c"), "avg_Value"), Is.EqualTo(5.0));
    }

    [Test]
    [Description("min() and max() ignore nulls; for an all-null group they are null")]
    public void NullValues_MinMaxIgnoreNulls()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Readings | summarize min(Value), max(Value) by Sensor");

        Assert.That(rows, Has.Count.EqualTo(3));
        var actual = rows.Select(row => (
            GetValue<string>(row, "Sensor"),
            GetNullableValue<long>(row, "min_Value"),
            GetNullableValue<long>(row, "max_Value"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long?, long?)>
        {
            ("a", 1L, 3L), ("b", null, null), ("c", 5L, 5L)
        }));
    }

    [Test]
    [Description("dcount() does not count null as a value")]
    public void NullValues_DcountIgnoresNulls()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Sensor",
            columnName2: "dcount_Value",
            kustoDatabase,
            query: "Readings | summarize dcount(Value) by Sensor");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("a", 2L), ("b", 0L), ("c", 1L) }));
    }

    [Test]
    [Description("percentile() ignores nulls; for an all-null group it is null")]
    public void NullValues_PercentileIgnoresNulls()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Readings | summarize percentile(Value, 50) by Sensor");

        Assert.That(rows, Has.Count.EqualTo(3));
        var actual = rows.Select(row => (
            GetValue<string>(row, "Sensor"),
            GetNullableValue<long>(row, "percentile_Value_50"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long?)> { ("a", 1L), ("b", null), ("c", 5L) }));
    }

    [Test]
    [Description("stdev() and variance() ignore nulls: over 1, null, 3, null, null, 5 they are computed over 1, 3, 5")]
    public void NullValues_StdevAndVarianceIgnoreNulls()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Readings | summarize stdev(Value), variance(Value), stdevp(Value), variancep(Value)");

        AssertColumnsInOrder(row, "stdev_Value", "variance_Value", "stdevp_Value", "variancep_Value");
        Assert.That(GetValue<double>(row, "stdev_Value"), Is.EqualTo(2.0).Within(1e-9));
        Assert.That(GetValue<double>(row, "variance_Value"), Is.EqualTo(4.0).Within(1e-9));
        Assert.That(GetValue<double>(row, "stdevp_Value"), Is.EqualTo(Math.Sqrt(8.0 / 3)).Within(1e-9));
        Assert.That(GetValue<double>(row, "variancep_Value"), Is.EqualTo(8.0 / 3).Within(1e-9));
    }

    [Test]
    [Description("stdev() and variance() of an all-null group and of a single-value group are 0")]
    public void NullValues_StdevAndVariancePerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Readings | summarize stdev(Value), variance(Value), stdevp(Value), variancep(Value) by Sensor");

        Assert.That(rows, Has.Count.EqualTo(3));

        var a = GetSingleRowWhere(rows, "Sensor", "a");
        Assert.That(GetValue<double>(a, "stdev_Value"), Is.EqualTo(Math.Sqrt(2.0)).Within(1e-9));
        Assert.That(GetValue<double>(a, "variance_Value"), Is.EqualTo(2.0).Within(1e-9));
        Assert.That(GetValue<double>(a, "stdevp_Value"), Is.EqualTo(1.0).Within(1e-9));
        Assert.That(GetValue<double>(a, "variancep_Value"), Is.EqualTo(1.0).Within(1e-9));

        foreach (string sensor in new[] { "b", "c" })
        {
            var row = GetSingleRowWhere(rows, "Sensor", sensor);
            Assert.That(GetValue<double>(row, "stdev_Value"), Is.EqualTo(0.0), sensor);
            Assert.That(GetValue<double>(row, "variance_Value"), Is.EqualTo(0.0), sensor);
            Assert.That(GetValue<double>(row, "stdevp_Value"), Is.EqualTo(0.0), sensor);
            Assert.That(GetValue<double>(row, "variancep_Value"), Is.EqualTo(0.0), sensor);
        }
    }

    [Test]
    [Description("make_list() and make_set() skip nulls, make_list_with_nulls() keeps them (named list_Value1)")]
    public void NullValues_MakeListAndMakeSetSkipNulls_MakeListWithNullsKeepsThem()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Readings | summarize make_list(Value), make_set(Value), make_list_with_nulls(Value) by Sensor");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "Sensor", "list_Value", "set_Value", "list_Value1");

        var a = GetSingleRowWhere(rows, "Sensor", "a");
        Assert.That(GetDynamicArray(a, "list_Value"), Is.EquivalentTo(new object?[] { 1L, 3L }));
        Assert.That(GetDynamicArray(a, "set_Value"), Is.EquivalentTo(new object?[] { 1L, 3L }));
        Assert.That(GetDynamicArray(a, "list_Value1"), Is.EquivalentTo(new object?[] { 1L, null, 3L }));

        var b = GetSingleRowWhere(rows, "Sensor", "b");
        Assert.That(GetDynamicArray(b, "list_Value"), Is.Empty);
        Assert.That(GetDynamicArray(b, "set_Value"), Is.Empty);
        Assert.That(GetDynamicArray(b, "list_Value1"), Is.EqualTo(new object?[] { null, null }));

        var c = GetSingleRowWhere(rows, "Sensor", "c");
        Assert.That(GetDynamicArray(c, "list_Value"), Is.EqualTo(new object?[] { 5L }));
        Assert.That(GetDynamicArray(c, "set_Value"), Is.EqualTo(new object?[] { 5L }));
        Assert.That(GetDynamicArray(c, "list_Value1"), Is.EqualTo(new object?[] { 5L }));
    }

    [Test]
    [Description("countif() treats a null predicate result (null > 2) as false")]
    public void NullValues_CountifNullPredicateIsFalse()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Sensor",
            columnName2: "countif_",
            kustoDatabase,
            query: "Readings | summarize countif(Value > 2) by Sensor");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("a", 1L), ("b", 0L), ("c", 1L) }));
    }

    [Test]
    [Description("countif(isnull(..)) and countif(isnotnull(..)) count null and non-null values per group")]
    public void NullValues_CountifIsNullAndIsNotNull()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            columnName1: "Sensor",
            columnName2: "countif_",
            columnName3: "countif_1",
            kustoDatabase,
            query: "Readings | summarize countif(isnull(Value)), countif(isnotnull(Value)) by Sensor");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("a", 1L, 2L), ("b", 2L, 0L), ("c", 0L, 1L)
        }));
    }

    [Test]
    [Description("Without 'by', aggregates over a column with nulls ignore the nulls, while count() counts all rows")]
    public void NullValues_NoBy()
    {
        var kustoDatabase = CreateDatabase(BuildReadingsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Readings | summarize count(), sum(Value), avg(Value), min(Value), max(Value), dcount(Value)");

        AssertColumnsInOrder(row, "count_", "sum_Value", "avg_Value", "min_Value", "max_Value", "dcount_Value");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(6L));
        Assert.That(GetValue<long>(row, "sum_Value"), Is.EqualTo(9L));
        Assert.That(GetValue<double>(row, "avg_Value"), Is.EqualTo(3.0));
        Assert.That(GetValue<long>(row, "min_Value"), Is.EqualTo(1L));
        Assert.That(GetValue<long>(row, "max_Value"), Is.EqualTo(5L));
        Assert.That(GetValue<long>(row, "dcount_Value"), Is.EqualTo(3L));
    }

    [Test]
    [Description("A column with only nulls behaves like empty input, except that count() counts the rows")]
    public void NullValues_AllNullColumn()
    {
        Table table = new SummarizeOperatorOpusTableBuilder("AllNull")
            .WithColumn<long>("v")
            .WithRow(null)
            .WithRow(null)
            .WithRow(null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "AllNull | summarize count(), sum(v), avg(v), min(v), max(v), dcount(v), make_list(v), percentile(v, 50), " +
            "stdev(v), variance(v)");

        AssertColumnsInOrder(row, "count_", "sum_v", "avg_v", "min_v", "max_v", "dcount_v", "list_v", "percentile_v_50",
            "stdev_v", "variance_v");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(row, "sum_v"), Is.EqualTo(0L));
        Assert.That(GetValue<double>(row, "avg_v"), Is.NaN);
        AssertIsNull(row, "min_v");
        AssertIsNull(row, "max_v");
        Assert.That(GetValue<long>(row, "dcount_v"), Is.EqualTo(0L));
        Assert.That(GetDynamicArray(row, "list_v"), Is.Empty);
        AssertIsNull(row, "percentile_v_50");
        Assert.That(GetValue<double>(row, "stdev_v"), Is.EqualTo(0.0));
        Assert.That(GetValue<double>(row, "variance_v"), Is.EqualTo(0.0));
    }

    [Test]
    [Description("Aggregates over a real column with nulls ignore the nulls")]
    public void NullValues_RealColumn()
    {
        Table table = new SummarizeOperatorOpusTableBuilder("Reals")
            .WithColumn<double>("d")
            .WithRow(1.5)
            .WithRow(null)
            .WithRow(2.5)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Reals | summarize count(), sum(d), avg(d), min(d), max(d)");

        AssertColumnsInOrder(row, "count_", "sum_d", "avg_d", "min_d", "max_d");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(3L));
        Assert.That(GetValue<double>(row, "sum_d"), Is.EqualTo(4.0));
        Assert.That(GetValue<double>(row, "avg_d"), Is.EqualTo(2.0));
        Assert.That(GetValue<double>(row, "min_d"), Is.EqualTo(1.5));
        Assert.That(GetValue<double>(row, "max_d"), Is.EqualTo(2.5));
    }

    [Test]
    [Description("min() and max() over a datetime column with nulls ignore the nulls")]
    public void NullValues_DatetimeColumn()
    {
        Table table = new SummarizeOperatorOpusTableBuilder("Dates")
            .WithColumn<DateTime>("dt")
            .WithRow(new DateTime(2024, 1, 3))
            .WithRow(null)
            .WithRow(new DateTime(2024, 1, 1))
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Dates | summarize count(), min(dt), max(dt)");

        AssertColumnsInOrder(row, "count_", "min_dt", "max_dt");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(3L));
        Assert.That(GetValue<DateTime>(row, "min_dt"), Is.EqualTo(new DateTime(2024, 1, 1)));
        Assert.That(GetValue<DateTime>(row, "max_dt"), Is.EqualTo(new DateTime(2024, 1, 3)));
    }

    #endregion

    #region empty strings

    [Test]
    [Description("min() and max() of strings skip empty strings, but dcount(), make_list() and make_set() keep them")]
    public void EmptyStrings_MinMaxSkipThem_DcountAndListsKeepThem()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "", "a", "", "b" }, "s", "Words");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Words | summarize min(s), max(s), dcount(s), make_list(s), make_set(s), count()");

        AssertColumnsInOrder(row, "min_s", "max_s", "dcount_s", "list_s", "set_s", "count_");
        Assert.That(GetValue<string>(row, "min_s"), Is.EqualTo("a"));
        Assert.That(GetValue<string>(row, "max_s"), Is.EqualTo("b"));
        Assert.That(GetValue<long>(row, "dcount_s"), Is.EqualTo(3L));
        Assert.That(GetDynamicArray(row, "list_s"), Is.EquivalentTo(new object?[] { "", "a", "", "b" }));
        Assert.That(GetDynamicArray(row, "set_s"), Is.EquivalentTo(new object?[] { "", "a", "b" }));
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(4L));
    }

    #endregion

    #region take_any

    [Test]
    [Description("take_any() of a single column prefers a non-null / non-empty value, independently per aggregate")]
    public void TakeAny_PrefersNonNullAndNonEmptyValues()
    {
        // the builder shuffles the rows, so repeat to make an order-dependent implementation fail
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Table table = new SummarizeOperatorOpusTableBuilder("Sparse")
                .WithColumn<string>("g")
                .WithColumn<long>("x")
                .WithColumn<string>("s")
                .WithRow("a", null, "")
                .WithRow("a", 7L, "")
                .WithRow("a", null, "z")
                .WithRow("a", null, "")
                .WithRow("b", null, "")
                .WithRow("b", null, "")
                .Build();
            var kustoDatabase = CreateDatabase(table);

            var rows = ExecuteAndGetRows(kustoDatabase, "Sparse | summarize take_any(x), take_any(s) by g");

            Assert.That(rows, Has.Count.EqualTo(2));
            AssertColumnsInOrder(rows[0], "g", "x", "s");
            var a = GetSingleRowWhere(rows, "g", "a");
            Assert.That(GetValue<long>(a, "x"), Is.EqualTo(7L));
            Assert.That(GetValue<string>(a, "s"), Is.EqualTo("z"));
            var b = GetSingleRowWhere(rows, "g", "b");
            AssertIsNull(b, "x");
            Assert.That(GetValue<string>(b, "s"), Is.Empty);

            var row = ExecuteAndGetSingleRow(kustoDatabase, "Sparse | summarize take_any(x), take_any(s)");
            Assert.That(GetValue<long>(row, "x"), Is.EqualTo(7L));
            Assert.That(GetValue<string>(row, "s"), Is.EqualTo("z"));
        }
    }

    #endregion

    #region arg_max / arg_min

    [Test]
    [Description("arg_max() ignores rows where the maximized value is null; for an all-null group it returns one of the rows")]
    public void ArgMax_IgnoresNullValues()
    {
        var kustoDatabase = CreateDatabase(BuildTaggedTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Tagged | summarize arg_max(Value, Tag) by Sensor");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "Sensor", "Value", "Tag");

        var a = GetSingleRowWhere(rows, "Sensor", "a");
        Assert.That(GetValue<long>(a, "Value"), Is.EqualTo(3L));
        Assert.That(GetValue<string>(a, "Tag"), Is.EqualTo("t3"));

        var b = GetSingleRowWhere(rows, "Sensor", "b");
        AssertIsNull(b, "Value");
        Assert.That(GetValue<string>(b, "Tag"), Is.AnyOf("t4", "t5"));

        var c = GetSingleRowWhere(rows, "Sensor", "c");
        Assert.That(GetValue<long>(c, "Value"), Is.EqualTo(5L));
        Assert.That(GetValue<string>(c, "Tag"), Is.EqualTo("t6"));
    }

    [Test]
    [Description("arg_min() ignores rows where the minimized value is null (null is not the minimum)")]
    public void ArgMin_IgnoresNullValues()
    {
        var kustoDatabase = CreateDatabase(BuildTaggedTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Tagged | summarize arg_min(Value, Tag) by Sensor");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "Sensor", "Value", "Tag");

        var a = GetSingleRowWhere(rows, "Sensor", "a");
        Assert.That(GetValue<long>(a, "Value"), Is.EqualTo(1L));
        Assert.That(GetValue<string>(a, "Tag"), Is.EqualTo("t1"));

        var b = GetSingleRowWhere(rows, "Sensor", "b");
        AssertIsNull(b, "Value");
        Assert.That(GetValue<string>(b, "Tag"), Is.AnyOf("t4", "t5"));

        var c = GetSingleRowWhere(rows, "Sensor", "c");
        Assert.That(GetValue<long>(c, "Value"), Is.EqualTo(5L));
        Assert.That(GetValue<string>(c, "Tag"), Is.EqualTo("t6"));
    }

    #endregion

    #region null and empty group keys

    [Test]
    [Description("Null is a group key value of its own (long key)")]
    public void NullKeys_LongKeyFormsOwnGroup()
    {
        Table table = new SummarizeOperatorOpusTableBuilder("Keys")
            .WithColumn<long>("k")
            .WithColumn<long>("v")
            .WithRow(1L, 10L)
            .WithRow(null, 20L)
            .WithRow(1L, 30L)
            .WithRow(null, 40L)
            .WithRow(2L, 50L)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Keys | summarize sum(v), count() by k");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "k", "sum_v", "count_");
        var actual = rows.Select(row => (
            GetNullableValue<long>(row, "k"),
            GetValue<long>(row, "sum_v"),
            GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(long?, long, long)>
        {
            (1L, 40L, 2L), (null, 60L, 2L), (2L, 50L, 1L)
        }));
    }

    [Test]
    [Description("A bool key has three groups: true, false and null")]
    public void NullKeys_BoolKey()
    {
        Table table = new SummarizeOperatorOpusTableBuilder("Flags")
            .WithColumn<bool>("flag")
            .WithColumn<long>("v")
            .WithRow(true, 1L)
            .WithRow(false, 2L)
            .WithRow(null, 4L)
            .WithRow(true, 8L)
            .WithRow(null, 16L)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Flags | summarize sum(v) by flag");

        Assert.That(rows, Has.Count.EqualTo(3));
        var actual = rows.Select(row => (
            GetNullableValue<bool>(row, "flag"),
            GetValue<long>(row, "sum_v"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(bool?, long)> { (true, 9L), (false, 2L), (null, 20L) }));
    }

    [Test]
    [Description("Null in one of several keys: (a, null), (a, 1) and (b, null) are different groups")]
    public void NullKeys_MultipleKeys()
    {
        Table table = new SummarizeOperatorOpusTableBuilder("Pairs")
            .WithColumn<string>("g")
            .WithColumn<long>("k")
            .WithRow("a", null)
            .WithRow("a", 1L)
            .WithRow("a", null)
            .WithRow("b", null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Pairs | summarize count() by g, k");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "g", "k", "count_");
        var actual = rows.Select(row => (
            GetValue<string>(row, "g"),
            GetNullableValue<long>(row, "k"),
            GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long?, long)>
        {
            ("a", null, 2L), ("a", 1L, 1L), ("b", null, 1L)
        }));
    }

    [Test]
    [Description("bin() of null is null, so null values form their own bin")]
    public void NullKeys_BinOfNullIsNullGroup()
    {
        Table table = new SummarizeOperatorOpusTableBuilder("Bins")
            .WithColumn<long>("k")
            .WithRow(1L)
            .WithRow(12L)
            .WithRow(null)
            .WithRow(15L)
            .WithRow(null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Bins | summarize count() by bin(k, 10)");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "k", "count_");
        var actual = rows.Select(row => (
            GetNullableValue<long>(row, "k"),
            GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(long?, long)> { (0L, 1L), (10L, 2L), (null, 2L) }));
    }

    [Test]
    [Description("Null is a group key value of its own (datetime key)")]
    public void NullKeys_DatetimeKey()
    {
        Table table = new SummarizeOperatorOpusTableBuilder("Moments")
            .WithColumn<DateTime>("dt")
            .WithRow(new DateTime(2024, 1, 1))
            .WithRow(null)
            .WithRow(new DateTime(2024, 1, 1))
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Moments | summarize count() by dt");

        Assert.That(rows, Has.Count.EqualTo(2));
        var actual = rows.Select(row => (
            GetNullableValue<DateTime>(row, "dt"),
            GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(DateTime?, long)>
        {
            (new DateTime(2024, 1, 1), 2L), (null, 1L)
        }));
    }

    [Test]
    [Description("The empty string is a group key value of its own")]
    public void EmptyStringKey_FormsOwnGroup()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "", "a", "", "b" }, "s", "Words");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "s",
            columnName2: "count_",
            kustoDatabase,
            query: "Words | summarize count() by s");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("", 2L), ("a", 1L), ("b", 1L) }));
    }

    [Test]
    [Description("'summarize by k' returns the distinct key values, null included")]
    public void NullKeys_ByOnlyReturnsNullAsDistinctValue()
    {
        Table table = new SummarizeOperatorOpusTableBuilder("Keys")
            .WithColumn<long>("k")
            .WithRow(1L)
            .WithRow(null)
            .WithRow(1L)
            .WithRow(null)
            .WithRow(2L)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Keys | summarize by k");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "k");
        Assert.That(rows.Select(row => GetNullableValue<long>(row, "k")).ToList(),
            Is.EquivalentTo(new long?[] { 1L, null, 2L }));

        var countRow = ExecuteAndGetSingleRow(kustoDatabase, "Keys | summarize by k | count");
        Assert.That(GetValue<long>(countRow, "Count"), Is.EqualTo(3L));
    }

    #endregion
}
