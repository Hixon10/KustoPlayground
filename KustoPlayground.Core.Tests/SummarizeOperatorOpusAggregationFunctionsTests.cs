using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Aggregation functions inside summarize (https://learn.microsoft.com/en-us/kusto/query/aggregation-functions).
/// Every test pins the value and the exact result type that Kusto produces
/// (for example sum(int) is long, avg(long) is real, avg(decimal) is decimal, min(int) stays int).
/// </summary>
public class SummarizeOperatorOpusAggregationFunctionsTests
{
    // Numbers(x: long, y: long): x = 1..10, y = x % 3 = 1, 2, 0, 1, 2, 0, 1, 2, 0, 1
    private static Table BuildOneToTenTable()
    {
        var rows = Enumerable.Range(1, 10).Select(i => ((long)i, (long)(i % 3))).ToList();
        return TestUtils.GenerateTableWith2Columns(rows, "x", "y", "Numbers");
    }

    // Outliers(x: long) = 2, 4, 4, 4, 5, 5, 7, 9, 1000
    private static Table BuildOutliersTable()
    {
        return TestUtils.GenerateTableWithColumn(new[] { 2L, 4L, 4L, 4L, 5L, 5L, 7L, 9L, 1000L }, "x", "Outliers");
    }

    // Bits(x: long, w: long) = (12, 1), (10, 1), (14, 8)
    private static Table BuildBitsTable()
    {
        var rows = new List<(long, long)> { (12L, 1L), (10L, 1L), (14L, 8L) };
        return TestUtils.GenerateTableWith2Columns(rows, "x", "w", "Bits");
    }

    // Thousand(x: long, y: long): x = 1..1000, y = x % 7
    private static Table BuildThousandTable()
    {
        var rows = Enumerable.Range(1, 1000).Select(i => ((long)i, (long)(i % 7))).ToList();
        return TestUtils.GenerateTableWith2Columns(rows, "x", "y", "Thousand");
    }

    #region count, countif

    [Test]
    [Description("count() - number of rows per group, type long")]
    public void Count_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "count_",
            kustoDatabase,
            query: "Sales | summarize count() by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 3L), ("West", 4L) }));
    }

    [Test]
    [Description("countif() - counts matching rows per group; a group without matches still appears with 0")]
    public void Countif_PerGroup_IncludesGroupWithoutMatches()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "Big",
            kustoDatabase,
            query: "Sales | summarize Big = countif(Amount >= 10) by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 2L), ("West", 0L) }));
    }

    [Test]
    [Description("countif() - unnamed countif with a comparison is named countif_")]
    public void Countif_DefaultName()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize countif(Amount > 5)");

        AssertColumnsInOrder(row, "countif_");
        Assert.That(GetValue<long>(row, "countif_"), Is.EqualTo(3L));
    }

    [Test]
    [Description("countif() - string equality predicate")]
    public void Countif_StringEqualityPredicate()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "Apples",
            kustoDatabase,
            query: "Sales | summarize Apples = countif(Product == \"Apple\") by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 2L), ("West", 1L) }));
    }

    [Test]
    [Description("countif() - predicate combined with 'and'")]
    public void Countif_CompoundPredicate()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Count",
            kustoDatabase,
            "Sales | summarize Count = countif(Product == \"Pear\" and Amount > 1)");

        Assert.That(actualData, Is.EqualTo(new List<long> { 3L }));
    }

    [Test]
    [Description("countif() - a bool column as predicate; the result is named countif_<column>")]
    public void Countif_BoolColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { true, false, true }, "b", "Flags");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Flags | summarize countif(b)");

        AssertColumnsInOrder(row, "countif_b");
        Assert.That(GetValue<long>(row, "countif_b"), Is.EqualTo(2L));
    }

    [Test]
    [Description("countif() - predicate that uses a scalar function")]
    public void Countif_PredicateWithFunction()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "LongNames",
            kustoDatabase,
            query: "Sales | summarize LongNames = countif(strlen(Product) > 4) by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 2L), ("West", 1L) }));
    }

    #endregion

    #region sum, sumif

    [Test]
    [Description("sum() - long column per group, type long")]
    public void Sum_LongColumn_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize sum(Amount) by Region");

        AssertColumnsInOrder(rows[0], "Region", "sum_Amount");
        var actual = rows.Select(row => (GetValue<string>(row, "Region"), GetValue<long>(row, "sum_Amount"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("East", 35L), ("West", 13L) }));
    }

    [Test]
    [Description("sum() - int column is summed as long")]
    public void Sum_IntColumn_ReturnsLong()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1, 2, 3 }, "i", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize sum(i)");

        Assert.That(GetValue<long>(row, "sum_i"), Is.EqualTo(6L));
    }

    [Test]
    [Description("sum() - real column, type real")]
    public void Sum_RealColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1.5, 2.25, -0.75 }, "d", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize sum(d)");

        Assert.That(GetValue<double>(row, "sum_d"), Is.EqualTo(3.0).Within(1e-12));
    }

    [Test]
    [Description("sum() - decimal column, type decimal")]
    public void Sum_DecimalColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1.5m, -2.25m, 3m }, "dec", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize sum(dec)");

        Assert.That(GetValue<decimal>(row, "sum_dec"), Is.EqualTo(2.25m));
    }

    [Test]
    [Description("sum() - timespan column, type timespan")]
    public void Sum_TimeSpanColumn()
    {
        var values = new[] { TimeSpan.FromHours(1), TimeSpan.FromHours(3), TimeSpan.FromMinutes(30) };
        Table table = TestUtils.GenerateTableWithColumn(values, "ts", "Durations");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Durations | summarize sum(ts)");

        Assert.That(GetValue<TimeSpan>(row, "sum_ts"), Is.EqualTo(TimeSpan.FromMinutes(270)));
    }

    [Test]
    [Description("sum() - negative values")]
    public void Sum_NegativeValues()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { -5L, 3L, -10L }, "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize Total = sum(x)");

        Assert.That(GetValue<long>(row, "Total"), Is.EqualTo(-12L));
    }

    [Test]
    [Description("sumif() - sums only the rows matching the predicate, per group")]
    public void Sumif_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "AppleAmount",
            kustoDatabase,
            query: "Sales | summarize AppleAmount = sumif(Amount, Product == \"Apple\") by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 30L), ("West", 7L) }));
    }

    [Test]
    [Description("sumif() - no matching rows gives 0, not null")]
    public void Sumif_NoMatchingRows_ReturnsZero()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "Huge",
            kustoDatabase,
            query: "Sales | summarize Huge = sumif(Amount, Amount > 100) by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 0L), ("West", 0L) }));
    }

    [Test]
    [Description("sumif() - default name is sumif_<column>")]
    public void Sumif_DefaultName()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize sumif(x, x > 5)");

        AssertColumnsInOrder(row, "sumif_x");
        Assert.That(GetValue<long>(row, "sumif_x"), Is.EqualTo(40L));
    }

    #endregion

    #region avg, avgif

    [Test]
    [Description("avg() - average of a long column is real")]
    public void Avg_LongColumn_ReturnsReal()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize avg(Amount) by Region");

        AssertColumnsInOrder(rows[0], "Region", "avg_Amount");
        Assert.That(GetValue<double>(GetSingleRowWhere(rows, "Region", "East"), "avg_Amount"),
            Is.EqualTo(35.0 / 3).Within(1e-9));
        Assert.That(GetValue<double>(GetSingleRowWhere(rows, "Region", "West"), "avg_Amount"),
            Is.EqualTo(3.25).Within(1e-9));
    }

    [Test]
    [Description("avg() - average of an int column is real")]
    public void Avg_IntColumn_ReturnsReal()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1, 2 }, "i", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize avg(i)");

        Assert.That(GetValue<double>(row, "avg_i"), Is.EqualTo(1.5));
    }

    [Test]
    [Description("avg() - real column")]
    public void Avg_RealColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1.5, 2.25, -0.75 }, "d", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize avg(d)");

        Assert.That(GetValue<double>(row, "avg_d"), Is.EqualTo(1.0).Within(1e-12));
    }

    [Test]
    [Description("avg() - decimal column keeps the decimal type")]
    public void Avg_DecimalColumn_ReturnsDecimal()
    {
        var tableRows = new List<(string, decimal)> { ("a", 1.5m), ("a", -2.25m), ("a", 3m), ("b", 1.5m), ("b", 2.5m) };
        Table table = TestUtils.GenerateTableWith2Columns(tableRows, "g", "dec", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, decimal>(
            columnName1: "g",
            columnName2: "avg_dec",
            kustoDatabase,
            query: "Numbers | summarize avg(dec) by g");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, decimal)> { ("a", 0.75m), ("b", 2m) }));
    }

    [Test]
    [Description("avg() - timespan column keeps the timespan type")]
    public void Avg_TimeSpanColumn_ReturnsTimeSpan()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { TimeSpan.FromHours(1), TimeSpan.FromHours(3) },
            "ts", "Durations");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Durations | summarize avg(ts)");

        Assert.That(GetValue<TimeSpan>(row, "avg_ts"), Is.EqualTo(TimeSpan.FromHours(2)));
    }

    [Test]
    [Description("avg() - datetime column keeps the datetime type")]
    public void Avg_DateTimeColumn_ReturnsDateTime()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { new DateTime(2024, 1, 1), new DateTime(2024, 1, 3) },
            "dt", "Times");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Times | summarize avg(dt)");

        Assert.That(GetValue<DateTime>(row, "avg_dt"), Is.EqualTo(new DateTime(2024, 1, 2)));
    }

    [Test]
    [Description("avgif() - average of the matching rows only")]
    public void Avgif_AveragesMatchingRows()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize avgif(x, x > 5)");

        AssertColumnsInOrder(row, "avgif_x");
        Assert.That(GetValue<double>(row, "avgif_x"), Is.EqualTo(8.0));
    }

    #endregion

    #region min, max, minif, maxif

    [Test]
    [Description("min()/max() - long column per group")]
    public void MinMax_LongColumn_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            columnName1: "Region",
            columnName2: "min_Amount",
            columnName3: "max_Amount",
            kustoDatabase,
            query: "Sales | summarize min(Amount), max(Amount) by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("East", 5L, 20L), ("West", 1L, 7L)
        }));
    }

    [Test]
    [Description("min()/max() - int column keeps the int type")]
    public void MinMax_IntColumn_KeepsInt()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 3, -1, 7 }, "i", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize min(i), max(i)");

        Assert.That(GetValue<int>(row, "min_i"), Is.EqualTo(-1));
        Assert.That(GetValue<int>(row, "max_i"), Is.EqualTo(7));
    }

    [Test]
    [Description("min()/max() - real column")]
    public void MinMax_RealColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 2.5, -1.25, 0.0 }, "d", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize min(d), max(d)");

        Assert.That(GetValue<double>(row, "min_d"), Is.EqualTo(-1.25));
        Assert.That(GetValue<double>(row, "max_d"), Is.EqualTo(2.5));
    }

    [Test]
    [Description("min()/max() - decimal column")]
    public void MinMax_DecimalColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1.5m, -2.25m, 3m }, "dec", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize min(dec), max(dec)");

        Assert.That(GetValue<decimal>(row, "min_dec"), Is.EqualTo(-2.25m));
        Assert.That(GetValue<decimal>(row, "max_dec"), Is.EqualTo(3m));
    }

    [Test]
    [Description("min()/max() - datetime column per group (StormEvents)")]
    public void MinMax_DateTimeColumn_PerGroup()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, DateTime, DateTime>(
            columnName1: "State",
            columnName2: "First",
            columnName3: "Last",
            kustoDatabase,
            query: "StormEvents | summarize First = min(StartTime), Last = max(StartTime) by State");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, DateTime, DateTime)>
        {
            ("FLORIDA", new DateTime(2024, 6, 1, 16, 50, 30), new DateTime(2025, 8, 23, 6, 20, 0)),
            ("TEXAS", new DateTime(2023, 3, 28, 10, 30, 0), new DateTime(2023, 3, 28, 10, 30, 0))
        }));
    }

    [Test]
    [Description("min()/max() - timespan column")]
    public void MinMax_TimeSpanColumn()
    {
        var values = new[] { TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(30), TimeSpan.FromHours(3) };
        Table table = TestUtils.GenerateTableWithColumn(values, "ts", "Durations");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Durations | summarize min(ts), max(ts)");

        Assert.That(GetValue<TimeSpan>(row, "min_ts"), Is.EqualTo(TimeSpan.FromMinutes(30)));
        Assert.That(GetValue<TimeSpan>(row, "max_ts"), Is.EqualTo(TimeSpan.FromHours(3)));
    }

    [Test]
    [Description("min()/max() - strings are compared ordinally (case-sensitive: 'B' < 'a' < 'b')")]
    public void MinMax_StringColumn_OrdinalComparison()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "b", "a", "B" }, "s", "Strings");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Strings | summarize min(s), max(s)");

        Assert.That(GetValue<string>(row, "min_s"), Is.EqualTo("B"));
        Assert.That(GetValue<string>(row, "max_s"), Is.EqualTo("b"));
    }

    [Test]
    [Description("min()/max() - bool column (false < true)")]
    public void MinMax_BoolColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { true, false, true }, "b", "Flags");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Flags | summarize min(b), max(b)");

        Assert.That(GetValue<bool>(row, "min_b"), Is.False);
        Assert.That(GetValue<bool>(row, "max_b"), Is.True);
    }

    [Test]
    [Description("minif()/maxif() - minimum and maximum of the matching rows; default names minif_x and maxif_x")]
    public void MinifMaxif_DefaultNames()
    {
        var kustoDatabase = CreateDatabase(BuildBitsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Bits | summarize minif(x, x > 10), maxif(x, x < 13)");

        AssertColumnsInOrder(row, "minif_x", "maxif_x");
        Assert.That(GetValue<long>(row, "minif_x"), Is.EqualTo(12L));
        Assert.That(GetValue<long>(row, "maxif_x"), Is.EqualTo(12L));
    }

    [Test]
    [Description("minif()/maxif() - per group with string predicates")]
    public void MinifMaxif_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            columnName1: "Region",
            columnName2: "CheapestApple",
            columnName3: "PriciestPear",
            kustoDatabase,
            query: "Sales | summarize CheapestApple = minif(Amount, Product == \"Apple\"), " +
                   "PriciestPear = maxif(Amount, Product == \"Pear\") by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("East", 10L, 5L), ("West", 7L, 3L)
        }));
    }

    #endregion

    #region dcount, dcountif, count_distinct, count_distinctif

    [Test]
    [Description("dcount() - number of distinct values per group")]
    public void Dcount_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            columnName1: "Region",
            columnName2: "Products",
            columnName3: "Amounts",
            kustoDatabase,
            query: "Sales | summarize Products = dcount(Product), Amounts = dcount(Amount) by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("East", 2L, 3L), ("West", 2L, 4L)
        }));
    }

    [Test]
    [Description("dcount() - exact for small cardinalities (1000 distinct values and 7 distinct values)")]
    public void Dcount_ExactForSmallCardinalities()
    {
        var kustoDatabase = CreateDatabase(BuildThousandTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Thousand | summarize dcount(x), dcount(y)");

        AssertColumnsInOrder(row, "dcount_x", "dcount_y");
        Assert.That(GetValue<long>(row, "dcount_x"), Is.EqualTo(1000L));
        Assert.That(GetValue<long>(row, "dcount_y"), Is.EqualTo(7L));
    }

    [Test]
    [Description("dcount() - strings are distinct case-sensitively")]
    public void Dcount_StringsAreCaseSensitive()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "a", "A", "a", "b" }, "s", "Strings");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Strings | summarize dcount(s)");

        Assert.That(GetValue<long>(row, "dcount_s"), Is.EqualTo(3L));
    }

    [Test]
    [Description("dcount() - the optional accuracy argument does not change small results; repeated names get a suffix")]
    public void Dcount_WithAccuracyLevel()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize dcount(x, 0), dcount(x, 4)");

        AssertColumnsInOrder(row, "dcount_x", "dcount_x1");
        Assert.That(GetValue<long>(row, "dcount_x"), Is.EqualTo(10L));
        Assert.That(GetValue<long>(row, "dcount_x1"), Is.EqualTo(10L));
    }

    [Test]
    [Description("dcountif() - distinct values among the matching rows")]
    public void Dcountif_CountsDistinctMatchingValues()
    {
        var kustoDatabase = CreateDatabase(BuildThousandTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Thousand | summarize dcountif(x, x > 500)");

        AssertColumnsInOrder(row, "dcountif_x");
        Assert.That(GetValue<long>(row, "dcountif_x"), Is.EqualTo(500L));
    }

    [Test]
    [Description("count_distinct() - exact number of distinct values")]
    public void CountDistinct_Exact()
    {
        var kustoDatabase = CreateDatabase(BuildThousandTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Thousand | summarize count_distinct(x), count_distinct(y)");

        AssertColumnsInOrder(row, "count_distinct_x", "count_distinct_y");
        Assert.That(GetValue<long>(row, "count_distinct_x"), Is.EqualTo(1000L));
        Assert.That(GetValue<long>(row, "count_distinct_y"), Is.EqualTo(7L));
    }

    [Test]
    [Description("count_distinctif() - distinct values of y among rows with x > 995 (x = 996..1000 gives y = 2..6)")]
    public void CountDistinctif_CountsDistinctMatchingValues()
    {
        var kustoDatabase = CreateDatabase(BuildThousandTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Thousand | summarize count_distinctif(y, x > 995)");

        AssertColumnsInOrder(row, "count_distinctif_y");
        Assert.That(GetValue<long>(row, "count_distinctif_y"), Is.EqualTo(5L));
    }

    [Test]
    [Description("count_distinct() - per group")]
    public void CountDistinct_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, long>(
            columnName1: "y",
            columnName2: "count_distinct_x",
            kustoDatabase,
            query: "Numbers | summarize count_distinct(x) by y");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long)> { (0L, 3L), (1L, 4L), (2L, 3L) }));
    }

    #endregion

    #region take_any, take_anyif

    [Test]
    [Description("take_any() - returns one of the values of the group; the result column keeps the column name")]
    public void TakeAny_ReturnsValueOfTheGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize take_any(Amount) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Amount");
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "Region", "East"), "Amount"), Is.AnyOf(10L, 20L, 5L));
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "Region", "West"), "Amount"), Is.AnyOf(7L, 1L, 2L, 3L));
    }

    [Test]
    [Description("take_any() - with several columns all values come from the same input row")]
    public void TakeAny_SeveralColumns_ComeFromSameRow()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize take_any(Product, Amount) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Product", "Amount");
        var salesRows = new List<(string, string, long)>
        {
            ("East", "Apple", 10L), ("East", "Apple", 20L), ("East", "Pear", 5L), ("West", "Apple", 7L),
            ("West", "Pear", 1L), ("West", "Pear", 2L), ("West", "Pear", 3L)
        };
        foreach (var row in rows)
        {
            var actual = (GetValue<string>(row, "Region"), GetValue<string>(row, "Product"),
                GetValue<long>(row, "Amount"));
            Assert.That(salesRows, Does.Contain(actual));
        }
    }

    [Test]
    [Description("take_any(*) - returns a whole row of the group: the keys, then all other columns")]
    public void TakeAny_Star()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize take_any(*) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        var salesRows = new List<(string, string, long)>
        {
            ("East", "Apple", 10L), ("East", "Apple", 20L), ("East", "Pear", 5L), ("West", "Apple", 7L),
            ("West", "Pear", 1L), ("West", "Pear", 2L), ("West", "Pear", 3L)
        };
        foreach (var row in rows)
        {
            AssertColumnsStartWith(row, "Region");
            AssertColumnSet(row, "Region", "Product", "Amount");
            var actual = (GetValue<string>(row, "Region"), GetValue<string>(row, "Product"),
                GetValue<long>(row, "Amount"));
            Assert.That(salesRows, Does.Contain(actual));
        }
    }

    [Test]
    [Description("take_any() - named result")]
    public void TakeAny_Named()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize Sample = take_any(Product) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Sample");
        Assert.That(GetValues<string>(rows, "Sample"), Is.All.AnyOf("Apple", "Pear"));
    }

    [Test]
    [Description("take_anyif() - returns a value of a matching row; the result column keeps the column name")]
    public void TakeAnyif_ReturnsMatchingValue()
    {
        var kustoDatabase = CreateDatabase(BuildBitsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Bits | summarize take_anyif(x, x > 13)");

        AssertColumnsInOrder(row, "x");
        Assert.That(GetValue<long>(row, "x"), Is.EqualTo(14L));
    }

    [Test]
    [Description("take_anyif() - per group; a group without matching rows gets null")]
    public void TakeAnyif_PerGroup_NullWhenNothingMatches()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize BigOrder = take_anyif(Amount, Amount >= 10) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "Region", "East"), "BigOrder"), Is.AnyOf(10L, 20L));
        AssertIsNull(GetSingleRowWhere(rows, "Region", "West"), "BigOrder");
    }

    #endregion

    #region arg_max, arg_min

    [Test]
    [Description("arg_max() - row with the maximum per group: the maximized column, then the requested columns")]
    public void ArgMax_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize arg_max(Amount, Product) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Amount", "Product");
        var actual = rows.Select(row => (
            GetValue<string>(row, "Region"),
            GetValue<long>(row, "Amount"),
            GetValue<string>(row, "Product"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long, string)>
        {
            ("East", 20L, "Apple"), ("West", 7L, "Apple")
        }));
    }

    [Test]
    [Description("arg_min() - row with the minimum per group")]
    public void ArgMin_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, string>(
            columnName1: "Region",
            columnName2: "Amount",
            columnName3: "Product",
            kustoDatabase,
            query: "Sales | summarize arg_min(Amount, Product) by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, string)>
        {
            ("East", 5L, "Pear"), ("West", 1L, "Pear")
        }));
    }

    [Test]
    [Description("arg_max() - without 'by' over the whole table, returning several columns")]
    public void ArgMax_WithoutBy()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize arg_max(Amount, Region, Product)");

        AssertColumnsInOrder(row, "Amount", "Region", "Product");
        Assert.That(GetValue<long>(row, "Amount"), Is.EqualTo(20L));
        Assert.That(GetValue<string>(row, "Region"), Is.EqualTo("East"));
        Assert.That(GetValue<string>(row, "Product"), Is.EqualTo("Apple"));
    }

    [Test]
    [Description("arg_max(x, *) - returns the whole row: the keys, the maximized column, then all other columns")]
    public void ArgMax_Star()
    {
        var tableRows = new List<(string, string, long, bool)>
        {
            ("one", "a", 1L, true), ("three", "a", 3L, false), ("two", "b", 2L, true), ("zero", "b", 0L, false)
        };
        Table table = TestUtils.GenerateTableWith4Columns(tableRows, "s", "g", "x", "b", "Items");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Items | summarize arg_max(x, *) by g");

        Assert.That(rows, Has.Count.EqualTo(2));
        foreach (var row in rows)
        {
            AssertColumnsStartWith(row, "g", "x");
            AssertColumnSet(row, "g", "x", "s", "b");
        }

        var actual = rows.Select(row => (
            GetValue<string>(row, "g"),
            GetValue<long>(row, "x"),
            GetValue<string>(row, "s"),
            GetValue<bool>(row, "b"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long, string, bool)>
        {
            ("a", 3L, "three", false), ("b", 2L, "two", true)
        }));
    }

    [Test]
    [Description("arg_min(StartTime, *) - first storm event of every state")]
    public void ArgMin_Star_FirstEventPerState()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "StormEvents | summarize arg_min(StartTime, *) by State");

        Assert.That(rows, Has.Count.EqualTo(2));
        foreach (var row in rows)
        {
            AssertColumnsStartWith(row, "State", "StartTime");
            AssertColumnSet(row, "State", "StartTime", "EventType", "DamageProperty");
        }

        var actual = rows.Select(row => (
            GetValue<string>(row, "State"),
            GetValue<DateTime>(row, "StartTime"),
            GetValue<string>(row, "EventType"),
            GetValue<int>(row, "DamageProperty"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, DateTime, string, int)>
        {
            ("FLORIDA", new DateTime(2024, 6, 1, 16, 50, 30), "Tornado", 5000),
            ("TEXAS", new DateTime(2023, 3, 28, 10, 30, 0), "Flood", 5000)
        }));
    }

    [Test]
    [Description("arg_max(StartTime, EventType) - latest storm event of every state")]
    public void ArgMax_LatestEventPerState()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, DateTime, string>(
            columnName1: "State",
            columnName2: "StartTime",
            columnName3: "EventType",
            kustoDatabase,
            query: "StormEvents | summarize arg_max(StartTime, EventType) by State");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, DateTime, string)>
        {
            ("FLORIDA", new DateTime(2025, 8, 23, 6, 20, 0), "Hurricane"),
            ("TEXAS", new DateTime(2023, 3, 28, 10, 30, 0), "Flood")
        }));
    }

    [Test]
    [Description("arg_max() - with ties any of the tied rows may be returned, but the values come from one row")]
    public void ArgMax_Ties()
    {
        var tableRows = new List<(long, string)> { (5L, "a"), (5L, "b"), (3L, "c") };
        Table table = TestUtils.GenerateTableWith2Columns(tableRows, "x", "s", "Items");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Items | summarize arg_max(x, s)");

        Assert.That(GetValue<long>(row, "x"), Is.EqualTo(5L));
        Assert.That(GetValue<string>(row, "s"), Is.AnyOf("a", "b"));
    }

    [Test]
    [Description("arg_max() - the maximized value can be an expression; it is named strlen_<column>")]
    public void ArgMax_OfExpression()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "a", "bbb", "cc" }, "Word", "Words");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Words | summarize arg_max(strlen(Word), Word)");

        AssertColumnsInOrder(row, "strlen_Word", "Word");
        Assert.That(GetNumber(row, "strlen_Word"), Is.EqualTo(3.0));
        Assert.That(GetValue<string>(row, "Word"), Is.EqualTo("bbb"));
    }

    #endregion

    #region percentile, percentiles, percentilew, percentilesw, percentiles_array, percentilesw_array

    [Test]
    [Description("percentile() - nearest-rank on 1..4 (no interpolation); result keeps the long type")]
    public void Percentile_NearestRank_FourValues()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1L, 2L, 3L, 4L }, "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | summarize percentile(x, 0), percentile(x, 25), percentile(x, 50), percentile(x, 51), " +
            "percentile(x, 75), percentile(x, 100)");

        AssertColumnsInOrder(row, "percentile_x_0", "percentile_x_25", "percentile_x_50", "percentile_x_51",
            "percentile_x_75", "percentile_x_100");
        Assert.That(GetValue<long>(row, "percentile_x_0"), Is.EqualTo(1L));
        Assert.That(GetValue<long>(row, "percentile_x_25"), Is.EqualTo(1L));
        Assert.That(GetValue<long>(row, "percentile_x_50"), Is.EqualTo(2L));
        Assert.That(GetValue<long>(row, "percentile_x_51"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(row, "percentile_x_75"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(row, "percentile_x_100"), Is.EqualTo(4L));
    }

    [Test]
    [Description("percentile() - nearest-rank on 1..10")]
    public void Percentile_TenValues()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | summarize percentile(x, 10), percentile(x, 50), percentile(x, 90), percentile(x, 95)");

        Assert.That(GetValue<long>(row, "percentile_x_10"), Is.EqualTo(1L));
        Assert.That(GetValue<long>(row, "percentile_x_50"), Is.EqualTo(5L));
        Assert.That(GetValue<long>(row, "percentile_x_90"), Is.EqualTo(9L));
        Assert.That(GetValue<long>(row, "percentile_x_95"), Is.EqualTo(10L));
    }

    [Test]
    [Description("percentile() - 1..100, including a fractional percentage (99.9 is named percentile_x_99_9)")]
    public void Percentile_HundredValues()
    {
        Table table = TestUtils.GenerateTableWithColumn(Enumerable.Range(1, 100).Select(i => (long)i), "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | summarize percentile(x, 1), percentile(x, 25), percentile(x, 50), percentile(x, 75), " +
            "percentile(x, 99), percentile(x, 99.9)");

        AssertColumnsInOrder(row, "percentile_x_1", "percentile_x_25", "percentile_x_50", "percentile_x_75",
            "percentile_x_99", "percentile_x_99_9");
        Assert.That(GetValue<long>(row, "percentile_x_1"), Is.EqualTo(1L));
        Assert.That(GetValue<long>(row, "percentile_x_25"), Is.EqualTo(25L));
        Assert.That(GetValue<long>(row, "percentile_x_50"), Is.EqualTo(50L));
        Assert.That(GetValue<long>(row, "percentile_x_75"), Is.EqualTo(75L));
        Assert.That(GetValue<long>(row, "percentile_x_99"), Is.EqualTo(99L));
        Assert.That(GetValue<long>(row, "percentile_x_99_9"), Is.EqualTo(100L));
    }

    [Test]
    [Description("percentile() - fractional percentage 50.5 on 1..10 is named percentile_x_50_5")]
    public void Percentile_FractionalPercentage()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize percentile(x, 50.5)");

        AssertColumnsInOrder(row, "percentile_x_50_5");
        Assert.That(GetValue<long>(row, "percentile_x_50_5"), Is.EqualTo(6L));
    }

    [Test]
    [Description("percentile() - a single value is every percentile")]
    public void Percentile_SingleValue()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 5L }, "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | summarize percentile(x, 0), percentile(x, 50), percentile(x, 100)");

        Assert.That(GetValue<long>(row, "percentile_x_0"), Is.EqualTo(5L));
        Assert.That(GetValue<long>(row, "percentile_x_50"), Is.EqualTo(5L));
        Assert.That(GetValue<long>(row, "percentile_x_100"), Is.EqualTo(5L));
    }

    [Test]
    [Description("percentile() - real column returns a real value from the input (no interpolation)")]
    public void Percentile_RealColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1.0, 2.0, 3.0, 4.0 }, "d", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize percentile(d, 50)");

        Assert.That(GetValue<double>(row, "percentile_d_50"), Is.EqualTo(2.0));
    }

    [Test]
    [Description("percentile() - datetime column returns a datetime")]
    public void Percentile_DateTimeColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { new DateTime(2024, 1, 1), new DateTime(2024, 1, 3) },
            "dt", "Times");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Times | summarize percentile(dt, 50)");

        Assert.That(GetValue<DateTime>(row, "percentile_dt_50"), Is.EqualTo(new DateTime(2024, 1, 1)));
    }

    [Test]
    [Description("percentile() - timespan column returns a timespan")]
    public void Percentile_TimeSpanColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { TimeSpan.FromHours(1), TimeSpan.FromHours(3) },
            "ts", "Durations");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Durations | summarize percentile(ts, 50)");

        Assert.That(GetValue<TimeSpan>(row, "percentile_ts_50"), Is.EqualTo(TimeSpan.FromHours(1)));
    }

    [Test]
    [Description("percentile() - median per group (East: 5, 10, 20 -> 10; West: 1, 2, 3, 7 -> 2)")]
    public void Percentile_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "Median",
            kustoDatabase,
            query: "Sales | summarize Median = percentile(Amount, 50) by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 10L), ("West", 2L) }));
    }

    [Test]
    [Description("percentiles() - one column per requested percentage")]
    public void Percentiles_OneColumnPerPercentage()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize percentiles(x, 10, 50, 90)");

        AssertColumnsInOrder(row, "percentile_x_10", "percentile_x_50", "percentile_x_90");
        Assert.That(GetValue<long>(row, "percentile_x_10"), Is.EqualTo(1L));
        Assert.That(GetValue<long>(row, "percentile_x_50"), Is.EqualTo(5L));
        Assert.That(GetValue<long>(row, "percentile_x_90"), Is.EqualTo(9L));
    }

    [Test]
    [Description("percentiles() - the result columns can be renamed with a tuple, per group")]
    public void Percentiles_RenamedWithTuple_PerGroup()
    {
        var tableRows = new List<(string, long)> { ("a", 1L), ("a", 2L), ("a", 3L), ("a", 4L), ("b", 10L) };
        Table table = TestUtils.GenerateTableWith2Columns(tableRows, "g", "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize (p50, p90) = percentiles(x, 50, 90) by g");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "g", "p50", "p90");
        var actual = rows.Select(row => (
            GetValue<string>(row, "g"),
            GetValue<long>(row, "p50"),
            GetValue<long>(row, "p90"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long, long)> { ("a", 2L, 4L), ("b", 10L, 10L) }));
    }

    [Test]
    [Description("percentilew() - weighted percentile; named like percentile(): percentile_x_50")]
    public void Percentilew_Weighted()
    {
        var kustoDatabase = CreateDatabase(BuildBitsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Bits | summarize percentilew(x, w, 50)");

        AssertColumnsInOrder(row, "percentile_x_50");
        Assert.That(GetValue<long>(row, "percentile_x_50"), Is.EqualTo(14L));
    }

    [Test]
    [Description("percentile() vs percentilew() - the weight changes the median (12 unweighted, 14 weighted)")]
    public void Percentile_UnweightedVersusWeighted()
    {
        var kustoDatabase = CreateDatabase(BuildBitsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Bits | summarize Plain = percentile(x, 50), Weighted = percentilew(x, w, 50)");

        Assert.That(GetValue<long>(row, "Plain"), Is.EqualTo(12L));
        Assert.That(GetValue<long>(row, "Weighted"), Is.EqualTo(14L));
    }

    [Test]
    [Description("percentilesw() - several weighted percentiles, one column each")]
    public void Percentilesw_SeveralWeightedPercentiles()
    {
        var kustoDatabase = CreateDatabase(BuildBitsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Bits | summarize percentilesw(x, w, 10, 20, 50)");

        AssertColumnsInOrder(row, "percentile_x_10", "percentile_x_20", "percentile_x_50");
        Assert.That(GetValue<long>(row, "percentile_x_10"), Is.EqualTo(10L));
        Assert.That(GetValue<long>(row, "percentile_x_20"), Is.EqualTo(12L));
        Assert.That(GetValue<long>(row, "percentile_x_50"), Is.EqualTo(14L));
    }

    [Test]
    [Description("percentiles_array() - returns a dynamic array named percentiles_x")]
    public void PercentilesArray_ReturnsDynamicArray()
    {
        var kustoDatabase = CreateDatabase(BuildBitsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Bits | summarize percentiles_array(x, 0, 100)");

        AssertColumnsInOrder(row, "percentiles_x");
        Assert.That(GetDynamicArray(row, "percentiles_x"), Is.EqualTo(new object?[] { 10L, 14L }));
    }

    [Test]
    [Description("percentilesw_array() - weighted percentiles as a dynamic array")]
    public void PercentileswArray_ReturnsDynamicArray()
    {
        var kustoDatabase = CreateDatabase(BuildBitsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Bits | summarize percentilesw_array(x, w, 10, 20, 50)");

        AssertColumnsInOrder(row, "percentiles_x");
        Assert.That(GetDynamicArray(row, "percentiles_x"), Is.EqualTo(new object?[] { 10L, 12L, 14L }));
    }

    #endregion

    #region stdev, stdevp, variance, variancep, stdevif, varianceif, variancepif

    [Test]
    [Description("stdev()/variance() - sample statistics (divide by n - 1)")]
    public void StdevVariance_Sample()
    {
        var kustoDatabase = CreateDatabase(BuildOutliersTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Outliers | summarize stdev(x), variance(x)");

        AssertColumnsInOrder(row, "stdev_x", "variance_x");
        Assert.That(GetValue<double>(row, "stdev_x"), Is.EqualTo(331.67269676260327).Within(1e-6));
        Assert.That(GetValue<double>(row, "variance_x"), Is.EqualTo(110006.7777777778).Within(1e-6));
    }

    [Test]
    [Description("stdevp()/variancep() - population statistics (divide by n)")]
    public void StdevpVariancep_Population()
    {
        var kustoDatabase = CreateDatabase(BuildOutliersTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Outliers | summarize stdevp(x), variancep(x)");

        AssertColumnsInOrder(row, "stdevp_x", "variancep_x");
        Assert.That(GetValue<double>(row, "stdevp_x"), Is.EqualTo(312.7040173536883).Within(1e-6));
        Assert.That(GetValue<double>(row, "variancep_x"), Is.EqualTo(97783.8024691358).Within(1e-6));
    }

    [Test]
    [Description("stdevif()/varianceif()/variancepif() - statistics of the matching rows only")]
    public void StatisticsIf_OnlyMatchingRows()
    {
        var kustoDatabase = CreateDatabase(BuildOutliersTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Outliers | summarize stdevif(x, x < 100), varianceif(x, x < 100), variancepif(x, x < 100), " +
            "avgif(x, x < 100), sumif(x, x < 100)");

        AssertColumnsInOrder(row, "stdevif_x", "varianceif_x", "variancepif_x", "avgif_x", "sumif_x");
        Assert.That(GetValue<double>(row, "stdevif_x"), Is.EqualTo(2.1380899352927134).Within(1e-9));
        Assert.That(GetValue<double>(row, "varianceif_x"), Is.EqualTo(4.571428571428571).Within(1e-9));
        Assert.That(GetValue<double>(row, "variancepif_x"), Is.EqualTo(4.0).Within(1e-9));
        Assert.That(GetValue<double>(row, "avgif_x"), Is.EqualTo(5.0));
        Assert.That(GetValue<long>(row, "sumif_x"), Is.EqualTo(40L));
    }

    [Test]
    [Description("stdev family - all statistics on 1..10")]
    public void StatisticsFamily_OneToTen()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | summarize stdev(x), variance(x), stdevp(x), variancep(x), stdevif(x, x > 5), " +
            "varianceif(x, x > 5), variancepif(x, x > 5)");

        Assert.That(GetValue<double>(row, "stdev_x"), Is.EqualTo(3.0276503540974917).Within(1e-9));
        Assert.That(GetValue<double>(row, "variance_x"), Is.EqualTo(9.166666666666666).Within(1e-9));
        Assert.That(GetValue<double>(row, "stdevp_x"), Is.EqualTo(2.8722813232690143).Within(1e-9));
        Assert.That(GetValue<double>(row, "variancep_x"), Is.EqualTo(8.25).Within(1e-9));
        Assert.That(GetValue<double>(row, "stdevif_x"), Is.EqualTo(1.5811388300841898).Within(1e-9));
        Assert.That(GetValue<double>(row, "varianceif_x"), Is.EqualTo(2.5).Within(1e-9));
        Assert.That(GetValue<double>(row, "variancepif_x"), Is.EqualTo(2.0).Within(1e-9));
    }

    [Test]
    [Description("stdev family - a single value has zero deviation (not NaN, not null)")]
    public void StatisticsFamily_SingleValueIsZero()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 5L }, "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | summarize stdev(x), stdevp(x), variance(x), variancep(x)");

        Assert.That(GetValue<double>(row, "stdev_x"), Is.EqualTo(0.0));
        Assert.That(GetValue<double>(row, "stdevp_x"), Is.EqualTo(0.0));
        Assert.That(GetValue<double>(row, "variance_x"), Is.EqualTo(0.0));
        Assert.That(GetValue<double>(row, "variancep_x"), Is.EqualTo(0.0));
    }

    [Test]
    [Description("variance() - per group (East: 175/3, West: 83/12)")]
    public void Variance_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize Variance = variance(Amount) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(GetValue<double>(GetSingleRowWhere(rows, "Region", "East"), "Variance"),
            Is.EqualTo(175.0 / 3).Within(1e-9));
        Assert.That(GetValue<double>(GetSingleRowWhere(rows, "Region", "West"), "Variance"),
            Is.EqualTo(83.0 / 12).Within(1e-9));
    }

    #endregion

    #region make_list, make_set, make_list_if, make_set_if

    [Test]
    [Description("make_list() - all values of the group (with duplicates) as a dynamic array named list_<column>")]
    public void MakeList_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize make_list(Amount) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "list_Amount");
        Assert.That(GetDynamicArray(GetSingleRowWhere(rows, "Region", "East"), "list_Amount"),
            Is.EquivalentTo(new object?[] { 10L, 20L, 5L }));
        Assert.That(GetDynamicArray(GetSingleRowWhere(rows, "Region", "West"), "list_Amount"),
            Is.EquivalentTo(new object?[] { 7L, 1L, 2L, 3L }));
    }

    [Test]
    [Description("make_list() - keeps duplicate strings")]
    public void MakeList_KeepsDuplicates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize Products = make_list(Product) by Region");

        Assert.That(GetDynamicArray(GetSingleRowWhere(rows, "Region", "East"), "Products"),
            Is.EquivalentTo(new object?[] { "Apple", "Apple", "Pear" }));
        Assert.That(GetDynamicArray(GetSingleRowWhere(rows, "Region", "West"), "Products"),
            Is.EquivalentTo(new object?[] { "Apple", "Pear", "Pear", "Pear" }));
    }

    [Test]
    [Description("make_set() - distinct values of the group as a dynamic array named set_<column>")]
    public void MakeSet_RemovesDuplicates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize make_set(Product) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "set_Product");
        Assert.That(GetDynamicArray(GetSingleRowWhere(rows, "Region", "East"), "set_Product"),
            Is.EquivalentTo(new object?[] { "Apple", "Pear" }));
        Assert.That(GetDynamicArray(GetSingleRowWhere(rows, "Region", "West"), "set_Product"),
            Is.EquivalentTo(new object?[] { "Apple", "Pear" }));
    }

    [Test]
    [Description("make_set() - numbers")]
    public void MakeSet_Numbers()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1L, 1L, 2L, 2L, 3L }, "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize make_set(x)");

        Assert.That(GetDynamicArray(row, "set_x"), Is.EquivalentTo(new object?[] { 1L, 2L, 3L }));
    }

    [Test]
    [Description("make_set() - strings are distinct case-sensitively")]
    public void MakeSet_StringsAreCaseSensitive()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "a", "A", "a" }, "s", "Strings");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Strings | summarize make_set(s)");

        Assert.That(GetDynamicArray(row, "set_s"), Is.EquivalentTo(new object?[] { "a", "A" }));
    }

    [Test]
    [Description("make_list(x, 3) - the optional second argument limits the number of elements")]
    public void MakeList_MaxSize()
    {
        var kustoDatabase = CreateDatabase(BuildThousandTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Thousand | summarize make_list(x, 3)");

        List<object?> values = GetDynamicArray(row, "list_x");
        Assert.That(values, Has.Count.EqualTo(3));
        Assert.That(values, Is.Unique);
        Assert.That(values, Is.All.InRange(1L, 1000L));
    }

    [Test]
    [Description("make_set(y, 2) - the optional second argument limits the number of distinct elements")]
    public void MakeSet_MaxSize()
    {
        var kustoDatabase = CreateDatabase(BuildThousandTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Thousand | summarize Limited = make_set(y, 2), All = make_set(y)");

        List<object?> limited = GetDynamicArray(row, "Limited");
        Assert.That(limited, Has.Count.EqualTo(2));
        Assert.That(limited, Is.Unique);
        Assert.That(limited, Is.All.InRange(0L, 6L));
        Assert.That(GetDynamicArray(row, "All"), Is.EquivalentTo(new object?[] { 0L, 1L, 2L, 3L, 4L, 5L, 6L }));
    }

    [Test]
    [Description("make_list_if()/make_set_if() - only values of the matching rows; named list_<column>/set_<column>")]
    public void MakeListIfMakeSetIf()
    {
        var kustoDatabase = CreateDatabase(BuildBitsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Bits | summarize make_list_if(x, x > 10), make_set_if(w, x > 10)");

        AssertColumnsInOrder(row, "list_x", "set_w");
        Assert.That(GetDynamicArray(row, "list_x"), Is.EquivalentTo(new object?[] { 12L, 14L }));
        Assert.That(GetDynamicArray(row, "set_w"), Is.EquivalentTo(new object?[] { 1L, 8L }));
    }

    [Test]
    [Description("make_list() - real values")]
    public void MakeList_RealValues()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1.5, 2.5 }, "d", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize make_list(d)");

        Assert.That(GetDynamicArray(row, "list_d"), Is.EquivalentTo(new object?[] { 1.5, 2.5 }));
    }

    [Test]
    [Description("make_list() - without 'by' collects the whole column")]
    public void MakeList_WithoutBy()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Numbers | summarize Values = make_list(x)");

        Assert.That(GetDynamicArray(row, "Values"),
            Is.EquivalentTo(Enumerable.Range(1, 10).Select(i => (object?)(long)i)));
    }

    #endregion

    #region binary_all_and, binary_all_or, binary_all_xor

    [Test]
    [Description("binary_all_and()/binary_all_or()/binary_all_xor() - bitwise aggregation of 12, 10, 14")]
    public void BinaryAll_BitwiseAggregation()
    {
        var kustoDatabase = CreateDatabase(BuildBitsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Bits | summarize AllAnd = binary_all_and(x), AllOr = binary_all_or(x), AllXor = binary_all_xor(x)");

        Assert.That(GetValue<long>(row, "AllAnd"), Is.EqualTo(8L));
        Assert.That(GetValue<long>(row, "AllOr"), Is.EqualTo(14L));
        Assert.That(GetValue<long>(row, "AllXor"), Is.EqualTo(8L));
    }

    #endregion

    #region several aggregates and expressions

    [Test]
    [Description("Several aggregates in one summarize, per group")]
    public void SeveralAggregates_PerGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize Orders = count(), Total = sum(Amount), Average = avg(Amount), " +
            "Smallest = min(Amount), Largest = max(Amount), Products = dcount(Product) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Orders", "Total", "Average", "Smallest", "Largest", "Products");

        var east = GetSingleRowWhere(rows, "Region", "East");
        Assert.That(GetValue<long>(east, "Orders"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(east, "Total"), Is.EqualTo(35L));
        Assert.That(GetValue<double>(east, "Average"), Is.EqualTo(35.0 / 3).Within(1e-9));
        Assert.That(GetValue<long>(east, "Smallest"), Is.EqualTo(5L));
        Assert.That(GetValue<long>(east, "Largest"), Is.EqualTo(20L));
        Assert.That(GetValue<long>(east, "Products"), Is.EqualTo(2L));

        var west = GetSingleRowWhere(rows, "Region", "West");
        Assert.That(GetValue<long>(west, "Orders"), Is.EqualTo(4L));
        Assert.That(GetValue<long>(west, "Total"), Is.EqualTo(13L));
        Assert.That(GetValue<double>(west, "Average"), Is.EqualTo(3.25).Within(1e-9));
        Assert.That(GetValue<long>(west, "Smallest"), Is.EqualTo(1L));
        Assert.That(GetValue<long>(west, "Largest"), Is.EqualTo(7L));
        Assert.That(GetValue<long>(west, "Products"), Is.EqualTo(2L));
    }

    [Test]
    [Description("sum() of an arithmetic expression")]
    public void Sum_OfExpression()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize Doubled = sum(Amount * 2) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Region", "East"), "Doubled"), Is.EqualTo(70.0));
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Region", "West"), "Doubled"), Is.EqualTo(26.0));
    }

    [Test]
    [Description("max() of a function call is named max_strlen_<column>")]
    public void Max_OfFunctionCall()
    {
        var tableRows = new List<(string, string)> { ("fruit", "fig"), ("fruit", "banana"), ("veg", "kale"), ("veg", "pea") };
        Table table = TestUtils.GenerateTableWith2Columns(tableRows, "Kind", "Word", "Words");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Words | summarize max(strlen(Word)) by Kind");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Kind", "max_strlen_Word");
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Kind", "fruit"), "max_strlen_Word"), Is.EqualTo(6.0));
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Kind", "veg"), "max_strlen_Word"), Is.EqualTo(4.0));
    }

    [Test]
    [Description("Arithmetic over aggregates: max() - min()")]
    public void Arithmetic_OverAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize Range = max(Amount) - min(Amount) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Range");
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Region", "East"), "Range"), Is.EqualTo(15.0));
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Region", "West"), "Range"), Is.EqualTo(6.0));
    }

    [Test]
    [Description("Arithmetic that combines two aggregates: 1.0 * sum() / count()")]
    public void Arithmetic_CombiningTwoAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize Mean = 1.0 * sum(Amount) / count() by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Region", "East"), "Mean"), Is.EqualTo(35.0 / 3).Within(1e-9));
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Region", "West"), "Mean"), Is.EqualTo(3.25).Within(1e-9));
    }

    [Test]
    [Description("max(datetime) - min(datetime) is a timespan")]
    public void Arithmetic_DateTimeAggregatesGiveTimeSpan()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, TimeSpan>(
            columnName1: "State",
            columnName2: "Span",
            kustoDatabase,
            query: "StormEvents | summarize Span = max(StartTime) - min(StartTime) by State");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, TimeSpan)>
        {
            ("FLORIDA", new DateTime(2025, 8, 23, 6, 20, 0) - new DateTime(2024, 6, 1, 16, 50, 30)),
            ("TEXAS", TimeSpan.Zero)
        }));
    }

    #endregion
}
