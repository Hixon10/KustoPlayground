using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Complementary summarize nullable edge cases (extending existing summarize null tests)
/// and end-to-end multi-operator KQL pipelines over nullable columns.
/// </summary>
public class NullableSummarizeAndPipelinesNeonTests
{
    #region Complementary summarize nullable edge cases

    [Test]
    [Description("summarize by bin_at() places null values into their own null bucket")]
    public void Summarize_ByBinAt_NullValuesFormOwnGroup()
    {
        var table = new NullableTableBuilder("Events")
            .WithColumn<DateTime>("Ts")
            .WithColumn<int>("Val")
            .WithRow(new DateTime(2024, 1, 2, 6, 0, 0, DateTimeKind.Utc), 12)
            .WithRow(new DateTime(2024, 1, 2, 10, 0, 0, DateTimeKind.Utc), 14)
            .WithRow(new DateTime(2024, 1, 2, 18, 0, 0, DateTimeKind.Utc), 4)
            .WithRow(null, null)
            .WithRow(null, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var byDateBin = ExecuteAndGetNullable2Columns<DateTime?, long?>(
            "DayBucket",
            "Cnt",
            kustoDatabase,
            "Events | summarize Cnt = count() by DayBucket = bin_at(Ts, 1d, datetime(2024-01-01 12:00:00))");

        Assert.That(byDateBin, Is.EquivalentTo(new List<(DateTime?, long?)>
        {
            (new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), 2L),
            (new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc), 1L),
            (null, 2L)
        }));

        var byNumBin = ExecuteAndGetNullable2Columns<long?, long?>(
            "NumBucket",
            "Cnt",
            kustoDatabase,
            "Events | summarize Cnt = count() by NumBucket = bin_at(Val, 10, 5)");

        Assert.That(byNumBin, Is.EquivalentTo(new List<(long?, long?)>
        {
            (5L, 2L),
            (-5L, 1L),
            (null, 2L)
        }));
    }

    [Test]
    [Description("summarize groups cleanly by nullable Guid, TimeSpan, and decimal keys")]
    public void Summarize_NullableGuidTimeSpanDecimalGroupKeys()
    {
        var g1 = new Guid("11111111-1111-1111-1111-111111111111");
        var table = new NullableTableBuilder("Grouped")
            .WithColumn<Guid>("G")
            .WithColumn<TimeSpan>("Ts")
            .WithColumn<decimal>("Dec")
            .WithRow(g1, TimeSpan.FromMinutes(5), 1.5m)
            .WithRow(g1, TimeSpan.FromMinutes(5), 1.5m)
            .WithRow(null, null, null)
            .WithRow(null, null, null)
            .WithRow(null, TimeSpan.FromMinutes(5), null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var byGuid = ExecuteAndGetNullable2Columns<Guid?, long?>("G", "C", kustoDatabase, "Grouped | summarize C = count() by G");
        Assert.That(byGuid, Is.EquivalentTo(new List<(Guid?, long?)> { (g1, 2L), (null, 3L) }));

        var byTs = ExecuteAndGetNullable2Columns<TimeSpan?, long?>("Ts", "C", kustoDatabase, "Grouped | summarize C = count() by Ts");
        Assert.That(byTs, Is.EquivalentTo(new List<(TimeSpan?, long?)> { (TimeSpan.FromMinutes(5), 3L), (null, 2L) }));

        var byDec = ExecuteAndGetNullable2Columns<decimal?, long?>("Dec", "C", kustoDatabase, "Grouped | summarize C = count() by Dec");
        Assert.That(byDec, Is.EquivalentTo(new List<(decimal?, long?)> { (1.5m, 2L), (null, 3L) }));
    }

    [Test]
    [Description("Conditional aggregates (*if) treat bool(null) predicates as false, while != predicates return true for null operands")]
    public void Summarize_ConditionalAggregates_ThreeValuedNullPredicates()
    {
        var table = new NullableTableBuilder("Metrics")
            .WithColumn<string>("Grp", isNullable: false)
            .WithColumn<int>("Val")
            .WithColumn<int>("Gate")
            .WithRow("g1", 10, 1)
            .WithRow("g1", 20, null) // Gate > 0 is null (false for *if), Gate != 1 is true for *if!
            .WithRow("g1", 30, 0)
            .WithRow("g1", null, 1)  // Val is null, Gate > 0 is true
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(
            kustoDatabase,
            "Metrics | summarize " +
            "CntGt = countif(Gate > 0), " +
            "SumGt = sumif(Val, Gate > 0), " +
            "AvgGt = avgif(Val, Gate > 0), " +
            "MinGt = minif(Val, Gate > 0), " +
            "MaxGt = maxif(Val, Gate > 0), " +
            "DcntGt = dcountif(Val, Gate > 0), " +
            "CntNeq = countif(Gate != 1), " +
            "SumNeq = sumif(Val, Gate != 1), " +
            "ListNeq = make_list_if(Val, Gate != 1), " +
            "SetNeq = make_set_if(Val, Gate != 1)");

        // Gate > 0 matches rows (10, 1) and (null, 1) -> countif=2, sumif=10, avgif=10, minif=10, maxif=10, dcountif=1
        Assert.That(AsNullableLong(row["CntGt"]), Is.EqualTo(2L));
        Assert.That(AsNullableLong(row["SumGt"]), Is.EqualTo(10L));
        Assert.That(AsNullableDouble(row["AvgGt"]), Is.EqualTo(10.0));
        Assert.That(AsNullableLong(row["MinGt"]), Is.EqualTo(10L));
        Assert.That(AsNullableLong(row["MaxGt"]), Is.EqualTo(10L));
        Assert.That(AsNullableLong(row["DcntGt"]), Is.EqualTo(1L));

        // Gate != 1 matches rows (20, null) and (30, 0) because null != 1 is true!
        Assert.That(AsNullableLong(row["CntNeq"]), Is.EqualTo(2L));
        Assert.That(AsNullableLong(row["SumNeq"]), Is.EqualTo(50L));
        Assert.That(ExtractDynamicArray(row["ListNeq"]).Select(AsNullableLong), Is.EquivalentTo(new long?[] { 20L, 30L }));
        Assert.That(ExtractDynamicArray(row["SetNeq"]).Select(AsNullableLong), Is.EquivalentTo(new long?[] { 20L, 30L }));
    }

    [Test]
    [Description("count_distinct() and count_distinctif() ignore nulls and return 0 on all-null groups")]
    public void Summarize_CountDistinctAndCountDistinctIf_WithNulls()
    {
        var table = CreateNullableTable3<string?, int?, bool?>(
            [("a", 1, true), ("a", 1, true), ("a", 2, false), ("a", null, true), ("b", null, true), ("b", null, null)],
            tableName: "Items",
            columnName1: "Grp",
            columnName2: "Val",
            columnName3: "Flag");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetNullable3Columns<string?, long?, long?>(
            "Grp",
            "Cd",
            "CdIf",
            kustoDatabase,
            "Items | summarize Cd = count_distinct(Val), CdIf = count_distinctif(Val, Flag) by Grp");

        Assert.That(rows, Is.EquivalentTo(new List<(string?, long?, long?)>
        {
            ("a", 2L, 1L),
            ("b", 0L, 0L)
        }));
    }

    [Test]
    [Description("percentilew(), percentiles(), and percentiles_array() ignore rows with null values or null weights")]
    public void Summarize_WeightedAndMultiPercentiles_WithNullValuesAndWeights()
    {
        var table = new NullableTableBuilder("Samples")
            .WithColumn<string>("Grp", isNullable: false)
            .WithColumn<long>("Val")
            .WithColumn<long>("Weight")
            .WithRow("a", 10L, 1L)
            .WithRow("a", 50L, 3L)
            .WithRow("a", null, 10L)  // null value ignored
            .WithRow("a", 999L, null) // null weight ignored
            .WithRow("b", null, 5L)
            .WithRow("b", 42L, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Samples | summarize P50W = percentilew(Val, Weight, 50), Arr = percentiles_array(Val, 25, 75) by Grp");

        var byGrp = rows.ToDictionary(r => (string)r["Grp"]!);

        // Group "a" valid weighted rows: (10, w=1), (50, w=3) -> total weight=4 -> rank(50%)=2 -> 50
        Assert.That(AsNullableLong(byGrp["a"]["P50W"]), Is.EqualTo(50L));
        // Unweighted non-null values in "a": 10, 50, 999 -> P25=10, P75=999
        Assert.That(ExtractDynamicArray(byGrp["a"]["Arr"]).Select(AsNullableLong), Is.EqualTo(new long?[] { 10L, 999L }));

        // Group "b" has no row with both non-null value and positive non-null weight
        Assert.That(byGrp["b"]["P50W"], Is.Null);
        Assert.That(ExtractDynamicArray(byGrp["b"]["Arr"]).Select(AsNullableLong), Is.EqualTo(new long?[] { 42L, 42L }));
    }

    [Test]
    [Description("hll(), hll_merge(), and dcount_hll() ignore nulls across groups including all-null groups")]
    public void Summarize_HllMergeAndDcountHll_WithNulls()
    {
        var table = CreateNullableTable2<string?, int?>(
            [("a", 1), ("a", 2), ("a", null), ("b", 2), ("b", 3), ("b", null), ("c", null), ("c", null)],
            tableName: "Hits",
            columnName1: "Shard",
            columnName2: "UserId");
        var kustoDatabase = CreateDatabase(table);

        var perShard = ExecuteAndGetNullable2Columns<string?, long?>(
            "Shard",
            "ApproxUsers",
            kustoDatabase,
            "Hits | summarize Sketch = hll(UserId) by Shard | project Shard, ApproxUsers = dcount_hll(Sketch)");

        Assert.That(perShard, Is.EquivalentTo(new List<(string?, long?)>
        {
            ("a", 2L),
            ("b", 2L),
            ("c", 0L)
        }));

        var merged = ExecuteAndGetSingleRow(
            kustoDatabase,
            "Hits | summarize Sketch = hll(UserId) by Shard | summarize Merged = hll_merge(Sketch) | project Total = dcount_hll(Merged)");
        Assert.That(AsNullableLong(merged["Total"]), Is.EqualTo(3L));
    }

    [Test]
    [Description("covariance() and covariancep() ignore rows where either X or Y is null")]
    public void Summarize_Covariance_IgnoresPairsWithEitherSideNull()
    {
        var table = CreateNullableTable2<double?, double?>(
            [(1.0, 2.0), (2.0, 4.0), (3.0, 6.0), (100.0, null), (null, 100.0), (null, null)],
            tableName: "Points",
            columnName1: "X",
            columnName2: "Y");
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(
            kustoDatabase,
            "Points | summarize Cov = covariance(X, Y), CovP = covariancep(X, Y)");

        // Valid pairs: (1,2), (2,4), (3,6) -> sample cov = 2.0, population cov = 4/3
        Assert.That(AsNullableDouble(row["Cov"]), Is.EqualTo(2.0).Within(1e-9));
        Assert.That(AsNullableDouble(row["CovP"]), Is.EqualTo(4.0 / 3.0).Within(1e-9));
    }

    [Test]
    [Description("make_bag() and make_bag_if() ignore dynamic(null) inputs and rows where predicate is null or false")]
    public void Summarize_MakeBagAndMakeBagIf_WithNullInputsAndNullPredicates()
    {
        var table = new NullableTableBuilder("Props")
            .WithColumn<string>("K", isNullable: false)
            .WithColumn<int>("V")
            .WithColumn<bool>("Include")
            .WithRow("a", 1, true)
            .WithRow("b", null, true)
            .WithRow("c", 3, null)
            .WithRow("d", 4, false)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var row = ExecuteAndGetSingleRow(
            kustoDatabase,
            "Props | extend Bag = bag_pack(K, V) | summarize AllBag = make_bag(Bag), FilteredBag = make_bag_if(Bag, Include)");

        var allBag = ExtractDynamicBag(row["AllBag"]);
        Assert.That(allBag.Keys, Is.EquivalentTo(new[] { "a", "b", "c", "d" }));
        Assert.That(AsNullableLong(allBag["a"]), Is.EqualTo(1L));
        Assert.That(allBag["b"], Is.Null);

        var filteredBag = ExtractDynamicBag(row["FilteredBag"]);
        Assert.That(filteredBag.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        Assert.That(AsNullableLong(filteredBag["a"]), Is.EqualTo(1L));
        Assert.That(filteredBag["b"], Is.Null);
    }

    [Test]
    [Description("binary_all_and/or/xor ignore nulls within mixed groups and return null on all-null groups")]
    public void Summarize_BinaryAllBitwise_AllNullAndMixedGroups()
    {
        var table = CreateNullableTable2<string?, long?>(
            [("mixed", 0b1100L), ("mixed", null), ("mixed", 0b1010L), ("allnull", null), ("allnull", null)],
            tableName: "Bits",
            columnName1: "Grp",
            columnName2: "Mask");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Bits | summarize BAnd = binary_all_and(Mask), BOr = binary_all_or(Mask), BXor = binary_all_xor(Mask) by Grp");
        var byGrp = rows.ToDictionary(r => (string)r["Grp"]!);

        Assert.That(AsNullableLong(byGrp["mixed"]["BAnd"]), Is.EqualTo(0b1000L));
        Assert.That(AsNullableLong(byGrp["mixed"]["BOr"]), Is.EqualTo(0b1110L));
        Assert.That(AsNullableLong(byGrp["mixed"]["BXor"]), Is.EqualTo(0b0110L));

        Assert.That(byGrp["allnull"]["BAnd"], Is.Null);
        Assert.That(byGrp["allnull"]["BOr"], Is.Null);
        Assert.That(byGrp["allnull"]["BXor"], Is.Null);
    }

    [Test]
    [Description("arg_max() and arg_min() handle nullable return columns and ignore rows with null comparison keys")]
    public void Summarize_ArgMaxAndArgMin_NullableReturnColumn()
    {
        var table = new NullableTableBuilder("Scores")
            .WithColumn<string>("Team", isNullable: false)
            .WithColumn<int>("Points")
            .WithColumn<int>("Bonus")
            .WithRow("red", 10, 100)
            .WithRow("red", 50, null)  // winner for arg_max has null Bonus!
            .WithRow("red", null, 999) // null Points ignored for both arg_max and arg_min
            .WithRow("blue", null, 5)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Scores | summarize arg_max(Points, MaxBonus = Bonus), arg_min(Points, MinBonus = Bonus) by Team");
        var byTeam = rows.ToDictionary(r => (string)r["Team"]!);

        Assert.That(AsNullableLong(byTeam["red"]["Points"]), Is.EqualTo(50L));
        Assert.That(byTeam["red"]["MaxBonus"], Is.Null);
        Assert.That(AsNullableLong(byTeam["red"]["Points1"]), Is.EqualTo(10L));
        Assert.That(AsNullableLong(byTeam["red"]["MinBonus"]), Is.EqualTo(100L));

        Assert.That(byTeam["blue"]["Points"], Is.Null);
        Assert.That(AsNullableLong(byTeam["blue"]["MaxBonus"]), Is.EqualTo(5L));
    }

    [Test]
    [Description("Post-aggregation expressions propagate nulls from min/max on all-null groups while sum(null) contributes 0")]
    public void Summarize_PostAggregationExpressions_WithNullAggregates()
    {
        var table = CreateNullableTable2<string?, long?>(
            [("a", 10L), ("a", 25L), ("a", null), ("b", null), ("b", null)],
            tableName: "Readings",
            columnName1: "Sensor",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Readings | summarize Spread = max(Val) - min(Val), SumPlus10 = sum(Val) + 10, NoMax = isnull(max(Val)) by Sensor");
        var bySensor = rows.ToDictionary(r => (string)r["Sensor"]!);

        Assert.That(AsNullableDouble(bySensor["a"]["Spread"]), Is.EqualTo(15.0));
        Assert.That(AsNullableDouble(bySensor["a"]["SumPlus10"]), Is.EqualTo(45.0));
        Assert.That(bySensor["a"]["NoMax"], Is.EqualTo(false));

        Assert.That(bySensor["b"]["Spread"], Is.Null);
        Assert.That(AsNullableDouble(bySensor["b"]["SumPlus10"]), Is.EqualTo(10.0));
        Assert.That(bySensor["b"]["NoMax"], Is.EqualTo(true));
    }

    #endregion

    #region End-to-end multi-operator pipelines with nullable columns

    [Test]
    [Description("Pipeline: where !in (subquery) -> extend -> summarize -> where -> sort nulls last -> take -> project")]
    public void Pipeline_SubqueryFilterExtendSummarizeSortTakeProject_WithNulls()
    {
        var orders = new NullableTableBuilder("Orders")
            .WithColumn<int>("OrderId", isNullable: false)
            .WithColumn<int>("CustomerId")
            .WithColumn<string>("Region")
            .WithColumn<int>("Amount")
            .WithRow(1, 100, "West", 50)
            .WithRow(2, 100, "West", null)
            .WithRow(3, 200, "East", 80)
            .WithRow(4, 300, "East", 40)  // CustomerId 300 is blocked
            .WithRow(5, null, "North", null) // CustomerId null is kept by !in when Blocked has no nulls!
            .WithRow(6, 400, null, 120)
            .Build();
        var blocked = CreateNullableTable<int?>([300], tableName: "Blocked", columnName: "CustomerId");
        var kustoDatabase = CreateDatabase(orders, blocked);

        string query = """
                       Orders
                       | where CustomerId !in (Blocked | project CustomerId)
                       | extend Taxed = Amount * 2
                       | summarize RowCnt = count(), MaxTaxed = max(Taxed), TotalTaxed = sum(Taxed) by Region
                       | sort by MaxTaxed desc nulls last, Region asc nulls last
                       | project Region, RowCnt, MaxTaxed, TotalTaxed
                       """;

        var rows = ExecuteAndAssertSuccess(kustoDatabase, query);
        Assert.That(rows, Has.Count.EqualTo(4));

        // Expected order by MaxTaxed desc nulls last:
        // 1. Region="": MaxTaxed=240, RowCnt=1, TotalTaxed=240
        // 2. Region="East": MaxTaxed=160, RowCnt=1, TotalTaxed=160
        // 3. Region="West": MaxTaxed=100, RowCnt=2, TotalTaxed=100
        // 4. Region="North": MaxTaxed=null, RowCnt=1, TotalTaxed=0
        Assert.That(rows[0]["Region"], Is.EqualTo(""));
        Assert.That(AsNullableDouble(rows[0]["MaxTaxed"]), Is.EqualTo(240.0));
        Assert.That(AsNullableLong(rows[0]["RowCnt"]), Is.EqualTo(1L));

        Assert.That(rows[1]["Region"], Is.EqualTo("East"));
        Assert.That(AsNullableDouble(rows[1]["MaxTaxed"]), Is.EqualTo(160.0));

        Assert.That(rows[2]["Region"], Is.EqualTo("West"));
        Assert.That(AsNullableDouble(rows[2]["MaxTaxed"]), Is.EqualTo(100.0));
        Assert.That(AsNullableLong(rows[2]["RowCnt"]), Is.EqualTo(2L));

        Assert.That(rows[3]["Region"], Is.EqualTo("North"));
        Assert.That(rows[3]["MaxTaxed"], Is.Null);
        Assert.That(AsNullableDouble(rows[3]["TotalTaxed"]), Is.EqualTo(0.0));
    }

    [Test]
    [Description("Pipeline: extend with bin() and arithmetic -> distinct -> sort asc nulls first")]
    public void Pipeline_ExtendBinDistinctSort_WithNulls()
    {
        var table = CreateNullableTable<int?>([14, 19, null, 25, null, 3, 7], tableName: "Scores", columnName: "Val");
        var kustoDatabase = CreateDatabase(table);

        var buckets = ExecuteAndGetNullableColumn<long?>(
            "Bucket",
            kustoDatabase,
            "Scores | extend Bucket = bin(Val, 10) | distinct Bucket | sort by Bucket asc nulls first");

        Assert.That(buckets, Is.EqualTo(new long?[] { null, 0L, 10L, 20L }));
    }

    [Test]
    [Description("Pipeline: string parsing (todatetime, toreal) -> where between or isnull -> sort -> count")]
    public void Pipeline_ConversionBetweenSortAndCount_WithNulls()
    {
        var table = new NullableTableBuilder("RawLogs")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<string>("TsText")
            .WithColumn<string>("LatencyText")
            .WithRow(1, "2024-06-10 10:00:00", "12.5")
            .WithRow(2, "2024-06-15 10:00:00", "bad")
            .WithRow(3, "bad-date", "5.0")
            .WithRow(4, null, null)
            .WithRow(5, "2024-06-25 10:00:00", "30.0")
            .Build();
        var kustoDatabase = CreateDatabase(table);

        string query = """
                       RawLogs
                       | extend Ts = todatetime(TsText), Latency = toreal(LatencyText)
                       | where Ts between (datetime(2024-06-01) .. 20d) or isnull(Ts)
                       | sort by Latency asc nulls last, Id asc
                       | project Id, Latency
                       """;

        var actual = ExecuteAndGetNullable2Columns<int?, double?>("Id", "Latency", kustoDatabase, query);

        // Matching rows:
        // Row 1 (Ts in range, Latency=12.5)
        // Row 2 (Ts in range, Latency=null)
        // Row 3 (Ts=null -> kept by or isnull(Ts), Latency=5.0)
        // Row 4 (Ts=null -> kept by or isnull(Ts), Latency=null)
        // Row 5 (Ts out of range -> dropped)
        Assert.That(actual, Is.EqualTo(new List<(int?, double?)>
        {
            (3, 5.0),
            (1, 12.5),
            (2, null),
            (4, null)
        }));
    }

    [Test]
    [Description("Pipeline where where filters everything out (including via null comparison) flows cleanly into downstream operators")]
    public void Pipeline_EmptyIntermediateFromNullFilter_DownstreamOperatorsSucceed()
    {
        var table = CreateNullableTable2<int?, int?>([(1, null), (2, 10)], "Items", "Id", "Val");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndAssertSuccess(kustoDatabase, "Items | where Val == int(null) | extend X = Val + 1 | distinct X | sort by X asc | take 5"),
            Is.Empty);

        Assert.That(
            AsNullableLong(ExecuteAndGetSingleRow(kustoDatabase, "Items | where Val == int(null) | count")["Count"]),
            Is.EqualTo(0L));

        var noByRow = ExecuteAndGetSingleRow(
            kustoDatabase,
            "Items | where Val == int(null) | summarize Cnt = count(), MaxVal = max(Val)");
        Assert.That(AsNullableLong(noByRow["Cnt"]), Is.EqualTo(0L));
        Assert.That(noByRow["MaxVal"], Is.Null);
    }

    #endregion
}
