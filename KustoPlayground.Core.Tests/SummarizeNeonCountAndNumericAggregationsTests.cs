namespace KustoPlayground.Core.Tests;

public class SummarizeNeonCountAndNumericAggregationsTests
{
    #region count(), cnt(), and countif()

    [Test]
    [Description("Summarize - count() without by returns total row count as long with default column name count_")]
    public void Summarize_Count_NoBy()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 10, 20, 30, 40, 50 },
            tableName: "table1",
            columnName: "Val");
        kustoDatabase.AddTable(table);

        List<long> actual = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "count_",
            kustoDatabase,
            "table1 | summarize count()");

        Assert.That(actual, Is.EqualTo(new List<long> { 5L }));
    }

    [Test]
    [Description("Summarize - cnt() alias behaves identically to count() with default column name cnt_")]
    public void Summarize_CntAlias_MatchesCount()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<string> { "A", "A", "B" },
            tableName: "table1",
            columnName: "Cat");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "Cat",
            "cnt_",
            kustoDatabase,
            "table1 | summarize cnt() by Cat");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("A", 2L), ("B", 1L) }));
    }

    [Test]
    [Description("Summarize - count() includes rows whose other columns contain null")]
    public void Summarize_Count_IncludesNullRows()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, int?>(
            [
                ("A", 1),
                ("A", null),
                ("A", null),
                ("B", null)
            ],
            tableName: "table1",
            columnName1: "Cat",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "Cat",
            "count_",
            kustoDatabase,
            "table1 | summarize count() by Cat");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("A", 3L), ("B", 1L) }));
    }

    [Test]
    [Description("Summarize - countif() evaluates predicate with and without by")]
    public void Summarize_CountIf_BasicAndGrouped()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, int, bool)>
            {
                ("TX", 100, true),
                ("TX", 0, false),
                ("TX", 50, true),
                ("CA", 200, false),
                ("CA", 10, true),
                ("NY", 0, false)
            },
            tableName: "Events",
            columnName1: "State",
            columnName2: "Damage",
            columnName3: "Severe");
        kustoDatabase.AddTable(table);

        var ungrouped = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Events | summarize Total = count(), SevereCnt = countif(Severe), HighDmg = countif(Damage >= 100)");

        Assert.That(ungrouped["Total"], Is.EqualTo(6L));
        Assert.That(ungrouped["SevereCnt"], Is.EqualTo(3L));
        Assert.That(ungrouped["HighDmg"], Is.EqualTo(2L));

        var grouped = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            "State",
            "SevereAndDamaging",
            "ZeroMatches",
            kustoDatabase,
            "Events | summarize SevereAndDamaging = countif(Severe and Damage > 20), ZeroMatches = countif(Damage > 9999) by State");

        var expected = new List<(string, long, long)>
        {
            ("TX", 2L, 0L),
            ("CA", 0L, 0L),
            ("NY", 0L, 0L)
        };
        Assert.That(grouped, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - countif() treats null boolean or null comparison operands as false")]
    public void Summarize_CountIf_NullPredicateValuesNotCounted()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<bool?, int?>(
            [
                (true, 10),
                (false, 20),
                (null, 30),
                (true, null)
            ],
            tableName: "table1",
            columnName1: "Flag",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize FlagTrue = countif(Flag), ValPositive = countif(Val > 0)");

        Assert.That(row["FlagTrue"], Is.EqualTo(2L));
        Assert.That(row["ValPositive"], Is.EqualTo(3L));
    }

    #endregion

    #region dcount(), dcountif(), count_distinct(), count_distinctif()

    [Test]
    [Description("Summarize - dcount() counts distinct values across int, string, DateTime, and bool columns")]
    public void Summarize_DCount_MultipleDataTypes()
    {
        var kustoDatabase = new KustoDatabase();
        var dt1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dt2 = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var table = SummarizeNeonTestUtils.GenerateTableWithSchema(
            "Data",
            [
                new Column<int>("IntCol", isNullable: false),
                new Column<string>("StrCol", isNullable: false),
                new Column<DateTime>("DtCol", isNullable: false),
                new Column<bool>("BoolCol", isNullable: false)
            ],
            [
                new() { ["IntCol"] = 1, ["StrCol"] = "a", ["DtCol"] = dt1, ["BoolCol"] = true },
                new() { ["IntCol"] = 1, ["StrCol"] = "A", ["DtCol"] = dt1, ["BoolCol"] = true },
                new() { ["IntCol"] = 2, ["StrCol"] = "a", ["DtCol"] = dt2, ["BoolCol"] = false },
                new() { ["IntCol"] = 3, ["StrCol"] = "b", ["DtCol"] = dt2, ["BoolCol"] = true }
            ]);
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Data | summarize dcount(IntCol), dcount(StrCol), dcount(DtCol), dcount(BoolCol)");

        Assert.That(row["dcount_IntCol"], Is.EqualTo(3L));
        Assert.That(row["dcount_StrCol"], Is.EqualTo(3L)); // "a", "A", "b" (case-sensitive)
        Assert.That(row["dcount_DtCol"], Is.EqualTo(2L));
        Assert.That(row["dcount_BoolCol"], Is.EqualTo(2L));
    }

    [Test]
    [Description("Summarize - dcount() accepts optional accuracy parameter (0, 1, 2, 4) and groups correctly")]
    public void Summarize_DCount_WithAccuracyParameter()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, string)>
            {
                ("US", "u1"), ("US", "u2"), ("US", "u1"), ("US", "u3"),
                ("EU", "u1"), ("EU", "u1")
            },
            tableName: "Visits",
            columnName1: "Region",
            columnName2: "UserId");
        kustoDatabase.AddTable(table);

        var rowResult = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Visits | summarize A0 = dcount(UserId, 0), A1 = dcount(UserId, 1), A2 = dcount(UserId, 2), A4 = dcount(UserId, 4) by Region");

        var actual = rowResult.Select(r => (
            (string)r["Region"]!,
            (long)r["A0"]!,
            (long)r["A1"]!,
            (long)r["A2"]!,
            (long)r["A4"]!)).ToList();

        Assert.That(actual, Is.EquivalentTo(new List<(string, long, long, long, long)>
        {
            ("US", 3L, 3L, 3L, 3L),
            ("EU", 1L, 1L, 1L, 1L)
        }));
    }

    [Test]
    [Description("Summarize - dcount() ignores null values and returns 0 for an all-null group")]
    public void Summarize_DCount_IgnoresNulls()
    {
        var kustoDatabase = new KustoDatabase();
        // A dynamic column can hold both string values and genuine nulls.
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, object>(
            [
                ("G1", "x"),
                ("G1", "x"),
                ("G1", null),
                ("G1", "y"),
                ("G2", null),
                ("G2", null)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "Grp",
            "dcount_Val",
            kustoDatabase,
            "table1 | summarize dcount(Val) by Grp");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("G1", 2L), ("G2", 0L) }));
    }

    [Test]
    [Description("Summarize - dcount() with computed expression inside argument")]
    public void Summarize_DCount_ComputedExpression()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<string> { "Alpha", "ALPHA", "alpha", "Beta" },
            tableName: "table1",
            columnName: "Word");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize dcount(tolower(Word))");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(row), Is.EqualTo(new List<string> { "dcount_" }));
        Assert.That(row["dcount_"], Is.EqualTo(2L));
    }

    [Test]
    [Description("Summarize - dcountif() counts distinct values only where predicate is true and ignores nulls")]
    public void Summarize_DCountIf_GroupedAndNulls()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith3Columns<string, object, bool>(
            [
                ("G1", "a", true),
                ("G1", "a", true),
                ("G1", "b", true),
                ("G1", "c", false),
                ("G1", null, true),
                ("G2", "a", false),
                ("G2", "b", false)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Item",
            columnName3: "Active");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "Grp",
            "dcountif_Item",
            kustoDatabase,
            "table1 | summarize dcountif(Item, Active) by Grp");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("G1", 2L), ("G2", 0L) }));
    }

    [Test]
    [Description("Summarize - count_distinct() and count_distinctif() return exact distinct counts with Kusto default names")]
    public void Summarize_CountDistinct_AndCountDistinctIf()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith3Columns<string, int?, bool>(
            [
                ("A", 1, true),
                ("A", 1, false),
                ("A", 2, true),
                ("A", 3, false),
                ("A", null, true),
                ("B", 10, false),
                ("B", null, false)
            ],
            tableName: "table1",
            columnName1: "Cat",
            columnName2: "Id",
            columnName3: "Keep");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            "Cat",
            "count_distinct_Id",
            "count_distinctif_Id",
            kustoDatabase,
            "table1 | summarize count_distinct(Id), count_distinctif(Id, Keep) by Cat");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("A", 3L, 2L),
            ("B", 1L, 0L)
        }));
    }

    #endregion

    #region sum() and sumif()

    [Test]
    [Description("Summarize - sum() on int column widens to long and does not overflow 32-bit int")]
    public void Summarize_Sum_IntOverflowToLong()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { int.MaxValue, int.MaxValue, 10 },
            tableName: "table1",
            columnName: "Amount");
        kustoDatabase.AddTable(table);

        List<long> actual = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "sum_Amount",
            kustoDatabase,
            "table1 | summarize sum(Amount)");

        long expected = (long)int.MaxValue + int.MaxValue + 10L;
        Assert.That(actual, Is.EqualTo(new List<long> { expected }));
    }

    [Test]
    [Description("Summarize - sum() across long, double, decimal, and TimeSpan types including negative values")]
    public void Summarize_Sum_AllSupportedNumericAndTimeSpanTypes()
    {
        var kustoDatabase = new KustoDatabase();
        var table = SummarizeNeonTestUtils.GenerateTableWithSchema(
            "Metrics",
            [
                new Column<long>("LongVal", isNullable: false),
                new Column<double>("DoubleVal", isNullable: false),
                new Column<decimal>("DecVal", isNullable: false),
                new Column<TimeSpan>("Dur", isNullable: false)
            ],
            [
                new() { ["LongVal"] = 10000000000L, ["DoubleVal"] = 1.25,  ["DecVal"] = 10.10m, ["Dur"] = TimeSpan.FromMinutes(30) },
                new() { ["LongVal"] = -5000000000L, ["DoubleVal"] = -0.50, ["DecVal"] = 20.25m, ["Dur"] = TimeSpan.FromMinutes(45) },
                new() { ["LongVal"] = 2500000000L,  ["DoubleVal"] = 2.75,  ["DecVal"] = -5.35m, ["Dur"] = TimeSpan.FromSeconds(30) }
            ]);
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Metrics | summarize sum(LongVal), sum(DoubleVal), sum(DecVal), sum(Dur)");

        Assert.That(row["sum_LongVal"], Is.EqualTo(7500000000L));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["sum_DoubleVal"]), Is.EqualTo(3.5).Within(1e-9));
        Assert.That((decimal)row["sum_DecVal"]!, Is.EqualTo(25.00m));
        Assert.That((TimeSpan)row["sum_Dur"]!, Is.EqualTo(new TimeSpan(1, 15, 30)));
    }

    [Test]
    [Description("Summarize - sum() ignores nulls and returns 0 when all values in a group are null")]
    public void Summarize_Sum_NullsAndAllNullGroup()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, int?>(
            [
                ("A", 10),
                ("A", null),
                ("A", -3),
                ("B", null),
                ("B", null)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "Grp",
            "sum_Val",
            kustoDatabase,
            "table1 | summarize sum(Val) by Grp");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("A", 7L), ("B", 0L) }));
    }

    [Test]
    [Description("Summarize - sumif() sums only matching non-null rows and returns 0 when predicate never matches")]
    public void Summarize_SumIf_GroupedAndAllFalse()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith3Columns<string, int?, string>(
            [
                ("TX", 100, "Hail"),
                ("TX", 250, "Wind"),
                ("TX", null, "Hail"),
                ("TX", 50, "Hail"),
                ("CA", 500, "Fire")
            ],
            tableName: "Storms",
            columnName1: "State",
            columnName2: "Damage",
            columnName3: "Kind");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "State",
            "sumif_Damage",
            kustoDatabase,
            "Storms | summarize sumif(Damage, Kind == 'Hail') by State");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("TX", 150L), ("CA", 0L) }));
    }

    [Test]
    [Description("Summarize - sum() over an arithmetic expression inside aggregation")]
    public void Summarize_Sum_ComputedExpressionInsideAggregation()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, int, double)>
            {
                ("Order1", 2, 10.5),
                ("Order1", 3, 5.0),
                ("Order2", 4, 2.25)
            },
            tableName: "Items",
            columnName1: "OrderId",
            columnName2: "Qty",
            columnName3: "UnitPrice");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Items | summarize Revenue = sum(Qty * UnitPrice) by OrderId");

        var actual = rows.Select(r => ((string)r["OrderId"]!, SummarizeNeonTestUtils.AsDouble(r["Revenue"]))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, double)>
        {
            ("Order1", 36.0),
            ("Order2", 9.0)
        }));
    }

    #endregion

    #region avg() and avgif()

    [Test]
    [Description("Summarize - avg() on int, long, double, decimal, and TimeSpan columns")]
    public void Summarize_Avg_AllSupportedTypes()
    {
        var kustoDatabase = new KustoDatabase();
        var table = SummarizeNeonTestUtils.GenerateTableWithSchema(
            "Measurements",
            [
                new Column<int>("IntVal", isNullable: false),
                new Column<long>("LongVal", isNullable: false),
                new Column<double>("DoubleVal", isNullable: false),
                new Column<decimal>("DecVal", isNullable: false),
                new Column<TimeSpan>("Dur", isNullable: false)
            ],
            [
                new() { ["IntVal"] = 1, ["LongVal"] = 10L, ["DoubleVal"] = 1.5, ["DecVal"] = 10.0m, ["Dur"] = TimeSpan.FromSeconds(10) },
                new() { ["IntVal"] = 2, ["LongVal"] = 20L, ["DoubleVal"] = 2.5, ["DecVal"] = 20.0m, ["Dur"] = TimeSpan.FromSeconds(20) },
                new() { ["IntVal"] = 4, ["LongVal"] = 60L, ["DoubleVal"] = 5.0, ["DecVal"] = 30.0m, ["Dur"] = TimeSpan.FromSeconds(60) }
            ]);
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Measurements | summarize avg(IntVal), avg(LongVal), avg(DoubleVal), avg(DecVal), avg(Dur)");

        Assert.That(SummarizeNeonTestUtils.AsDouble(row["avg_IntVal"]), Is.EqualTo(7.0 / 3.0).Within(1e-9));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["avg_LongVal"]), Is.EqualTo(30.0).Within(1e-9));
        Assert.That(SummarizeNeonTestUtils.AsDouble(row["avg_DoubleVal"]), Is.EqualTo(3.0).Within(1e-9));
        Assert.That((decimal)row["avg_DecVal"]!, Is.EqualTo(20.0m));
        Assert.That((TimeSpan)row["avg_Dur"]!, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    [Description("Summarize - avg() ignores nulls from both sum and denominator, and returns NaN for an all-null group")]
    public void Summarize_Avg_ExcludesNullsFromDenominatorAndAllNullIsNaN()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, int?>(
            [
                ("A", 10),
                ("A", null),
                ("A", 20),
                ("A", null),
                ("B", null),
                ("B", null)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize avg(Val) by Grp");

        var byGroup = rows.ToDictionary(r => (string)r["Grp"]!, r => SummarizeNeonTestUtils.AsDouble(r["avg_Val"]));
        Assert.That(byGroup["A"], Is.EqualTo(15.0).Within(1e-9)); // (10 + 20) / 2, NOT / 4
        Assert.That(double.IsNaN(byGroup["B"]), Is.True, "avg() over all-null group should be NaN.");
    }

    [Test]
    [Description("Summarize - avgif() averages matching non-null values and returns NaN when no rows match")]
    public void Summarize_AvgIf_GroupedAndNoMatchIsNaN()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith3Columns<string, int?, bool>(
            [
                ("G1", 10, true),
                ("G1", 30, true),
                ("G1", null, true),
                ("G1", 1000, false),
                ("G2", 50, false)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val",
            columnName3: "Include");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize avgif(Val, Include) by Grp");

        var byGroup = rows.ToDictionary(r => (string)r["Grp"]!, r => SummarizeNeonTestUtils.AsDouble(r["avgif_Val"]));
        Assert.That(byGroup["G1"], Is.EqualTo(20.0).Within(1e-9));
        Assert.That(double.IsNaN(byGroup["G2"]), Is.True, "avgif() with no matching rows should return NaN.");
    }

    [Test]
    [Description("Summarize - avg() on DateTime difference expression produces average TimeSpan")]
    public void Summarize_Avg_DateTimeDifferenceExpression()
    {
        var kustoDatabase = new KustoDatabase();
        var t0 = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(DateTime, DateTime)>
            {
                (t0, t0.AddMinutes(10)),
                (t0, t0.AddMinutes(30)),
                (t0, t0.AddMinutes(50))
            },
            tableName: "Jobs",
            columnName1: "StartTime",
            columnName2: "EndTime");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Jobs | summarize AvgDur = avg(EndTime - StartTime)");

        Assert.That((TimeSpan)row["AvgDur"]!, Is.EqualTo(TimeSpan.FromMinutes(30)));
    }

    #endregion

    #region min(), max(), minif(), maxif()

    [Test]
    [Description("Summarize - min() and max() across int, long, double, decimal, DateTime, TimeSpan, string, and bool")]
    public void Summarize_MinAndMax_AllComparableTypes()
    {
        var kustoDatabase = new KustoDatabase();
        var dt1 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dt2 = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var dt3 = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        var table = SummarizeNeonTestUtils.GenerateTableWithSchema(
            "AllTypes",
            [
                new Column<int>("IntCol", isNullable: false),
                new Column<long>("LongCol", isNullable: false),
                new Column<double>("DoubleCol", isNullable: false),
                new Column<decimal>("DecCol", isNullable: false),
                new Column<DateTime>("DtCol", isNullable: false),
                new Column<TimeSpan>("TsCol", isNullable: false),
                new Column<string>("StrCol", isNullable: false),
                new Column<bool>("BoolCol", isNullable: false)
            ],
            [
                new() { ["IntCol"] = -10, ["LongCol"] = 500L,  ["DoubleCol"] = -3.5, ["DecCol"] = 99.99m, ["DtCol"] = dt2, ["TsCol"] = TimeSpan.FromSeconds(30), ["StrCol"] = "banana", ["BoolCol"] = true },
                new() { ["IntCol"] = 25,  ["LongCol"] = -200L, ["DoubleCol"] = 0.0,  ["DecCol"] = -1.50m, ["DtCol"] = dt1, ["TsCol"] = TimeSpan.FromSeconds(5),  ["StrCol"] = "Apple",  ["BoolCol"] = false },
                new() { ["IntCol"] = 0,   ["LongCol"] = 9999L, ["DoubleCol"] = 42.1, ["DecCol"] = 10.00m, ["DtCol"] = dt3, ["TsCol"] = TimeSpan.FromMinutes(2),  ["StrCol"] = "cherry", ["BoolCol"] = true }
            ]);
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "AllTypes | summarize min(IntCol), max(IntCol), min(LongCol), max(LongCol), min(DoubleCol), max(DoubleCol), min(DecCol), max(DecCol), min(DtCol), max(DtCol), min(TsCol), max(TsCol), min(StrCol), max(StrCol), min(BoolCol), max(BoolCol)");

        Assert.That(row["min_IntCol"], Is.EqualTo(-10));
        Assert.That(row["max_IntCol"], Is.EqualTo(25));
        Assert.That(row["min_LongCol"], Is.EqualTo(-200L));
        Assert.That(row["max_LongCol"], Is.EqualTo(9999L));
        Assert.That(row["min_DoubleCol"], Is.EqualTo(-3.5));
        Assert.That(row["max_DoubleCol"], Is.EqualTo(42.1));
        Assert.That(row["min_DecCol"], Is.EqualTo(-1.50m));
        Assert.That(row["max_DecCol"], Is.EqualTo(99.99m));
        Assert.That(row["min_DtCol"], Is.EqualTo(dt1));
        Assert.That(row["max_DtCol"], Is.EqualTo(dt3));
        Assert.That(row["min_TsCol"], Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(row["max_TsCol"], Is.EqualTo(TimeSpan.FromMinutes(2)));
        Assert.That(row["min_StrCol"], Is.EqualTo("Apple")); // Ordinal: 'A' (65) < 'b' (98) < 'c' (99)
        Assert.That(row["max_StrCol"], Is.EqualTo("cherry"));
        Assert.That(row["min_BoolCol"], Is.EqualTo(false));
        Assert.That(row["max_BoolCol"], Is.EqualTo(true));
    }

    [Test]
    [Description("Summarize - min() and max() ignore nulls and return null for an all-null group")]
    public void Summarize_MinAndMax_NullHandling()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, int?>(
            [
                ("A", null),
                ("A", 5),
                ("A", 20),
                ("A", null),
                ("B", null),
                ("B", null)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var actual = SummarizeNeonTestUtils.ExecuteAndGetNullableDataFor3Columns<string, int?, int?>(
            "Grp",
            "min_Val",
            "max_Val",
            kustoDatabase,
            "table1 | summarize min(Val), max(Val) by Grp");

        var expected = new List<(string?, int?, int?)>
        {
            ("A", 5, 20),
            ("B", null, null)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - minif() and maxif() filter by predicate, ignore nulls, and return null when no rows match")]
    public void Summarize_MinIfAndMaxIf_GroupedAndNoMatches()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith3Columns<string, int?, bool>(
            [
                ("G1", 1, false),
                ("G1", 10, true),
                ("G1", 50, true),
                ("G1", null, true),
                ("G1", 999, false),
                ("G2", 25, false)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val",
            columnName3: "Valid");
        kustoDatabase.AddTable(table);

        var actual = SummarizeNeonTestUtils.ExecuteAndGetNullableDataFor3Columns<string, int?, int?>(
            "Grp",
            "minif_Val",
            "maxif_Val",
            kustoDatabase,
            "table1 | summarize minif(Val, Valid), maxif(Val, Valid) by Grp");

        var expected = new List<(string?, int?, int?)>
        {
            ("G1", 10, 50),
            ("G2", null, null)
        };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Summarize - max() and min() on string length expression")]
    public void Summarize_MinAndMax_OnScalarFunctionExpression()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, string)>
            {
                ("G1", "a"),
                ("G1", "hello"),
                ("G1", "hi"),
                ("G2", "world!")
            },
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Word");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize MinLen = min(strlen(Word)), MaxLen = max(strlen(Word)) by Grp");

        var actual = rows.Select(r => (
            (string)r["Grp"]!,
            SummarizeNeonTestUtils.AsLong(r["MinLen"]),
            SummarizeNeonTestUtils.AsLong(r["MaxLen"]))).ToList();

        Assert.That(actual, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("G1", 1L, 5L),
            ("G2", 6L, 6L)
        }));
    }

    #endregion
}
