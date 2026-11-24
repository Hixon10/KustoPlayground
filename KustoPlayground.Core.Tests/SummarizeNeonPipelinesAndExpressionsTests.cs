namespace KustoPlayground.Core.Tests;

public class SummarizeNeonPipelinesAndExpressionsTests
{
    #region Post-aggregation scalar expressions

    [Test]
    [Description("Summarize - Post-aggregation expressions combine multiple aggregations in a single summarize clause")]
    public void Summarize_PostAggregationExpressions_RangeAndRatio()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, int, bool)>
            {
                ("SuiteA", 10, true),
                ("SuiteA", 30, true),
                ("SuiteA", 50, false),
                ("SuiteA", 70, true),
                ("SuiteB", 100, false),
                ("SuiteB", 120, false)
            },
            tableName: "TestRuns",
            columnName1: "Suite",
            columnName2: "DurationMs",
            columnName3: "Passed");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "TestRuns | summarize Spread = max(DurationMs) - min(DurationMs), PassRate = 1.0 * countif(Passed) / count(), TotalPlusTen = sum(DurationMs) + 10 by Suite");

        var bySuite = rows.ToDictionary(
            r => (string)r["Suite"]!,
            r => (
                Spread: SummarizeNeonTestUtils.AsDouble(r["Spread"]),
                PassRate: SummarizeNeonTestUtils.AsDouble(r["PassRate"]),
                TotalPlusTen: SummarizeNeonTestUtils.AsDouble(r["TotalPlusTen"])));

        Assert.That(bySuite["SuiteA"].Spread, Is.EqualTo(60.0).Within(1e-9));
        Assert.That(bySuite["SuiteA"].PassRate, Is.EqualTo(0.75).Within(1e-9));
        Assert.That(bySuite["SuiteA"].TotalPlusTen, Is.EqualTo(170.0).Within(1e-9));

        Assert.That(bySuite["SuiteB"].Spread, Is.EqualTo(20.0).Within(1e-9));
        Assert.That(bySuite["SuiteB"].PassRate, Is.EqualTo(0.0).Within(1e-9));
        Assert.That(bySuite["SuiteB"].TotalPlusTen, Is.EqualTo(230.0).Within(1e-9));
    }

    [Test]
    [Description("Summarize - Unnamed post-aggregation scalar expressions receive default names Column1, Column2")]
    public void Summarize_UnnamedPostAggregationExpressions_DefaultNames()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 2, 4, 6 },
            tableName: "table1",
            columnName: "Val");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize sum(Val) + 1, max(Val) - min(Val)");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(row), Is.EqualTo(new List<string> { "Column1", "Column2" }));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["Column1"]), Is.EqualTo(13.0).Within(1e-9));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["Column2"]), Is.EqualTo(4.0).Within(1e-9));
    }

    [Test]
    [Description("Summarize - Post-aggregation arithmetic expression combining sum() and count() with numeric group key")]
    public void Summarize_PostAggregationExpression_WithNumericGroupKey()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(int, int)>
            {
                (10, 1),
                (10, 2),
                (20, 5)
            },
            tableName: "table1",
            columnName1: "Bucket",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize Weighted = sum(Val) * 2 + count() by Bucket");

        var actual = rows.Select(r => ((int)r["Bucket"]!, SummarizeNeonTestUtils.AsDouble(r["Weighted"]))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(int, double)>
        {
            (10, 8.0),  // 3 * 2 + 2
            (20, 11.0)  // 5 * 2 + 1
        }));
    }

    #endregion

    #region Upstream pipeline operators feeding into summarize

    [Test]
    [Description("Summarize - where | summarize filters rows before grouping and handles empty filtered results properly")]
    public void Summarize_AfterWhere_NonEmptyAndFilteredToEmpty()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, string, int)>
            {
                ("TX", "Hail", 100),
                ("TX", "Wind", 50),
                ("TX", "Hail", 200),
                ("CA", "Hail", 300),
                ("CA", "Fire", 500)
            },
            tableName: "Storms",
            columnName1: "State",
            columnName2: "EventType",
            columnName3: "Damage");
        kustoDatabase.AddTable(table);

        // Non-empty filter
        var filtered = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            "State",
            "Cnt",
            "Total",
            kustoDatabase,
            "Storms | where EventType == 'Hail' | summarize Cnt = count(), Total = sum(Damage) by State");

        Assert.That(filtered, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("TX", 2L, 300L),
            ("CA", 1L, 300L)
        }));

        // Filtered to 0 rows WITH by => 0 rows
        var emptyWithBy = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Storms | where Damage > 999999 | summarize count(), sum(Damage) by State");
        Assert.That(emptyWithBy, Is.Empty);

        // Filtered to 0 rows WITHOUT by => 1 row of defaults
        var emptyWithoutBy = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Storms | where Damage > 999999 | summarize count(), sum(Damage), min(Damage)");
        Assert.That(emptyWithoutBy["count_"], Is.EqualTo(0L));
        Assert.That(emptyWithoutBy["sum_Damage"], Is.EqualTo(0L));
        Assert.That(emptyWithoutBy["min_Damage"], Is.Null);
    }

    [Test]
    [Description("Summarize - extend | summarize groups by and aggregates newly extended columns")]
    public void Summarize_AfterExtend()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, int, int)>
            {
                ("alpha", 2, 50),
                ("ALPHA", 3, 100),
                ("beta", 4, 25)
            },
            tableName: "Sales",
            columnName1: "Product",
            columnName2: "Qty",
            columnName3: "Price");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Sales | extend NormProduct = tolower(Product), LineTotal = Qty * Price | summarize TotalRevenue = sum(LineTotal), Rows = count() by NormProduct");

        var actual = rows.Select(r => (
            (string)r["NormProduct"]!,
            SummarizeNeonTestUtils.AsDouble(r["TotalRevenue"]),
            (long)r["Rows"]!)).ToList();

        Assert.That(actual, Is.EquivalentTo(new List<(string, double, long)>
        {
            ("alpha", 400.0, 2L),
            ("beta", 100.0, 1L)
        }));
    }

    [Test]
    [Description("Summarize - project | summarize operates on projected and renamed columns")]
    public void Summarize_AfterProject()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, int, string)>
            {
                ("US", 10, "extra1"),
                ("US", 20, "extra2"),
                ("EU", 5, "extra3")
            },
            tableName: "table1",
            columnName1: "Region",
            columnName2: "Val",
            columnName3: "Ignored");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "Area",
            "Total",
            kustoDatabase,
            "table1 | project Area = Region, Amount = Val | summarize Total = sum(Amount) by Area");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long)>
        {
            ("US", 30L),
            ("EU", 5L)
        }));
    }

    #endregion

    #region Downstream pipeline operators consuming summarize output

    [Test]
    [Description("Summarize - summarize | where filters groups (SQL HAVING equivalent)")]
    public void Summarize_FollowedByWhere_HavingSemantics()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)>
            {
                ("TX", 100), ("TX", 200), ("TX", 50),
                ("CA", 500),
                ("NY", 10), ("NY", 20)
            },
            tableName: "Sales",
            columnName1: "State",
            columnName2: "Amount");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            "State",
            "Cnt",
            "Total",
            kustoDatabase,
            "Sales | summarize Cnt = count(), Total = sum(Amount) by State | where Cnt >= 2 and Total > 100");

        Assert.That(actual, Is.EqualTo(new List<(string, long, long)> { ("TX", 3L, 350L) }));
    }

    [Test]
    [Description("Summarize - summarize | sort by | take implements Top-N groups in deterministic order")]
    public void Summarize_FollowedBySortAndTake_TopN()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)>
            {
                ("A", 10), ("A", 20),
                ("B", 100), ("B", 50),
                ("C", 5),
                ("D", 70), ("D", 10)
            },
            tableName: "Sales",
            columnName1: "Region",
            columnName2: "Amount");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "Region",
            "Total",
            kustoDatabase,
            "Sales | summarize Total = sum(Amount) by Region | sort by Total desc | take 2");

        // Order matters here because of sort + take
        Assert.That(actual, Is.EqualTo(new List<(string, long)>
        {
            ("B", 150L),
            ("D", 80L)
        }));
    }

    [Test]
    [Description("Summarize - summarize | extend | project computes derived columns and reshapes output")]
    public void Summarize_FollowedByExtendAndProject()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)>
            {
                ("A", 10), ("A", 30),
                ("B", 50), ("B", 50)
            },
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize Total = sum(Val), Cnt = count() by Grp | extend Mean = Total / Cnt | project GroupName = Grp, Mean");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(rows[0]), Is.EqualTo(new List<string> { "GroupName", "Mean" }));
        var actual = rows.Select(r => ((string)r["GroupName"]!, SummarizeNeonTestUtils.AsDouble(r["Mean"]))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, double)>
        {
            ("A", 20.0),
            ("B", 50.0)
        }));
    }

    [Test]
    [Description("Summarize - summarize | count counts the number of distinct groups")]
    public void Summarize_FollowedByCount()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<string> { "A", "A", "B", "C", "C", "C" },
            tableName: "table1",
            columnName: "Cat");
        kustoDatabase.AddTable(table);

        List<long> actual = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Count",
            kustoDatabase,
            "table1 | summarize count() by Cat | count");

        Assert.That(actual, Is.EqualTo(new List<long> { 3L }));
    }

    [Test]
    [Description("Summarize - summarize | distinct returns unique values of aggregated metric")]
    public void Summarize_FollowedByDistinct()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<string> { "A", "A", "B", "B", "C" },
            tableName: "table1",
            columnName: "Cat");
        kustoDatabase.AddTable(table);

        List<long> actual = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Cnt",
            kustoDatabase,
            "table1 | summarize Cnt = count() by Cat | distinct Cnt");

        Assert.That(actual, Is.EquivalentTo(new List<long> { 1L, 2L }));
    }

    [Test]
    [Description("Summarize - Summarized tabular subquery inside an in() filter on outer table")]
    public void Summarize_InsideInSubquery_FiltersOuterTable()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(int, string, int)>
            {
                (1, "Alice", 60),
                (2, "Alice", 70), // Alice total = 130 (>= 100)
                (3, "Bob", 40),   // Bob total = 40 (< 100)
                (4, "Cara", 150)  // Cara total = 150 (>= 100)
            },
            tableName: "Orders",
            columnName1: "OrderId",
            columnName2: "Customer",
            columnName3: "Amount");
        kustoDatabase.AddTable(table);

        List<int> actual = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            "OrderId",
            kustoDatabase,
            "Orders | where Customer in (Orders | summarize Total = sum(Amount) by Customer | where Total >= 100 | project Customer)");

        Assert.That(actual, Is.EquivalentTo(new List<int> { 1, 2, 4 }));
    }

    #endregion

    #region Multi-stage summarize pipelines & realistic end-to-end scenarios

    [Test]
    [Description("Summarize - Chained summarize | summarize computes two-level aggregation (per-user totals then global stats)")]
    public void Summarize_ChainedMultiStageSummarize()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)>
            {
                ("u1", 10), ("u1", 20), // u1 total = 30
                ("u2", 50),             // u2 total = 50
                ("u3", 10), ("u3", 30)  // u3 total = 40
            },
            tableName: "Purchases",
            columnName1: "UserId",
            columnName2: "Amount");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Purchases | summarize UserTotal = sum(Amount) by UserId | summarize UserCount = count(), AvgSpend = avg(UserTotal), MaxSpend = max(UserTotal), MinSpend = min(UserTotal)");

        Assert.That(row["UserCount"], Is.EqualTo(3L));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["AvgSpend"]), Is.EqualTo(40.0).Within(1e-9));
        Assert.That(row["MaxSpend"], Is.EqualTo(50L));
        Assert.That(row["MinSpend"], Is.EqualTo(30L));
    }

    [Test]
    [Description("Summarize - Chained bin() summarize followed by second summarize computes max hourly requests per service")]
    public void Summarize_ChainedBinSummarize_PeakHourlyRatePerService()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(DateTime, string)>
        {
            (new DateTime(2025, 4, 1, 10, 05, 0, DateTimeKind.Utc), "Auth"),
            (new DateTime(2025, 4, 1, 10, 20, 0, DateTimeKind.Utc), "Auth"),
            (new DateTime(2025, 4, 1, 10, 40, 0, DateTimeKind.Utc), "Auth"), // Auth @ 10h = 3
            (new DateTime(2025, 4, 1, 11, 10, 0, DateTimeKind.Utc), "Auth"), // Auth @ 11h = 1
            (new DateTime(2025, 4, 1, 10, 15, 0, DateTimeKind.Utc), "Api"),  // Api  @ 10h = 1
            (new DateTime(2025, 4, 1, 11, 01, 0, DateTimeKind.Utc), "Api"),
            (new DateTime(2025, 4, 1, 11, 30, 0, DateTimeKind.Utc), "Api")   // Api  @ 11h = 2
        };
        Table table = TestUtils.GenerateTableWith2Columns(rows, tableName: "Requests", columnName1: "Ts", columnName2: "Service");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            "Service",
            "PeakPerHour",
            "ActiveHours",
            kustoDatabase,
            "Requests | summarize HourlyCnt = count() by Service, bin(Ts, 1h) | summarize PeakPerHour = max(HourlyCnt), ActiveHours = count() by Service");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("Auth", 3L, 2L),
            ("Api", 2L, 2L)
        }));
    }

    [Test]
    [Description("Summarize - End-to-end realistic query on project's built-in StormEvents test table")]
    public void Summarize_StormEvents_BuiltInTable_EndToEnd()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        // Built-in StormEvents rows:
        // FLORIDA | Hurricane | 20000 | 2025-08-23 06:20:00
        // TEXAS   | Flood     |  5000 | 2023-03-28 10:30:00
        // FLORIDA | Tornado   |  5000 | 2024-06-01 16:50:30
        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "StormEvents | summarize EventCount = count(), TotalDamage = sum(DamageProperty), AvgDamage = avg(DamageProperty), DistinctEvents = dcount(EventType), LatestEvent = arg_max(StartTime, EventType) by State | sort by TotalDamage desc");

        Assert.That(rows, Has.Count.EqualTo(2));

        Assert.That(rows[0]["State"], Is.EqualTo("FLORIDA"));
        Assert.That(rows[0]["EventCount"], Is.EqualTo(2L));
        Assert.That(rows[0]["TotalDamage"], Is.EqualTo(25000L));
        Assert.That(SummarizeNeonTestUtils.AsDouble(rows[0]["AvgDamage"]), Is.EqualTo(12500.0).Within(1e-9));
        Assert.That(rows[0]["DistinctEvents"], Is.EqualTo(2L));
        Assert.That(rows[0]["LatestEvent"], Is.EqualTo(new DateTime(2025, 8, 23, 6, 20, 0)));
        Assert.That(rows[0]["EventType"], Is.EqualTo("Hurricane"));

        Assert.That(rows[1]["State"], Is.EqualTo("TEXAS"));
        Assert.That(rows[1]["EventCount"], Is.EqualTo(1L));
        Assert.That(rows[1]["TotalDamage"], Is.EqualTo(5000L));
        Assert.That(SummarizeNeonTestUtils.AsDouble(rows[1]["AvgDamage"]), Is.EqualTo(5000.0).Within(1e-9));
        Assert.That(rows[1]["DistinctEvents"], Is.EqualTo(1L));
        Assert.That(rows[1]["LatestEvent"], Is.EqualTo(new DateTime(2023, 3, 28, 10, 30, 0)));
        Assert.That(rows[1]["EventType"], Is.EqualTo("Flood"));
    }

    [Test]
    [Description("Summarize - Large group cardinality stress test with 1000 rows across 50 groups")]
    public void Summarize_LargerDataset_GroupingAndAggregations()
    {
        var kustoDatabase = new KustoDatabase();
        var inputRows = new List<(string, int)>();
        for (int g = 0; g < 50; g++)
        {
            for (int i = 1; i <= 20; i++)
            {
                inputRows.Add(($"Group_{g:D2}", i));
            }
        }

        Table table = TestUtils.GenerateTableWith2Columns(inputRows, tableName: "BigTable", columnName1: "Grp", columnName2: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "BigTable | summarize Cnt = count(), SumVal = sum(Val), MinVal = min(Val), MaxVal = max(Val), DistinctVals = dcount(Val) by Grp");

        Assert.That(rows, Has.Count.EqualTo(50));
        foreach (var row in rows)
        {
            Assert.That(row["Cnt"], Is.EqualTo(20L));
            Assert.That(row["SumVal"], Is.EqualTo(210L)); // sum(1..20) = 210
            Assert.That(row["MinVal"], Is.EqualTo(1));
            Assert.That(row["MaxVal"], Is.EqualTo(20));
            Assert.That(row["DistinctVals"], Is.EqualTo(20L));
        }
    }

    #endregion
}
