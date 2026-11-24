using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Grouping semantics of the summarize operator (https://learn.microsoft.com/en-us/kusto/query/summarize-operator):
/// keys of every scalar type, several keys, computed keys, bin() keys and the examples from the documentation.
/// The order of the output rows is not defined by Kusto, so rows are compared as sets.
/// </summary>
public class SummarizeOperatorOpusGroupingTests
{
    [Test]
    [Description("Summarize - count() without 'by' returns exactly one row with the number of input rows")]
    public void Summarize_CountWithoutBy_ReturnsSingleRow()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "StormEvents | summarize count()");

        AssertColumnsInOrder(row, "count_");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(3L));
    }

    [Test]
    [Description("Summarize - named count() without 'by'")]
    public void Summarize_NamedCountWithoutBy()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize Orders = count()");

        AssertColumnsInOrder(row, "Orders");
        Assert.That(GetValue<long>(row, "Orders"), Is.EqualTo(7L));
    }

    [Test]
    [Description("Summarize - count() without 'by' on a table with 10 000 rows")]
    public void Summarize_CountWithoutBy_LargeTable()
    {
        Table table = TestUtils.GenerateTableWithColumn(Enumerable.Range(0, 10_000).Select(i => (long)i),
            columnName: "x", tableName: "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "count_",
            kustoDatabase,
            "Numbers | summarize count()");

        Assert.That(actualData, Is.EqualTo(new List<long> { 10_000L }));
    }

    [Test]
    [Description("Summarize - count() by a string column (StormEvents by State)")]
    public void Summarize_CountByStringColumn()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "StormEvents | summarize count() by State");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "State", "count_");
        var actual = rows.Select(row => (GetValue<string>(row, "State"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("FLORIDA", 2L), ("TEXAS", 1L) }));
    }

    [Test]
    [Description("Summarize - sum() of an int column by State returns long (Kusto widens sum(int) to long)")]
    public void Summarize_SumOfIntColumnByState_ReturnsLong()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "State",
            columnName2: "TotalDamage",
            kustoDatabase,
            query: "StormEvents | summarize TotalDamage = sum(DamageProperty) by State");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("FLORIDA", 25000L), ("TEXAS", 5000L) }));
    }

    [Test]
    [Description("Summarize - group by two columns produces one row per existing combination")]
    public void Summarize_ByTwoColumns()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize Orders = count(), Total = sum(Amount) by Region, Product");

        Assert.That(rows, Has.Count.EqualTo(4));
        AssertColumnsInOrder(rows[0], "Region", "Product", "Orders", "Total");
        var actual = rows.Select(row => (
            GetValue<string>(row, "Region"),
            GetValue<string>(row, "Product"),
            GetValue<long>(row, "Orders"),
            GetValue<long>(row, "Total"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, string, long, long)>
        {
            ("East", "Apple", 2L, 30L),
            ("East", "Pear", 1L, 5L),
            ("West", "Apple", 1L, 7L),
            ("West", "Pear", 3L, 6L)
        }));
    }

    [Test]
    [Description("Summarize - group by three columns (string, string, bool)")]
    public void Summarize_ByThreeColumns()
    {
        var tableRows = new List<(string, string, bool, long)>
        {
            ("East", "Apple", true, 10L),
            ("East", "Apple", true, 20L),
            ("East", "Apple", false, 5L),
            ("West", "Apple", true, 7L),
            ("West", "Pear", false, 1L),
            ("West", "Pear", false, 2L),
            ("West", "Pear", true, 3L)
        };
        Table table = TestUtils.GenerateTableWith4Columns(tableRows, "Region", "Product", "IsActive", "Amount", "Orders");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Orders | summarize Count = count(), Total = sum(Amount) by Region, Product, IsActive");

        Assert.That(rows, Has.Count.EqualTo(5));
        AssertColumnsInOrder(rows[0], "Region", "Product", "IsActive", "Count", "Total");
        var actual = rows.Select(row => (
            GetValue<string>(row, "Region"),
            GetValue<string>(row, "Product"),
            GetValue<bool>(row, "IsActive"),
            GetValue<long>(row, "Count"),
            GetValue<long>(row, "Total"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, string, bool, long, long)>
        {
            ("East", "Apple", true, 2L, 30L),
            ("East", "Apple", false, 1L, 5L),
            ("West", "Apple", true, 1L, 7L),
            ("West", "Pear", false, 2L, 3L),
            ("West", "Pear", true, 1L, 3L)
        }));
    }

    [Test]
    [Description("Summarize - long group key keeps the long type")]
    public void Summarize_ByLongKey()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1L, 1L, 2L, 3L, 3L, 3L }, "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, long>(
            columnName1: "x",
            columnName2: "count_",
            kustoDatabase,
            query: "Numbers | summarize count() by x");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long)> { (1L, 2L), (2L, 1L), (3L, 3L) }));
    }

    [Test]
    [Description("Summarize - int group key keeps the int type (it is not widened to long)")]
    public void Summarize_ByIntKey_KeepsIntType()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1, 1, 2, -7 }, "i", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize count() by i");

        var actual = rows.Select(row => (GetValue<int>(row, "i"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(int, long)> { (1, 2L), (2, 1L), (-7, 1L) }));
    }

    [Test]
    [Description("Summarize - real (double) group key")]
    public void Summarize_ByRealKey()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1.5, 1.5, 2.25, -0.5 }, "d", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize count() by d");

        var actual = rows.Select(row => (GetValue<double>(row, "d"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(double, long)> { (1.5, 2L), (2.25, 1L), (-0.5, 1L) }));
    }

    [Test]
    [Description("Summarize - decimal group key keeps the decimal type")]
    public void Summarize_ByDecimalKey()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1.5m, 1.5m, 2.25m }, "dec", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize count() by dec");

        var actual = rows.Select(row => (GetValue<decimal>(row, "dec"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(decimal, long)> { (1.5m, 2L), (2.25m, 1L) }));
    }

    [Test]
    [Description("Summarize - bool group key")]
    public void Summarize_ByBoolKey()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { true, false, true, true }, "b", "Flags");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<bool, long>(
            columnName1: "b",
            columnName2: "count_",
            kustoDatabase,
            query: "Flags | summarize count() by b");

        Assert.That(actualData, Is.EquivalentTo(new List<(bool, long)> { (true, 3L), (false, 1L) }));
    }

    [Test]
    [Description("Summarize - datetime group key groups by the exact value (different times of the same day are different keys)")]
    public void Summarize_ByDateTimeKey()
    {
        var morning = new DateTime(2024, 1, 1, 10, 0, 0);
        var values = new[] { morning, morning, morning.AddHours(1), new DateTime(2024, 1, 2) };
        Table table = TestUtils.GenerateTableWithColumn(values, "dt", "Times");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, long>(
            columnName1: "dt",
            columnName2: "count_",
            kustoDatabase,
            query: "Times | summarize count() by dt");

        Assert.That(actualData, Is.EquivalentTo(new List<(DateTime, long)>
        {
            (morning, 2L),
            (morning.AddHours(1), 1L),
            (new DateTime(2024, 1, 2), 1L)
        }));
    }

    [Test]
    [Description("Summarize - timespan group key")]
    public void Summarize_ByTimeSpanKey()
    {
        var values = new[]
        {
            TimeSpan.FromHours(1), TimeSpan.FromHours(1), TimeSpan.FromMinutes(90), TimeSpan.Zero
        };
        Table table = TestUtils.GenerateTableWithColumn(values, "ts", "Durations");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<TimeSpan, long>(
            columnName1: "ts",
            columnName2: "count_",
            kustoDatabase,
            query: "Durations | summarize count() by ts");

        Assert.That(actualData, Is.EquivalentTo(new List<(TimeSpan, long)>
        {
            (TimeSpan.FromHours(1), 2L),
            (TimeSpan.FromMinutes(90), 1L),
            (TimeSpan.Zero, 1L)
        }));
    }

    [Test]
    [Description("Summarize - guid group key")]
    public void Summarize_ByGuidKey()
    {
        var first = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        var second = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        Table table = TestUtils.GenerateTableWithColumn(new[] { first, second, first, first }, "id", "Ids");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<Guid, long>(
            columnName1: "id",
            columnName2: "count_",
            kustoDatabase,
            query: "Ids | summarize count() by id");

        Assert.That(actualData, Is.EquivalentTo(new List<(Guid, long)> { (first, 3L), (second, 1L) }));
    }

    [Test]
    [Description("Summarize - string keys are case-sensitive and the empty string is a regular key")]
    public void Summarize_ByStringKey_IsCaseSensitive()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "a", "A", "a", "", "" }, "s", "Strings");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "s",
            columnName2: "count_",
            kustoDatabase,
            query: "Strings | summarize count() by s");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("a", 2L), ("A", 1L), ("", 2L) }));
    }

    [Test]
    [Description("Summarize - leading and trailing whitespace makes string keys different")]
    public void Summarize_ByStringKey_WhitespaceMatters()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "a", "a ", " a", "a" }, "s", "Strings");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "s",
            columnName2: "count_",
            kustoDatabase,
            query: "Strings | summarize count() by s");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("a", 2L), ("a ", 1L), (" a", 1L) }));
    }

    [Test]
    [Description("Summarize - non-ASCII string keys are compared exactly (accent and case sensitive)")]
    public void Summarize_ByStringKey_NonAscii()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "é", "e", "É", "e", "é" }, "s", "Strings");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "s",
            columnName2: "count_",
            kustoDatabase,
            query: "Strings | summarize count() by s");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("é", 2L), ("e", 2L), ("É", 1L) }));
    }

    [Test]
    [Description("Summarize - 'summarize by' without aggregates returns the distinct key combinations and only the key columns")]
    public void Summarize_ByOnly_ReturnsDistinctCombinations()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize by Region, Product");

        Assert.That(rows, Has.Count.EqualTo(4));
        AssertColumnsInOrder(rows[0], "Region", "Product");
        var actual = rows.Select(row => (GetValue<string>(row, "Region"), GetValue<string>(row, "Product"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, string)>
        {
            ("East", "Apple"), ("East", "Pear"), ("West", "Apple"), ("West", "Pear")
        }));
    }

    [Test]
    [Description("Summarize - 'summarize by col' returns the same values as 'distinct col'")]
    public void Summarize_ByOnly_MatchesDistinct()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "apple", "banana", "apple", "pear", "pear", "Pear" },
            "Word", "Words");
        var kustoDatabase = CreateDatabase(table);

        var summarized = TestUtils.ExecuteAndGetDataForOneColumn<string>("Word", kustoDatabase,
            "Words | summarize by Word");
        var distinct = TestUtils.ExecuteAndGetDataForOneColumn<string>("Word", kustoDatabase,
            "Words | distinct Word");

        Assert.That(summarized, Is.EquivalentTo(new List<string> { "apple", "banana", "pear", "Pear" }));
        Assert.That(summarized, Is.EquivalentTo(distinct));
    }

    [Test]
    [Description("Summarize - a group key can be renamed with 'NewName = column'")]
    public void Summarize_ByRenamedKey()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize Orders = count() by Area = Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Area", "Orders");
        var actual = rows.Select(row => (GetValue<string>(row, "Area"), GetValue<long>(row, "Orders"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("East", 3L), ("West", 4L) }));
    }

    [Test]
    [Description("Summarize - an unnamed function call as group key is named Column1")]
    public void Summarize_ByFunctionExpression_IsNamedColumn1()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "Apple", "apple", "APPLE", "pear", "Pear" },
            "Word", "Words");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Words | summarize count() by tolower(Word)");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Column1", "count_");
        var actual = rows.Select(row => (GetValue<string>(row, "Column1"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("apple", 3L), ("pear", 2L) }));
    }

    [Test]
    [Description("Summarize - a named function call as group key")]
    public void Summarize_ByNamedFunctionExpression()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "Apple", "apple", "APPLE", "pear", "Pear" },
            "Word", "Words");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Words | summarize Count = count() by Lower = tolower(Word)");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Lower", "Count");
        var actual = rows.Select(row => (GetValue<string>(row, "Lower"), GetValue<long>(row, "Count"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("apple", 3L), ("pear", 2L) }));
    }

    [Test]
    [Description("Summarize - by strlen(Word): the key is named strlen_Word (function name + column name)")]
    public void Summarize_ByStrlen_IsNamedAfterFunctionAndColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { "a", "bb", "cc", "ddd", "e" }, "Word", "Words");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Words | summarize count() by strlen(Word)");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "strlen_Word", "count_");
        // strlen() is long in Kusto; the type of the key follows strlen(), so only the numeric value is checked
        var actual = rows.Select(row => (GetNumber(row, "strlen_Word"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(double, long)> { (1.0, 2L), (2.0, 2L), (3.0, 1L) }));
    }

    [Test]
    [Description("Summarize - a named boolean expression as group key")]
    public void Summarize_ByNamedBooleanExpression()
    {
        Table table = TestUtils.GenerateTableWithColumn(Enumerable.Range(1, 10).Select(i => (long)i), "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize count() by IsBig = x > 5");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "IsBig", "count_");
        var actual = rows.Select(row => (GetValue<bool>(row, "IsBig"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(bool, long)> { (true, 5L), (false, 5L) }));
    }

    [Test]
    [Description("Summarize - two unnamed expressions as group keys are named Column1 and Column2")]
    public void Summarize_ByTwoUnnamedExpressions()
    {
        Table table = TestUtils.GenerateTableWithColumn(Enumerable.Range(1, 10).Select(i => (long)i), "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize count() by x % 2, x > 5");

        Assert.That(rows, Has.Count.EqualTo(4));
        AssertColumnsInOrder(rows[0], "Column1", "Column2", "count_");
        var actual = rows.Select(row => (
            GetNumber(row, "Column1"),
            GetValue<bool>(row, "Column2"),
            GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(double, bool, long)>
        {
            (1.0, false, 3L), // 1, 3, 5
            (0.0, false, 2L), // 2, 4
            (1.0, true, 2L), // 7, 9
            (0.0, true, 3L) // 6, 8, 10
        }));
    }

    [Test]
    [Description("Summarize - a plain column and a computed key together")]
    public void Summarize_ByColumnAndComputedKey()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize Orders = count() by Region, Upper = toupper(Product)");

        Assert.That(rows, Has.Count.EqualTo(4));
        AssertColumnsInOrder(rows[0], "Region", "Upper", "Orders");
        var actual = rows.Select(row => (
            GetValue<string>(row, "Region"),
            GetValue<string>(row, "Upper"),
            GetValue<long>(row, "Orders"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, string, long)>
        {
            ("East", "APPLE", 2L), ("East", "PEAR", 1L), ("West", "APPLE", 1L), ("West", "PEAR", 3L)
        }));
    }

    [Test]
    [Description("Summarize - the group key column can also be aggregated: sum(x) by x")]
    public void Summarize_AggregateOfKeyColumn()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1L, 2L, 2L, 3L, 3L, 3L }, "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize sum(x) by x");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "x", "sum_x");
        var actual = rows.Select(row => (GetValue<long>(row, "x"), GetValue<long>(row, "sum_x"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(long, long)> { (1L, 1L), (2L, 4L), (3L, 9L) }));
    }

    [Test]
    [Description("Summarize - all rows share the same key, so there is exactly one group")]
    public void Summarize_AllRowsInSingleGroup()
    {
        var tableRows = new List<(string, long)> { ("A", 1L), ("A", 2L), ("A", 3L) };
        Table table = TestUtils.GenerateTableWith2Columns(tableRows, "Kind", "x", "Items");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            columnName1: "Kind",
            columnName2: "Count",
            columnName3: "Total",
            kustoDatabase,
            query: "Items | summarize Count = count(), Total = sum(x) by Kind");

        Assert.That(actualData, Is.EqualTo(new List<(string, long, long)> { ("A", 3L, 6L) }));
    }

    [Test]
    [Description("Summarize - every key is unique, so there is one group per input row")]
    public void Summarize_AllKeysUnique()
    {
        var tableRows = Enumerable.Range(0, 100).Select(i => ($"k{i}", (long)i)).ToList();
        Table table = TestUtils.GenerateTableWith2Columns(tableRows, "Key", "x", "Items");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            columnName1: "Key",
            columnName2: "Count",
            columnName3: "Total",
            kustoDatabase,
            query: "Items | summarize Count = count(), Total = sum(x) by Key");

        var expected = tableRows.Select(row => (row.Item1, 1L, row.Item2)).ToList();
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - count/sum/min/max by key on 10 000 random rows match LINQ GroupBy")]
    public void Summarize_LargeTable_MatchesLinqGroupBy()
    {
        var random = new Random(20240601);
        var tableRows = Enumerable.Range(0, 10_000)
            .Select(_ => ($"key{random.Next(0, 37)}", (long)random.Next(-1000, 1001)))
            .ToList();
        Table table = TestUtils.GenerateTableWith2Columns(tableRows, "Key", "x", "Big");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Big | summarize Count = count(), Total = sum(x), Smallest = min(x), Largest = max(x) by Key");

        var expected = tableRows
            .GroupBy(row => row.Item1)
            .Select(group => (group.Key, (long)group.Count(), group.Sum(row => row.Item2),
                group.Min(row => row.Item2), group.Max(row => row.Item2)))
            .ToList();
        var actual = rows.Select(row => (
            GetValue<string>(row, "Key"),
            GetValue<long>(row, "Count"),
            GetValue<long>(row, "Total"),
            GetValue<long>(row, "Smallest"),
            GetValue<long>(row, "Largest"))).ToList();
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - the result does not depend on the order of the input rows")]
    public void Summarize_ResultDoesNotDependOnInputOrder()
    {
        var expected = new List<(string, string, long)>
        {
            ("East", "Apple", 30L), ("East", "Pear", 5L), ("West", "Apple", 7L), ("West", "Pear", 6L)
        };

        for (int attempt = 0; attempt < 5; attempt++)
        {
            // every build shuffles the rows of the table
            var kustoDatabase = CreateDatabase(BuildSalesTable());

            var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, string, long>(
                columnName1: "Region",
                columnName2: "Product",
                columnName3: "Total",
                kustoDatabase,
                query: "Sales | summarize Total = sum(Amount) by Region, Product");

            Assert.That(actualData, Is.EquivalentTo(expected));
        }
    }

    [Test]
    [Description("Summarize - bin() on a long column rounds down to a multiple of the bin size (also for negative values)")]
    public void Summarize_ByBinOfLong()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { -11L, -10L, -1L, 0L, 9L, 10L, 25L }, "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize count() by bin(x, 10)");

        Assert.That(rows, Has.Count.EqualTo(5));
        AssertColumnsInOrder(rows[0], "x", "count_");
        var actual = rows.Select(row => (GetValue<long>(row, "x"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(long, long)>
        {
            (-20L, 1L), (-10L, 2L), (0L, 2L), (10L, 1L), (20L, 1L)
        }));
    }

    [Test]
    [Description("Summarize - bin() on an int column produces a long key named after the column")]
    public void Summarize_ByBinOfInt_ReturnsLongKey()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1, 5, 12, 19, 20 }, "i", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize count() by bin(i, 10)");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "i", "count_");
        var actual = rows.Select(row => (GetValue<long>(row, "i"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(long, long)> { (0L, 2L), (10L, 2L), (20L, 1L) }));
    }

    [Test]
    [Description("Summarize - bin() on a real column with a fractional bin size")]
    public void Summarize_ByBinOfReal()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 0.1, 0.4, 0.5, 2.7, -0.2 }, "d", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<double, long>(
            columnName1: "d",
            columnName2: "count_",
            kustoDatabase,
            query: "Numbers | summarize count() by bin(d, 0.5)");

        Assert.That(actualData, Is.EquivalentTo(new List<(double, long)>
        {
            (0.0, 2L), (0.5, 1L), (2.5, 1L), (-0.5, 1L)
        }));
    }

    [Test]
    [Description("Summarize - bin(datetime, 1d) groups by calendar day")]
    public void Summarize_ByBinOfDateTime_Day()
    {
        var values = new[]
        {
            new DateTime(2024, 1, 1, 10, 0, 0),
            new DateTime(2024, 1, 1, 23, 59, 59),
            new DateTime(2024, 1, 2, 0, 0, 0),
            new DateTime(2024, 1, 2, 13, 30, 0)
        };
        Table table = TestUtils.GenerateTableWithColumn(values, "dt", "Times");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Times | summarize count() by bin(dt, 1d)");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "dt", "count_");
        var actual = rows.Select(row => (GetValue<DateTime>(row, "dt"), GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(DateTime, long)>
        {
            (new DateTime(2024, 1, 1), 2L),
            (new DateTime(2024, 1, 2), 2L)
        }));
    }

    [Test]
    [Description("Summarize - bin(datetime, 1h) creates only the bins that have rows (no empty 12:00 bin)")]
    public void Summarize_ByBinOfDateTime_Hour_NoEmptyBins()
    {
        var values = new[]
        {
            new DateTime(2024, 1, 1, 10, 5, 0),
            new DateTime(2024, 1, 1, 10, 59, 59),
            new DateTime(2024, 1, 1, 11, 0, 0),
            new DateTime(2024, 1, 1, 13, 30, 0)
        };
        Table table = TestUtils.GenerateTableWithColumn(values, "dt", "Times");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, long>(
            columnName1: "dt",
            columnName2: "count_",
            kustoDatabase,
            query: "Times | summarize count() by bin(dt, 1h)");

        Assert.That(actualData, Is.EquivalentTo(new List<(DateTime, long)>
        {
            (new DateTime(2024, 1, 1, 10, 0, 0), 2L),
            (new DateTime(2024, 1, 1, 11, 0, 0), 1L),
            (new DateTime(2024, 1, 1, 13, 0, 0), 1L)
        }));
    }

    [Test]
    [Description("Summarize - bin() on a timespan column")]
    public void Summarize_ByBinOfTimeSpan()
    {
        var values = new[]
        {
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(59), TimeSpan.FromHours(1), TimeSpan.FromMinutes(90),
            TimeSpan.FromHours(3)
        };
        Table table = TestUtils.GenerateTableWithColumn(values, "ts", "Durations");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<TimeSpan, long>(
            columnName1: "ts",
            columnName2: "count_",
            kustoDatabase,
            query: "Durations | summarize count() by bin(ts, 1h)");

        Assert.That(actualData, Is.EquivalentTo(new List<(TimeSpan, long)>
        {
            (TimeSpan.Zero, 2L),
            (TimeSpan.FromHours(1), 2L),
            (TimeSpan.FromHours(3), 1L)
        }));
    }

    [Test]
    [Description("Summarize - a named bin() key")]
    public void Summarize_ByNamedBin()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1L, 5L, 12L, 19L, 20L }, "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | summarize Count = count() by Bucket = bin(x, 10)");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "Bucket", "Count");
        var actual = rows.Select(row => (GetValue<long>(row, "Bucket"), GetValue<long>(row, "Count"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(long, long)> { (0L, 2L), (10L, 2L), (20L, 1L) }));
    }

    [Test]
    [Description("Summarize - group by a string column and bin(datetime, 1d)")]
    public void Summarize_ByColumnAndBinOfDateTime()
    {
        var tableRows = new List<(string, DateTime)>
        {
            ("Login", new DateTime(2024, 3, 1, 8, 0, 0)),
            ("Login", new DateTime(2024, 3, 1, 17, 45, 0)),
            ("Login", new DateTime(2024, 3, 2, 9, 0, 0)),
            ("Logout", new DateTime(2024, 3, 1, 18, 0, 0)),
            ("Logout", new DateTime(2024, 3, 3, 0, 0, 0))
        };
        Table table = TestUtils.GenerateTableWith2Columns(tableRows, "Kind", "Timestamp", "Events");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetRows(kustoDatabase, "Events | summarize count() by Kind, bin(Timestamp, 1d)");

        Assert.That(rows, Has.Count.EqualTo(4));
        AssertColumnsInOrder(rows[0], "Kind", "Timestamp", "count_");
        var actual = rows.Select(row => (
            GetValue<string>(row, "Kind"),
            GetValue<DateTime>(row, "Timestamp"),
            GetValue<long>(row, "count_"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, DateTime, long)>
        {
            ("Login", new DateTime(2024, 3, 1), 2L),
            ("Login", new DateTime(2024, 3, 2), 1L),
            ("Logout", new DateTime(2024, 3, 1), 1L),
            ("Logout", new DateTime(2024, 3, 3), 1L)
        }));
    }

    [Test]
    [Description("Summarize - histogram of 0..999 with bin(x, 100) has 10 buckets of 100 rows")]
    public void Summarize_Histogram()
    {
        Table table = TestUtils.GenerateTableWithColumn(Enumerable.Range(0, 1000).Select(i => (long)i), "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, long>(
            columnName1: "x",
            columnName2: "count_",
            kustoDatabase,
            query: "Numbers | summarize count() by bin(x, 100)");

        var expected = Enumerable.Range(0, 10).Select(i => (i * 100L, 100L)).ToList();
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - documentation example: unique combinations with 'summarize by State, EventType'")]
    public void Summarize_DocExample_UniqueCombinations()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "StormEvents | summarize by State, EventType");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertColumnsInOrder(rows[0], "State", "EventType");
        var actual = rows.Select(row => (GetValue<string>(row, "State"), GetValue<string>(row, "EventType"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, string)>
        {
            ("FLORIDA", "Hurricane"), ("TEXAS", "Flood"), ("FLORIDA", "Tornado")
        }));
    }

    [Test]
    [Description("Summarize - documentation example: minimum and maximum timestamp")]
    public void Summarize_DocExample_MinAndMaxTimestamp()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "StormEvents | summarize Min = min(StartTime), Max = max(StartTime)");

        AssertColumnsInOrder(row, "Min", "Max");
        Assert.That(GetValue<DateTime>(row, "Min"), Is.EqualTo(new DateTime(2023, 3, 28, 10, 30, 0)));
        Assert.That(GetValue<DateTime>(row, "Max"), Is.EqualTo(new DateTime(2025, 8, 23, 6, 20, 0)));
    }

    [Test]
    [Description("Summarize - documentation example: distinct count of event types per state")]
    public void Summarize_DocExample_DistinctCountPerState()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "State",
            columnName2: "TypesOfStorms",
            kustoDatabase,
            query: "StormEvents | summarize TypesOfStorms = dcount(EventType) by State");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("FLORIDA", 2L), ("TEXAS", 1L) }));
    }

    [Test]
    [Description("Summarize - documentation example: daily totals with bin(StartTime, 1d)")]
    public void Summarize_DocExample_DailyTotals()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, long>(
            columnName1: "StartTime",
            columnName2: "TotalDamage",
            kustoDatabase,
            query: "StormEvents | summarize TotalDamage = sum(DamageProperty) by bin(StartTime, 1d)");

        Assert.That(actualData, Is.EquivalentTo(new List<(DateTime, long)>
        {
            (new DateTime(2023, 3, 28), 5000L),
            (new DateTime(2024, 6, 1), 5000L),
            (new DateTime(2025, 8, 23), 20000L)
        }));
    }
}
