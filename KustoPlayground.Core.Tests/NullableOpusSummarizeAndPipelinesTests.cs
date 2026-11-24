using System.Globalization;
using System.Text.Json;
using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Nullable columns in summarize and in pipelines of several operators, as in Azure Data Explorer:
/// - the nulls of a group-by key form one group, separate from every value, also from the default value of the type
///   (0, 0.0, 0m, 00:00:00, the empty guid); NaN is a value, so it forms its own group, separate from the nulls;
/// - aggregates skip the nulls: sum of only nulls is 0 (00:00:00 for timespan), avg of only nulls is null for
///   decimal and timespan (NaN for long and real), min and max of only nulls are null, dcount and hll count 0;
/// - a conditional aggregate (countif, sumif, ...) takes a row only when its predicate is true: null > 1,
///   null == null and not(null > 1) are null and skip the row, while null != 5 and not(null == 5) are true;
/// - a null aggregate (max of only nulls) and a null group key behave like any other null in the operators after
///   summarize (where, extend, sort, take, in, summarize again);
/// - make_list_with_nulls keeps the position of the nulls given by sort; make_bag and bag_pack keep the properties
///   whose value is null;
/// - tables loaded from CSV (an empty cell) and from JSON (a null value) follow the same rules.
/// The null semantics that other summarize tests already cover (count, sum, avg, min, max, dcount, percentiles,
/// stdev, variance, make_list, make_set, take_any, arg_max, arg_min, conditional aggregates with a null predicate,
/// null keys of long, bool and datetime columns, multi-key and bin groups) are not repeated here.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class NullableOpusSummarizeAndPipelinesTests
{
    // the 'Groups' table summarized per group: a: m = 5, s = 6, a = 3.0, c = 2; b (only nulls): m = null, s = 0,
    // a = NaN, c = 2; c: m = 3, s = 3, a = 3.0, c = 1
    private const string GroupStatsQuery = "Groups | summarize m = max(X), s = sum(X), a = avg(X), c = count() by G";

    // People: the CSV text of the UI, where an empty cell is a missing value
    private const string PeopleCsv = """
        Id,Score,Name,Active,When
        1,10,alpha,true,2020-01-01
        2,,beta,false,
        3,30,,true,2021-06-01
        4,,,,2020-03-01
        5,50,gamma,,
        """;

    // Orders: the JSON table definition of the UI, where every column but Id is nullable and row 2 has only nulls
    private const string OrdersJson = """
        {
          "Name": "Orders",
          "Columns": [
            { "Name": "Id", "Type": "int", "Nullable": false },
            { "Name": "Amount", "Type": "double", "Nullable": true },
            { "Name": "Price", "Type": "decimal", "Nullable": true },
            { "Name": "Duration", "Type": "timespan", "Nullable": true },
            { "Name": "Key", "Type": "guid", "Nullable": true },
            { "Name": "Tag", "Type": "string", "Nullable": true }
          ],
          "Rows": [
            { "Id": 1, "Amount": 10, "Price": 2, "Duration": "01:00:00",
              "Key": "00000000-0000-0000-0000-000000000001", "Tag": "x" },
            { "Id": 2, "Amount": null, "Price": null, "Duration": null, "Key": null, "Tag": null },
            { "Id": 3, "Amount": 30, "Price": 4, "Duration": "03:00:00",
              "Key": "00000000-0000-0000-0000-000000000003", "Tag": "" }
          ]
        }
        """;

    // Numbers(Id: int, X: long?) = 1: 1, 2: 3, 3: 5, 4: null, 5: null
    private static Table BuildNumbersTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Numbers")
            .WithColumn<int>("Id")
            .WithColumn<long>("X")
            .WithRow(1, 1L)
            .WithRow(2, 3L)
            .WithRow(3, 5L)
            .WithRow(4, null)
            .WithRow(5, null)
            .Build();
    }

    // Measures(Id: int, X: long?, V: long) = 1: (1, 10), 2: (null, 20), 3: (5, 30), 4: (null, 40)
    private static Table BuildMeasuresTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Measures")
            .WithColumn<int>("Id")
            .WithColumn<long>("X")
            .WithColumn<long>("V")
            .WithRow(1, 1L, 10L)
            .WithRow(2, null, 20L)
            .WithRow(3, 5L, 30L)
            .WithRow(4, null, 40L)
            .Build();
    }

    // NullPairs(A: long?, B: long?) = (1, null), (1, null), (null, null), (null, null), (null, 1)
    private static Table BuildNullPairsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("NullPairs")
            .WithColumn<long>("A")
            .WithColumn<long>("B")
            .WithRow(1L, null)
            .WithRow(1L, null)
            .WithRow(null, null)
            .WithRow(null, null)
            .WithRow(null, 1L)
            .Build();
    }

    // Groups(G: string, X: long?) = ("a", 1), ("a", 5), ("b", null), ("b", null), ("c", 3)
    private static Table BuildGroupsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Groups")
            .WithColumn<string>("G")
            .WithColumn<long>("X")
            .WithRow("a", 1L)
            .WithRow("a", 5L)
            .WithRow("b", null)
            .WithRow("b", null)
            .WithRow("c", 3L)
            .Build();
    }

    // Reals(R: real?) = 1.0, null, NaN, null, NaN, 1.0
    private static Table BuildRealsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Reals")
            .WithColumn<double>("R")
            .WithRow(1.0)
            .WithRow(null)
            .WithRow(double.NaN)
            .WithRow(null)
            .WithRow(double.NaN)
            .WithRow(1.0)
            .Build();
    }

    // Money(G: string, D: decimal?, T: timespan?) =
    // ("a", 1.5, 1h), ("a", null, null), ("a", 2.5, 2h), ("a", null, null), ("b", null, null), ("b", null, null)
    private static Table BuildMoneyTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Money")
            .WithColumn<string>("G")
            .WithColumn<decimal>("D")
            .WithColumn<TimeSpan>("T")
            .WithRow("a", 1.5m, TimeSpan.FromHours(1))
            .WithRow("a", null, null)
            .WithRow("a", 2.5m, TimeSpan.FromHours(2))
            .WithRow("a", null, null)
            .WithRow("b", null, null)
            .WithRow("b", null, null)
            .Build();
    }

    // Sequence(X: long?) = 1, null, 5, null, 3, 1
    private static Table BuildSequenceTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Sequence")
            .WithColumn<long>("X")
            .WithRow(1L)
            .WithRow(null)
            .WithRow(5L)
            .WithRow(null)
            .WithRow(3L)
            .WithRow(1L)
            .Build();
    }

    // Events(X: long?, V: long) = (1, 10), (null, 20), (3, 30), (null, 40), (1, 50)
    private static Table BuildEventsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Events")
            .WithColumn<long>("X")
            .WithColumn<long>("V")
            .WithRow(1L, 10L)
            .WithRow(null, 20L)
            .WithRow(3L, 30L)
            .WithRow(null, 40L)
            .WithRow(1L, 50L)
            .Build();
    }

    // Props(K: string, X: long?) = ("a", 1), ("b", null), ("c", 3)
    private static Table BuildPropsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Props")
            .WithColumn<string>("K")
            .WithColumn<long>("X")
            .WithRow("a", 1L)
            .WithRow("b", null)
            .WithRow("c", 3L)
            .Build();
    }

    // Keys(Id: int, K: T?, V: long) = 1: (value, 1), 2: (null, 2), 3: (value, 3), 4: (null, 4), 5: (null, 5)
    private static KustoDatabase CreateKeysDatabase<T>(T value)
        where T : notnull
    {
        return CreateDatabase(new SummarizeOperatorOpusTableBuilder("Keys")
            .WithColumn<int>("Id")
            .WithColumn<T>("K")
            .WithColumn<long>("V")
            .WithRow(1, value, 1L)
            .WithRow(2, null, 2L)
            .WithRow(3, value, 3L)
            .WithRow(4, null, 4L)
            .WithRow(5, null, 5L)
            .Build());
    }

    // The nulls of K form one group, separate from the group of the value, also when the value is the default of the
    // type: value: c = 2, s = 1 + 3; null: c = 3, s = 2 + 4 + 5. dcount and distinct see 1 value and 1 null.
    private static void AssertNullKeyFormsOneGroup<T>(T value)
        where T : struct
    {
        KustoDatabase kustoDatabase = CreateKeysDatabase(value);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "Keys | summarize c = count(), s = sum(V) by K");

        Assert.That(rows.Select(row => GetNullableValue<T>(row, "K")), Is.EquivalentTo(new T?[] { value, null }),
            "group keys");
        IReadOnlyDictionary<string, object?> valueGroup = GetSingleRowWhere(rows, "K", value);
        Assert.That(GetValue<long>(valueGroup, "c"), Is.EqualTo(2), "count() of the value group");
        Assert.That(GetValue<long>(valueGroup, "s"), Is.EqualTo(4), "sum(V) of the value group");
        IReadOnlyDictionary<string, object?> nullGroup = GetSingleRowWhere(rows, "K", null);
        Assert.That(GetValue<long>(nullGroup, "c"), Is.EqualTo(3), "count() of the null group");
        Assert.That(GetValue<long>(nullGroup, "s"), Is.EqualTo(11), "sum(V) of the null group");

        IReadOnlyDictionary<string, object?> counts = ExecuteAndGetSingleRow(kustoDatabase,
            "Keys | summarize d = dcount(K), n = countif(isnull(K))");
        Assert.That(GetValue<long>(counts, "d"), Is.EqualTo(1), "dcount(K) skips the nulls");
        Assert.That(GetValue<long>(counts, "n"), Is.EqualTo(3), "countif(isnull(K))");
        Assert.That(ExecuteAndGetCount(kustoDatabase, "Keys | distinct K | count"), Is.EqualTo(2), "distinct K");
    }

    // a dynamic property bag as a compact text, for example {"x": null} is "x=null"
    private static string DescribeBag(string bagJson)
    {
        Dictionary<string, JsonElement>? bag = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bagJson);
        Assert.That(bag, Is.Not.Null, $"not a property bag: {bagJson}");
        return string.Join(", ", bag!.Select(property => $"{property.Key}={property.Value.GetRawText()}"));
    }

    #region null group keys of every type

    [TestCase(7)]
    [TestCase(0)]
    public void Summarize_ByNullableIntKey_NullsFormOneGroupSeparateFromTheValue(int value)
    {
        AssertNullKeyFormsOneGroup(value);
    }

    [TestCase(2.5)]
    [TestCase(0.0)]
    public void Summarize_ByNullableRealKey_NullsFormOneGroupSeparateFromTheValue(double value)
    {
        AssertNullKeyFormsOneGroup(value);
    }

    [TestCase("2.5")]
    [TestCase("0")]
    public void Summarize_ByNullableDecimalKey_NullsFormOneGroupSeparateFromTheValue(string value)
    {
        AssertNullKeyFormsOneGroup(decimal.Parse(value, CultureInfo.InvariantCulture));
    }

    [TestCase("01:00:00")]
    [TestCase("00:00:00")]
    public void Summarize_ByNullableTimespanKey_NullsFormOneGroupSeparateFromTheValue(string value)
    {
        AssertNullKeyFormsOneGroup(TimeSpan.Parse(value, CultureInfo.InvariantCulture));
    }

    [TestCase("6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public void Summarize_ByNullableGuidKey_NullsFormOneGroupSeparateFromTheValue(string value)
    {
        AssertNullKeyFormsOneGroup(Guid.Parse(value));
    }

    [Test]
    [Description("NaN is a value of a real column, not a null: summarize by R gives the groups 1.0, NaN and null")]
    public void Summarize_ByRealKeyWithNaNAndNulls_GivesSeparateNaNAndNullGroups()
    {
        var kustoDatabase = CreateDatabase(BuildRealsTable());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "Reals | summarize c = count() by R");

        Assert.That(rows, Has.Count.EqualTo(3), "number of groups");
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "R", 1.0), "c"), Is.EqualTo(2), "group 1.0");
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "R", double.NaN), "c"), Is.EqualTo(2), "group NaN");
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "R", null), "c"), Is.EqualTo(2), "group null");
    }

    [Test]
    [Description("dcount counts NaN as a value and skips the nulls, count() counts every row")]
    public void Dcount_OfRealWithNaNAndNulls_CountsNaNButNotNull()
    {
        var kustoDatabase = CreateDatabase(BuildRealsTable());

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Reals | summarize d = dcount(R), c = count(), n = countif(isnull(R))");

        Assert.That(GetValue<long>(row, "d"), Is.EqualTo(2), "dcount(R): 1.0 and NaN");
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(6), "count()");
        Assert.That(GetValue<long>(row, "n"), Is.EqualTo(2), "countif(isnull(R)): NaN is not null");
    }

    #endregion

    #region decimal and timespan aggregates

    [Test]
    [Description("sum, avg, min, max and dcount of a decimal column skip the nulls and keep the decimal type")]
    public void Aggregates_OfDecimalWithNulls_SkipTheNulls()
    {
        var kustoDatabase = CreateDatabase(BuildMoneyTable());

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Money | where G == 'a' " +
            "| summarize s = sum(D), a = avg(D), mn = min(D), mx = max(D), d = dcount(D), c = count()");

        Assert.That(GetValue<decimal>(row, "s"), Is.EqualTo(4m), "sum(D)");
        Assert.That(GetValue<decimal>(row, "a"), Is.EqualTo(2m), "avg(D) of the 2 values, not of the 4 rows");
        Assert.That(GetValue<decimal>(row, "mn"), Is.EqualTo(1.5m), "min(D)");
        Assert.That(GetValue<decimal>(row, "mx"), Is.EqualTo(2.5m), "max(D)");
        Assert.That(GetValue<long>(row, "d"), Is.EqualTo(2), "dcount(D)");
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(4), "count()");
    }

    [Test]
    [Description("In a group whose decimals are all null: sum is 0, avg (null, not NaN), min and max are null, dcount is 0")]
    public void Aggregates_OfDecimalGroupWithOnlyNulls()
    {
        var kustoDatabase = CreateDatabase(BuildMoneyTable());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndGetRows(kustoDatabase,
            "Money | summarize s = sum(D), a = avg(D), mn = min(D), mx = max(D), d = dcount(D), c = count() by G");

        Assert.That(rows, Has.Count.EqualTo(2), "number of groups");
        IReadOnlyDictionary<string, object?> row = GetSingleRowWhere(rows, "G", "b");
        Assert.That(GetValue<decimal>(row, "s"), Is.EqualTo(0m), "sum(D)");
        AssertIsNull(row, "a");
        AssertIsNull(row, "mn");
        AssertIsNull(row, "mx");
        Assert.That(GetValue<long>(row, "d"), Is.EqualTo(0), "dcount(D)");
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(2), "count()");
    }

    [Test]
    [Description("sum, avg, min, max and dcount of a timespan column skip the nulls and keep the timespan type")]
    public void Aggregates_OfTimespanWithNulls_SkipTheNulls()
    {
        var kustoDatabase = CreateDatabase(BuildMoneyTable());

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Money | where G == 'a' " +
            "| summarize s = sum(T), a = avg(T), mn = min(T), mx = max(T), d = dcount(T), c = count()");

        Assert.That(GetValue<TimeSpan>(row, "s"), Is.EqualTo(TimeSpan.FromHours(3)), "sum(T)");
        Assert.That(GetValue<TimeSpan>(row, "a"), Is.EqualTo(TimeSpan.FromMinutes(90)),
            "avg(T) of the 2 values, not of the 4 rows");
        Assert.That(GetValue<TimeSpan>(row, "mn"), Is.EqualTo(TimeSpan.FromHours(1)), "min(T)");
        Assert.That(GetValue<TimeSpan>(row, "mx"), Is.EqualTo(TimeSpan.FromHours(2)), "max(T)");
        Assert.That(GetValue<long>(row, "d"), Is.EqualTo(2), "dcount(T)");
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(4), "count()");
    }

    [Test]
    [Description("In a group whose timespans are all null: sum is 00:00:00, avg (null, not NaN), min and max are null, dcount is 0")]
    public void Aggregates_OfTimespanGroupWithOnlyNulls()
    {
        var kustoDatabase = CreateDatabase(BuildMoneyTable());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndGetRows(kustoDatabase,
            "Money | summarize s = sum(T), a = avg(T), mn = min(T), mx = max(T), d = dcount(T), c = count() by G");

        Assert.That(rows, Has.Count.EqualTo(2), "number of groups");
        IReadOnlyDictionary<string, object?> row = GetSingleRowWhere(rows, "G", "b");
        Assert.That(GetValue<TimeSpan>(row, "s"), Is.EqualTo(TimeSpan.Zero), "sum(T)");
        AssertIsNull(row, "a");
        AssertIsNull(row, "mn");
        AssertIsNull(row, "mx");
        Assert.That(GetValue<long>(row, "d"), Is.EqualTo(0), "dcount(T)");
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(2), "count()");
    }

    #endregion

    #region conditional aggregates with !=, not() and three-valued logic

    // Measures: X = 1, null, 5, null and V = 10, 20, 30, 40
    [TestCase("countif(X != 5)", 3L, Description = "1 != 5 and null != 5 are true, 5 != 5 is false")]
    [TestCase("countif(not(X == 5))", 3L, Description = "null == 5 is false, so not(null == 5) is true")]
    [TestCase("countif(not(X > 1))", 1L, Description = "null > 1 is null and not(null) is null")]
    [TestCase("countif(X == X)", 2L, Description = "null == null is null")]
    [TestCase("countif(X != X)", 0L, Description = "null != null is null")]
    [TestCase("countif(X > 1 or isnull(X))", 3L, Description = "null or true is true")]
    [TestCase("countif(X > 1 and isnull(X))", 0L, Description = "null and true is null, 5 > 1 and false is false")]
    [TestCase("countif(not(X > 1 and isnull(X)))", 2L, Description = "not(null and true) is null, not null")]
    [TestCase("sumif(V, X != 5)", 70L, Description = "10 + 20 + 40")]
    [TestCase("sumif(V, X == X)", 40L, Description = "10 + 30, the rows with a null X are skipped")]
    [TestCase("minif(V, X != 1)", 20L, Description = "the rows 20, 30, 40: a null X is != 1")]
    [TestCase("maxif(V, X != 1)", 40L, Description = "the rows 20, 30, 40: a null X is != 1")]
    [TestCase("dcountif(V, X != 5)", 3L, Description = "the rows 10, 20, 40")]
    public void ConditionalAggregate_WithNotEqualNotAndLogicOverNulls(string aggregate, long expected)
    {
        var kustoDatabase = CreateDatabase(BuildMeasuresTable());

        IReadOnlyDictionary<string, object?> row =
            ExecuteAndGetSingleRow(kustoDatabase, $"Measures | summarize r = {aggregate}");

        Assert.That(GetValue<long>(row, "r"), Is.EqualTo(expected));
    }

    [Test]
    [Description("avgif(V, X != 5) averages the rows 10, 20, 40, two of them with a null X")]
    public void Avgif_WithNotEqualPredicateOverNulls_AveragesTheRowsWithANullValue()
    {
        var kustoDatabase = CreateDatabase(BuildMeasuresTable());

        IReadOnlyDictionary<string, object?> row =
            ExecuteAndGetSingleRow(kustoDatabase, "Measures | summarize a = avgif(V, X != 5)");

        Assert.That(GetValue<double>(row, "a"), Is.EqualTo(70.0 / 3).Within(1e-9));
    }

    [Test]
    [Description("make_list_if(V, X != 5) takes the rows 10, 20, 40, two of them with a null X")]
    public void MakeListIf_WithNotEqualPredicateOverNulls_TakesTheRowsWithANullValue()
    {
        var kustoDatabase = CreateDatabase(BuildMeasuresTable());

        IReadOnlyDictionary<string, object?> row =
            ExecuteAndGetSingleRow(kustoDatabase, "Measures | summarize l = make_list_if(V, X != 5)");

        Assert.That(GetDynamicArray(row, "l"), Is.EquivalentTo(new object?[] { 10L, 20L, 40L }));
    }

    // NullPairs: (1, null) x2, (null, null) x2, (null, 1)
    [TestCase("countif(A == B)", 0L, Description = "1 == null is false, null == null is null")]
    [TestCase("countif(A != B)", 3L, Description = "1 != null is true, null != null is null")]
    [TestCase("countif(not(A == B))", 3L, Description = "not(1 == null) is true, not(null == null) is null")]
    [TestCase("countif(A < B)", 0L, Description = "an ordering comparison with a null is null")]
    [TestCase("countif(isnull(A) and isnull(B))", 2L, Description = "the 2 rows (null, null)")]
    public void ConditionalAggregate_ComparingTwoNullableColumns(string aggregate, long expected)
    {
        var kustoDatabase = CreateDatabase(BuildNullPairsTable());

        IReadOnlyDictionary<string, object?> row =
            ExecuteAndGetSingleRow(kustoDatabase, $"NullPairs | summarize r = {aggregate}");

        Assert.That(GetValue<long>(row, "r"), Is.EqualTo(expected));
    }

    [Test]
    [Description("After summarize by two nullable keys, 'where A == B' drops every group, even the group (null, null)")]
    public void Where_ComparingNullGroupKeysAfterSummarize_NullIsNotEqualToNull()
    {
        var kustoDatabase = CreateDatabase(BuildNullPairsTable());

        Assert.That(ExecuteAndGetRows(kustoDatabase, "NullPairs | summarize c = count() by A, B | where A == B"),
            Is.Empty, "where A == B");

        IReadOnlyList<IReadOnlyDictionary<string, object?>> notEqual = ExecuteAndGetRows(kustoDatabase,
            "NullPairs | summarize c = count() by A, B | where A != B");
        Assert.That(notEqual, Has.Count.EqualTo(2), "where A != B: the groups (1, null) and (null, 1)");
        Assert.That(notEqual.Sum(row => GetValue<long>(row, "c")), Is.EqualTo(3), "rows of the 2 groups");
    }

    #endregion

    #region operators after summarize on a null aggregate

    [Test]
    [Description("The group whose values are all null: max is null, sum is 0, avg is NaN and count() counts its rows")]
    public void Summarize_ByGroupWithOnlyNulls_GivesNullMaxZeroSumAndNaNAvg()
    {
        var kustoDatabase = CreateDatabase(BuildGroupsTable());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndGetRows(kustoDatabase, GroupStatsQuery);

        Assert.That(rows, Has.Count.EqualTo(3), "number of groups");
        IReadOnlyDictionary<string, object?> row = GetSingleRowWhere(rows, "G", "b");
        AssertIsNull(row, "m");
        Assert.That(GetValue<long>(row, "s"), Is.EqualTo(0), "sum(X)");
        Assert.That(GetValue<double>(row, "a"), Is.NaN, "avg(X)");
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(2), "count()");
    }

    [TestCase("m != 5", new[] { "b", "c" })]
    [TestCase("not(m == 5)", new[] { "b", "c" })]
    [TestCase("m > 1", new[] { "a", "c" })]
    [TestCase("not(m > 1)", new string[] { })]
    [TestCase("isnull(m)", new[] { "b" })]
    [TestCase("isnotnull(m)", new[] { "a", "c" })]
    [TestCase("m == s", new[] { "c" })]
    [TestCase("s == 0", new[] { "b" })]
    [TestCase("a > 2", new[] { "a", "c" })]
    [Description("A null aggregate follows the rules of any null in 'where': == is false, != is true, > is null")]
    public void Where_AfterSummarize_OnANullAggregate(string predicate, string[] expectedGroups)
    {
        var kustoDatabase = CreateDatabase(BuildGroupsTable());

        List<string?> actual = ExecuteAndGetStringColumn(kustoDatabase,
            $"{GroupStatsQuery} | where {predicate} | project G", "G");

        Assert.That(actual, Is.EquivalentTo(expectedGroups));
    }

    [Test]
    [Description("Arithmetic with a null aggregate is null: s * m and m + 1 are null for the group with only nulls")]
    public void Extend_AfterSummarize_ArithmeticWithANullAggregateIsNull()
    {
        var kustoDatabase = CreateDatabase(BuildGroupsTable());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, $"{GroupStatsQuery} | extend r = s * m, t = m + 1");

        Assert.That(rows.ToDictionary(row => GetValue<string>(row, "G"), row => GetNumberOrNull(row, "r")),
            Is.EqualTo(new Dictionary<string, double?> { ["a"] = 30, ["b"] = null, ["c"] = 9 }), "s * m");
        Assert.That(rows.ToDictionary(row => GetValue<string>(row, "G"), row => GetNumberOrNull(row, "t")),
            Is.EqualTo(new Dictionary<string, double?> { ["a"] = 6, ["b"] = null, ["c"] = 4 }), "m + 1");
    }

    [TestCase("asc", new[] { "b", "c", "a" })]
    [TestCase("desc", new[] { "a", "c", "b" })]
    [TestCase("asc nulls last", new[] { "c", "a", "b" })]
    [Description("sort by a null aggregate puts its group first for asc and last for desc")]
    public void Sort_AfterSummarize_ByANullAggregate(string direction, string[] expectedGroups)
    {
        var kustoDatabase = CreateDatabase(BuildGroupsTable());

        List<string?> actual = ExecuteAndGetStringColumn(kustoDatabase,
            $"{GroupStatsQuery} | sort by m {direction} | project G", "G");

        Assert.That(actual, Is.EqualTo(expectedGroups));
    }

    [Test]
    [Description("A second summarize over a null aggregate: count() counts it, sum and min skip it")]
    public void Summarize_AfterSummarize_SkipsTheNullAggregate()
    {
        var kustoDatabase = CreateDatabase(BuildGroupsTable());

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            $"{GroupStatsQuery} | summarize c = count(), n = countif(isnull(m)), s = sum(m), mn = min(m)");

        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(3), "count()");
        Assert.That(GetValue<long>(row, "n"), Is.EqualTo(1), "countif(isnull(m))");
        Assert.That(GetValue<long>(row, "s"), Is.EqualTo(8), "sum(m): 5 + 3");
        Assert.That(GetValue<long>(row, "mn"), Is.EqualTo(3), "min(m)");
    }

    [Test]
    [Description("summarize by m > 2, where m is null for one group, gives the groups true and null")]
    public void Summarize_ByAComparisonOfANullAggregate_GivesANullGroup()
    {
        var kustoDatabase = CreateDatabase(BuildGroupsTable());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, $"{GroupStatsQuery} | summarize c = count() by IsBig = m > 2");

        Assert.That(rows.Select(row => GetNullableValue<bool>(row, "IsBig")),
            Is.EquivalentTo(new bool?[] { true, null }), "group keys");
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "IsBig", true), "c"), Is.EqualTo(2), "group true");
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "IsBig", null), "c"), Is.EqualTo(1), "group null");
    }

    #endregion

    #region operators after summarize by a nullable key

    [TestCase("asc", "null, 1, 3, 5")]
    [TestCase("desc", "5, 3, 1, null")]
    [TestCase("asc nulls last", "1, 3, 5, null")]
    [TestCase("desc nulls first", "null, 5, 3, 1")]
    public void Sort_AfterSummarizeByANullableKey_PlacesTheNullGroup(string direction, string expectedKeys)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        string actual = ExecuteAndGetColumnAsText<long>(kustoDatabase,
            $"Numbers | summarize c = count() by X | sort by X {direction}", "X");

        Assert.That(actual, Is.EqualTo(expectedKeys));
    }

    [Test]
    [Description("take 1 after sort by X asc returns the null group, which counts the 2 rows with a null X")]
    public void Take_AfterSortedSummarizeByANullableKey_ReturnsTheNullGroupFirst()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | summarize c = count() by X | sort by X asc | take 1");

        AssertIsNull(row, "X");
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(2), "count() of the null group");
    }

    [Test]
    [Description("'where' after summarize treats the null group key like any null value")]
    public void Where_AfterSummarizeByANullableKey_FiltersTheNullGroupLikeANullValue()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());
        const string grouped = "Numbers | summarize c = count() by X";

        IReadOnlyDictionary<string, object?> nullGroup =
            ExecuteAndGetSingleRow(kustoDatabase, $"{grouped} | where isnull(X)");
        Assert.That(GetValue<long>(nullGroup, "c"), Is.EqualTo(2), "where isnull(X): the null group counts 2 rows");
        Assert.That(ExecuteAndGetColumn<long>(kustoDatabase, $"{grouped} | where X > 1", "X"),
            Is.EquivalentTo(new long?[] { 3, 5 }), "where X > 1");
        Assert.That(ExecuteAndGetColumn<long>(kustoDatabase, $"{grouped} | where X != 3", "X"),
            Is.EquivalentTo(new long?[] { 1, 5, null }), "where X != 3 keeps the null group");
        Assert.That(ExecuteAndGetColumn<long>(kustoDatabase, $"{grouped} | where not(X > 1)", "X"),
            Is.EquivalentTo(new long?[] { 1 }), "where not(X > 1) drops the null group");
    }

    [Test]
    [Description("summarize by X and distinct X both keep one null key")]
    public void Count_AfterSummarizeOrDistinctByANullableKey_CountsTheNullKeyOnce()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        Assert.That(ExecuteAndGetCount(kustoDatabase, "Numbers | summarize by X | count"), Is.EqualTo(4),
            "summarize by X | count");
        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | distinct X | summarize c = count(), n = countif(isnull(X))");
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(4), "count() after distinct X");
        Assert.That(GetValue<long>(row, "n"), Is.EqualTo(1), "countif(isnull(X)) after distinct X");
    }

    [Test]
    [Description("A null produced by extend (X * 2 with a null X) is skipped by sum and counted by isnull")]
    public void Summarize_AfterExtendWithNullArithmetic_SkipsTheNullResults()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        Assert.That(ExecuteAndGetCount(kustoDatabase, "Numbers | extend Y = X * 2 | where isnull(Y) | count"),
            Is.EqualTo(2), "where isnull(Y)");
        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | extend Y = X * 2 | summarize s = sum(Y), n = countif(isnull(Y)), c = count()");
        Assert.That(GetNumber(row, "s"), Is.EqualTo(18), "sum(Y): 2 + 6 + 10");
        Assert.That(GetValue<long>(row, "n"), Is.EqualTo(2), "countif(isnull(Y))");
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(5), "count()");
    }

    [TestCase("Events | extend Big = X > 2 | summarize c = count(), s = sum(V) by Big")]
    [TestCase("Events | summarize c = count(), s = sum(V) by Big = X > 2")]
    [Description("A comparison with a null X is null, so grouping by it gives the groups true, false and null")]
    public void Summarize_ByAComparisonOfANullableColumn_GivesTrueFalseAndNullGroups(string query)
    {
        var kustoDatabase = CreateDatabase(BuildEventsTable());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndGetRows(kustoDatabase, query);

        Assert.That(rows.Select(row => GetNullableValue<bool>(row, "Big")),
            Is.EquivalentTo(new bool?[] { true, false, null }), "group keys");
        IReadOnlyDictionary<string, object?> nullGroup = GetSingleRowWhere(rows, "Big", null);
        Assert.That(GetValue<long>(nullGroup, "c"), Is.EqualTo(2), "count() of the group null");
        Assert.That(GetValue<long>(nullGroup, "s"), Is.EqualTo(60), "sum(V) of the group null: 20 + 40");
        IReadOnlyDictionary<string, object?> falseGroup = GetSingleRowWhere(rows, "Big", false);
        Assert.That(GetValue<long>(falseGroup, "c"), Is.EqualTo(2), "count() of the group false");
        Assert.That(GetValue<long>(falseGroup, "s"), Is.EqualTo(60), "sum(V) of the group false: 10 + 50");
        IReadOnlyDictionary<string, object?> trueGroup = GetSingleRowWhere(rows, "Big", true);
        Assert.That(GetValue<long>(trueGroup, "c"), Is.EqualTo(1), "count() of the group true");
        Assert.That(GetValue<long>(trueGroup, "s"), Is.EqualTo(30), "sum(V) of the group true");
    }

    [TestCase("asc", "null, false, true")]
    [TestCase("desc", "true, false, null")]
    public void Sort_ByANullableBoolGroupKey_PlacesTheNullGroup(string direction, string expectedKeys)
    {
        var kustoDatabase = CreateDatabase(BuildEventsTable());

        string actual = ExecuteAndGetColumnAsText<bool>(kustoDatabase,
            $"Events | summarize c = count() by Big = X > 2 | order by Big {direction}", "Big");

        Assert.That(actual, Is.EqualTo(expectedKeys));
    }

    [TestCase("X in (Numbers | summarize by X)", new[] { 1, 2, 3 })]
    [TestCase("X !in (Numbers | summarize by X)", new[] { 4, 5 })]
    [Description("The null group key of a subquery does not make 'in' match the rows with a null X")]
    public void In_SubqueryOfSummarizeWithANullKey_NeverMatchesANull(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        List<int> actual = ExecuteAndGetIds(kustoDatabase, $"Numbers | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    #endregion

    #region collections and hll

    [TestCase("asc", new object?[] { null, null, 1L, 1L, 3L, 5L })]
    [TestCase("desc", new object?[] { 5L, 3L, 1L, 1L, null, null })]
    [TestCase("asc nulls last", new object?[] { 1L, 1L, 3L, 5L, null, null })]
    [Description("make_list_with_nulls after sort keeps the order of the rows, nulls included")]
    public void MakeListWithNulls_AfterSort_KeepsThePositionOfTheNulls(string direction, object?[] expected)
    {
        var kustoDatabase = CreateDatabase(BuildSequenceTable());

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            $"Sequence | sort by X {direction} | summarize l = make_list_with_nulls(X)");

        Assert.That(GetDynamicArray(row, "l"), Is.EqualTo(expected));
    }

    [Test]
    [Description("make_list after sort skips the nulls and keeps the order of the values")]
    public void MakeList_AfterSort_SkipsTheNulls()
    {
        var kustoDatabase = CreateDatabase(BuildSequenceTable());

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sequence | sort by X asc | summarize l = make_list(X)");

        Assert.That(GetDynamicArray(row, "l"), Is.EqualTo(new object?[] { 1L, 1L, 3L, 5L }));
    }

    [Test]
    [Description("hll skips the nulls: dcount_hll(hll(X)) is dcount(X), and 0 when X has only nulls")]
    public void DcountHll_OfHllWithNulls_SkipsTheNulls()
    {
        var kustoDatabase = CreateDatabase(BuildSequenceTable());

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sequence | summarize h = hll(X), d = dcount(X) | extend e = dcount_hll(h) | project d, e");
        Assert.That(GetValue<long>(row, "d"), Is.EqualTo(3), "dcount(X)");
        Assert.That(GetValue<long>(row, "e"), Is.EqualTo(3), "dcount_hll(hll(X))");

        IReadOnlyDictionary<string, object?> onlyNulls = ExecuteAndGetSingleRow(kustoDatabase,
            "Sequence | where isnull(X) | summarize h = hll(X) | extend e = dcount_hll(h) | project e");
        Assert.That(GetValue<long>(onlyNulls, "e"), Is.EqualTo(0), "dcount_hll of the hll of only nulls");
    }

    [Test]
    [Description("hll_merge of the hll of the null group and of the value group counts only the values")]
    public void HllMerge_OfGroupsWithAndWithoutNulls_CountsOnlyTheValues()
    {
        var kustoDatabase = CreateDatabase(BuildSequenceTable());

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Sequence | summarize h = hll(X) by N = isnull(X) | summarize m = hll_merge(h) " +
            "| extend e = dcount_hll(m) | project e");

        Assert.That(GetValue<long>(row, "e"), Is.EqualTo(3));
    }

    [Test]
    [Description("make_bag of bag_pack(K, X) keeps the property whose value is null")]
    public void MakeBag_OfBagPackWithANullValue_KeepsTheNullProperty()
    {
        var kustoDatabase = CreateDatabase(BuildPropsTable());

        IReadOnlyDictionary<string, object?> row =
            ExecuteAndGetSingleRow(kustoDatabase, "Props | summarize b = make_bag(bag_pack(K, X))");

        Assert.That(GetBagPropertiesAsJson(row, "b"),
            Is.EqualTo(new Dictionary<string, string> { ["a"] = "1", ["b"] = "null", ["c"] = "3" }));
    }

    [Test]
    [Description("make_bag_if(bag_pack(K, X), X > 1) skips the row whose predicate is null (X is null)")]
    public void MakeBagIf_WithANullPredicate_SkipsTheRow()
    {
        var kustoDatabase = CreateDatabase(BuildPropsTable());

        IReadOnlyDictionary<string, object?> row =
            ExecuteAndGetSingleRow(kustoDatabase, "Props | summarize b = make_bag_if(bag_pack(K, X), X > 1)");

        Assert.That(GetBagPropertiesAsJson(row, "b"), Is.EqualTo(new Dictionary<string, string> { ["c"] = "3" }));
    }

    [Test]
    [Description("make_list of bag_pack('x', X) keeps the bag {\"x\": null}: the bag is not null, only its value is")]
    public void MakeList_OfBagPackWithANullValue_KeepsTheBag()
    {
        var kustoDatabase = CreateDatabase(BuildPropsTable());

        IReadOnlyDictionary<string, object?> row =
            ExecuteAndGetSingleRow(kustoDatabase, "Props | summarize l = make_list(bag_pack('x', X))");

        Assert.That(GetArrayItemsAsJson(row, "l").Select(DescribeBag),
            Is.EquivalentTo(new[] { "x=1", "x=null", "x=3" }));
    }

    #endregion

    #region a table loaded from CSV with empty cells

    [Test]
    [Description("An empty CSV cell is a null of the inferred column type")]
    public void Csv_EmptyCells_AreNullsOfTheInferredType()
    {
        var kustoDatabase = CreateDatabase(BuildTableFromCsv("People", PeopleCsv));

        Assert.That(ExecuteAndGetNumbersById(kustoDatabase, "People | project Id, Score", "Score"),
            Is.EqualTo(ExpectedById<double>(10, null, 30, null, 50)), "Score");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, "People | project Id, Active", "Active"),
            Is.EqualTo(ExpectedById<bool>(true, false, true, null, null)), "Active");
        Assert.That(ExecuteAndGetValuesById<DateTime>(kustoDatabase, "People | project Id, When", "When"),
            Is.EqualTo(ExpectedById<DateTime>(new DateTime(2020, 1, 1), null, new DateTime(2021, 6, 1),
                new DateTime(2020, 3, 1), null)), "When");
    }

    [TestCase("isnull(Score)", new[] { 2, 4 })]
    [TestCase("isnotnull(Score)", new[] { 1, 3, 5 })]
    [TestCase("Score > 20", new[] { 3, 5 })]
    [TestCase("Score != 30", new[] { 1, 2, 4, 5 })]
    [TestCase("not(Score > 20)", new[] { 1 })]
    [TestCase("Score in (10, 50)", new[] { 1, 5 })]
    [TestCase("Score !in (10, 50)", new[] { 2, 3, 4 })]
    [TestCase("Score between (10 .. 30)", new[] { 1, 3 })]
    [TestCase("Score !between (10 .. 30)", new[] { 5 })]
    [TestCase("Name == ''", new[] { 3, 4 })]
    [TestCase("Name contains 'a'", new[] { 1, 2, 5 })]
    [TestCase("Name !contains 'a'", new[] { 3, 4 })]
    [TestCase("Active", new[] { 1, 3 })]
    [TestCase("not(Active)", new[] { 2 })]
    [TestCase("Active != true", new[] { 2, 4, 5 })]
    [TestCase("Active == false", new[] { 2 })]
    [TestCase("isnull(Active)", new[] { 4, 5 })]
    [TestCase("When > datetime(2020-06-01)", new[] { 3 })]
    [TestCase("When < datetime(2020-06-01)", new[] { 1, 4 })]
    [TestCase("When != datetime(2020-01-01)", new[] { 2, 3, 4, 5 })]
    [TestCase("isnull(When)", new[] { 2, 5 })]
    public void Csv_Where_OnColumnsWithEmptyCells(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildTableFromCsv("People", PeopleCsv));

        List<int> actual = ExecuteAndGetIds(kustoDatabase, $"People | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [Test]
    [Description("Aggregates of a CSV table skip the empty cells")]
    public void Csv_Aggregates_SkipTheEmptyCells()
    {
        var kustoDatabase = CreateDatabase(BuildTableFromCsv("People", PeopleCsv));

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "People | summarize c = count(), s = sum(Score), a = avg(Score), n = countif(isnull(Score)), " +
            "d = dcount(Active), mn = min(When), mx = max(When)");

        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(5), "count()");
        Assert.That(GetValue<long>(row, "s"), Is.EqualTo(90), "sum(Score)");
        Assert.That(GetValue<double>(row, "a"), Is.EqualTo(30.0), "avg(Score) of the 3 values");
        Assert.That(GetValue<long>(row, "n"), Is.EqualTo(2), "countif(isnull(Score))");
        Assert.That(GetValue<long>(row, "d"), Is.EqualTo(2), "dcount(Active): true and false");
        Assert.That(GetValue<DateTime>(row, "mn"), Is.EqualTo(new DateTime(2020, 1, 1)), "min(When)");
        Assert.That(GetValue<DateTime>(row, "mx"), Is.EqualTo(new DateTime(2021, 6, 1)), "max(When)");
    }

    [Test]
    [Description("summarize by a bool column with empty cells gives the groups true, false and null")]
    public void Csv_SummarizeByABoolColumnWithEmptyCells_GivesANullGroup()
    {
        var kustoDatabase = CreateDatabase(BuildTableFromCsv("People", PeopleCsv));

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "People | summarize c = count(), s = sum(Score) by Active");

        Assert.That(rows.Select(row => GetNullableValue<bool>(row, "Active")),
            Is.EquivalentTo(new bool?[] { true, false, null }), "group keys");
        IReadOnlyDictionary<string, object?> trueGroup = GetSingleRowWhere(rows, "Active", true);
        Assert.That(GetValue<long>(trueGroup, "c"), Is.EqualTo(2), "count() of the group true");
        Assert.That(GetValue<long>(trueGroup, "s"), Is.EqualTo(40), "sum(Score) of the group true");
        IReadOnlyDictionary<string, object?> falseGroup = GetSingleRowWhere(rows, "Active", false);
        Assert.That(GetValue<long>(falseGroup, "c"), Is.EqualTo(1), "count() of the group false");
        Assert.That(GetValue<long>(falseGroup, "s"), Is.EqualTo(0), "sum(Score) of the group false: only a null");
        IReadOnlyDictionary<string, object?> nullGroup = GetSingleRowWhere(rows, "Active", null);
        Assert.That(GetValue<long>(nullGroup, "c"), Is.EqualTo(2), "count() of the group null");
        Assert.That(GetValue<long>(nullGroup, "s"), Is.EqualTo(50), "sum(Score) of the group null");
    }

    [Test]
    [Description("sort puts the empty cells first for asc and last for desc")]
    public void Csv_Sort_PlacesTheEmptyCells()
    {
        var kustoDatabase = CreateDatabase(BuildTableFromCsv("People", PeopleCsv));

        AssertIdsInOrderWithTies(ExecuteAndGetIds(kustoDatabase, "People | sort by Score asc | project Id"),
            [2, 4], [1], [3], [5]);
        AssertIdsInOrderWithTies(ExecuteAndGetIds(kustoDatabase, "People | sort by Score desc | project Id"),
            [5], [3], [1], [2, 4]);
        AssertIdsInOrderWithTies(ExecuteAndGetIds(kustoDatabase, "People | sort by When asc | project Id"),
            [2, 5], [1], [4], [3]);
    }

    #endregion

    #region a table loaded from JSON with null values

    [Test]
    [Description("A JSON null is a null of the declared column type")]
    public void Json_NullValues_AreNullsOfTheDeclaredType()
    {
        var kustoDatabase = CreateDatabase(BuildTableFromJson(OrdersJson));

        Assert.That(ExecuteAndGetValuesById<double>(kustoDatabase, "Orders | project Id, Amount", "Amount"),
            Is.EqualTo(ExpectedById<double>(10, null, 30)), "Amount");
        Assert.That(ExecuteAndGetValuesById<decimal>(kustoDatabase, "Orders | project Id, Price", "Price"),
            Is.EqualTo(ExpectedById<decimal>(2, null, 4)), "Price");
        Assert.That(ExecuteAndGetValuesById<TimeSpan>(kustoDatabase, "Orders | project Id, Duration", "Duration"),
            Is.EqualTo(ExpectedById<TimeSpan>(TimeSpan.FromHours(1), null, TimeSpan.FromHours(3))), "Duration");
        Assert.That(ExecuteAndGetValuesById<Guid>(kustoDatabase, "Orders | project Id, Key", "Key"),
            Is.EqualTo(ExpectedById<Guid>(Guid.Parse("00000000-0000-0000-0000-000000000001"), null,
                Guid.Parse("00000000-0000-0000-0000-000000000003"))), "Key");
    }

    [TestCase("isnull(Amount)", new[] { 2 })]
    [TestCase("Amount > 5", new[] { 1, 3 })]
    [TestCase("Amount != 10", new[] { 2, 3 })]
    [TestCase("Amount == Amount", new[] { 1, 3 })]
    [TestCase("Price < 3", new[] { 1 })]
    [TestCase("Duration > 2h", new[] { 3 })]
    [TestCase("isnull(Duration)", new[] { 2 })]
    [TestCase("Key == guid(00000000-0000-0000-0000-000000000001)", new[] { 1 })]
    [TestCase("Key != guid(00000000-0000-0000-0000-000000000001)", new[] { 2, 3 })]
    [TestCase("isnull(Key)", new[] { 2 })]
    [TestCase("Tag == ''", new[] { 2, 3 })]
    [TestCase("Tag == 'x'", new[] { 1 })]
    public void Json_Where_OnColumnsWithNullValues(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildTableFromJson(OrdersJson));

        List<int> actual = ExecuteAndGetIds(kustoDatabase, $"Orders | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [Test]
    [Description("Arithmetic with a JSON null is null; strlen of a null string is 0")]
    public void Json_ArithmeticAndStrlen_OnNullValues()
    {
        var kustoDatabase = CreateDatabase(BuildTableFromJson(OrdersJson));

        Assert.That(ExecuteAndGetNumbersById(kustoDatabase, "Orders | extend P = Price * 2", "P"),
            Is.EqualTo(ExpectedById<double>(4, null, 8)), "Price * 2");
        Assert.That(ExecuteAndGetNumbersById(kustoDatabase, "Orders | extend L = strlen(Tag)", "L"),
            Is.EqualTo(ExpectedById<double>(1, 0, 0)), "strlen(Tag)");
    }

    [Test]
    [Description("Aggregates of a JSON table skip the null values")]
    public void Json_Aggregates_SkipTheNullValues()
    {
        var kustoDatabase = CreateDatabase(BuildTableFromJson(OrdersJson));

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Orders | summarize s = sum(Amount), c = count(), n = countif(isnull(Key)), p = sum(Price), " +
            "t = avg(Duration), mx = max(Duration), d = dcount(Key)");

        Assert.That(GetValue<double>(row, "s"), Is.EqualTo(40.0), "sum(Amount)");
        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(3), "count()");
        Assert.That(GetValue<long>(row, "n"), Is.EqualTo(1), "countif(isnull(Key))");
        Assert.That(GetValue<decimal>(row, "p"), Is.EqualTo(6m), "sum(Price)");
        Assert.That(GetValue<TimeSpan>(row, "t"), Is.EqualTo(TimeSpan.FromHours(2)), "avg(Duration) of the 2 values");
        Assert.That(GetValue<TimeSpan>(row, "mx"), Is.EqualTo(TimeSpan.FromHours(3)), "max(Duration)");
        Assert.That(GetValue<long>(row, "d"), Is.EqualTo(2), "dcount(Key)");
    }

    #endregion
}
