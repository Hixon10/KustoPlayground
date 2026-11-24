namespace KustoPlayground.Core.Tests;

public class SummarizeNeonGroupingAndSchemaTests
{
    #region Grouping without aggregations (distinct combinations via summarize by)

    [Test]
    [Description("Summarize - Grouping by a single string column without any aggregation returns distinct values")]
    public void Summarize_BySingleStringColumn_NoAggregation()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<string> { "TX", "CA", "TX", "NY", "CA", "TX" },
            tableName: "table1",
            columnName: "State");
        kustoDatabase.AddTable(table);

        List<string> actual = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "State",
            kustoDatabase,
            "table1 | summarize by State");

        Assert.That(actual, Is.EquivalentTo(new List<string> { "TX", "CA", "NY" }));
    }

    [Test]
    [Description("Summarize - Grouping by multiple columns without aggregation returns distinct combinations")]
    public void Summarize_ByMultipleColumns_NoAggregation()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(string, string)>
        {
            ("TX", "Hail"),
            ("TX", "Hail"),
            ("TX", "Wind"),
            ("CA", "Hail"),
            ("CA", "Fire"),
            ("CA", "Fire")
        };
        Table table = TestUtils.GenerateTableWith2Columns(rows, tableName: "table1", columnName1: "State", columnName2: "EventType");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "State",
            "EventType",
            kustoDatabase,
            "table1 | summarize by State, EventType");

        var expected = new List<(string, string)>
        {
            ("TX", "Hail"),
            ("TX", "Wind"),
            ("CA", "Hail"),
            ("CA", "Fire")
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - Grouping by int, long, double, decimal, bool, DateTime, and TimeSpan columns")]
    public void Summarize_ByVariousScalarDataTypes()
    {
        var kustoDatabase = new KustoDatabase();
        var dt1 = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var dt2 = new DateTime(2025, 1, 16, 12, 30, 0, DateTimeKind.Utc);
        var ts1 = TimeSpan.FromMinutes(5);
        var ts2 = TimeSpan.FromHours(1);

        var table = SummarizeNeonTestUtils.GenerateTableWithSchema(
            "Events",
            [
                new Column<int>("IntCol", isNullable: false),
                new Column<long>("LongCol", isNullable: false),
                new Column<double>("DoubleCol", isNullable: false),
                new Column<decimal>("DecimalCol", isNullable: false),
                new Column<bool>("BoolCol", isNullable: false),
                new Column<DateTime>("DtCol", isNullable: false),
                new Column<TimeSpan>("TsCol", isNullable: false)
            ],
            [
                new() { ["IntCol"] = 1, ["LongCol"] = 100L, ["DoubleCol"] = 1.5, ["DecimalCol"] = 10.25m, ["BoolCol"] = true,  ["DtCol"] = dt1, ["TsCol"] = ts1 },
                new() { ["IntCol"] = 1, ["LongCol"] = 100L, ["DoubleCol"] = 1.5, ["DecimalCol"] = 10.25m, ["BoolCol"] = true,  ["DtCol"] = dt1, ["TsCol"] = ts1 },
                new() { ["IntCol"] = 2, ["LongCol"] = 200L, ["DoubleCol"] = 2.5, ["DecimalCol"] = 20.50m, ["BoolCol"] = false, ["DtCol"] = dt2, ["TsCol"] = ts2 }
            ]);
        kustoDatabase.AddTable(table);

        Assert.That(
            TestUtils.ExecuteAndGetDataFor2Columns<int, long>("IntCol", "count_", kustoDatabase, "Events | summarize count() by IntCol"),
            Is.EquivalentTo(new List<(int, long)> { (1, 2L), (2, 1L) }));
        Assert.That(
            TestUtils.ExecuteAndGetDataFor2Columns<long, long>("LongCol", "count_", kustoDatabase, "Events | summarize count() by LongCol"),
            Is.EquivalentTo(new List<(long, long)> { (100L, 2L), (200L, 1L) }));
        Assert.That(
            TestUtils.ExecuteAndGetDataFor2Columns<double, long>("DoubleCol", "count_", kustoDatabase, "Events | summarize count() by DoubleCol"),
            Is.EquivalentTo(new List<(double, long)> { (1.5, 2L), (2.5, 1L) }));
        Assert.That(
            TestUtils.ExecuteAndGetDataFor2Columns<decimal, long>("DecimalCol", "count_", kustoDatabase, "Events | summarize count() by DecimalCol"),
            Is.EquivalentTo(new List<(decimal, long)> { (10.25m, 2L), (20.50m, 1L) }));
        Assert.That(
            TestUtils.ExecuteAndGetDataFor2Columns<bool, long>("BoolCol", "count_", kustoDatabase, "Events | summarize count() by BoolCol"),
            Is.EquivalentTo(new List<(bool, long)> { (true, 2L), (false, 1L) }));
        Assert.That(
            TestUtils.ExecuteAndGetDataFor2Columns<DateTime, long>("DtCol", "count_", kustoDatabase, "Events | summarize count() by DtCol"),
            Is.EquivalentTo(new List<(DateTime, long)> { (dt1, 2L), (dt2, 1L) }));
        Assert.That(
            TestUtils.ExecuteAndGetDataFor2Columns<TimeSpan, long>("TsCol", "count_", kustoDatabase, "Events | summarize count() by TsCol"),
            Is.EquivalentTo(new List<(TimeSpan, long)> { (ts1, 2L), (ts2, 1L) }));
    }

    [Test]
    [Description("Summarize - String group keys are case-sensitive and distinguish empty string from whitespace")]
    public void Summarize_ByString_CaseSensitiveAndWhitespaceDistinct()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<string> { "Alpha", "alpha", "ALPHA", "Alpha", "", " ", "" },
            tableName: "table1",
            columnName: "Key");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "Key",
            "count_",
            kustoDatabase,
            "table1 | summarize count() by Key");

        var expected = new List<(string, long)>
        {
            ("Alpha", 2L),
            ("alpha", 1L),
            ("ALPHA", 1L),
            ("", 2L),
            (" ", 1L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - Grouping on a nullable column merges all null keys into one group")]
    public void Summarize_ByNullableIntColumn_GroupsNullsTogether()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<int?, int>(
            [
                (1, 10),
                (null, 20),
                (1, 30),
                (null, 40),
                (2, 50)
            ],
            tableName: "table1",
            columnName1: "GroupId",
            columnName2: "Amount");
        kustoDatabase.AddTable(table);

        var actual = SummarizeNeonTestUtils.ExecuteAndGetNullableDataFor3Columns<int?, long, long>(
            "GroupId",
            "count_",
            "sum_Amount",
            kustoDatabase,
            "table1 | summarize count(), sum(Amount) by GroupId");

        var expected = new List<(int?, long, long)>
        {
            (1, 2L, 40L),
            (null, 2L, 60L),
            (2, 1L, 50L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - Missing string cells and empty strings form one group")]
    public void Summarize_ByNullableStringColumn_MissingAndEmptyFormOneGroup()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWithColumn<string>(
            [null, "", "A", null, "", "A"],
            tableName: "table1",
            columnName: "Category");
        kustoDatabase.AddTable(table);

        var actual = SummarizeNeonTestUtils.ExecuteAndGetNullableDataFor2Columns<string, long>(
            "Category",
            "count_",
            kustoDatabase,
            "table1 | summarize count() by Category");

        var expected = new List<(string?, long)>
        {
            ("", 4L),
            ("A", 2L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - Multi-column grouping where some key columns contain nulls")]
    public void Summarize_ByMultipleNullableColumns()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, int?>(
            [
                ("A", null),
                ("A", null),
                ("A", 1),
                (null, null),
                (null, 1),
                (null, null)
            ],
            tableName: "table1",
            columnName1: "K1",
            columnName2: "K2");
        kustoDatabase.AddTable(table);

        var actual = SummarizeNeonTestUtils.ExecuteAndGetNullableDataFor3Columns<string, int?, long>(
            "K1",
            "K2",
            "count_",
            kustoDatabase,
            "table1 | summarize count() by K1, K2");

        var expected = new List<(string?, int?, long)>
        {
            ("A", null, 2L),
            ("A", 1, 1L),
            ("", null, 2L),
            ("", 1, 1L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - Grouping by three columns")]
    public void Summarize_ByThreeColumns()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(string, string, bool, int)>
        {
            ("US", "West", true, 10),
            ("US", "West", true, 20),
            ("US", "West", false, 5),
            ("US", "East", true, 7),
            ("EU", "West", false, 15)
        };
        Table table = TestUtils.GenerateTableWith4Columns(
            rows,
            tableName: "Sales",
            columnName1: "Region",
            columnName2: "Zone",
            columnName3: "Active",
            columnName4: "Amount");
        kustoDatabase.AddTable(table);

        var resultRows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Sales | summarize Total = sum(Amount) by Region, Zone, Active");

        var actual = resultRows.Select(r => (
            (string)r["Region"]!,
            (string)r["Zone"]!,
            (bool)r["Active"]!,
            (long)r["Total"]!)).ToList();

        var expected = new List<(string, string, bool, long)>
        {
            ("US", "West", true, 30L),
            ("US", "West", false, 5L),
            ("US", "East", true, 7L),
            ("EU", "West", false, 15L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    #endregion

    #region Computed and aliased by expressions

    [Test]
    [Description("Summarize - Aliased by column renames output grouping column")]
    public void Summarize_ByAliasedColumn()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<string> { "A", "A", "B" },
            tableName: "table1",
            columnName: "Cat");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize Cnt = count() by Category = Cat");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(rows[0]), Is.EqualTo(new List<string> { "Category", "Cnt" }));
        var actual = rows.Select(r => ((string)r["Category"]!, (long)r["Cnt"]!)).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("A", 2L), ("B", 1L) }));
    }

    [Test]
    [Description("Summarize - Grouping by computed string function tolower with alias")]
    public void Summarize_ByComputedExpression_ToLowerWithAlias()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<string> { "Alpha", "alpha", "ALPHA", "Beta", "BETA" },
            tableName: "table1",
            columnName: "Word");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "NormWord",
            "count_",
            kustoDatabase,
            "table1 | summarize count() by NormWord = tolower(Word)");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("alpha", 3L), ("beta", 2L) }));
    }

    [Test]
    [Description("Summarize - Grouping by computed boolean expression and strlen with alias")]
    public void Summarize_ByComputedBooleanAndStrlen()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (5, "hi"),
                (15, "cat"),
                (20, "dog"),
                (3, "no")
            },
            tableName: "table1",
            columnName1: "Score",
            columnName2: "Label");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize count() by IsHigh = Score >= 10, Len = strlen(Label)");

        var actual = rows.Select(r => (
            (bool)r["IsHigh"]!,
            SummarizeNeonTestUtils.AsLong(r["Len"]),
            (long)r["count_"]!)).ToList();

        var expected = new List<(bool, long, long)>
        {
            (false, 2L, 2L),
            (true, 3L, 2L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - Grouping by unnamed computed expressions gets default names Column1, Column2")]
    public void Summarize_ByUnnamedComputedExpressions_DefaultColumnNames()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)>
            {
                ("Ab", 10),
                ("ab", 20),
                ("XYZ", 5)
            },
            tableName: "table1",
            columnName1: "Name",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize count() by tolower(Name), Val > 15");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(rows[0]),
            Is.EqualTo(new List<string> { "Column1", "Column2", "count_" }));

        var actual = rows.Select(r => ((string)r["Column1"]!, (bool)r["Column2"]!, (long)r["count_"]!)).ToList();
        var expected = new List<(string, bool, long)>
        {
            ("ab", false, 1L),
            ("ab", true, 1L),
            ("xyz", false, 1L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - Grouping by arithmetic modulo expression")]
    public void Summarize_ByArithmeticExpression()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 1, 2, 3, 4, 5, 6 },
            tableName: "table1",
            columnName: "Num");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize Cnt = count() by Parity = Num % 2");

        var actual = rows.Select(r => (SummarizeNeonTestUtils.AsLong(r["Parity"]), (long)r["Cnt"]!)).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(long, long)> { (0L, 3L), (1L, 3L) }));
    }

    #endregion

    #region bin() and bin_at() grouping

    [Test]
    [Description("Summarize - Grouping by bin() on integer column keeps original column name and floors to multiples")]
    public void Summarize_ByBin_Integers()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 0, 4, 9, 10, 15, 19, 20, 29 },
            tableName: "table1",
            columnName: "Age");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize count() by bin(Age, 10)");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(rows[0]), Is.EqualTo(new List<string> { "Age", "count_" }));
        var actual = rows.Select(r => (SummarizeNeonTestUtils.AsLong(r["Age"]), (long)r["count_"]!)).ToList();
        var expected = new List<(long, long)>
        {
            (0L, 3L),
            (10L, 3L),
            (20L, 2L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - bin() on negative integers rounds down toward negative infinity")]
    public void Summarize_ByBin_NegativeIntegers()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { -11, -10, -6, -5, -1, 0, 4, 5 },
            tableName: "table1",
            columnName: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize count() by bin(Val, 5)");

        var actual = rows.Select(r => (SummarizeNeonTestUtils.AsLong(r["Val"]), (long)r["count_"]!)).ToList();
        var expected = new List<(long, long)>
        {
            (-15L, 1L), // -11
            (-10L, 2L), // -10, -6
            (-5L, 2L),  // -5, -1
            (0L, 2L),   // 0, 4
            (5L, 1L)    // 5
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - Grouping by bin() on real/double values with fractional bin size")]
    public void Summarize_ByBin_Doubles()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<double> { 0.1, 0.4, 0.5, 0.9, 1.0, 1.49, -0.2 },
            tableName: "table1",
            columnName: "Price");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize count() by Bucket = bin(Price, 0.5)");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(rows[0]), Is.EqualTo(new List<string> { "Bucket", "count_" }));
        var actual = rows.Select(r => (Math.Round(SummarizeNeonTestUtils.AsDouble(r["Bucket"]), 6), (long)r["count_"]!)).ToList();
        var expected = new List<(double, long)>
        {
            (-0.5, 1L),
            (0.0, 2L),
            (0.5, 2L),
            (1.0, 2L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - Grouping by bin() on DateTime with 1h and 1d timespan steps")]
    public void Summarize_ByBin_DateTime_HoursAndDays()
    {
        var kustoDatabase = new KustoDatabase();
        var timestamps = new List<DateTime>
        {
            new(2025, 3, 10, 10, 05, 00, DateTimeKind.Utc),
            new(2025, 3, 10, 10, 59, 59, DateTimeKind.Utc),
            new(2025, 3, 10, 11, 00, 00, DateTimeKind.Utc),
            new(2025, 3, 11, 00, 15, 00, DateTimeKind.Utc)
        };
        Table table = TestUtils.GenerateTableWithColumn(timestamps, tableName: "Logs", columnName: "Timestamp");
        kustoDatabase.AddTable(table);

        var hourly = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, long>(
            "Timestamp",
            "count_",
            kustoDatabase,
            "Logs | summarize count() by bin(Timestamp, 1h)");

        Assert.That(hourly, Is.EquivalentTo(new List<(DateTime, long)>
        {
            (new DateTime(2025, 3, 10, 10, 0, 0, DateTimeKind.Utc), 2L),
            (new DateTime(2025, 3, 10, 11, 0, 0, DateTimeKind.Utc), 1L),
            (new DateTime(2025, 3, 11, 0, 0, 0, DateTimeKind.Utc), 1L)
        }));

        var daily = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, long>(
            "Timestamp",
            "count_",
            kustoDatabase,
            "Logs | summarize count() by bin(Timestamp, 1d)");

        Assert.That(daily, Is.EquivalentTo(new List<(DateTime, long)>
        {
            (new DateTime(2025, 3, 10, 0, 0, 0, DateTimeKind.Utc), 3L),
            (new DateTime(2025, 3, 11, 0, 0, 0, DateTimeKind.Utc), 1L)
        }));
    }

    [Test]
    [Description("Summarize - Grouping by bin() on TimeSpan column")]
    public void Summarize_ByBin_TimeSpan()
    {
        var kustoDatabase = new KustoDatabase();
        var durations = new List<TimeSpan>
        {
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(59),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(119),
            TimeSpan.FromSeconds(125)
        };
        Table table = TestUtils.GenerateTableWithColumn(durations, tableName: "Runs", columnName: "Duration");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<TimeSpan, long>(
            "Duration",
            "count_",
            kustoDatabase,
            "Runs | summarize count() by bin(Duration, 1m)");

        var expected = new List<(TimeSpan, long)>
        {
            (TimeSpan.FromMinutes(0), 2L),
            (TimeSpan.FromMinutes(1), 2L),
            (TimeSpan.FromMinutes(2), 1L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - Grouping by bin() combined with a categorical column")]
    public void Summarize_ByBinAndCategory()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(DateTime, string, int)>
        {
            (new DateTime(2025, 5, 1, 8, 10, 0, DateTimeKind.Utc), "OK", 100),
            (new DateTime(2025, 5, 1, 8, 45, 0, DateTimeKind.Utc), "OK", 200),
            (new DateTime(2025, 5, 1, 8, 50, 0, DateTimeKind.Utc), "ERR", 50),
            (new DateTime(2025, 5, 1, 9, 05, 0, DateTimeKind.Utc), "OK", 300)
        };
        Table table = TestUtils.GenerateTableWith3Columns(
            rows,
            tableName: "Requests",
            columnName1: "Ts",
            columnName2: "Status",
            columnName3: "Latency");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor3Columns<DateTime, string, long>(
            "Ts",
            "Status",
            "sum_Latency",
            kustoDatabase,
            "Requests | summarize sum(Latency) by bin(Ts, 1h), Status");

        var expected = new List<(DateTime, string, long)>
        {
            (new DateTime(2025, 5, 1, 8, 0, 0, DateTimeKind.Utc), "OK", 300L),
            (new DateTime(2025, 5, 1, 8, 0, 0, DateTimeKind.Utc), "ERR", 50L),
            (new DateTime(2025, 5, 1, 9, 0, 0, DateTimeKind.Utc), "OK", 300L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - bin() on nullable column produces null bin and groups nulls together")]
    public void Summarize_ByBin_NullableValues()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWithColumn<int?>(
            [3, 7, null, 12, null],
            tableName: "table1",
            columnName: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize count() by bin(Val, 10)");

        var actual = rows.Select(r => (
            r["Val"] is null ? (long?)null : SummarizeNeonTestUtils.AsLong(r["Val"]),
            (long)r["count_"]!)).ToList();

        var expected = new List<(long?, long)>
        {
            (0L, 2L),
            (10L, 1L),
            (null, 2L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - bin() with negative bin size yields null for all rows and collapses them into one null group")]
    public void Summarize_ByBin_NegativeBinSize_ReturnsNull()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 1, 10, 25 },
            tableName: "table1",
            columnName: "Val");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize count() by bin(Val, -5)");

        Assert.That(row["Val"], Is.Null);
        Assert.That(row["count_"], Is.EqualTo(3L));
    }

    [Test]
    [Description("Summarize - bin_at() on integers and DateTime aligns bins to the fixed anchor point")]
    public void Summarize_ByBinAt_NumericAndDateTime()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(int, DateTime)>
        {
            (1, new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc)),
            (2, new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)),
            (6, new DateTime(2025, 1, 2, 11, 30, 0, DateTimeKind.Utc)),
            (7, new DateTime(2025, 1, 2, 12, 30, 0, DateTimeKind.Utc))
        };
        Table table = TestUtils.GenerateTableWith2Columns(rows, tableName: "table1", columnName1: "Num", columnName2: "Ts");
        kustoDatabase.AddTable(table);

        // Numeric fixed point = 2, step = 5 => bins are ..., -3, 2, 7, ...
        var numRows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize count() by bin_at(Num, 5, 2)");
        var actualNum = numRows.Select(r => (SummarizeNeonTestUtils.AsLong(r["Num"]), (long)r["count_"]!)).ToList();
        Assert.That(actualNum, Is.EquivalentTo(new List<(long, long)>
        {
            (-3L, 1L), // 1
            (2L, 2L),  // 2, 6
            (7L, 1L)   // 7
        }));

        // DateTime fixed point = 2025-01-01 12:00:00, step = 1d
        var dtActual = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, long>(
            "Ts",
            "count_",
            kustoDatabase,
            "table1 | summarize count() by bin_at(Ts, 1d, datetime(2025-01-01 12:00:00))");
        Assert.That(dtActual, Is.EquivalentTo(new List<(DateTime, long)>
        {
            (new DateTime(2024, 12, 31, 12, 0, 0, DateTimeKind.Utc), 1L), // 2025-01-01 11:00
            (new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc), 2L),   // 2025-01-01 12:00 and 2025-01-02 11:30
            (new DateTime(2025, 1, 2, 12, 0, 0, DateTimeKind.Utc), 1L)    // 2025-01-02 12:30
        }));
    }

    [Test]
    [Description("Summarize - bin_at() on TimeSpan with fixed point matching official Kusto example")]
    public void Summarize_ByBinAt_TimeSpan()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<TimeSpan> { TimeSpan.FromHours(1), TimeSpan.FromHours(11), TimeSpan.FromHours(12), TimeSpan.FromHours(20) },
            tableName: "table1",
            columnName: "T");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<TimeSpan, long>(
            "T",
            "count_",
            kustoDatabase,
            "table1 | summarize count() by bin_at(T, 1d, 12h)");

        Assert.That(actual, Is.EquivalentTo(new List<(TimeSpan, long)>
        {
            (TimeSpan.FromHours(-12), 2L), // 1h and 11h round down to -12h
            (TimeSpan.FromHours(12), 2L)   // 12h and 20h round down to 12h
        }));
    }

    #endregion

    #region Empty table & single-row table semantics

    [Test]
    [Description("Summarize - Empty table WITH by clause returns zero rows")]
    public void Summarize_EmptyTable_WithByClause_ReturnsZeroRows()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateEmptyTable(
            "EmptyTable",
            new Column<string>("State", isNullable: false),
            new Column<int>("Val", isNullable: false));
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "EmptyTable | summarize count(), sum(Val), avg(Val), min(Val), max(Val) by State");

        Assert.That(rows, Is.Empty);
    }

    [Test]
    [Description("Summarize - Empty table WITH by clause and no aggregations returns zero rows")]
    public void Summarize_EmptyTable_ByOnly_ReturnsZeroRows()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateEmptyTable(
            "EmptyTable",
            new Column<string>("State", isNullable: false));
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "EmptyTable | summarize by State");

        Assert.That(rows, Is.Empty);
    }

    [Test]
    [Description("Summarize - Empty table WITHOUT by clause returns 1 row with Kusto default aggregation values")]
    public void Summarize_EmptyTable_WithoutByClause_ReturnsSingleDefaultRow()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateEmptyTable(
            "EmptyTable",
            new Column<int>("Val", isNullable: false),
            new Column<bool>("Flag", isNullable: false),
            new Column<string>("Name", isNullable: false));
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "EmptyTable | summarize count(), countif(Flag), countif(Val > 0), dcount(Val), count_distinct(Val), sum(Val), sumif(Val, Flag), avg(Val), min(Val), max(Val), make_list(Name), make_set(Name), take_any(Name)");

        Assert.That(row["count_"], Is.EqualTo(0L));
        Assert.That(row["countif_Flag"], Is.EqualTo(0L));
        Assert.That(row["countif_"], Is.EqualTo(0L));
        Assert.That(row["dcount_Val"], Is.EqualTo(0L));
        Assert.That(row["count_distinct_Val"], Is.EqualTo(0L));
        Assert.That(row["sum_Val"], Is.EqualTo(0L));
        Assert.That(row["sumif_Val"], Is.EqualTo(0L));
        Assert.That(double.IsNaN(SummarizeNeonTestUtils.AsDouble(row["avg_Val"])), Is.True, "avg() over empty input should be NaN in Kusto.");
        Assert.That(row["min_Val"], Is.Null);
        Assert.That(row["max_Val"], Is.Null);
        Assert.That(SummarizeNeonTestUtils.ExtractDynamicArray(row["list_Name"]), Is.Empty);
        Assert.That(SummarizeNeonTestUtils.ExtractDynamicArray(row["set_Name"]), Is.Empty);
        Assert.That(row["Name"], Is.EqualTo(string.Empty));
    }

    [Test]
    [Description("Summarize - Single-row table with and without by clause")]
    public void Summarize_SingleRowTable()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)> { ("Only", 42) },
            tableName: "Single",
            columnName1: "Key",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var noGroupRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Single | summarize count(), sum(Val), avg(Val), min(Val), max(Val), dcount(Val)");

        Assert.That(noGroupRow["count_"], Is.EqualTo(1L));
        Assert.That(noGroupRow["sum_Val"], Is.EqualTo(42L));
        Assert.That(SummarizeNeonTestUtils.AsDouble(noGroupRow["avg_Val"]), Is.EqualTo(42.0).Within(1e-9));
        Assert.That(noGroupRow["min_Val"], Is.EqualTo(42));
        Assert.That(noGroupRow["max_Val"], Is.EqualTo(42));
        Assert.That(noGroupRow["dcount_Val"], Is.EqualTo(1L));

        var groupedRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Single | summarize count(), sum(Val) by Key");

        Assert.That(groupedRow["Key"], Is.EqualTo("Only"));
        Assert.That(groupedRow["count_"], Is.EqualTo(1L));
        Assert.That(groupedRow["sum_Val"], Is.EqualTo(42L));
    }

    #endregion

    #region Output column ordering, default names, and duplicate disambiguation

    [Test]
    [Description("Summarize - Output columns are ordered with by-columns first in listed order, then aggregation columns in listed order")]
    public void Summarize_OutputColumnOrder_ByFirstThenAggregations()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, string, int)> { ("A", "X", 10) },
            tableName: "table1",
            columnName1: "K1",
            columnName2: "K2",
            columnName3: "Val");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize Total = sum(Val), Cnt = count(), MinV = min(Val) by K2, FirstKey = K1");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(row),
            Is.EqualTo(new List<string> { "K2", "FirstKey", "Total", "Cnt", "MinV" }));
    }

    [Test]
    [Description("Summarize - Default aggregation column names follow Kusto naming rules for simple columns vs compound expressions")]
    public void Summarize_DefaultColumnNames_SimpleColumnVsCompoundExpression()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(int, bool)> { (10, true), (20, false) },
            tableName: "table1",
            columnName1: "Val",
            columnName2: "Flag");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize count(), countif(Flag), countif(Val > 15), dcount(Val), dcount(Val + 1), sum(Val), sum(Val + 1), avg(Val), avg(Val * 2), min(Val), min(Val - 1), max(Val), max(Val + 1)");

        var expectedColumns = new List<string>
        {
            "count_",
            "countif_Flag",
            "countif_",
            "dcount_Val",
            "dcount_",
            "sum_Val",
            "sum_",
            "avg_Val",
            "avg_",
            "min_Val",
            "min_",
            "max_Val",
            "max_"
        };
        Assert.That(SummarizeNeonTestUtils.GetColumnNames(row), Is.EqualTo(expectedColumns));
    }

    [Test]
    [Description("Summarize - Duplicate unnamed aggregations auto-suffix with 1, 2, etc.")]
    public void Summarize_DuplicateDefaultColumnNames_AutoDisambiguated()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(int, bool)> { (10, true), (20, false), (30, true) },
            tableName: "table1",
            columnName1: "Val",
            columnName2: "Flag");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize count(), count(), count(), sum(Val), sum(Val), countif(Flag), countif(Flag), countif(Val > 5), countif(Val > 15)");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(row),
            Is.EqualTo(new List<string>
            {
                "count_", "count_1", "count_2",
                "sum_Val", "sum_Val1",
                "countif_Flag", "countif_Flag1",
                "countif_", "countif_1"
            }));

        Assert.That(row["count_"], Is.EqualTo(3L));
        Assert.That(row["count_1"], Is.EqualTo(3L));
        Assert.That(row["count_2"], Is.EqualTo(3L));
        Assert.That(row["sum_Val"], Is.EqualTo(60L));
        Assert.That(row["sum_Val1"], Is.EqualTo(60L));
        Assert.That(row["countif_Flag"], Is.EqualTo(2L));
        Assert.That(row["countif_Flag1"], Is.EqualTo(2L));
        Assert.That(row["countif_"], Is.EqualTo(3L));
        Assert.That(row["countif_1"], Is.EqualTo(2L));
    }

    [Test]
    [Description("Summarize - Duplicate unnamed bin() calls on the same column auto-disambiguate to Col, Col1")]
    public void Summarize_DuplicateUnnamedBinColumns_AutoDisambiguated()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 15, 18 },
            tableName: "table1",
            columnName: "Num");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize count() by bin(Num, 10), bin(Num, 100)");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(row), Is.EqualTo(new List<string> { "Num", "Num1", "count_" }));
        Assert.That(SummarizeNeonTestUtils.AsLong(row["Num"]), Is.EqualTo(10L));
        Assert.That(SummarizeNeonTestUtils.AsLong(row["Num1"]), Is.EqualTo(0L));
        Assert.That(row["count_"], Is.EqualTo(2L));
    }

    [Test]
    [Description("Summarize - Duplicate default names for make_list and make_set auto-suffix")]
    public void Summarize_DuplicateMakeListAndMakeSetNames_AutoDisambiguated()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, bool)> { ("a", true), ("b", false) },
            tableName: "table1",
            columnName1: "Name",
            columnName2: "Flag");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize make_list(Name), make_list_if(Name, Flag), make_set(Name), make_set_if(Name, Flag)");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(row),
            Is.EqualTo(new List<string> { "list_Name", "list_Name1", "set_Name", "set_Name1" }));
    }

    #endregion

    #region Summarize hints (shuffle strategy, shufflekey, num_partitions)

    [Test]
    [Description("Summarize - hint.strategy=shuffle executes with identical semantics")]
    public void Summarize_HintStrategyShuffle_ProducesSameResults()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)> { ("A", 10), ("A", 20), ("B", 5) },
            tableName: "table1",
            columnName1: "Key",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "Key",
            "sum_Val",
            kustoDatabase,
            "table1 | summarize hint.strategy=shuffle sum(Val) by Key");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("A", 30L), ("B", 5L) }));
    }

    [Test]
    [Description("Summarize - hint.shufflekey and hint.num_partitions execute with identical semantics")]
    public void Summarize_HintShuffleKeyAndNumPartitions_ProducesSameResults()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, string, int)>
            {
                ("US", "East", 1),
                ("US", "East", 2),
                ("US", "West", 4),
                ("EU", "North", 8)
            },
            tableName: "table1",
            columnName1: "Region",
            columnName2: "Zone",
            columnName3: "Val");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor3Columns<string, string, long>(
            "Region",
            "Zone",
            "sum_Val",
            kustoDatabase,
            "table1 | summarize hint.shufflekey=Region hint.num_partitions=4 sum(Val) by Region, Zone");

        var expected = new List<(string, string, long)>
        {
            ("US", "East", 3L),
            ("US", "West", 4L),
            ("EU", "North", 8L)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    #endregion

    #region Error and invalid query edge cases

    [Test]
    [Description("Summarize - Unknown column in aggregation returns an execution error")]
    public void Summarize_Error_UnknownColumnInAggregation()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(new List<int> { 1, 2 }, tableName: "table1", columnName: "Val");
        kustoDatabase.AddTable(table);

        SummarizeNeonTestUtils.ExecuteAndAssertError(kustoDatabase, "table1 | summarize sum(NonExistent)");
    }

    [Test]
    [Description("Summarize - Unknown column in by clause returns an execution error")]
    public void Summarize_Error_UnknownColumnInByClause()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(new List<int> { 1, 2 }, tableName: "table1", columnName: "Val");
        kustoDatabase.AddTable(table);

        SummarizeNeonTestUtils.ExecuteAndAssertError(kustoDatabase, "table1 | summarize count() by NonExistent");
    }

    [Test]
    [Description("Summarize - Duplicate explicit column name in by clause returns an execution error")]
    public void Summarize_Error_DuplicateByColumnName()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(new List<string> { "A", "B" }, tableName: "table1", columnName: "Cat");
        kustoDatabase.AddTable(table);

        SummarizeNeonTestUtils.ExecuteAndAssertError(kustoDatabase, "table1 | summarize count() by Cat, Cat");
    }

    [Test]
    [Description("Summarize - Duplicate explicit alias across aggregations returns an execution error")]
    public void Summarize_Error_DuplicateExplicitAggregationAlias()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(new List<int> { 1, 2 }, tableName: "table1", columnName: "Val");
        kustoDatabase.AddTable(table);

        SummarizeNeonTestUtils.ExecuteAndAssertError(kustoDatabase, "table1 | summarize Result = sum(Val), Result = count()");
    }

    [Test]
    [Description("Summarize - Explicit aggregation alias colliding with by column name returns an execution error")]
    public void Summarize_Error_AggregationAliasCollidesWithByColumn()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(new List<string> { "A", "B" }, tableName: "table1", columnName: "Cat");
        kustoDatabase.AddTable(table);

        SummarizeNeonTestUtils.ExecuteAndAssertError(kustoDatabase, "table1 | summarize Cat = count() by Cat");
    }

    [Test]
    [Description("Summarize - sum() and avg() on string column return execution errors")]
    public void Summarize_Error_SumOrAvgOnStringColumn()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(new List<string> { "1", "2" }, tableName: "table1", columnName: "Text");
        kustoDatabase.AddTable(table);

        SummarizeNeonTestUtils.ExecuteAndAssertError(kustoDatabase, "table1 | summarize sum(Text)");
        SummarizeNeonTestUtils.ExecuteAndAssertError(kustoDatabase, "table1 | summarize avg(Text)");
    }

    [Test]
    [Description("Summarize - Unknown aggregation function returns an execution error")]
    public void Summarize_Error_UnknownAggregationFunction()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(new List<int> { 1, 2 }, tableName: "table1", columnName: "Val");
        kustoDatabase.AddTable(table);

        SummarizeNeonTestUtils.ExecuteAndAssertError(kustoDatabase, "table1 | summarize not_a_real_agg(Val)");
    }

    [Test]
    [Description("Summarize - Referencing a non-grouped, non-aggregated column directly in summarize fails")]
    public void Summarize_Error_NonAggregatedColumnWithoutGrouping()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)> { ("A", 1), ("A", 2) },
            tableName: "table1",
            columnName1: "Cat",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        SummarizeNeonTestUtils.ExecuteAndAssertError(kustoDatabase, "table1 | summarize Val by Cat");
    }

    [Test]
    [Description("Summarize - Referencing a by-group column directly inside a summarize aggregation expression fails in Kusto")]
    public void Summarize_Error_ReferencingByColumnInsideAggregationExpression()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(int, int)> { (10, 1), (10, 2) },
            tableName: "table1",
            columnName1: "Bucket",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        SummarizeNeonTestUtils.ExecuteAndAssertError(kustoDatabase, "table1 | summarize Weighted = Bucket + sum(Val) by Bucket");
    }

    #endregion
}
