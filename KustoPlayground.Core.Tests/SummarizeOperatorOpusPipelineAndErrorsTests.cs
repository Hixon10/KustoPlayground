using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// summarize inside larger queries (https://learn.microsoft.com/en-us/kusto/query/summarize-operator):
/// where/extend/project/distinct/sort/take before summarize; where/sort/take/count/project/extend/distinct after it;
/// chained summarize and summarize in an 'in' subquery; documentation examples over durations; shuffle hints;
/// and invalid queries that Kusto rejects (the Kusto error codes are given in the descriptions).
/// </summary>
public class SummarizeOperatorOpusPipelineAndErrorsTests
{
    // Storms(EventType: string, StartTime: datetime, EndTime: datetime) with the durations
    // Drought 30d6h and 30d12h, Flood 2d1h, 3h and 1d12h, Heat 10d and exactly 1d
    private static Table BuildStormsTable()
    {
        var rows = new List<(string, DateTime, DateTime)>
        {
            ("Drought", new DateTime(2024, 1, 1), new DateTime(2024, 1, 31, 6, 0, 0)),
            ("Drought", new DateTime(2024, 2, 1), new DateTime(2024, 3, 2, 12, 0, 0)),
            ("Flood", new DateTime(2024, 4, 1), new DateTime(2024, 4, 3, 1, 0, 0)),
            ("Flood", new DateTime(2024, 5, 1), new DateTime(2024, 5, 1, 3, 0, 0)),
            ("Heat", new DateTime(2024, 6, 1), new DateTime(2024, 6, 11)),
            ("Heat", new DateTime(2024, 7, 1), new DateTime(2024, 7, 2)),
            ("Flood", new DateTime(2024, 8, 1), new DateTime(2024, 8, 2, 12, 0, 0))
        };

        return TestUtils.GenerateTableWith3Columns(rows, "EventType", "StartTime", "EndTime", "Storms");
    }

    #region operators before summarize

    [Test]
    [Description("where before summarize: only the matching rows are aggregated")]
    public void Where_BeforeSummarize_AggregatesOnlyMatchingRows()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            columnName1: "Region",
            columnName2: "Total",
            columnName3: "Orders",
            kustoDatabase,
            query: "Sales | where Amount > 2 | summarize Total = sum(Amount), Orders = count() by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("East", 35L, 3L), ("West", 10L, 2L)
        }));
    }

    [Test]
    [Description("where on a string column before summarize")]
    public void WhereOnString_BeforeSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            columnName1: "Product",
            columnName2: "Orders",
            columnName3: "Total",
            kustoDatabase,
            query: "Sales | where Region == 'West' | summarize Orders = count(), Total = sum(Amount) by Product");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("Apple", 1L, 7L), ("Pear", 3L, 6L)
        }));
    }

    [Test]
    [Description("where ... in (list) before summarize")]
    public void WhereIn_BeforeSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "sum_Amount",
            kustoDatabase,
            query: "Sales | where Product in ('Apple') | summarize sum(Amount) by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 30L), ("West", 7L) }));
    }

    [Test]
    [Description("where ... between before summarize (both bounds are inclusive)")]
    public void WhereBetween_BeforeSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "count_",
            kustoDatabase,
            query: "Sales | where Amount between (2 .. 10) | summarize count() by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 2L), ("West", 3L) }));
    }

    [Test]
    [Description("A bool column computed by extend is used as the group key")]
    public void Extend_BoolColumnAsKey()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<bool, long, long>(
            columnName1: "IsBig",
            columnName2: "Orders",
            columnName3: "Total",
            kustoDatabase,
            query: "Sales | extend IsBig = Amount >= 10 | summarize Orders = count(), Total = sum(Amount) by IsBig");

        Assert.That(actualData, Is.EquivalentTo(new List<(bool, long, long)>
        {
            (true, 2L, 30L), (false, 5L, 18L)
        }));
    }

    [Test]
    [Description("A string column computed by extend with a function is used as the group key")]
    public void Extend_FunctionResultAsKey()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Lower",
            columnName2: "count_",
            kustoDatabase,
            query: "Sales | extend Lower = tolower(Region) | summarize count() by Lower");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("east", 3L), ("west", 4L) }));
    }

    [Test]
    [Description("A numeric column computed by extend is aggregated (sum of Amount * 2)")]
    public void Extend_ComputedColumnIsAggregated()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | extend Doubled = Amount * 2 | summarize Total = sum(Doubled) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Total");
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Region", "East"), "Total"), Is.EqualTo(70.0));
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Region", "West"), "Total"), Is.EqualTo(26.0));
    }

    [Test]
    [Description("summarize aggregates the new value of a column overwritten by extend")]
    public void Extend_OverwrittenColumnIsAggregated()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | extend Amount = Amount * 10 | summarize sum(Amount)");

        AssertColumnsInOrder(row, "sum_Amount");
        Assert.That(GetNumber(row, "sum_Amount"), Is.EqualTo(480.0));
    }

    [Test]
    [Description("Columns renamed by project are used as key and aggregation argument; the key keeps the new name")]
    public void Project_RenamedColumnsBeforeSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | project R = Region, A = Amount | summarize Total = sum(A) by R");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "R", "Total");
        var actual = rows.Select(row => (GetValue<string>(row, "R"), GetValue<long>(row, "Total"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("East", 35L), ("West", 13L) }));
    }

    [Test]
    [Description("distinct before summarize: number of distinct products per region")]
    public void Distinct_BeforeSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "Products",
            kustoDatabase,
            query: "Sales | distinct Region, Product | summarize Products = count() by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)> { ("East", 2L), ("West", 2L) }));
    }

    [Test]
    [Description("sort and take before summarize: only the 3 largest amounts are aggregated")]
    public void SortAndTake_BeforeSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | sort by Amount desc | take 3 | summarize sum(Amount)");

        Assert.That(GetValue<long>(row, "sum_Amount"), Is.EqualTo(37L));
    }

    [Test]
    [Description("take before summarize: count() counts only the taken rows")]
    public void Take_BeforeSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | take 5 | summarize count()");

        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(5L));
    }

    #endregion

    #region operators after summarize

    [Test]
    [Description("where on an aggregate after summarize (like SQL HAVING)")]
    public void Where_OnAggregate()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, string, long>(
            columnName1: "Region",
            columnName2: "Product",
            columnName3: "Total",
            kustoDatabase,
            query: "Sales | summarize Total = sum(Amount) by Region, Product | where Total > 5");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string, long)>
        {
            ("East", "Apple", 30L), ("West", "Apple", 7L), ("West", "Pear", 6L)
        }));
    }

    [Test]
    [Description("where on the default aggregate name count_")]
    public void Where_OnDefaultAggregateName()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, string, long>(
            columnName1: "Product",
            columnName2: "Region",
            columnName3: "count_",
            kustoDatabase,
            query: "Sales | summarize count() by Product, Region | where count_ > 1");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string, long)>
        {
            ("Apple", "East", 2L), ("Pear", "West", 3L)
        }));
    }

    [Test]
    [Description("where with a compound condition over two aggregates")]
    public void Where_CompoundConditionOnAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize Orders = count(), Total = sum(Amount) by Region, Product " +
            "| where Orders >= 2 and Total < 10");

        AssertColumnsInOrder(row, "Region", "Product", "Orders", "Total");
        Assert.That(GetValue<string>(row, "Region"), Is.EqualTo("West"));
        Assert.That(GetValue<string>(row, "Product"), Is.EqualTo("Pear"));
        Assert.That(GetValue<long>(row, "Orders"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(row, "Total"), Is.EqualTo(6L));
    }

    [Test]
    [Description("where on a group key after summarize")]
    public void Where_OnKey()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize Total = sum(Amount) by Region | where Region == 'West'");

        AssertColumnsInOrder(row, "Region", "Total");
        Assert.That(GetValue<string>(row, "Region"), Is.EqualTo("West"));
        Assert.That(GetValue<long>(row, "Total"), Is.EqualTo(13L));
    }

    [Test]
    [Description("sort by an aggregate ascending")]
    public void Sort_ByAggregateAscending()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Product",
            columnName2: "Total",
            kustoDatabase,
            query: "Sales | summarize Total = sum(Amount) by Product | sort by Total asc");

        Assert.That(actualData, Is.EqualTo(new List<(string, long)> { ("Pear", 11L), ("Apple", 37L) }));
    }

    [Test]
    [Description("sort by an aggregate without asc/desc sorts descending")]
    public void Sort_ByAggregateIsDescendingByDefault()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Product",
            columnName2: "Total",
            kustoDatabase,
            query: "Sales | summarize Total = sum(Amount) by Product | sort by Total");

        Assert.That(actualData, Is.EqualTo(new List<(string, long)> { ("Apple", 37L), ("Pear", 11L) }));
    }

    [Test]
    [Description("sort by a key ascending, then by an aggregate descending")]
    public void Sort_ByKeyThenByAggregate()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, string, long>(
            columnName1: "Region",
            columnName2: "Product",
            columnName3: "Total",
            kustoDatabase,
            query: "Sales | summarize Total = sum(Amount) by Region, Product | sort by Region asc, Total desc");

        Assert.That(actualData, Is.EqualTo(new List<(string, string, long)>
        {
            ("East", "Apple", 30L), ("East", "Pear", 5L), ("West", "Apple", 7L), ("West", "Pear", 6L)
        }));
    }

    [Test]
    [Description("sort by a datetime key produced by bin()")]
    public void Sort_ByBinnedDateTimeKey()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<DateTime, long, long>(
            columnName1: "Day",
            columnName2: "Events",
            columnName3: "Damage",
            kustoDatabase,
            query: "StormEvents | summarize Events = count(), Damage = sum(DamageProperty) by Day = bin(StartTime, 1d) " +
                   "| sort by Day asc");

        Assert.That(actualData, Is.EqualTo(new List<(DateTime, long, long)>
        {
            (new DateTime(2023, 3, 28), 1L, 5000L),
            (new DateTime(2024, 6, 1), 1L, 5000L),
            (new DateTime(2025, 8, 23), 1L, 20000L)
        }));
    }

    [Test]
    [Description("sort and take 1 after summarize return the top group")]
    public void SortAndTake_TopGroup()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize Total = sum(Amount) by Region, Product | sort by Total desc | take 1");

        AssertColumnsInOrder(row, "Region", "Product", "Total");
        Assert.That(GetValue<string>(row, "Region"), Is.EqualTo("East"));
        Assert.That(GetValue<string>(row, "Product"), Is.EqualTo("Apple"));
        Assert.That(GetValue<long>(row, "Total"), Is.EqualTo(30L));
    }

    [Test]
    [Description("take after summarize without sort returns any one of the groups")]
    public void Take_AfterSummarize_ReturnsOneOfTheGroups()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "Region",
            columnName2: "count_",
            kustoDatabase,
            query: "Sales | summarize count() by Region | take 1");

        Assert.That(actualData, Has.Count.EqualTo(1));
        Assert.That(actualData[0], Is.AnyOf(("East", 3L), ("West", 4L)));
    }

    [Test]
    [Description("The count operator after summarize returns the number of groups")]
    public void CountOperator_NumberOfGroups()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize by Region, Product | count");

        AssertColumnsInOrder(row, "Count");
        Assert.That(GetValue<long>(row, "Count"), Is.EqualTo(4L));
    }

    [Test]
    [Description("project after summarize reorders and drops columns")]
    public void Project_ReordersAndDropsColumns()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize Total = sum(Amount), Orders = count() by Region | project Orders, Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Orders", "Region");
        var actual = rows.Select(row => (GetValue<long>(row, "Orders"), GetValue<string>(row, "Region"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(long, string)> { (3L, "East"), (4L, "West") }));
    }

    [Test]
    [Description("project after summarize renames a key")]
    public void Project_RenamesKeyAfterSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize Total = sum(Amount) by Region | project Area = Region, Total");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Area", "Total");
        var actual = rows.Select(row => (GetValue<string>(row, "Area"), GetValue<long>(row, "Total"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("East", 35L), ("West", 13L) }));
    }

    [Test]
    [Description("extend after summarize references a default aggregate name (sum_Amount)")]
    public void Extend_OverDefaultAggregateName()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize sum(Amount) by Region | extend Doubled = sum_Amount * 2");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "sum_Amount", "Doubled");

        var east = GetSingleRowWhere(rows, "Region", "East");
        Assert.That(GetValue<long>(east, "sum_Amount"), Is.EqualTo(35L));
        Assert.That(GetNumber(east, "Doubled"), Is.EqualTo(70.0));

        var west = GetSingleRowWhere(rows, "Region", "West");
        Assert.That(GetValue<long>(west, "sum_Amount"), Is.EqualTo(13L));
        Assert.That(GetNumber(west, "Doubled"), Is.EqualTo(26.0));
    }

    [Test]
    [Description("extend after summarize combines two aggregates (average order value)")]
    public void Extend_RatioOfAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize Total = sum(Amount), Orders = count() by Region | extend AvgOrder = Total * 1.0 / Orders");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Total", "Orders", "AvgOrder");
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Region", "East"), "AvgOrder"),
            Is.EqualTo(35.0 / 3).Within(1e-9));
        Assert.That(GetNumber(GetSingleRowWhere(rows, "Region", "West"), "AvgOrder"),
            Is.EqualTo(3.25).Within(1e-9));
    }

    [Test]
    [Description("extend after summarize with a comparison over an aggregate")]
    public void Extend_ComparisonOverAggregate()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, bool>(
            columnName1: "Region",
            columnName2: "IsLarge",
            kustoDatabase,
            query: "Sales | summarize Total = sum(Amount) by Region | extend IsLarge = Total > 20");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, bool)> { ("East", true), ("West", false) }));
    }

    [Test]
    [Description("distinct after summarize")]
    public void Distinct_AfterSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>("Region", kustoDatabase,
            "Sales | summarize by Region, Product | distinct Region");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "East", "West" }));
    }

    [Test]
    [Description("A report: where, summarize, where on an aggregate, sort by an aggregate")]
    public void ReportPipeline_WhereSummarizeWhereSort()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor4Columns<string, string, long, long>(
            columnName1: "Region",
            columnName2: "Product",
            columnName3: "Orders",
            columnName4: "Total",
            kustoDatabase,
            query: "Sales | where Amount > 1 | summarize Orders = count(), Total = sum(Amount) by Region, Product " +
                   "| where Orders > 1 | sort by Total desc");

        Assert.That(actualData, Is.EqualTo(new List<(string, string, long, long)>
        {
            ("East", "Apple", 2L, 30L), ("West", "Pear", 2L, 5L)
        }));
    }

    #endregion

    #region chained summarize and subqueries

    [Test]
    [Description("A second summarize re-aggregates the groups of the first one by a coarser key")]
    public void Chained_ReaggregateByCoarserKey()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>(
            columnName1: "Region",
            columnName2: "Best",
            columnName3: "Products",
            kustoDatabase,
            query: "Sales | summarize Total = sum(Amount) by Region, Product " +
                   "| summarize Best = max(Total), Products = count() by Region");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, long)>
        {
            ("East", 30L, 2L), ("West", 7L, 2L)
        }));
    }

    [Test]
    [Description("Frequency of frequencies: how many values occur once and how many occur twice")]
    public void Chained_FrequencyOfCounts()
    {
        Table table = TestUtils.GenerateTableWithColumn(new[] { 1L, 1L, 2L, 2L, 3L, 4L }, "x", "Numbers");
        var kustoDatabase = CreateDatabase(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, long>(
            columnName1: "Count",
            columnName2: "Groups",
            kustoDatabase,
            query: "Numbers | summarize Count = count() by x | summarize Groups = count() by Count");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long)> { (1L, 2L), (2L, 2L) }));
    }

    [Test]
    [Description("Three chained summarize operators")]
    public void Chained_ThreeLevels()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | summarize count() by Region, Product | summarize Products = count() by Region " +
            "| summarize Regions = count(), TotalProducts = sum(Products)");

        AssertColumnsInOrder(row, "Regions", "TotalProducts");
        Assert.That(GetValue<long>(row, "Regions"), Is.EqualTo(2L));
        Assert.That(GetValue<long>(row, "TotalProducts"), Is.EqualTo(4L));
    }

    [Test]
    [Description("summarize in an 'in' subquery selects the regions with a total above 20")]
    public void Subquery_InWithSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | where Region in (Sales | summarize Total = sum(Amount) by Region | where Total > 20 | project Region) " +
            "| summarize Orders = count(), Total = sum(Amount)");

        Assert.That(GetValue<long>(row, "Orders"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(row, "Total"), Is.EqualTo(35L));
    }

    [Test]
    [Description("summarize in a '!in' subquery excludes the regions with a total above 20")]
    public void Subquery_NotInWithSummarize()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sales | where Region !in (Sales | summarize Total = sum(Amount) by Region | where Total > 20 | project Region) " +
            "| summarize Orders = count(), Total = sum(Amount)");

        Assert.That(GetValue<long>(row, "Orders"), Is.EqualTo(4L));
        Assert.That(GetValue<long>(row, "Total"), Is.EqualTo(13L));
    }

    #endregion

    #region documentation examples

    [Test]
    [Description("Documentation example: minimum and maximum duration (Duration = EndTime - StartTime is computed with extend)")]
    public void DocExample_MinAndMaxDuration()
    {
        var kustoDatabase = CreateDatabase(BuildStormsTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Storms | extend Duration = EndTime - StartTime | summarize Min = min(Duration), Max = max(Duration)");

        AssertColumnsInOrder(row, "Min", "Max");
        Assert.That(GetValue<TimeSpan>(row, "Min"), Is.EqualTo(TimeSpan.FromHours(3)));
        Assert.That(GetValue<TimeSpan>(row, "Max"), Is.EqualTo(new TimeSpan(30, 12, 0, 0)));
    }

    [Test]
    [Description("Documentation example: event types of storms longer than 1 day, grouped into 1-day bins, sorted by length")]
    public void DocExample_DurationHistogram()
    {
        var kustoDatabase = CreateDatabase(BuildStormsTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Storms | extend Duration = EndTime - StartTime | where Duration > 1d " +
            "| summarize EventCount = count() by EventType, Length = bin(Duration, 1d) | sort by Length");

        Assert.That(rows, Has.Count.EqualTo(4));
        AssertColumnsInOrder(rows[0], "EventType", "Length", "EventCount");
        var actual = rows.Select(row => (
            GetValue<string>(row, "EventType"),
            GetValue<TimeSpan>(row, "Length"),
            GetValue<long>(row, "EventCount"))).ToList();
        Assert.That(actual, Is.EqualTo(new List<(string, TimeSpan, long)>
        {
            ("Drought", TimeSpan.FromDays(30), 2L),
            ("Heat", TimeSpan.FromDays(10), 1L),
            ("Flood", TimeSpan.FromDays(2), 1L),
            ("Flood", TimeSpan.FromDays(1), 1L)
        }));
    }

    [Test]
    [Description("Documentation example: number of distinct event types per state, sorted by that number")]
    public void DocExample_DistinctCountPerStateSorted()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            columnName1: "State",
            columnName2: "TypesOfStorms",
            kustoDatabase,
            query: "StormEvents | summarize TypesOfStorms = dcount(EventType) by State | sort by TypesOfStorms");

        Assert.That(actualData, Is.EqualTo(new List<(string, long)> { ("FLORIDA", 2L), ("TEXAS", 1L) }));
    }

    #endregion

    #region shuffle hints

    private static void AssertTotalsPerRegion(string query)
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, query);

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region", "Total");
        var actual = rows.Select(row => (GetValue<string>(row, "Region"), GetValue<long>(row, "Total"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new List<(string, long)> { ("East", 35L), ("West", 13L) }));
    }

    [Test]
    [Description("hint.strategy=shuffle does not change the result")]
    public void Hint_StrategyShuffle()
    {
        AssertTotalsPerRegion("Sales | summarize hint.strategy=shuffle Total = sum(Amount) by Region");
    }

    [Test]
    [Description("hint.shufflekey does not change the result")]
    public void Hint_ShuffleKey()
    {
        AssertTotalsPerRegion("Sales | summarize hint.shufflekey=Region Total = sum(Amount) by Region");
    }

    [Test]
    [Description("hint.num_partitions together with hint.strategy=shuffle does not change the result")]
    public void Hint_NumPartitions()
    {
        AssertTotalsPerRegion("Sales | summarize hint.strategy=shuffle hint.num_partitions=2 Total = sum(Amount) by Region");
    }

    [Test]
    [Description("A shuffle hint on summarize without by still returns a single row")]
    public void Hint_WithoutBy()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Sales | summarize hint.strategy=shuffle count()");

        AssertColumnsInOrder(row, "count_");
        Assert.That(GetValue<long>(row, "count_"), Is.EqualTo(7L));
    }

    [Test]
    [Description("A shuffle hint on summarize with only a by clause returns the distinct keys")]
    public void Hint_ByOnly()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Sales | summarize hint.strategy=shuffle by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0], "Region");
        Assert.That(GetValues<string>(rows, "Region"), Is.EquivalentTo(new[] { "East", "West" }));
    }

    [Test]
    [Description("hint.shufflekey with percentiles(), dcount(), make_list() and take_any() (shape of the shuffle query documentation example)")]
    public void Hint_ShuffleKeyWithSeveralAggregates()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Sales | summarize hint.shufflekey=Region (p50, p90) = percentiles(Amount, 50, 90), dcount(Amount), " +
            "dcount(Amount, 4), make_list(Amount), take_any(Amount, Region) by Region");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertColumnsInOrder(rows[0],
            "Region", "p50", "p90", "dcount_Amount", "dcount_Amount1", "list_Amount", "Amount", "Region1");

        var east = GetSingleRowWhere(rows, "Region", "East");
        Assert.That(GetValue<long>(east, "p50"), Is.EqualTo(10L));
        Assert.That(GetValue<long>(east, "p90"), Is.EqualTo(20L));
        Assert.That(GetValue<long>(east, "dcount_Amount"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(east, "dcount_Amount1"), Is.EqualTo(3L));
        Assert.That(GetDynamicArray(east, "list_Amount"), Is.EquivalentTo(new object?[] { 10L, 20L, 5L }));
        Assert.That(GetValue<long>(east, "Amount"), Is.AnyOf(10L, 20L, 5L));
        Assert.That(GetValue<string>(east, "Region1"), Is.EqualTo("East"));

        var west = GetSingleRowWhere(rows, "Region", "West");
        Assert.That(GetValue<long>(west, "p50"), Is.EqualTo(2L));
        Assert.That(GetValue<long>(west, "p90"), Is.EqualTo(7L));
        Assert.That(GetValue<long>(west, "dcount_Amount"), Is.EqualTo(4L));
        Assert.That(GetValue<long>(west, "dcount_Amount1"), Is.EqualTo(4L));
        Assert.That(GetDynamicArray(west, "list_Amount"), Is.EquivalentTo(new object?[] { 7L, 1L, 2L, 3L }));
        Assert.That(GetValue<long>(west, "Amount"), Is.AnyOf(7L, 1L, 2L, 3L));
        Assert.That(GetValue<string>(west, "Region1"), Is.EqualTo("West"));
    }

    #endregion

    #region invalid queries

    [Test]
    [Description("summarize without aggregations and without by is rejected (SEM0001)")]
    public void Invalid_NoAggregationsAndNoBy()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize");
    }

    [Test]
    [Description("An empty by clause is a syntax error (KS006)")]
    public void Invalid_EmptyByClause()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize by");
    }

    [Test]
    [Description("A plain column is not an aggregation (KS236)")]
    public void Invalid_ColumnInsteadOfAggregation()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize Amount by Region");
    }

    [Test]
    [Description("A scalar function is not an aggregation (SEM0001)")]
    public void Invalid_ScalarFunctionInsteadOfAggregation()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize strlen(Region)");
    }

    [Test]
    [Description("An expression without an aggregation is rejected (KS236)")]
    public void Invalid_ExpressionWithoutAggregation()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize Amount + 1");
    }

    [Test]
    [Description("Unknown aggregation function (KS211)")]
    public void Invalid_UnknownFunction()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize foo(Amount)");
    }

    [Test]
    [Description("sum() and avg() of a string column are rejected (KS138, KS127)")]
    public void Invalid_SumAndAvgOfString()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        AssertQueryFails(kustoDatabase, "Sales | summarize sum(Region)");
        AssertQueryFails(kustoDatabase, "Sales | summarize avg(Region)");
    }

    [Test]
    [Description("Unlike string, sum() and avg() of a bool column are accepted (true counts as 1, false as 0): sum() returns long, avg() returns real")]
    public void SumAndAvgOfBool_AreAccepted()
    {
        var kustoDatabase = CreateDatabase(TestUtils.GenerateTableWithColumn(new[] { true, false, true }, "Flag", "Flags"));

        var sumRow = ExecuteAndGetSingleRow(kustoDatabase, "Flags | summarize sum(Flag)");
        AssertColumnsInOrder(sumRow, "sum_Flag");
        Assert.That(GetValue<long>(sumRow, "sum_Flag"), Is.EqualTo(2L));

        var avgRow = ExecuteAndGetSingleRow(kustoDatabase, "Flags | summarize avg(Flag)");
        AssertColumnsInOrder(avgRow, "avg_Flag");
        Assert.That(GetValue<double>(avgRow, "avg_Flag"), Is.EqualTo(2.0 / 3).Within(1e-9));
    }

    [Test]
    [Description("sum(), min() and avg() of a guid column are rejected (KS138, KS206, KS127)")]
    public void Invalid_AggregationsOfGuid()
    {
        var ids = new[]
        {
            Guid.Parse("2b6e1b4c-0d7a-4f43-9a51-6c1f0f3e8a01"),
            Guid.Parse("7f1c9d2e-3b4a-4c5d-8e6f-1a2b3c4d5e6f")
        };
        var kustoDatabase = CreateDatabase(TestUtils.GenerateTableWithColumn(ids, "Id", "Ids"));

        AssertQueryFails(kustoDatabase, "Ids | summarize sum(Id)");
        AssertQueryFails(kustoDatabase, "Ids | summarize min(Id)");
        AssertQueryFails(kustoDatabase, "Ids | summarize avg(Id)");
    }

    [Test]
    [Description("Unknown column in the by clause (KS142)")]
    public void Invalid_UnknownColumnInBy()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize count() by Nope");
    }

    [Test]
    [Description("Unknown column in an aggregation (KS142)")]
    public void Invalid_UnknownColumnInAggregation()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize sum(Nope)");
    }

    [Test]
    [Description("A column removed by project can't be aggregated (KS142)")]
    public void Invalid_ColumnRemovedByProject()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | project Region | summarize sum(Amount)");
    }

    [Test]
    [Description("Only keys and aggregates exist after summarize: the next summarize can't use other input columns (KS142)")]
    public void Invalid_DroppedColumnInNextSummarize()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()),
            "Sales | summarize count() by Region | summarize sum(Amount)");
    }

    [Test]
    [Description("Only keys and aggregates exist after summarize: where can't use other input columns (KS142)")]
    public void Invalid_DroppedColumnInWhereAfterSummarize()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()),
            "Sales | summarize Total = sum(Amount) by Region | where Amount > 1");
    }

    [Test]
    [Description("The by clause can't reference an aggregate of the same summarize (KS142)")]
    public void Invalid_ByReferencesAggregateName()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize Total = sum(Amount) by Total");
    }

    [Test]
    [Description("An aggregation in the by clause is rejected (KS144)")]
    public void Invalid_AggregationInBy()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize count() by sum(Amount)");
    }

    [Test]
    [Description("Nested aggregations are rejected (KS144)")]
    public void Invalid_NestedAggregation()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize sum(count())");
    }

    [Test]
    [Description("The same key twice is rejected (KS171)")]
    public void Invalid_DuplicateKey()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize count() by Region, Region");
    }

    [Test]
    [Description("Two aggregates with the same explicit name are rejected (KS171)")]
    public void Invalid_DuplicateAggregateNames()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize A = count(), A = sum(Amount)");
    }

    [Test]
    [Description("An explicit aggregate name equal to a key name is rejected (KS171)")]
    public void Invalid_AggregateNamedLikeKey()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        AssertQueryFails(kustoDatabase, "Sales | summarize Region = count() by Region");
        AssertQueryFails(kustoDatabase, "Sales | summarize Total = sum(Amount) by Total = Region");
    }

    [Test]
    [Description("Missing arguments: sum(), percentile(x), take_any(), arg_max() (KS119, KS120) and arg_max(x) (SEM0222)")]
    public void Invalid_MissingArguments()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        AssertQueryFails(kustoDatabase, "Sales | summarize sum()");
        AssertQueryFails(kustoDatabase, "Sales | summarize percentile(Amount)");
        AssertQueryFails(kustoDatabase, "Sales | summarize take_any()");
        AssertQueryFails(kustoDatabase, "Sales | summarize arg_max()");
        AssertQueryFails(kustoDatabase, "Sales | summarize arg_max(Amount)");
    }

    [Test]
    [Description("Too many arguments: count() takes at most one argument (KS119)")]
    public void Invalid_TooManyArguments()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize count(Amount, Region)");
    }

    [Test]
    [Description("The predicate of countif() and sumif() must be bool (KS107)")]
    public void Invalid_NonBoolPredicate()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        AssertQueryFails(kustoDatabase, "Sales | summarize countif(Amount)");
        AssertQueryFails(kustoDatabase, "Sales | summarize sumif(Amount, Region)");
    }

    [Test]
    [Description("The percentile must be a constant in [0, 100] (SEM0033)")]
    public void Invalid_PercentileOutOfRange()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        AssertQueryFails(kustoDatabase, "Sales | summarize percentile(Amount, 101)");
        AssertQueryFails(kustoDatabase, "Sales | summarize percentile(Amount, -1)");
    }

    [Test]
    [Description("The accuracy of dcount() must be 0, 1, 2, 3 or 4 (SEM0031)")]
    public void Invalid_DcountAccuracyOutOfRange()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize dcount(Amount, 10)");
    }

    [Test]
    [Description("The max size of make_list() must be an integer (KS134)")]
    public void Invalid_MakeListMaxSizeIsNotInteger()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize make_list(Amount, 'x')");
    }

    [Test]
    [Description("A key expression with incompatible operand types is rejected (KS106)")]
    public void Invalid_KeyExpressionTypeMismatch()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize count() by Amount + Region");
    }

    [Test]
    [Description("hint.strategy accepts only shuffle (KS005)")]
    public void Invalid_UnknownHintStrategy()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Sales | summarize hint.strategy=foo count()");
    }

    [Test]
    [Description("Syntax errors: missing comma, two by clauses, by after a trailing comma (KS005, KS198, KS006)")]
    public void Invalid_SyntaxErrors()
    {
        var kustoDatabase = CreateDatabase(BuildSalesTable());

        AssertQueryFails(kustoDatabase, "Sales | summarize count() Region");
        AssertQueryFails(kustoDatabase, "Sales | summarize count() by Region by Product");
        AssertQueryFails(kustoDatabase, "Sales | summarize count(), by Region");
    }

    [Test]
    [Description("summarize over an unknown table (KS204)")]
    public void Invalid_UnknownTable()
    {
        AssertQueryFails(CreateDatabase(BuildSalesTable()), "Nope | summarize count()");
    }

    #endregion
}
