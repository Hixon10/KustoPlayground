using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Result schema of summarize (https://learn.microsoft.com/en-us/kusto/query/summarize-operator):
/// default names of aggregates and group keys, suffixes for duplicate names, generated ColumnN names,
/// column order (keys first in 'by' order, then aggregates in declared order), tuple naming and result types.
/// </summary>
public class SummarizeOperatorOpusNamingAndSchemaTests
{
    // Bits(x: long, w: long) = (12, 1), (10, 1), (14, 8)
    private static Table BuildBitsTable()
    {
        var rows = new List<(long, long)> { (12L, 1L), (10L, 1L), (14L, 8L) };
        return TestUtils.GenerateTableWith2Columns(rows, "x", "w", "Bits");
    }

    // Numbers(x: long) = 1..10
    private static Table BuildOneToTenTable()
    {
        return TestUtils.GenerateTableWithColumn(Enumerable.Range(1, 10).Select(i => (long)i), "x", "Numbers");
    }

    #region default names of aggregates

    [Test]
    [Description("Default names: count_, <function>_<column> for sum/avg/min/max/dcount")]
    public void DefaultNames_BasicAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize count(), sum(Amount), avg(Amount), min(Amount), max(Amount), dcount(Product)");

        AssertColumnsInOrder(row, "count_", "sum_Amount", "avg_Amount", "min_Amount", "max_Amount", "dcount_Product");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(7L));
        Assert.That(GetValue<long>(row, "sum_Amount"), Is.EqualTo(48L));
        Assert.That(GetValue<double>(row, "avg_Amount"), Is.EqualTo(48.0 / 7).Within(1e-9));
        Assert.That(GetValue<long>(row, "min_Amount"), Is.EqualTo(1L));
        Assert.That(GetValue<long>(row, "max_Amount"), Is.EqualTo(20L));
        Assert.That(GetValue<long>(row, "dcount_Product"), Is.EqualTo(2L));
    }

    [Test]
    [Description("Default names of conditional aggregates: countif_ (predicate only), <function>_<column> for the others")]
    public void DefaultNames_ConditionalAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize countif(Amount > 5), sumif(Amount, Amount > 5), avgif(Amount, Amount > 5), " +
            "minif(Amount, Amount > 5), maxif(Amount, Amount > 5), dcountif(Product, Amount > 5)");

        AssertColumnsInOrder(row, "countif_", "sumif_Amount", "avgif_Amount", "minif_Amount", "maxif_Amount",
            "dcountif_Product");
        Assert.That(GetValue<long>(row, "countif_"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(row, "sumif_Amount"), Is.EqualTo(37L));
        Assert.That(GetValue<double>(row, "avgif_Amount"), Is.EqualTo(37.0 / 3).Within(1e-9));
        Assert.That(GetValue<long>(row, "minif_Amount"), Is.EqualTo(7L));
        Assert.That(GetValue<long>(row, "maxif_Amount"), Is.EqualTo(20L));
        Assert.That(GetValue<long>(row, "dcountif_Product"), Is.EqualTo(1L));
    }

    [Test]
    [Description("Default names of the statistics aggregates")]
    public void DefaultNames_StatisticsAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize stdev(Amount), stdevp(Amount), variance(Amount), variancep(Amount)");

        AssertColumnsInOrder(row, "stdev_Amount", "stdevp_Amount", "variance_Amount", "variancep_Amount");
        Assert.That(GetValue<double>(row, "stdev_Amount"), Is.EqualTo(6.568322247187633).Within(1e-9));
        Assert.That(GetValue<double>(row, "stdevp_Amount"), Is.EqualTo(6.081084758223652).Within(1e-9));
        Assert.That(GetValue<double>(row, "variance_Amount"), Is.EqualTo(302.0 / 7).Within(1e-9));
        Assert.That(GetValue<double>(row, "variancep_Amount"), Is.EqualTo(1812.0 / 49).Within(1e-9));
    }

    [Test]
    [Description("Default names of the collection aggregates: list_<column> and set_<column> (also for the _if variants)")]
    public void DefaultNames_CollectionAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize make_list(Amount), make_set(Product), make_list_if(Region, Amount > 5), " +
            "make_set_if(Amount, Amount > 5)");

        AssertColumnsInOrder(row, "list_Amount", "set_Product", "list_Region", "set_Amount");
        Assert.That(GetDynamicArray(row, "list_Amount"),
            Is.EquivalentTo(new object?[] { 10L, 20L, 5L, 7L, 1L, 2L, 3L }));
        Assert.That(GetDynamicArray(row, "set_Product"), Is.EquivalentTo(new object?[] { "Apple", "Pear" }));
        Assert.That(GetDynamicArray(row, "list_Region"), Is.EquivalentTo(new object?[] { "East", "East", "West" }));
        Assert.That(GetDynamicArray(row, "set_Amount"), Is.EquivalentTo(new object?[] { 10L, 20L, 7L }));
    }

    [Test]
    [Description("Default names of the percentile family: percentile_<column>_<percentage> ('.' becomes '_'), percentiles_<column> for arrays")]
    public void DefaultNames_PercentileFamily()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize percentile(Amount, 50), percentiles(Amount, 25, 75), percentile(Amount, 99.5), " +
            "percentiles_array(Amount, 50)");

        AssertColumnsInOrder(row, "percentile_Amount_50", "percentile_Amount_25", "percentile_Amount_75",
            "percentile_Amount_99_5", "percentiles_Amount");
        Assert.That(GetValue<long>(row, "percentile_Amount_50"), Is.EqualTo(5L));
        Assert.That(GetValue<long>(row, "percentile_Amount_25"), Is.EqualTo(2L));
        Assert.That(GetValue<long>(row, "percentile_Amount_75"), Is.EqualTo(10L));
        Assert.That(GetValue<long>(row, "percentile_Amount_99_5"), Is.EqualTo(20L));
        Assert.That(GetDynamicArray(row, "percentiles_Amount"), Is.EqualTo(new object?[] { 5L }));
    }

    [Test]
    [Description("Default names of weighted percentiles are the same as for the unweighted ones")]
    public void DefaultNames_WeightedPercentileFamily()
    {
        var kustoDatabase = CreateDatabase(BuildBitsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Bits | summarize percentilesw(x, w, 10, 50), percentilesw_array(x, w, 90)");

        AssertColumnsInOrder(row, "percentile_x_10", "percentile_x_50", "percentiles_x");
        Assert.That(GetValue<long>(row, "percentile_x_10"), Is.EqualTo(10L));
        Assert.That(GetValue<long>(row, "percentile_x_50"), Is.EqualTo(14L));
        Assert.That(GetDynamicArray(row, "percentiles_x"), Is.EqualTo(new object?[] { 14L }));
    }

    [Test]
    [Description("take_any() keeps the name of its column")]
    public void DefaultNames_TakeAnyKeepsColumnName()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize take_any(Product), take_any(Amount)");

        AssertColumnsInOrder(row, "Product", "Amount");
        Assert.That(GetValue<string>(row, "Product"), Is.AnyOf("Apple", "Pear"));
        Assert.That(GetValue<long>(row, "Amount"), Is.AnyOf(10L, 20L, 5L, 7L, 1L, 2L, 3L));
    }

    [Test]
    [Description("arg_max() keeps the names of its columns")]
    public void DefaultNames_ArgMaxKeepsColumnNames()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize take_any(Product), arg_max(Amount, Region)");

        AssertColumnsInOrder(row, "Product", "Amount", "Region");
        Assert.That(GetValue<string>(row, "Product"), Is.AnyOf("Apple", "Pear"));
        Assert.That(GetValue<long>(row, "Amount"), Is.EqualTo(20L));
        Assert.That(GetValue<string>(row, "Region"), Is.EqualTo("East"));
    }

    [Test]
    [Description("Expression arguments: sum(Amount * 2) is sum_, max(toupper(..)) is max_, but max(strlen(Product)) is max_strlen_Product")]
    public void DefaultNames_ExpressionArguments()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize sum(Amount * 2), max(toupper(Product)), max(strlen(Product)), min(tolower(Region))");

        AssertColumnsInOrder(row, "sum_", "max_", "max_strlen_Product", "min_");
        Assert.That(GetNumber(row, "sum_"), Is.EqualTo(96.0));
        Assert.That(GetValue<string>(row, "max_"), Is.EqualTo("PEAR"));
        Assert.That(GetNumber(row, "max_strlen_Product"), Is.EqualTo(5.0));
        Assert.That(GetValue<string>(row, "min_"), Is.EqualTo("east"));
    }

    [Test]
    [Description("take_any(strlen(Product)) is named strlen_Product")]
    public void DefaultNames_TakeAnyOfStrlen()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize take_any(strlen(Product))");

        AssertColumnsInOrder(row, "strlen_Product");
        Assert.That(GetNumber(row, "strlen_Product"), Is.AnyOf(4.0, 5.0));
    }

    [Test]
    [Description("Conversion functions keep the column name: sum(todouble(Amount)) is sum_Amount (real)")]
    public void DefaultNames_ConversionFunctionsKeepColumnName()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize sum(todouble(Amount)), max(tostring(Amount))");

        AssertColumnsInOrder(row, "sum_Amount", "max_Amount");
        Assert.That(GetValue<double>(row, "sum_Amount"), Is.EqualTo(48.0));
        Assert.That(GetValue<string>(row, "max_Amount"), Is.EqualTo("7"));
    }

    [Test]
    [Description("countif() with a predicate on the group key is named countif_; groups without matches get 0")]
    public void DefaultNames_CountifOnGroupKey()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize countif(Region == \"East\") by Region");

        AssertColumnsInOrder(rows[0], "Region", "countif_");
        var actual = rows.Select(row => (GetValue<string>(row, "Region"), GetValue<long>(row, "countif_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("East", 3L), ("West", 0L) }));
    }

    #endregion

    #region duplicate names

    [Test]
    [Description("Repeated count() gets numeric suffixes: count_, count_1, count_2")]
    public void DuplicateNames_RepeatedCount()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize count(), count(), count()");

        AssertColumnsInOrder(row, "count_", "count_1", "count_2");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(7L));
        Assert.That(GetValue<long>(row, "count_1"), Is.EqualTo(7L));
        Assert.That(GetValue<long>(row, "count_2"), Is.EqualTo(7L));
    }

    [Test]
    [Description("Repeated aggregate on the same column gets a suffix: avg_Amount, avg_Amount1")]
    public void DuplicateNames_RepeatedAggregate()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize avg(Amount), avg(Amount) by Region");

        AssertColumnsInOrder(rows[0], "Region", "avg_Amount", "avg_Amount1");
        var east = GetSingleRowWhere(rows, "Region", "East");
        Assert.That(GetValue<double>(east, "avg_Amount"), Is.EqualTo(35.0 / 3).Within(1e-9));
        Assert.That(GetValue<double>(east, "avg_Amount1"), Is.EqualTo(35.0 / 3).Within(1e-9));
    }

    [Test]
    [Description("An explicit name takes the default name of a later aggregate, which then gets a suffix")]
    public void DuplicateNames_ExplicitNameTakesDefaultNameOfLaterAggregate()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize min_Amount = max(Amount), min(Amount)");

        AssertColumnsInOrder(row, "min_Amount", "min_Amount1");
        Assert.That(GetValue<long>(row, "min_Amount"), Is.EqualTo(20L));
        Assert.That(GetValue<long>(row, "min_Amount1"), Is.EqualTo(1L));
    }

    [Test]
    [Description("An explicit 'count_' name pushes the default count() name to count_1")]
    public void DuplicateNames_ExplicitCountName()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize count_ = sum(Amount), count()");

        AssertColumnsInOrder(row, "count_", "count_1");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(48L));
        Assert.That(GetValue<long>(row, "count_1"), Is.EqualTo(7L));
    }

    [Test]
    [Description("arg_min() and arg_max() over the same columns: the second set of columns gets suffixes")]
    public void DuplicateNames_ArgMinAndArgMax()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize arg_min(Amount, Region), arg_max(Amount, Region) by Product");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Product", "Amount", "Region", "Amount1", "Region1");
        var actual = rows.Select(row => (
            GetValue<string>(row, "Product"),
            GetValue<long>(row, "Amount"),
            GetValue<string>(row, "Region"),
            GetValue<long>(row, "Amount1"),
            GetValue<string>(row, "Region1"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long, string, long, string)>
        {
            ("Apple", 7L, "West", 20L, "East"),
            ("Pear", 1L, "West", 5L, "East")
        }));
    }

    [Test]
    [Description("arg_max() returning the group key column: the returned column gets a suffix")]
    public void DuplicateNames_ArgMaxReturnsKeyColumn()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, string>(
            columnName1: "Product",
            columnName2: "Amount",
            columnName3: "Product1",
            kustoDatabase,
            query: "Sales | summarize arg_max(Amount, Product) by Product");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, string)>
        {
            ("Apple", 20L, "Apple"), ("Pear", 5L, "Pear")
        }));
    }

    [Test]
    [Description("take_any() of the group key column: the aggregate column is named Region1")]
    public void DuplicateNames_TakeAnyOfKeyColumn()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize take_any(Region) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Region1");
        foreach (var row in rows)
        {
            Assert.That(GetValue<string>(row, "Region1"), Is.EqualTo(GetValue<string>(row, "Region")));
        }
    }

    [Test]
    [Description("bin(Amount, 10) and Amount as keys: the second key is named Amount1")]
    public void DuplicateNames_BinKeyAndPlainKeyOfSameColumn()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<long, long, long>(
            columnName1: "Amount",
            columnName2: "Amount1",
            columnName3: "sum_Amount",
            kustoDatabase,
            query: "Sales | summarize sum(Amount) by bin(Amount, 10), Amount");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long, long)>
        {
            (0L, 1L, 1L), (0L, 2L, 2L), (0L, 3L, 3L), (0L, 5L, 5L), (0L, 7L, 7L), (10L, 10L, 10L), (20L, 20L, 20L)
        }));
    }

    [Test]
    [Description("Keys are named first, then aggregates get the next free names: count_, x, x1, count_1, g")]
    public void DuplicateNames_KeysAreNamedBeforeAggregates()
    {
        var tableRows = new List<(string, long)> { ("a", 1L), ("a", 1L), ("b", 12L) };
        Table table = TestUtils.GenerateTableWith2Columns(tableRows, "g", "x", "Items");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Items | summarize count(), take_any(g) by count_ = g, bin(x, 10), x");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "count_", "x", "x1", "count_1", "g");
        var actual = rows.Select(row => (
            GetValue<string>(row, "count_"),
            GetValue<long>(row, "x"),
            GetValue<long>(row, "x1"),
            GetValue<long>(row, "count_1"),
            GetValue<string>(row, "g"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long, long, long, string)>
        {
            ("a", 0L, 1L, 2L, "a"), ("b", 10L, 12L, 1L, "b")
        }));
    }

    [Test]
    [Description("Collisions between take_any(), arg_max() and the keys: g, i, s, x, s1, count_ (bin of an int key is long)")]
    public void DuplicateNames_CollisionsBetweenAggregates()
    {
        var tableRows = new List<(string, int, string, long)>
        {
            ("a", 1, "one", 1L), ("a", 2, "two", 2L), ("b", 15, "three", 3L)
        };
        Table table = TestUtils.GenerateTableWith4Columns(tableRows, "g", "i", "s", "x", "Items");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Items | summarize take_any(s), arg_max(x, s), count() by g, bin(i, 10)");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "g", "i", "s", "x", "s1", "count_");

        var a = GetSingleRowWhere(rows, "g", "a");
        Assert.That(GetValue<long>(a, "i"), Is.EqualTo(0L));
        Assert.That(GetValue<string>(a, "s"), Is.AnyOf("one", "two"));
        Assert.That(GetValue<long>(a, "x"), Is.EqualTo(2L));
        Assert.That(GetValue<string>(a, "s1"), Is.EqualTo("two"));
        Assert.That(GetValue<long>(a, "count_"), Is.EqualTo(2L));

        var b = GetSingleRowWhere(rows, "g", "b");
        Assert.That(GetValue<long>(b, "i"), Is.EqualTo(10L));
        Assert.That(GetValue<string>(b, "s"), Is.EqualTo("three"));
        Assert.That(GetValue<long>(b, "x"), Is.EqualTo(3L));
        Assert.That(GetValue<string>(b, "s1"), Is.EqualTo("three"));
        Assert.That(GetValue<long>(b, "count_"), Is.EqualTo(1L));
    }

    #endregion

    #region generated ColumnN names

    [Test]
    [Description("tolower(Word) as key is named Column1, strlen(Word) as key is named strlen_Word")]
    public void GeneratedNames_ComputedKeys()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "Apple", "apple", "Pear", "Fig" }, "Word", "Words");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Words | summarize count() by tolower(Word), strlen(Word)");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "Column1", "strlen_Word", "count_");
        var actual = rows.Select(row => (
            GetValue<string>(row, "Column1"),
            GetNumber(row, "strlen_Word"),
            GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, double, long)>
        {
            ("apple", 5.0, 2L), ("pear", 4.0, 1L), ("fig", 3.0, 1L)
        }));
    }

    [Test]
    [Description("Mix of plain, named, strlen() and unnamed keys: Region, Key, strlen_Region, Column1, count_")]
    public void GeneratedNames_MixedNamedAndUnnamedKeys()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize count() by Region, Key = tolower(Region), strlen(Region), tolower(Product)");

        Assert.That(rows, Has.Count.EqualTo(4));
        AssertColumnsInOrder(rows[0], "Region", "Key", "strlen_Region", "Column1", "count_");
        var actual = rows.Select(row => (
            GetValue<string>(row, "Region"),
            GetValue<string>(row, "Key"),
            GetNumber(row, "strlen_Region"),
            GetValue<string>(row, "Column1"),
            GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, string, double, string, long)>
        {
            ("East", "east", 4.0, "apple", 2L),
            ("East", "east", 4.0, "pear", 1L),
            ("West", "west", 4.0, "apple", 1L),
            ("West", "west", 4.0, "pear", 3L)
        }));
    }

    [Test]
    [Description("Unnamed predicate key is named Column1; the same predicate with a name keeps the name")]
    public void GeneratedNames_UnnamedAndNamedPredicateKeys()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<bool, bool, long>(
            columnName1: "Column1",
            columnName2: "IsBig",
            columnName3: "count_",
            kustoDatabase,
            query: "Numbers | summarize count() by x > 5, IsBig = x > 5");

        Assert.That(actualData, Is.EquivalentTo(new List<(bool, bool, long)> { (false, false, 5L), (true, true, 5L) }));
    }

    [Test]
    [Description("Generated names are numbered in output order: the key is Column1, the unnamed aggregate expression is Column2")]
    public void GeneratedNames_KeyThenAggregate()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize sum(Amount) + 1, count() by tolower(Region)");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Column1", "Column2", "count_");
        var east = GetSingleRowWhere(rows, "Column1", "east");
        Assert.That(GetNumber(east, "Column2"), Is.EqualTo(36.0));
        Assert.That(GetValue<long>(east, "count_"), Is.EqualTo(3L));
        var west = GetSingleRowWhere(rows, "Column1", "west");
        Assert.That(GetNumber(west, "Column2"), Is.EqualTo(14.0));
        Assert.That(GetValue<long>(west, "count_"), Is.EqualTo(4L));
    }

    [Test]
    [Description("Arithmetic over aggregates without a name: max(x) - min(x) by x % 2 gives Column1 (key) and Column2")]
    public void GeneratedNames_ArithmeticOverAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildOneToTenTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize max(x) - min(x) by x % 2");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Column1", "Column2");
        var actual = rows.Select(row => (GetNumber(row, "Column1"), GetNumber(row, "Column2"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(double, double)> { (0.0, 8.0), (1.0, 8.0) }));
    }

    #endregion

    #region column order

    [Test]
    [Description("Keys come first in 'by' order, then aggregates in declared order")]
    public void ColumnOrder_KeysThenAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize Total = sum(Amount), Orders = count() by Product, Region");

        Assert.That(rows, Has.Count.EqualTo(4));
        foreach (var row in rows)
        {
            AssertColumnsInOrder(row, "Product", "Region", "Total", "Orders");
        }

        var actual = rows.Select(row => (
            GetValue<string>(row, "Product"),
            GetValue<string>(row, "Region"),
            GetValue<long>(row, "Total"),
            GetValue<long>(row, "Orders"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, string, long, long)>
        {
            ("Apple", "East", 30L, 2L), ("Pear", "East", 5L, 1L), ("Apple", "West", 7L, 1L), ("Pear", "West", 6L, 3L)
        }));
    }

    [Test]
    [Description("The order of the keys follows the 'by' clause, not the input schema")]
    public void ColumnOrder_FollowsByClause()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var regionFirst = ExecuteAndGetRows(kustoDatabase, "Sales | summarize count() by Region, Product");
        var productFirst = ExecuteAndGetRows(kustoDatabase, "Sales | summarize count() by Product, Region");

        Assert.That(regionFirst, Has.Count.EqualTo(4));
        Assert.That(productFirst, Has.Count.EqualTo(4));
        foreach (var row in regionFirst)
        {
            AssertColumnsInOrder(row, "Region", "Product", "count_");
        }

        foreach (var row in productFirst)
        {
            AssertColumnsInOrder(row, "Product", "Region", "count_");
        }
    }

    [Test]
    [Description("Aggregates keep the declared order, named or not")]
    public void ColumnOrder_AggregatesKeepDeclaredOrder()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize max(Amount), Orders = count(), min(Amount), sum(Amount)");

        AssertColumnsInOrder(row, "max_Amount", "Orders", "min_Amount", "sum_Amount");
        Assert.That(GetValue<long>(row, "max_Amount"), Is.EqualTo(20L));
        Assert.That(GetValue<long>(row, "Orders"), Is.EqualTo(7L));
        Assert.That(GetValue<long>(row, "min_Amount"), Is.EqualTo(1L));
        Assert.That(GetValue<long>(row, "sum_Amount"), Is.EqualTo(48L));
    }

    #endregion

    #region tuple naming

    [Test]
    [Description("(Lo, Hi) = percentiles(Amount, 10, 90) renames both result columns")]
    public void TupleNaming_Percentiles()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize (Lo, Hi) = percentiles(Amount, 10, 90) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Lo", "Hi");
        var actual = rows.Select(row => (
            GetValue<string>(row, "Region"),
            GetValue<long>(row, "Lo"),
            GetValue<long>(row, "Hi"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long, long)> { ("East", 5L, 20L), ("West", 1L, 7L) }));
    }

    [Test]
    [Description("(m, n) = arg_max(x, s, d) renames only the first two result columns; d keeps its name")]
    public void TupleNaming_ArgMaxPartialRename()
    {
        var tableRows = new List<(long, string, double)> { (1L, "a", 1.5), (3L, "c", 0.5), (2L, "b", 2.5) };
        Table table = TestUtils.GenerateTableWith3Columns(tableRows, "x", "s", "d", "Items");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Items | summarize (m, n) = arg_max(x, s, d)");

        AssertColumnsInOrder(row, "m", "n", "d");
        Assert.That(GetValue<long>(row, "m"), Is.EqualTo(3L));
        Assert.That(GetValue<string>(row, "n"), Is.EqualTo("c"));
        Assert.That(GetValue<double>(row, "d"), Is.EqualTo(0.5));
    }

    #endregion

    #region explicit names

    [Test]
    [Description("Column names are case-sensitive: 'total' and 'Total' are different columns")]
    public void ExplicitNames_AreCaseSensitive()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize total = sum(Amount), Total = count()");

        AssertColumnsInOrder(row, "total", "Total");
        Assert.That(GetValue<long>(row, "total"), Is.EqualTo(48L));
        Assert.That(GetValue<long>(row, "Total"), Is.EqualTo(7L));
    }

    [Test]
    [Description("Bracketed aggregate name with a space: ['Total Amount']")]
    public void ExplicitNames_BracketedAggregateName()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "Total Amount",
            kustoDatabase,
            query: "Sales | summarize ['Total Amount'] = sum(Amount) by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 35L), ("West", 13L) }));
    }

    [Test]
    [Description("Bracketed key name with a space: ['Sales Region']")]
    public void ExplicitNames_BracketedKeyName()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize count() by ['Sales Region'] = Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Sales Region", "count_");
        var actual = rows.Select(row => (GetValue<string>(row, "Sales Region"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("East", 3L), ("West", 4L) }));
    }

    [Test]
    [Description("A key can be renamed to the name of another input column: by Product = Region")]
    public void ExplicitNames_KeyRenamedToAnotherInputColumnName()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize count() by Product = Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Product", "count_");
        var actual = rows.Select(row => (GetValue<string>(row, "Product"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("East", 3L), ("West", 4L) }));
    }

    [Test]
    [Description("An aggregate can be named like its input column: Amount = sum(Amount)")]
    public void ExplicitNames_AggregateNamedLikeItsInputColumn()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize Amount = sum(Amount) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Amount");
        var actual = rows.Select(row => (GetValue<string>(row, "Region"), GetValue<long>(row, "Amount"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("East", 35L), ("West", 13L) }));
    }

    [Test]
    [Description("Named and unnamed versions of the same aggregate do not collide: Total and sum_Amount")]
    public void ExplicitNames_MixedWithDefaultNames()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            columnName1: "Region",
            columnName2: "Total",
            columnName3: "sum_Amount",
            kustoDatabase,
            query: "Sales | summarize Total = sum(Amount), sum(Amount) by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("East", 35L, 35L), ("West", 13L, 13L)
        }));
    }

    #endregion

    #region chained summarize

    [Test]
    [Description("Default names nest in a chained summarize: sum(sum_Amount) is sum_sum_Amount")]
    public void Chained_DefaultNamesNest()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize sum(Amount) by Region | summarize sum(sum_Amount)");

        AssertColumnsInOrder(row, "sum_sum_Amount");
        Assert.That(GetValue<long>(row, "sum_sum_Amount"), Is.EqualTo(48L));
    }

    [Test]
    [Description("count() over the result of a summarize counts the groups")]
    public void Chained_CountOfGroups()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize count() by Region | summarize count()");

        AssertColumnsInOrder(row, "count_");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(2L));
    }

    [Test]
    [Description("The count_ column of a previous summarize can be aggregated: sum(count_) is sum_count_")]
    public void Chained_SumOfCounts()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Product",
            columnName2: "sum_count_",
            kustoDatabase,
            query: "Sales | summarize count() by Product, Region | summarize sum(count_) by Product");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("Apple", 3L), ("Pear", 4L) }));
    }

    [Test]
    [Description("A previous aggregate can be used as the group key of the next summarize")]
    public void Chained_GroupByPreviousAggregate()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, long>(
            columnName1: "x",
            columnName2: "count_",
            kustoDatabase,
            query: "Sales | summarize x = count() by Region | summarize count() by x");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long)> { (3L, 1L), (4L, 1L) }));
    }

    [Test]
    [Description("make_list() of dynamic arrays concatenates them; the result is named list_list_Amount")]
    public void Chained_MakeListOfArraysIsFlattened()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize make_list(Amount) by Region | summarize make_list(list_Amount)");

        AssertColumnsInOrder(row, "list_list_Amount");
        Assert.That(GetDynamicArray(row, "list_list_Amount"),
            Is.EquivalentTo(new object?[] { 10L, 20L, 5L, 7L, 1L, 2L, 3L }));
    }

    #endregion

    #region result types

    [Test]
    [Description("Result types for an int column: sum is long, avg is real, max stays int, percentile is long")]
    public void ResultTypes_IntColumn()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "StormEvents | summarize sum(DamageProperty), avg(DamageProperty), max(DamageProperty), " +
            "percentile(DamageProperty, 50) by State");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "State", "sum_DamageProperty", "avg_DamageProperty", "max_DamageProperty",
            "percentile_DamageProperty_50");

        var florida = GetSingleRowWhere(rows, "State", "FLORIDA");
        Assert.That(GetValue<long>(florida, "sum_DamageProperty"), Is.EqualTo(25000L));
        Assert.That(GetValue<double>(florida, "avg_DamageProperty"), Is.EqualTo(12500.0));
        Assert.That(GetValue<int>(florida, "max_DamageProperty"), Is.EqualTo(20000));
        Assert.That(GetValue<long>(florida, "percentile_DamageProperty_50"), Is.EqualTo(5000L));

        var texas = GetSingleRowWhere(rows, "State", "TEXAS");
        Assert.That(GetValue<long>(texas, "sum_DamageProperty"), Is.EqualTo(5000L));
        Assert.That(GetValue<double>(texas, "avg_DamageProperty"), Is.EqualTo(5000.0));
        Assert.That(GetValue<int>(texas, "max_DamageProperty"), Is.EqualTo(5000));
        Assert.That(GetValue<long>(texas, "percentile_DamageProperty_50"), Is.EqualTo(5000L));
    }

    [Test]
    [Description("Result types for a decimal column: sum, avg and min stay decimal, but percentile is real")]
    public void ResultTypes_DecimalColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1.5m, 2.5m, 3.5m }, "dec", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | summarize sum(dec), avg(dec), min(dec), percentile(dec, 50)");

        AssertColumnsInOrder(row, "sum_dec", "avg_dec", "min_dec", "percentile_dec_50");
        Assert.That(GetValue<decimal>(row, "sum_dec"), Is.EqualTo(7.5m));
        Assert.That(GetValue<decimal>(row, "avg_dec"), Is.EqualTo(2.5m));
        Assert.That(GetValue<decimal>(row, "min_dec"), Is.EqualTo(1.5m));
        Assert.That(GetValue<double>(row, "percentile_dec_50"), Is.EqualTo(2.5));
    }

    #endregion
}
