namespace KustoPlayground.Core.Tests;

public class SummarizeNeonRowSelectorsAndCollectionsTests
{
    #region arg_max() and arg_min()

    [Test]
    [Description("Summarize - arg_max() and arg_min() return row columns corresponding to maximum and minimum key per group")]
    public void Summarize_ArgMaxAndArgMin_BasicGrouped()
    {
        var kustoDatabase = new KustoDatabase();
        var t1 = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2025, 1, 2, 10, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2025, 1, 3, 10, 0, 0, DateTimeKind.Utc);

        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, DateTime, string)>
            {
                ("TX", t1, "Flood"),
                ("TX", t3, "Tornado"),
                ("TX", t2, "Hail"),
                ("CA", t2, "Wildfire"),
                ("CA", t1, "Wind")
            },
            tableName: "Storms",
            columnName1: "State",
            columnName2: "Timestamp",
            columnName3: "EventType");
        kustoDatabase.AddTable(table);

        var maxRows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Storms | summarize arg_max(Timestamp, EventType) by State");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(maxRows[0]),
            Is.EqualTo(new List<string> { "State", "Timestamp", "EventType" }));

        var actualMax = maxRows.Select(r => ((string)r["State"]!, (DateTime)r["Timestamp"]!, (string)r["EventType"]!)).ToList();
        Assert.That(actualMax, Is.EquivalentTo(new List<(string, DateTime, string)>
        {
            ("TX", t3, "Tornado"),
            ("CA", t2, "Wildfire")
        }));

        var minActual = TestUtils.ExecuteAndGetDataFor3Columns<string, DateTime, string>(
            "State",
            "Timestamp",
            "EventType",
            kustoDatabase,
            "Storms | summarize arg_min(Timestamp, EventType) by State");

        Assert.That(minActual, Is.EquivalentTo(new List<(string, DateTime, string)>
        {
            ("TX", t1, "Flood"),
            ("CA", t1, "Wind")
        }));
    }

    [Test]
    [Description("Summarize - arg_max() with wildcard (*) returns all other columns from the winning row (with and without by)")]
    public void Summarize_ArgMax_Wildcard_WithAndWithoutBy()
    {
        var kustoDatabase = new KustoDatabase();
        var table = SummarizeNeonTestUtils.GenerateTableWithSchema(
            "Orders",
            [
                new Column<string>("Customer", isNullable: false),
                new Column<int>("OrderId", isNullable: false),
                new Column<int>("Amount", isNullable: false),
                new Column<string>("Status", isNullable: false)
            ],
            [
                new() { ["Customer"] = "Alice", ["OrderId"] = 1, ["Amount"] = 50,  ["Status"] = "Shipped" },
                new() { ["Customer"] = "Alice", ["OrderId"] = 2, ["Amount"] = 300, ["Status"] = "Pending" },
                new() { ["Customer"] = "Bob",   ["OrderId"] = 3, ["Amount"] = 120, ["Status"] = "Delivered" },
                new() { ["Customer"] = "Bob",   ["OrderId"] = 4, ["Amount"] = 80,  ["Status"] = "Cancelled" }
            ]);
        kustoDatabase.AddTable(table);

        // With by clause: output columns are by-col, maximize-col, then remaining input columns
        var groupedRows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Orders | summarize arg_max(Amount, *) by Customer");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(groupedRows[0]),
            Is.EqualTo(new List<string> { "Customer", "Amount", "OrderId", "Status" }));

        var byCustomer = groupedRows.ToDictionary(
            r => (string)r["Customer"]!,
            r => ((int)r["Amount"]!, (int)r["OrderId"]!, (string)r["Status"]!));

        Assert.That(byCustomer["Alice"], Is.EqualTo((300, 2, "Pending")));
        Assert.That(byCustomer["Bob"], Is.EqualTo((120, 3, "Delivered")));

        // Without by clause: single overall winning row
        var topRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "Orders | summarize arg_max(Amount, *)");

        Assert.That(topRow["Customer"], Is.EqualTo("Alice"));
        Assert.That(topRow["Amount"], Is.EqualTo(300));
        Assert.That(topRow["OrderId"], Is.EqualTo(2));
        Assert.That(topRow["Status"], Is.EqualTo("Pending"));
    }

    [Test]
    [Description("Summarize - arg_min() with wildcard (*) returns all columns from the row with minimum value")]
    public void Summarize_ArgMin_Wildcard()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, int, string)>
            {
                ("A", 30, "slow"),
                ("A", 5, "fast"),
                ("B", 12, "medium"),
                ("B", 2, "instant")
            },
            tableName: "Runs",
            columnName1: "Service",
            columnName2: "LatencyMs",
            columnName3: "Tag");
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor3Columns<string, int, string>(
            "Service",
            "LatencyMs",
            "Tag",
            kustoDatabase,
            "Runs | summarize arg_min(LatencyMs, *) by Service");

        Assert.That(actual, Is.EquivalentTo(new List<(string, int, string)>
        {
            ("A", 5, "fast"),
            ("B", 2, "instant")
        }));
    }

    [Test]
    [Description("Summarize - arg_max() with multiple return columns, inline alias, tuple alias, and single alias with wildcard")]
    public void Summarize_ArgMax_AliasingForms()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, int, string)>
            {
                ("G1", 10, "low"),
                ("G1", 99, "high")
            },
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Score",
            columnName3: "Label");
        kustoDatabase.AddTable(table);

        // Tuple alias: (MaxScore, TopLabel) = arg_max(Score, Label)
        var tupleRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize (MaxScore, TopLabel) = arg_max(Score, Label) by Grp");
        Assert.That(SummarizeNeonTestUtils.GetColumnNames(tupleRow), Is.EqualTo(new List<string> { "Grp", "MaxScore", "TopLabel" }));
        Assert.That(tupleRow["MaxScore"], Is.EqualTo(99));
        Assert.That(tupleRow["TopLabel"], Is.EqualTo("high"));

        // Inline argument alias: arg_max(Score, TopLabel = Label)
        var inlineRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize arg_max(Score, TopLabel = Label) by Grp");
        Assert.That(SummarizeNeonTestUtils.GetColumnNames(inlineRow), Is.EqualTo(new List<string> { "Grp", "Score", "TopLabel" }));
        Assert.That(inlineRow["Score"], Is.EqualTo(99));
        Assert.That(inlineRow["TopLabel"], Is.EqualTo("high"));

        // Single alias with wildcard renames the maximize expression column only
        var singleAliasRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize BestScore = arg_max(Score, *) by Grp");
        Assert.That(SummarizeNeonTestUtils.GetColumnNames(singleAliasRow), Is.EqualTo(new List<string> { "Grp", "BestScore", "Label" }));
        Assert.That(singleAliasRow["BestScore"], Is.EqualTo(99));
        Assert.That(singleAliasRow["Label"], Is.EqualTo("high"));
    }

    [Test]
    [Description("Summarize - arg_max() with compound maximize expression and duplicate column disambiguation")]
    public void Summarize_ArgMax_ComputedExpressionAndDuplicateDisambiguation()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, int, int)>
            {
                ("A", 2, 10),  // sum = 12
                ("A", 8, 9)    // sum = 17 (winner)
            },
            tableName: "table1",
            columnName1: "Cat",
            columnName2: "X",
            columnName3: "Y");
        kustoDatabase.AddTable(table);

        // Compound maximize expression defaults to "max_"
        var exprRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize arg_max(X + Y, X, Y) by Cat");
        Assert.That(SummarizeNeonTestUtils.GetColumnNames(exprRow), Is.EqualTo(new List<string> { "Cat", "max_", "X", "Y" }));
        Assert.That(SummarizeNeonTestUtils.AsDouble(exprRow["max_"]), Is.EqualTo(17.0).Within(1e-9));
        Assert.That(exprRow["X"], Is.EqualTo(8));
        Assert.That(exprRow["Y"], Is.EqualTo(9));

        // Returning the by column itself disambiguates to Cat1
        var dupRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize arg_max(X, Cat) by Cat");
        Assert.That(SummarizeNeonTestUtils.GetColumnNames(dupRow), Is.EqualTo(new List<string> { "Cat", "X", "Cat1" }));
        Assert.That(dupRow["Cat"], Is.EqualTo("A"));
        Assert.That(dupRow["X"], Is.EqualTo(8));
        Assert.That(dupRow["Cat1"], Is.EqualTo("A"));
    }

    [Test]
    [Description("Summarize - arg_max() and arg_min() ignore null keys unless all keys in the group are null")]
    public void Summarize_ArgMaxAndArgMin_NullKeyHandling()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith3Columns<string, int?, string>(
            [
                ("Mixed", null, "null-row"),
                ("Mixed", 10, "ten"),
                ("Mixed", 25, "twenty-five"),
                ("AllNull", null, "only-null-row")
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Key",
            columnName3: "Payload");
        kustoDatabase.AddTable(table);

        var maxRows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize arg_max(Key, Payload) by Grp");

        var maxByGrp = maxRows.ToDictionary(r => (string)r["Grp"]!, r => (r["Key"], (string?)r["Payload"]));
        Assert.That(maxByGrp["Mixed"], Is.EqualTo(((object?)25, "twenty-five")));
        Assert.That(maxByGrp["AllNull"], Is.EqualTo(((object?)null, "only-null-row")));

        var minRows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize arg_min(Key, Payload) by Grp");

        var minByGrp = minRows.ToDictionary(r => (string)r["Grp"]!, r => (r["Key"], (string?)r["Payload"]));
        Assert.That(minByGrp["Mixed"], Is.EqualTo(((object?)10, "ten")));
        Assert.That(minByGrp["AllNull"], Is.EqualTo(((object?)null, "only-null-row")));
    }

    [Test]
    [Description("Summarize - arg_max() when tied selects one of the tied rows with consistent column values from that same row")]
    public void Summarize_ArgMax_TiedKeys_ReturnsConsistentRow()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(int, string, int)>
            {
                (5, "low", 1),
                (10, "tieA", 101),
                (10, "tieB", 202)
            },
            tableName: "table1",
            columnName1: "Score",
            columnName2: "Name",
            columnName3: "Code");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize arg_max(Score, Name, Code)");

        Assert.That(row["Score"], Is.EqualTo(10));
        var validPairs = new List<(string, int)> { ("tieA", 101), ("tieB", 202) };
        Assert.That(validPairs, Does.Contain(((string)row["Name"]!, (int)row["Code"]!)));
    }

    [Test]
    [Description("Summarize - arg_max() and arg_min() on empty input return null numeric values and empty strings")]
    public void Summarize_ArgMaxAndArgMin_EmptyTable()
    {
        var kustoDatabase = new KustoDatabase();
        Table empty = SummarizeNeonTestUtils.GenerateEmptyTable(
            "EmptyTable",
            new Column<int>("Score", isNullable: false),
            new Column<string>("Name", isNullable: false));
        kustoDatabase.AddTable(empty);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "EmptyTable | summarize (MaxS, MaxN) = arg_max(Score, Name), (MinS, MinN) = arg_min(Score, Name)");

        Assert.That(row["MaxS"], Is.Null);
        Assert.That(row["MaxN"], Is.EqualTo(string.Empty));
        Assert.That(row["MinS"], Is.Null);
        Assert.That(row["MinN"], Is.EqualTo(string.Empty));
    }

    [Test]
    [Description("Summarize - Deprecated argmax() and argmin() aliases follow legacy Kusto default column names")]
    public void Summarize_DeprecatedArgMaxAndArgMinAliases()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)> { (1, "first"), (9, "last") },
            tableName: "table1",
            columnName1: "Val",
            columnName2: "Name");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize argmax(Val, Name), argmin(Val, Name)");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(row),
            Is.EqualTo(new List<string> { "max_Val", "max_Val_Name", "min_Val", "min_Val_Name" }));
        Assert.That(row["max_Val"], Is.EqualTo(9));
        Assert.That(row["max_Val_Name"], Is.EqualTo("last"));
        Assert.That(row["min_Val"], Is.EqualTo(1));
        Assert.That(row["min_Val_Name"], Is.EqualTo("first"));
    }

    #endregion

    #region take_any(), take_anyif(), any(), anyif()

    [Test]
    [Description("Summarize - take_any() with multiple columns returns values from the SAME row")]
    public void Summarize_TakeAny_MultipleColumns_ConsistentRow()
    {
        var kustoDatabase = new KustoDatabase();
        var validRows = new List<(string, int, string)>
        {
            ("G1", 1, "one"),
            ("G1", 2, "two"),
            ("G1", 3, "three"),
            ("G2", 10, "ten"),
            ("G2", 20, "twenty")
        };
        Table table = TestUtils.GenerateTableWith3Columns(
            validRows,
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Id",
            columnName3: "Word");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize take_any(Id, Word) by Grp");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(rows[0]),
            Is.EqualTo(new List<string> { "Grp", "Id", "Word" }));
        Assert.That(rows, Has.Count.EqualTo(2));

        foreach (var row in rows)
        {
            var tuple = ((string)row["Grp"]!, (int)row["Id"]!, (string)row["Word"]!);
            Assert.That(validRows, Does.Contain(tuple), "take_any(Id, Word) must select Id and Word from the same input row.");
        }
    }

    [Test]
    [Description("Summarize - take_any(*) selects an arbitrary full row per group")]
    public void Summarize_TakeAny_Wildcard()
    {
        var kustoDatabase = new KustoDatabase();
        var validRows = new List<(string, int, string)>
        {
            ("A", 1, "x"),
            ("A", 2, "y"),
            ("B", 3, "z")
        };
        Table table = TestUtils.GenerateTableWith3Columns(
            validRows,
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Num",
            columnName3: "Tag");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize take_any(*) by Grp");

        Assert.That(
            SummarizeNeonTestUtils.GetColumnNames(rows[0]),
            Is.EqualTo(new List<string> { "Grp", "Num", "Tag" }));
        Assert.That(rows, Has.Count.EqualTo(2));

        foreach (var row in rows)
        {
            Assert.That(validRows, Does.Contain(((string)row["Grp"]!, (int)row["Num"]!, (string)row["Tag"]!)));
        }
    }

    [Test]
    [Description("Summarize - take_any() on a single column prefers a non-null/non-empty value when available")]
    public void Summarize_TakeAny_SingleColumn_PrefersNonNull()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, string>(
            [
                ("G1", null),
                ("G1", null),
                ("G1", "Present"),
                ("G1", null),
                ("G2", null),
                ("G2", null)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var actual = SummarizeNeonTestUtils.ExecuteAndGetNullableDataFor2Columns<string, string>(
            "Grp",
            "Val",
            kustoDatabase,
            "table1 | summarize take_any(Val) by Grp");

        Assert.That(actual, Is.EquivalentTo(new List<(string?, string?)>
        {
            ("G1", "Present"),
            ("G2", "")
        }));
    }

    [Test]
    [Description("Summarize - take_any() supports inline alias, tuple alias, and computed expression default name any_arg0")]
    public void Summarize_TakeAny_AliasingAndComputedExpression()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)> { (10, "alpha") },
            tableName: "table1",
            columnName1: "Id",
            columnName2: "Name");
        kustoDatabase.AddTable(table);

        var inlineRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize take_any(Picked = Name)");
        Assert.That(SummarizeNeonTestUtils.GetColumnNames(inlineRow), Is.EqualTo(new List<string> { "Picked" }));
        Assert.That(inlineRow["Picked"], Is.EqualTo("alpha"));

        var tupleRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize (A, B) = take_any(Id, Name)");
        Assert.That(SummarizeNeonTestUtils.GetColumnNames(tupleRow), Is.EqualTo(new List<string> { "A", "B" }));
        Assert.That(tupleRow["A"], Is.EqualTo(10));
        Assert.That(tupleRow["B"], Is.EqualTo("alpha"));

        var exprRow = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize take_any(Id + 1)");
        Assert.That(SummarizeNeonTestUtils.GetColumnNames(exprRow), Is.EqualTo(new List<string> { "any_arg0" }));
        Assert.That(SummarizeNeonTestUtils.AsDouble(exprRow["any_arg0"]), Is.EqualTo(11.0).Within(1e-9));
    }

    [Test]
    [Description("Summarize - take_anyif() selects a matching string or the empty string when none match")]
    public void Summarize_TakeAnyIf_MatchingAndNonMatching()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, string, bool)>
            {
                ("G1", "skip1", false),
                ("G1", "validA", true),
                ("G1", "validB", true),
                ("G2", "skip2", false)
            },
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Item",
            columnName3: "IsValid");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize take_anyif(Item, IsValid) by Grp");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(rows[0]), Is.EqualTo(new List<string> { "Grp", "Item" }));
        var byGroup = rows.ToDictionary(r => (string)r["Grp"]!, r => (string?)r["Item"]);

        Assert.That(new[] { "validA", "validB" }, Does.Contain(byGroup["G1"]));
        Assert.That(byGroup["G2"], Is.EqualTo(string.Empty));
    }

    [Test]
    [Description("Summarize - Deprecated any() and anyif() aliases produce any_<col> and anyif_<col>")]
    public void Summarize_DeprecatedAnyAndAnyIfAliases()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith2Columns(
            new List<(string, bool)> { ("yes", true), ("no", false) },
            tableName: "table1",
            columnName1: "Word",
            columnName2: "Flag");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize any(Word), anyif(Word, Flag)");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(row), Is.EqualTo(new List<string> { "any_Word", "anyif_Word" }));
        Assert.That(new[] { "yes", "no" }, Does.Contain((string)row["any_Word"]!));
        Assert.That(row["anyif_Word"], Is.EqualTo("yes"));
    }

    #endregion

    #region make_list(), make_list_if(), make_list_with_nulls(), makelist()

    [Test]
    [Description("Summarize - make_list() preserves duplicates, excludes nulls, and uses default column name list_<col>")]
    public void Summarize_MakeList_PreservesDuplicatesExcludesNulls()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, int?>(
            [
                ("G1", 1),
                ("G1", 2),
                ("G1", 2),
                ("G1", null),
                ("G1", 3),
                ("G2", null),
                ("G2", null)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize make_list(Val) by Grp");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(rows[0]), Is.EqualTo(new List<string> { "Grp", "list_Val" }));
        var byGroup = rows.ToDictionary(
            r => (string)r["Grp"]!,
            r => SummarizeNeonTestUtils.ExtractTypedDynamicArray<int>(r["list_Val"]));

        Assert.That(byGroup["G1"], Is.EquivalentTo(new List<int> { 1, 2, 2, 3 }));
        Assert.That(byGroup["G2"], Is.Empty);
    }

    [Test]
    [Description("Summarize - make_list() after sort preserves the sorted input order")]
    public void Summarize_MakeList_AfterSort_PreservesOrder()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(
            new List<(string, int, string)>
            {
                ("A", 3, "third"),
                ("A", 1, "first"),
                ("A", 2, "second"),
                ("B", 20, "later"),
                ("B", 10, "earlier")
            },
            tableName: "Events",
            columnName1: "Grp",
            columnName2: "Seq",
            columnName3: "Step");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "Events | sort by Seq asc | summarize Steps = make_list(Step) by Grp");

        var byGroup = rows.ToDictionary(
            r => (string)r["Grp"]!,
            r => SummarizeNeonTestUtils.ExtractTypedDynamicArray<string>(r["Steps"]));

        Assert.That(byGroup["A"], Is.EqualTo(new List<string> { "first", "second", "third" }));
        Assert.That(byGroup["B"], Is.EqualTo(new List<string> { "earlier", "later" }));
    }

    [Test]
    [Description("Summarize - make_list(col, maxSize) caps output array length to maxSize")]
    public void Summarize_MakeList_WithMaxSize()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 1, 2, 3, 4, 5, 6 },
            tableName: "table1",
            columnName: "Val");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | sort by Val asc | summarize Limited = make_list(Val, 3)");

        var list = SummarizeNeonTestUtils.ExtractTypedDynamicArray<int>(row["Limited"]);
        Assert.That(list, Has.Count.EqualTo(3));
        Assert.That(list, Is.EqualTo(new List<int> { 1, 2, 3 }));
    }

    [Test]
    [Description("Summarize - make_list_if() collects only values satisfying predicate (with and without maxSize) and returns empty array when none match")]
    public void Summarize_MakeListIf_BasicAndMaxSize()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith3Columns<string, object, bool>(
            [
                ("G1", "a", true),
                ("G1", "a", true),
                ("G1", "b", false),
                ("G1", "c", true),
                ("G1", null, true),
                ("G2", "x", false)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Item",
            columnName3: "Keep");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize AllMatched = make_list_if(Item, Keep), TopTwo = make_list_if(Item, Keep, 2) by Grp");

        var byGroup = rows.ToDictionary(
            r => (string)r["Grp"]!,
            r => (
                All: SummarizeNeonTestUtils.ExtractTypedDynamicArray<string>(r["AllMatched"]),
                TopTwo: SummarizeNeonTestUtils.ExtractTypedDynamicArray<string>(r["TopTwo"])));

        Assert.That(byGroup["G1"].All, Is.EquivalentTo(new List<string> { "a", "a", "c" }));
        Assert.That(byGroup["G1"].TopTwo, Has.Count.EqualTo(2));
        Assert.That(byGroup["G2"].All, Is.Empty);
        Assert.That(byGroup["G2"].TopTwo, Is.Empty);
    }

    [Test]
    [Description("Summarize - make_list_with_nulls() retains null values in the resulting dynamic array")]
    public void Summarize_MakeListWithNulls_RetainsNulls()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, int?>(
            [
                ("G1", 1),
                ("G1", null),
                ("G1", 2),
                ("G1", null),
                ("G2", null)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Val");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize make_list_with_nulls(Val) by Grp");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(rows[0]), Is.EqualTo(new List<string> { "Grp", "list_Val" }));
        var byGroup = rows.ToDictionary(
            r => (string)r["Grp"]!,
            r => SummarizeNeonTestUtils.ExtractTypedDynamicArray<int?>(r["list_Val"]));

        Assert.That(byGroup["G1"], Is.EquivalentTo(new List<int?> { 1, null, 2, null }));
        Assert.That(byGroup["G2"], Is.EquivalentTo(new List<int?> { null }));
    }

    [Test]
    [Description("Summarize - Deprecated makelist() behaves identically to make_list()")]
    public void Summarize_DeprecatedMakeListAlias()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 1, 1, 2 },
            tableName: "table1",
            columnName: "Val");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize makelist(Val)");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(row), Is.EqualTo(new List<string> { "list_Val" }));
        Assert.That(SummarizeNeonTestUtils.ExtractTypedDynamicArray<int>(row["list_Val"]), Is.EquivalentTo(new List<int> { 1, 1, 2 }));
    }

    #endregion

    #region make_set(), make_set_if(), makeset()

    [Test]
    [Description("Summarize - make_set() deduplicates values, excludes nulls, preserves case sensitivity, and returns empty array for all-null group")]
    public void Summarize_MakeSet_DeduplicatesExcludesNullsAndCaseSensitive()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<string, object>(
            [
                ("G1", "apple"),
                ("G1", "apple"),
                ("G1", "Apple"),
                ("G1", null),
                ("G1", "banana"),
                ("G2", null),
                ("G2", null)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Fruit");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize make_set(Fruit) by Grp");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(rows[0]), Is.EqualTo(new List<string> { "Grp", "set_Fruit" }));
        var byGroup = rows.ToDictionary(
            r => (string)r["Grp"]!,
            r => SummarizeNeonTestUtils.ExtractTypedDynamicArray<string>(r["set_Fruit"]));

        Assert.That(byGroup["G1"], Is.EquivalentTo(new List<string> { "apple", "Apple", "banana" }));
        Assert.That(byGroup["G2"], Is.Empty);
    }

    [Test]
    [Description("Summarize - make_set(col, maxSize) caps number of distinct values to maxSize")]
    public void Summarize_MakeSet_WithMaxSize()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<int> { 1, 1, 2, 2, 3, 4, 5 },
            tableName: "table1",
            columnName: "Val");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize LimitedSet = make_set(Val, 3)");

        var set = SummarizeNeonTestUtils.ExtractTypedDynamicArray<int>(row["LimitedSet"]);
        Assert.That(set, Has.Count.EqualTo(3));
        Assert.That(set.Distinct().Count(), Is.EqualTo(3));
        Assert.That(set, Is.SubsetOf(new List<int> { 1, 2, 3, 4, 5 }));
    }

    [Test]
    [Description("Summarize - make_set_if() collects distinct values satisfying predicate and supports maxSize")]
    public void Summarize_MakeSetIf_BasicAndMaxSize()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = SummarizeNeonTestUtils.GenerateNullableTableWith3Columns<string, int?, bool>(
            [
                ("G1", 10, true),
                ("G1", 10, true),
                ("G1", 20, true),
                ("G1", 30, true),
                ("G1", 99, false),
                ("G1", null, true),
                ("G2", 50, false)
            ],
            tableName: "table1",
            columnName1: "Grp",
            columnName2: "Code",
            columnName3: "Keep");
        kustoDatabase.AddTable(table);

        var rows = SummarizeNeonTestUtils.ExecuteAndAssertSuccess(
            kustoDatabase,
            "table1 | summarize DistinctCodes = make_set_if(Code, Keep), CappedCodes = make_set_if(Code, Keep, 2) by Grp");

        var byGroup = rows.ToDictionary(
            r => (string)r["Grp"]!,
            r => (
                All: SummarizeNeonTestUtils.ExtractTypedDynamicArray<int>(r["DistinctCodes"]),
                Capped: SummarizeNeonTestUtils.ExtractTypedDynamicArray<int>(r["CappedCodes"])));

        Assert.That(byGroup["G1"].All, Is.EquivalentTo(new List<int> { 10, 20, 30 }));
        Assert.That(byGroup["G1"].Capped, Has.Count.EqualTo(2));
        Assert.That(byGroup["G1"].Capped.Distinct().Count(), Is.EqualTo(2));
        Assert.That(byGroup["G1"].Capped, Is.SubsetOf(new List<int> { 10, 20, 30 }));
        Assert.That(byGroup["G2"].All, Is.Empty);
        Assert.That(byGroup["G2"].Capped, Is.Empty);
    }

    [Test]
    [Description("Summarize - Deprecated makeset() behaves identically to make_set()")]
    public void Summarize_DeprecatedMakeSetAlias()
    {
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWithColumn(
            new List<string> { "x", "x", "y" },
            tableName: "table1",
            columnName: "Val");
        kustoDatabase.AddTable(table);

        var row = SummarizeNeonTestUtils.ExecuteAndGetSingleRow(
            kustoDatabase,
            "table1 | summarize makeset(Val)");

        Assert.That(SummarizeNeonTestUtils.GetColumnNames(row), Is.EqualTo(new List<string> { "set_Val" }));
        Assert.That(SummarizeNeonTestUtils.ExtractTypedDynamicArray<string>(row["set_Val"]), Is.EquivalentTo(new List<string> { "x", "y" }));
    }

    #endregion
}
