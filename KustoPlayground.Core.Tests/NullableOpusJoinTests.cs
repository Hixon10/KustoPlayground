using System.Globalization;
using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Nullable join keys and the nulls in the result of join, as in Azure Data Explorer.
/// join is not in the README yet: these tests describe the null semantics to implement together with join.
/// - unlike the == operator (null == null is null, so 'where' drops the row), join matches a null key to a null key:
///   every join kind (inner, innerunique, leftouter, rightouter, fullouter, leftsemi, leftanti, rightsemi, rightanti),
///   'on K' and 'on $left.K == $right.RK', and every scalar type (int, long, real, decimal, datetime, timespan, bool,
///   guid, string) treat the nulls of a key as one more key value;
/// - a null key matches only a null key, never the default value of its type (0, 0.0, 0m, 0001-01-01, 00:00:00,
///   false, the empty guid), and NaN matches NaN but not null;
/// - a key of several columns matches when every column matches, a null column matching a null column;
/// - every null key of the left side matches every null key of the right side (like any other repeated key),
///   and innerunique (the default kind) keeps one left row per key, the null key included;
/// - the right key column is K1 in the result: after an outer join isnull(K1) is true both for an unmatched row and
///   for a row matched by a null key, while isnull(W) of a non-key column tells them apart;
/// - the right columns of an unmatched leftouter row and the left columns of an unmatched rightouter row are null,
///   and they behave like any other null in the operators after join (where, project, extend, summarize, sort,
///   distinct, count);
/// - 'where K in (subquery)' and 'where K !in (subquery)' never match a null K, so they differ from the leftsemi and
///   leftanti joins when the key has nulls, and 'where K == K1' after a join drops the rows matched by null keys;
///   to keep the null keys out of a join, filter them out first (R | where isnotnull(K)).
/// The rows of a join come in any order, so the results are compared in any order unless the query sorts them.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class NullableOpusJoinTests
{
    // L(Id: int, K: long?) = 1: 1, 2: null, 3: 2, 4: null
    // The left rows 1, 2 and 4 have a match in R (2 and 4 by the null key), the left row 3 has none.
    private static Table BuildLTable()
    {
        return new SummarizeOperatorOpusTableBuilder("L")
            .WithColumn<int>("Id")
            .WithColumn<long>("K")
            .WithRow(1, 1L)
            .WithRow(2, null)
            .WithRow(3, 2L)
            .WithRow(4, null)
            .Build();
    }

    // R(K: long?, W: long?) = (1, 100), (null, 200), (3, 300); RenamedR is the same table with the key column RK.
    // The right rows 100 and 200 have a match in L (200 by the null key), the right row 300 has none.
    private static Table BuildRTable(string tableName, string keyColumnName)
    {
        return new SummarizeOperatorOpusTableBuilder(tableName)
            .WithColumn<long>(keyColumnName)
            .WithColumn<long>("W")
            .WithRow(1L, 100L)
            .WithRow(null, 200L)
            .WithRow(3L, 300L)
            .Build();
    }

    // PairsL(Id: int, A: long?, B: long?) = 1: (1, 1), 2: (1, null), 3: (null, null), 4: (null, 1), 5: (2, null)
    private static Table BuildPairsLTable()
    {
        return new SummarizeOperatorOpusTableBuilder("PairsL")
            .WithColumn<int>("Id")
            .WithColumn<long>("A")
            .WithColumn<long>("B")
            .WithRow(1, 1L, 1L)
            .WithRow(2, 1L, null)
            .WithRow(3, null, null)
            .WithRow(4, null, 1L)
            .WithRow(5, 2L, null)
            .Build();
    }

    // PairsR(A: long?, B: long?, W: long?) = (1, 1, 10), (1, null, 20), (null, null, 30)
    private static Table BuildPairsRTable()
    {
        return new SummarizeOperatorOpusTableBuilder("PairsR")
            .WithColumn<long>("A")
            .WithColumn<long>("B")
            .WithColumn<long>("W")
            .WithRow(1L, 1L, 10L)
            .WithRow(1L, null, 20L)
            .WithRow(null, null, 30L)
            .Build();
    }

    // NaNL(Id: int, K: real?) = 1: 1.0, 2: NaN, 3: null; NaNR(K: real?, W: long?) = (1.0, 10), (NaN, 20), (null, 30)
    private static KustoDatabase CreateNaNKeysDatabase()
    {
        return CreateDatabase(
            new SummarizeOperatorOpusTableBuilder("NaNL")
                .WithColumn<int>("Id")
                .WithColumn<double>("K")
                .WithRow(1, 1.0)
                .WithRow(2, double.NaN)
                .WithRow(3, null)
                .Build(),
            new SummarizeOperatorOpusTableBuilder("NaNR")
                .WithColumn<double>("K")
                .WithColumn<long>("W")
                .WithRow(1.0, 10L)
                .WithRow(double.NaN, 20L)
                .WithRow(null, 30L)
                .Build());
    }

    private static KustoDatabase CreateLAndRDatabase()
    {
        return CreateDatabase(BuildLTable(), BuildRTable("R", "K"), BuildRTable("RenamedR", "RK"));
    }

    /// <summary>
    /// Executes the query and returns every result row as the values of the given columns joined by ':', like
    /// strcat(Id, ':', K, ':', K1, ':', W) in Kusto, where a null value is empty: (2, null, null, 200) is "2:::200".
    /// </summary>
    private static List<string> ExecuteAndGetRowTexts(KustoDatabase kustoDatabase, string query,
        params string[] columnNames)
    {
        return ExecuteAndGetRows(kustoDatabase, query)
            .Select(row => string.Join(":", columnNames.Select(columnName => FormatCell(row, columnName))))
            .ToList();
    }

    private static string FormatCell(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        Assert.That(row.Keys, Does.Contain(columnName), $"column '{columnName}' is missing");
        return row[columnName] switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            object value => value.ToString() ?? string.Empty
        };
    }

    #region null keys per join kind

    [TestCase("inner", new[] { "1:1:1:100", "2:::200", "4:::200" })]
    [TestCase("leftouter", new[] { "1:1:1:100", "2:::200", "3:2::", "4:::200" })]
    [TestCase("rightouter", new[] { "1:1:1:100", "2:::200", "4:::200", "::3:300" })]
    [TestCase("fullouter", new[] { "1:1:1:100", "2:::200", "3:2::", "4:::200", "::3:300" })]
    [Description("Rows as Id:K:K1:W. The left rows 2 and 4 (K = null) both match the right row (null, 200); the left " +
                 "row 3 and the right row 300 have no match, so the outer joins fill the other side with nulls")]
    public void Join_OnANullableKey_MatchesTheNullKeysToEachOther(string kind, string[] expectedRows)
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, $"L | join kind={kind} R on K", "Id", "K", "K1", "W"),
            Is.EquivalentTo(expectedRows));
    }

    [TestCase("leftsemi", new[] { 1, 2, 4 })]
    [TestCase("leftanti", new[] { 3 })]
    [Description("The left rows with a null key have a match (the right null key), so leftsemi keeps them and " +
                 "leftanti drops them")]
    public void Join_LeftSemiAndLeftAnti_TreatTheNullKeyRowsAsMatched(string kind, int[] expectedIds)
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetIds(kustoDatabase, $"L | join kind={kind} R on K"), Is.EquivalentTo(expectedIds));
    }

    [TestCase("rightsemi", new[] { 100L, 200L })]
    [TestCase("rightanti", new[] { 300L })]
    [Description("The right row with a null key (200) has a match (the left null keys), so rightsemi keeps it and " +
                 "rightanti drops it")]
    public void Join_RightSemiAndRightAnti_TreatTheNullKeyRowAsMatched(string kind, long[] expectedW)
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(GetValues<long>(ExecuteAndGetRows(kustoDatabase, $"L | join kind={kind} R on K"), "W"),
            Is.EquivalentTo(expectedW));
    }

    [TestCase("inner", 3L)]
    [TestCase("innerunique", 2L)]
    [TestCase("leftouter", 4L)]
    [TestCase("rightouter", 4L)]
    [TestCase("fullouter", 5L)]
    [TestCase("leftsemi", 3L)]
    [TestCase("leftanti", 1L)]
    [TestCase("rightsemi", 2L)]
    [TestCase("rightanti", 1L)]
    public void Count_AfterJoinOnANullableKey_IncludesTheRowsMatchedByNullKeys(string kind, long expectedCount)
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, $"L | join kind={kind} R on K | count"),
            Is.EqualTo(expectedCount));
    }

    [TestCase("kind=innerunique")]
    [TestCase("")]
    [Description("innerunique (also the default kind) keeps one left row per key before matching: row 1 for the key 1 " +
                 "and either row 2 or row 4 for the null key, which still matches the right null key")]
    public void Join_InnerUnique_KeepsOneLeftRowOfTheNullKey(string kind)
    {
        var kustoDatabase = CreateLAndRDatabase();

        List<string> rows = ExecuteAndGetRowTexts(kustoDatabase, $"L | join {kind} R on K", "Id", "K", "K1", "W");

        Assert.That(rows, Has.Count.EqualTo(2), $"rows as Id:K:K1:W: [{string.Join(", ", rows)}]");
        Assert.That(rows, Does.Contain("1:1:1:100"));
        Assert.That(rows.Single(row => row != "1:1:1:100"), Is.EqualTo("2:::200").Or.EqualTo("4:::200"),
            "the single row of the null key");
    }

    [TestCase("L | join kind=inner R on $left.K == $right.K", "K1")]
    [TestCase("L | join kind=inner RenamedR on $left.K == $right.RK", "RK")]
    [Description("An explicit equality also matches the null keys; the right key keeps its own name when it differs " +
                 "from the left key")]
    public void Join_OnAnExplicitEquality_MatchesTheNullKeysToEachOther(string query, string rightKeyColumn)
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Id", "K", rightKeyColumn, "W"),
            Is.EquivalentTo(new[] { "1:1:1:100", "2:::200", "4:::200" }));
    }

    [TestCase("inner", new[] { "Id", "K", "K1", "W" })]
    [TestCase("innerunique", new[] { "Id", "K", "K1", "W" })]
    [TestCase("leftouter", new[] { "Id", "K", "K1", "W" })]
    [TestCase("rightouter", new[] { "Id", "K", "K1", "W" })]
    [TestCase("fullouter", new[] { "Id", "K", "K1", "W" })]
    [TestCase("leftsemi", new[] { "Id", "K" })]
    [TestCase("leftanti", new[] { "Id", "K" })]
    [TestCase("rightsemi", new[] { "K", "W" })]
    [TestCase("rightanti", new[] { "K", "W" })]
    [Description("The joins that return both sides rename the right key column to K1, so the null key of the right " +
                 "side stays visible next to the left one")]
    public void Join_ResultColumns_PerJoinKind(string kind, string[] expectedColumns)
    {
        var kustoDatabase = CreateLAndRDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, $"L | join kind={kind} R on K");

        Assert.That(rows, Is.Not.Empty);
        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            AssertColumnsInOrder(row, expectedColumns);
        }
    }

    [Test]
    [Description("The key columns keep their type (long) and their nulls: K and K1 are 1 for the row 1 and null for the " +
                 "rows 2 and 4 matched by the null key")]
    public void Join_Inner_KeepsTheTypeAndTheNullsOfBothKeyColumns()
    {
        var kustoDatabase = CreateLAndRDatabase();
        const string query = "L | join kind=inner R on K";

        Assert.That(ExecuteAndGetValuesById<long>(kustoDatabase, query, "K"),
            Is.EqualTo(new Dictionary<int, long?> { [1] = 1, [2] = null, [4] = null }), "K");
        Assert.That(ExecuteAndGetValuesById<long>(kustoDatabase, query, "K1"),
            Is.EqualTo(new Dictionary<int, long?> { [1] = 1, [2] = null, [4] = null }), "K1");
        Assert.That(ExecuteAndGetValuesById<long>(kustoDatabase, query, "W"),
            Is.EqualTo(new Dictionary<int, long?> { [1] = 100, [2] = 200, [4] = 200 }), "W");
    }

    [Test]
    [Description("L | join kind=inner L on K: every null key row matches every null key row, itself included " +
                 "(2-2, 2-4, 4-2, 4-4), like the rows of a repeated key")]
    public void Join_SelfJoinOnANullableKey_MatchesEveryNullKeyRowWithEveryNullKeyRow()
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, "L | join kind=inner L on K", "Id", "Id1"),
            Is.EquivalentTo(new[] { "1:1", "3:3", "2:2", "2:4", "4:2", "4:4" }));
    }

    #endregion

    #region null keys per column type

    // TypedL(Id: int, K: T?) = 1: value, 2: null, 3: default(T); TypedR(K: T?, W: long?) = (value, 10), (null, 20).
    // The null key of row 2 matches only the right null key and the default value of row 3 matches nothing, so inner
    // gives the rows 1:10 and 2:20 (K1 = value and null) and leftanti gives the row 3.
    // An implementation that stores a null key as default(T) would also match the row 3 with the right null key.
    private static void AssertNullKeyMatchesOnlyTheNullKey<T>(T value)
        where T : struct
    {
        var kustoDatabase = CreateDatabase(
            new SummarizeOperatorOpusTableBuilder("TypedL")
                .WithColumn<int>("Id")
                .WithColumn<T>("K")
                .WithRow(1, value)
                .WithRow(2, null)
                .WithRow(3, default(T))
                .Build(),
            new SummarizeOperatorOpusTableBuilder("TypedR")
                .WithColumn<T>("K")
                .WithColumn<long>("W")
                .WithRow(value, 10L)
                .WithRow(null, 20L)
                .Build());

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, "TypedL | join kind=inner TypedR on K", "Id", "W"),
            Is.EquivalentTo(new[] { "1:10", "2:20" }), "inner join, rows as Id:W");
        Assert.That(ExecuteAndGetValuesById<T>(kustoDatabase, "TypedL | join kind=inner TypedR on K", "K1"),
            Is.EqualTo(new Dictionary<int, T?> { [1] = value, [2] = null }), "K1 of the inner join");
        Assert.That(ExecuteAndGetIds(kustoDatabase, "TypedL | join kind=leftanti TypedR on K"),
            Is.EquivalentTo(new[] { 3 }), "leftanti join");
    }

    [TestCase(7)]
    public void Join_OnANullableIntKey_MatchesTheNullKeyOnlyWithTheNullKey(int value)
    {
        AssertNullKeyMatchesOnlyTheNullKey(value);
    }

    [TestCase(7L)]
    public void Join_OnANullableLongKey_MatchesTheNullKeyOnlyWithTheNullKey(long value)
    {
        AssertNullKeyMatchesOnlyTheNullKey(value);
    }

    [TestCase(2.5)]
    public void Join_OnANullableRealKey_MatchesTheNullKeyOnlyWithTheNullKey(double value)
    {
        AssertNullKeyMatchesOnlyTheNullKey(value);
    }

    [TestCase("2.5")]
    public void Join_OnANullableDecimalKey_MatchesTheNullKeyOnlyWithTheNullKey(string value)
    {
        AssertNullKeyMatchesOnlyTheNullKey(decimal.Parse(value, CultureInfo.InvariantCulture));
    }

    [Test]
    public void Join_OnANullableDatetimeKey_MatchesTheNullKeyOnlyWithTheNullKey()
    {
        AssertNullKeyMatchesOnlyTheNullKey(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [TestCase("01:00:00")]
    public void Join_OnANullableTimespanKey_MatchesTheNullKeyOnlyWithTheNullKey(string value)
    {
        AssertNullKeyMatchesOnlyTheNullKey(TimeSpan.Parse(value, CultureInfo.InvariantCulture));
    }

    [TestCase(true)]
    public void Join_OnANullableBoolKey_MatchesTheNullKeyOnlyWithTheNullKey(bool value)
    {
        AssertNullKeyMatchesOnlyTheNullKey(value);
    }

    [TestCase("00000000-0000-0000-0000-000000000001")]
    public void Join_OnANullableGuidKey_MatchesTheNullKeyOnlyWithTheNullKey(string value)
    {
        AssertNullKeyMatchesOnlyTheNullKey(Guid.Parse(value));
    }

    [Test]
    [Description("NaN is a value, not a null: the NaN key matches the NaN key, the null key matches the null key, " +
                 "and NaN does not match null")]
    public void Join_OnARealKeyWithNaNAndNulls_MatchesNaNWithNaNAndNullWithNull()
    {
        var kustoDatabase = CreateNaNKeysDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, "NaNL | join kind=inner NaNR on K", "Id", "W"),
            Is.EquivalentTo(new[] { "1:10", "2:20", "3:30" }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "NaNL | join kind=leftanti NaNR on K"), Is.Empty);
    }

    // NamesL(Id: int, K: string?) = 1: "a", 2: empty, 3: "b", 4: empty; NamesR(K: string?, W: long?) = ("a", 10),
    // (empty, 20), (empty, 40). The 2 empty keys of each side match each other: 2 x 2 rows.
    private static void AssertEmptyStringKeysMatchEachOther(string? emptyKey)
    {
        var kustoDatabase = CreateDatabase(
            new SummarizeOperatorOpusTableBuilder("NamesL")
                .WithColumn<int>("Id")
                .WithColumn<string>("K")
                .WithRow(1, "a")
                .WithRow(2, emptyKey)
                .WithRow(3, "b")
                .WithRow(4, emptyKey)
                .Build(),
            new SummarizeOperatorOpusTableBuilder("NamesR")
                .WithColumn<string>("K")
                .WithColumn<long>("W")
                .WithRow("a", 10L)
                .WithRow(emptyKey, 20L)
                .WithRow(emptyKey, 40L)
                .Build());

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, "NamesL | join kind=inner NamesR on K", "Id", "W"),
            Is.EquivalentTo(new[] { "1:10", "2:20", "2:40", "4:20", "4:40" }), "inner join, rows as Id:W");
        Assert.That(ExecuteAndGetIds(kustoDatabase, "NamesL | join kind=leftanti NamesR on K"),
            Is.EquivalentTo(new[] { 3 }), "leftanti join");
    }

    [Test]
    [Description("A string is never null in Kusto (a null string cell is an empty string): the null string keys " +
                 "match each other")]
    public void Join_OnAStringKeyWithNullCells_MatchesTheNullCellsWithEachOther()
    {
        AssertEmptyStringKeysMatchEachOther(null);
    }

    [Test]
    public void Join_OnAStringKeyWithEmptyStrings_MatchesTheEmptyStringsWithEachOther()
    {
        AssertEmptyStringKeysMatchEachOther("");
    }

    [Test]
    [Description("on A, B matches when both columns match, a null with a null: (1, null) matches (1, null) and " +
                 "(null, null) matches (null, null), while (null, 1) and (2, null) have no match")]
    public void Join_OnTwoNullableKeyColumns_MatchesANullColumnOnlyWithANullColumn()
    {
        var kustoDatabase = CreateDatabase(BuildPairsLTable(), BuildPairsRTable());

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, "PairsL | join kind=inner PairsR on A, B", "Id", "W"),
            Is.EquivalentTo(new[] { "1:10", "2:20", "3:30" }), "inner join, rows as Id:W");
        Assert.That(ExecuteAndGetIds(kustoDatabase, "PairsL | join kind=leftanti PairsR on A, B"),
            Is.EquivalentTo(new[] { 4, 5 }), "leftanti join");
    }

    [Test]
    [Description("Tables loaded from CSV texts (as in the UI): the empty key cells are null keys and match each other")]
    public void Join_TablesLoadedFromCsvWithEmptyKeyCells_MatchesTheEmptyCellsWithEachOther()
    {
        var kustoDatabase = CreateDatabase(
            BuildTableFromCsv("L", """
                Id,K
                1,1
                2,
                3,2
                4,
                """),
            BuildTableFromCsv("R", """
                K,W
                1,100
                ,200
                3,300
                """));

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, "L | join kind=leftouter R on K", "Id", "K", "K1", "W"),
            Is.EquivalentTo(new[] { "1:1:1:100", "2:::200", "3:2::", "4:::200" }));
    }

    #endregion

    #region nulls in the result of outer joins

    [TestCase("isnull(K1)", new[] { 2, 3, 4 })]
    [TestCase("isnull(W)", new[] { 3 })]
    [TestCase("isnotnull(W)", new[] { 1, 2, 4 })]
    [TestCase("W > 150", new[] { 2, 4 })]
    [TestCase("not(W > 150)", new[] { 1 })]
    [TestCase("W != 100", new[] { 2, 3, 4 })]
    [TestCase("W == 200", new[] { 2, 4 })]
    [TestCase("isnull(W) or W > 150", new[] { 2, 3, 4 })]
    [Description("After L | join kind=leftouter R on K, W is null only for the unmatched row 3, while K1 is null for " +
                 "the row 3 and for the rows 2 and 4 matched by the null key; the comparisons with the null W follow " +
                 "the three-valued logic (null > 150 and not(null > 150) are null, null != 100 is true)")]
    public void Where_AfterLeftOuterJoin_OnTheNullsOfTheResult(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetIds(kustoDatabase, $"L | join kind=leftouter R on K | where {predicate}"),
            Is.EquivalentTo(expectedIds));
    }

    [Test]
    [Description("The left columns of the unmatched right row (300) of a rightouter join are null")]
    public void Where_AfterRightOuterJoin_IsNullOfALeftColumnFindsTheUnmatchedRightRow()
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(
            GetValues<long>(ExecuteAndGetRows(kustoDatabase, "L | join kind=rightouter R on K | where isnull(Id)"), "W"),
            Is.EqualTo(new[] { 300L }));
    }

    [Test]
    public void Project_AfterLeftOuterJoin_KeepsTheMissingRightValueAsNull()
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetValuesById<long>(kustoDatabase, "L | join kind=leftouter R on K | project Id, W", "W"),
            Is.EqualTo(ExpectedById<long>(100, 200, null, 200)));
    }

    [Test]
    public void Extend_AfterLeftOuterJoin_ArithmeticOnTheMissingRightValueIsNull()
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetNumbersById(kustoDatabase, "L | join kind=leftouter R on K | extend T = W + 1", "T"),
            Is.EqualTo(ExpectedById<double>(101, 201, null, 201)));
    }

    [Test]
    [Description("count() counts the unmatched row, sum skips its null W: c = 4, s = 100 + 200 + 200, n = 1")]
    public void Summarize_AfterLeftOuterJoin_AggregatesSkipTheMissingRightValue()
    {
        var kustoDatabase = CreateLAndRDatabase();

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "L | join kind=leftouter R on K | summarize c = count(), s = sum(W), n = countif(isnull(W))");

        Assert.That(GetValue<long>(row, "c"), Is.EqualTo(4), "count()");
        Assert.That(GetValue<long>(row, "s"), Is.EqualTo(500), "sum(W)");
        Assert.That(GetValue<long>(row, "n"), Is.EqualTo(1), "countif(isnull(W))");
    }

    [Test]
    [Description("summarize by K1 after a leftouter join puts the unmatched row 3 and the rows 2 and 4 matched by the " +
                 "null key into one null group: null: c = 3, 1: c = 1")]
    public void Summarize_ByTheRightKeyAfterLeftOuterJoin_PutsUnmatchedAndNullKeyRowsInOneGroup()
    {
        var kustoDatabase = CreateLAndRDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "L | join kind=leftouter R on K | summarize c = count() by K1");

        Assert.That(rows.Select(row => GetNullableValue<long>(row, "K1")), Is.EquivalentTo(new long?[] { 1, null }),
            "group keys");
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "K1", null), "c"), Is.EqualTo(3), "the null group");
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "K1", 1L), "c"), Is.EqualTo(1), "the group of 1");
    }

    [Test]
    [Description("The rows 2 and 4 matched by the null key form one null group after an inner join: null: s = 400, " +
                 "1: s = 100")]
    public void Summarize_ByTheKeyAfterInnerJoin_TheRowsMatchedByTheNullKeyFormOneGroup()
    {
        var kustoDatabase = CreateLAndRDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "L | join kind=inner R on K | summarize s = sum(W) by K");

        Assert.That(rows.Select(row => GetNullableValue<long>(row, "K")), Is.EquivalentTo(new long?[] { 1, null }),
            "group keys");
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "K", null), "s"), Is.EqualTo(400), "the null group");
        Assert.That(GetValue<long>(GetSingleRowWhere(rows, "K", 1L), "s"), Is.EqualTo(100), "the group of 1");
    }

    [Test]
    [Description("A fullouter join has one row without a left side (Id null: the right row 300), one row without a " +
                 "right side (W null: the left row 3) and two rows matched by the null key (K and K1 null)")]
    public void Summarize_AfterFullOuterJoin_CountsTheNullsOfBothSides()
    {
        var kustoDatabase = CreateLAndRDatabase();

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "L | join kind=fullouter R on K | summarize noLeft = countif(isnull(Id)), noRight = countif(isnull(W)), " +
            "nullKeys = countif(isnull(K) and isnull(K1))");

        Assert.That(GetValue<long>(row, "noLeft"), Is.EqualTo(1), "countif(isnull(Id))");
        Assert.That(GetValue<long>(row, "noRight"), Is.EqualTo(1), "countif(isnull(W))");
        Assert.That(GetValue<long>(row, "nullKeys"), Is.EqualTo(2), "countif(isnull(K) and isnull(K1))");
    }

    [TestCase("asc", new[] { 3, 1, 2, 4 })]
    [TestCase("desc", new[] { 2, 4, 1, 3 })]
    [Description("The null W of the unmatched row 3 comes first in ascending order and last in descending order")]
    public void Sort_ByTheMissingRightValueAfterLeftOuterJoin_PlacesTheUnmatchedRow(string direction,
        int[] expectedIds)
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetIds(kustoDatabase, $"L | join kind=leftouter R on K | sort by W {direction}, Id asc"),
            Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("distinct keeps one null: K1 has the values 1 and null, W has 100, 200 and null")]
    public void Distinct_AfterLeftOuterJoin_KeepsOneNull()
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetColumn<long>(kustoDatabase, "L | join kind=leftouter R on K | distinct K1", "K1"),
            Is.EquivalentTo(new long?[] { 1, null }), "distinct K1");
        Assert.That(ExecuteAndGetColumn<long>(kustoDatabase, "L | join kind=leftouter R on K | distinct W", "W"),
            Is.EquivalentTo(new long?[] { 100, 200, null }), "distinct W");
    }

    [Test]
    [Description("Joining every row with the statistics of its group: the null key rows 2 and 4 get the count of the " +
                 "null group (2), because the null key of the summarize result matches them")]
    public void Join_WithTheGroupsOfASummarizeByANullableKey_MatchesTheNullGroup()
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(
            ExecuteAndGetValuesById<long>(kustoDatabase,
                "L | join kind=leftouter (L | summarize c = count() by K) on K", "c"),
            Is.EqualTo(ExpectedById<long>(1, 2, 1, 2)));
    }

    #endregion

    #region null keys: join versus where, in and !in

    [TestCase("K == K1", new[] { 1 })]
    [TestCase("K != K1", new int[] { })]
    [Description("join matched the rows 2 and 4 by their null keys, but K == K1 and K != K1 are null for them " +
                 "(null == null is null), so where drops them")]
    public void Where_ComparingTheKeyColumnsAfterJoin_DropsTheRowsMatchedByNullKeys(string predicate,
        int[] expectedIds)
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetIds(kustoDatabase, $"L | join kind=inner R on K | where {predicate}"),
            Is.EquivalentTo(expectedIds));
    }

    [TestCase("L | where K in (R | project K)", new[] { 1 })]
    [TestCase("L | where K !in (R | project K)", new[] { 2, 3, 4 })]
    [Description("in never matches a null K, even when the subquery returns a null, so it is not a leftsemi join " +
                 "(which gives the rows 1, 2 and 4); !in keeps the null K rows, so it is not a leftanti join " +
                 "(which gives only the row 3)")]
    public void InAndNotIn_WithASubquery_DifferFromSemiAndAntiJoinsOnTheNullKeys(string query, int[] expectedIds)
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetIds(kustoDatabase, query), Is.EquivalentTo(expectedIds));
    }

    [TestCase("L | join kind=inner (R | where isnotnull(K)) on K")]
    [TestCase("L | where isnotnull(K) | join kind=inner R on K")]
    [Description("Filtering out the null keys of either side before the join keeps the null keys from matching")]
    public void Join_AfterFilteringOutTheNullKeys_MatchesOnlyTheValues(string query)
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(ExecuteAndGetIds(kustoDatabase, query), Is.EquivalentTo(new[] { 1 }));
    }

    [Test]
    [Description("With the right null key filtered out, the left null key rows 2 and 4 stay unmatched in a leftouter " +
                 "join and get a null W, like the row 3")]
    public void Join_LeftOuterWithTheRightNullKeysFilteredOut_LeavesTheNullKeyRowsUnmatched()
    {
        var kustoDatabase = CreateLAndRDatabase();

        Assert.That(
            ExecuteAndGetValuesById<long>(kustoDatabase,
                "L | join kind=leftouter (R | where isnotnull(K)) on K", "W"),
            Is.EqualTo(ExpectedById<long>(100, null, null, null)));
    }

    #endregion
}
