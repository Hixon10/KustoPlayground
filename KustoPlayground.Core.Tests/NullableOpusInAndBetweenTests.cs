using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Null semantics of the scalar operators in, in~, !in, !in~, between and !between, as in Azure Data Explorer:
/// - a null value is not 'in' a list of non-null values: X in (1, 5) is false and X !in (1, 5) is true for a null X,
///   so 'where X !in (...)' keeps the null rows;
/// - when the list itself contains a null, both 'in' and '!in' are null for a null X
///   (X in (1, long(null)) is true for 1, false for 3 and null for a null X);
/// - a null X is never found in the result of a subquery, even when the subquery returns nulls, so a semi / anti join
///   written with in / !in differs from the leftsemi / leftanti join, which matches a null key to a null key
///   (see <see cref="NullableOpusJoinTests"/>);
/// - X between (low .. high) is (X &gt;= low and X &lt;= high) and X !between (low .. high) is (X &lt; low or X &gt; high),
///   with the three-valued logic: both are null for a null X, so 'where' drops a null X for both of them (unlike !in),
///   and a null bound gives null unless the other bound decides the result
///   (5 between (long(null) .. 3) is false and 5 !between (long(null) .. 3) is true);
/// - a null string cell is compared as "" (see <see cref="NullableOpusStringOperatorsTests"/>).
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class NullableOpusInAndBetweenTests
{
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

    // Allowed(X: long?) = 1, null
    private static Table BuildAllowedTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Allowed")
            .WithColumn<long>("X")
            .WithRow(1L)
            .WithRow(null)
            .Build();
    }

    // OnlyNulls(X: long?) = null, null
    private static Table BuildOnlyNullsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("OnlyNulls")
            .WithColumn<long>("X")
            .WithRow(null)
            .WithRow(null)
            .Build();
    }

    // Names(Id: int, S: string?) = 1: "abc", 2: "ABC", 3: "", 4: null
    private static Table BuildNamesTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Names")
            .WithColumn<int>("Id")
            .WithColumn<string>("S")
            .WithRow(1, "abc")
            .WithRow(2, "ABC")
            .WithRow(3, "")
            .WithRow(4, null)
            .Build();
    }

    // Ranges(Id: int, X: long?, Lo: long?, Hi: long?) =
    // 1: (1, 1, 3), 2: (3, null, 3), 3: (5, 3, null), 4: (null, 1, 3), 5: (5, null, 3), 6: (2, 3, null)
    private static Table BuildRangesTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Ranges")
            .WithColumn<int>("Id")
            .WithColumn<long>("X")
            .WithColumn<long>("Lo")
            .WithColumn<long>("Hi")
            .WithRow(1, 1L, 1L, 3L)
            .WithRow(2, 3L, null, 3L)
            .WithRow(3, 5L, 3L, null)
            .WithRow(4, null, 1L, 3L)
            .WithRow(5, 5L, null, 3L)
            .WithRow(6, 2L, 3L, null)
            .Build();
    }

    // Typed(Id: int, V: T?) = 1: low, 2: high, 3: null
    private static KustoDatabase CreateTypedDatabase<T>(T low, T high)
        where T : notnull
    {
        return CreateDatabase(new SummarizeOperatorOpusTableBuilder("Typed")
            .WithColumn<int>("Id")
            .WithColumn<T>("V")
            .WithRow(1, low)
            .WithRow(2, high)
            .WithRow(3, null)
            .Build());
    }

    // On Typed(1: low, 2: high, 3: null): the null V is not in a list without nulls, and in / !in are null for it
    // when the list contains a null
    private static void AssertInWithNullValue(KustoDatabase kustoDatabase, string lowLiteral, string nullLiteral)
    {
        string query = $"Typed | extend In = V in ({lowLiteral}), NotIn = V !in ({lowLiteral}), " +
                       $"InWithNull = V in ({lowLiteral}, {nullLiteral}), NotInWithNull = V !in ({lowLiteral}, {nullLiteral})";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "In"),
            Is.EqualTo(ExpectedById<bool>(true, false, false)), "V in (low)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NotIn"),
            Is.EqualTo(ExpectedById<bool>(false, true, true)), "V !in (low)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "InWithNull"),
            Is.EqualTo(ExpectedById<bool>(true, false, null)), "V in (low, null)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NotInWithNull"),
            Is.EqualTo(ExpectedById<bool>(false, true, null)), "V !in (low, null)");
        Assert.That(ExecuteAndGetIds(kustoDatabase, $"Typed | where V in ({lowLiteral}) | project Id"),
            Is.EquivalentTo(new[] { 1 }), "where V in (low)");
        Assert.That(ExecuteAndGetIds(kustoDatabase, $"Typed | where V !in ({lowLiteral}) | project Id"),
            Is.EquivalentTo(new[] { 2, 3 }), "where V !in (low) keeps the null row");
        Assert.That(ExecuteAndGetIds(kustoDatabase,
                $"Typed | where V !in ({lowLiteral}, {nullLiteral}) | project Id"),
            Is.EquivalentTo(new[] { 2 }), "where V !in (low, null) drops the null row");
    }

    // On Typed(1: low, 2: high, 3: null): between / !between are null for the null V, and a null bound gives null
    // unless the other bound decides the result
    private static void AssertBetweenWithNulls(KustoDatabase kustoDatabase, string lowLiteral, string nullLiteral)
    {
        string query = $"Typed | extend B = V between ({lowLiteral} .. {lowLiteral}), " +
                       $"NB = V !between ({lowLiteral} .. {lowLiteral}), " +
                       $"BNullLow = V between ({nullLiteral} .. {lowLiteral}), " +
                       $"NBNullLow = V !between ({nullLiteral} .. {lowLiteral}), " +
                       $"BNullHigh = V between ({lowLiteral} .. {nullLiteral}), " +
                       $"NBNullHigh = V !between ({lowLiteral} .. {nullLiteral})";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "B"),
            Is.EqualTo(ExpectedById<bool>(true, false, null)), "V between (low .. low)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NB"),
            Is.EqualTo(ExpectedById<bool>(false, true, null)), "V !between (low .. low)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "BNullLow"),
            Is.EqualTo(ExpectedById<bool>(null, false, null)), "V between (null .. low)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NBNullLow"),
            Is.EqualTo(ExpectedById<bool>(null, true, null)), "V !between (null .. low)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "BNullHigh"),
            Is.EqualTo(ExpectedById<bool>(null, null, null)), "V between (low .. null)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NBNullHigh"),
            Is.EqualTo(ExpectedById<bool>(null, null, null)), "V !between (low .. null)");
        Assert.That(ExecuteAndGetIds(kustoDatabase,
                $"Typed | where V between ({lowLiteral} .. {lowLiteral}) | project Id"),
            Is.EquivalentTo(new[] { 1 }), "where V between (low .. low)");
        Assert.That(ExecuteAndGetIds(kustoDatabase,
                $"Typed | where V !between ({lowLiteral} .. {lowLiteral}) | project Id"),
            Is.EquivalentTo(new[] { 2 }), "where V !between (low .. low) drops the null row");
        Assert.That(ExecuteAndGetIds(kustoDatabase,
                $"Typed | where V !between ({nullLiteral} .. {lowLiteral}) | project Id"),
            Is.EquivalentTo(new[] { 2 }), "where V !between (null .. low)");
    }

    #region in / !in with a list of constants

    [TestCase("X in (1, 5)", new[] { 1, 3 })]
    [TestCase("X !in (1, 5)", new[] { 2, 4, 5 })]
    [TestCase("X in (2, 4)", new int[] { })]
    [TestCase("X !in (2, 4)", new[] { 1, 2, 3, 4, 5 })]
    [TestCase("X in (long(null))", new int[] { })]
    [TestCase("X !in (long(null))", new[] { 1, 2, 3 })]
    [TestCase("X in (1, long(null))", new[] { 1 })]
    [TestCase("X !in (1, long(null))", new[] { 2, 3 })]
    [TestCase("not(X in (1, 5))", new[] { 2, 4, 5 })]
    [TestCase("not(X !in (1, 5))", new[] { 1, 3 })]
    [TestCase("not(X in (1, long(null)))", new[] { 2, 3 })]
    [TestCase("X in (1, 5) or isnull(X)", new[] { 1, 3, 4, 5 })]
    [TestCase("X !in (1, 5) and isnotnull(X)", new[] { 2 })]
    [Description("A null X is not in a list of values, so !in keeps it; when the list contains a null, " +
                 "in and !in are null for a null X and 'where' drops it")]
    public void InList_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Numbers | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [TestCase("X in (1, 5)", true, false, true, false, false)]
    [TestCase("X !in (1, 5)", false, true, false, true, true)]
    [TestCase("X in (long(null))", false, false, false, null, null)]
    [TestCase("X !in (long(null))", true, true, true, null, null)]
    [TestCase("X in (1, long(null))", true, false, false, null, null)]
    [TestCase("X !in (1, long(null))", false, true, true, null, null)]
    [Description("in / !in per row: false / true for a null X, and null for a null X only when the list contains a null")]
    public void InList_PerRow(string expression, bool? id1, bool? id2, bool? id3, bool? id4, bool? id5)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetValuesById<bool>(kustoDatabase, $"Numbers | extend R = {expression}", "R");

        Assert.That(actual, Is.EqualTo(ExpectedById<bool>(id1, id2, id3, id4, id5)));
    }

    [Test]
    [Description("in / !in on a column computed with null operands (X * 2 is null for a null X)")]
    public void InList_OnComputedColumnWithNulls()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, "Numbers | extend Y = X * 2 | where Y in (2, 6) | project Id"),
            Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Numbers | extend Y = X * 2 | where Y !in (2, 6) | project Id"),
            Is.EquivalentTo(new[] { 3, 4, 5 }));
    }

    [Test]
    [Description("in / !in inside countif(): a null X matches X !in (1, 5), so the null rows are counted")]
    public void InList_InsideCountIf()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | summarize InCount = countif(X in (1, 5)), NotInCount = countif(X !in (1, 5)), " +
            "NotInWithNullCount = countif(X !in (1, long(null)))");

        Assert.That(GetValue<long>(row, "InCount"), Is.EqualTo(2L));
        Assert.That(GetValue<long>(row, "NotInCount"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(row, "NotInWithNullCount"), Is.EqualTo(2L));
    }

    #endregion

    #region in / !in with a subquery (semi join / anti join)

    [TestCase("X in (Allowed | project X)", new[] { 1 })]
    [TestCase("X !in (Allowed | project X)", new[] { 2, 3, 4, 5 })]
    [TestCase("X in (OnlyNulls | project X)", new int[] { })]
    [TestCase("X !in (OnlyNulls | project X)", new[] { 1, 2, 3, 4, 5 })]
    [TestCase("X in (Numbers | where Id > 2 | project X)", new[] { 3 })]
    [TestCase("X !in (Numbers | where Id > 2 | project X)", new[] { 1, 2, 4, 5 })]
    [TestCase("X in (Numbers | where isnull(X) | project X)", new int[] { })]
    [TestCase("X !in (Numbers | where isnull(X) | project X)", new[] { 1, 2, 3, 4, 5 })]
    [TestCase("X in (Numbers | where X > 100 | project X)", new int[] { })]
    [TestCase("X !in (Numbers | where X > 100 | project X)", new[] { 1, 2, 3, 4, 5 })]
    [Description("A null X is never found in a subquery result, even when the subquery returns nulls: " +
                 "in is false and !in is true for it (unlike a list of constants that contains a null)")]
    public void InSubquery_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable(), BuildAllowedTable(), BuildOnlyNullsTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Numbers | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [Test]
    [Description("in / !in with a subquery that returns a null are never null, also for a null X")]
    public void InSubquery_PerRow_IsNeverNull()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable(), BuildAllowedTable());
        const string query = "Numbers | extend In = X in (Allowed | project X), NotIn = X !in (Allowed | project X)";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "In"),
            Is.EqualTo(ExpectedById<bool>(true, false, false, false, false)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NotIn"),
            Is.EqualTo(ExpectedById<bool>(false, true, true, true, true)));
    }

    #endregion

    #region in / !in for every type

    [Test]
    [Description("int: a null value is not in (1); in / !in are null for it when the list contains int(null)")]
    public void InList_Int()
    {
        AssertInWithNullValue(CreateTypedDatabase(1, 2), "1", "int(null)");
    }

    [Test]
    [Description("real: a null value is not in (1.5); in / !in are null for it when the list contains real(null)")]
    public void InList_Real()
    {
        AssertInWithNullValue(CreateTypedDatabase(1.5, 2.5), "1.5", "real(null)");
    }

    [Test]
    [Description("datetime: a null value is not in (datetime(2020-01-01)); in / !in are null for it when the list " +
                 "contains datetime(null)")]
    public void InList_DateTime()
    {
        AssertInWithNullValue(
            CreateTypedDatabase(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
            "datetime(2020-01-01)", "datetime(null)");
    }

    [Test]
    [Description("timespan: a null value is not in (1h); in / !in are null for it when the list contains timespan(null)")]
    public void InList_TimeSpan()
    {
        AssertInWithNullValue(CreateTypedDatabase(TimeSpan.FromHours(1), TimeSpan.FromHours(2)), "1h",
            "timespan(null)");
    }

    [Test]
    [Description("bool: a null value is not in (true); in / !in are null for it when the list contains bool(null)")]
    public void InList_Bool()
    {
        AssertInWithNullValue(CreateTypedDatabase(true, false), "true", "bool(null)");
    }

    [Test]
    [Description("guid: a null value is not in (guid(...1)); in / !in are null for it when the list contains guid(null)")]
    public void InList_Guid()
    {
        AssertInWithNullValue(
            CreateTypedDatabase(new Guid("00000000-0000-0000-0000-000000000001"),
                new Guid("00000000-0000-0000-0000-000000000002")),
            "guid(00000000-0000-0000-0000-000000000001)", "guid(null)");
    }

    #endregion

    #region in / in~ / !in / !in~ with strings

    [TestCase("S in ('abc', '')", new[] { 1, 3, 4 })]
    [TestCase("S !in ('abc', '')", new[] { 2 })]
    [TestCase("S in ('ABC')", new[] { 2 })]
    [TestCase("S !in ('ABC')", new[] { 1, 3, 4 })]
    [TestCase("S in~ ('ABC')", new[] { 1, 2 })]
    [TestCase("S !in~ ('ABC')", new[] { 3, 4 })]
    [TestCase("S in ('')", new[] { 3, 4 })]
    [TestCase("S in~ ('')", new[] { 3, 4 })]
    [TestCase("S !in~ ('')", new[] { 1, 2 })]
    [TestCase("S in (Names | where Id > 2 | project S)", new[] { 3, 4 })]
    [TestCase("S !in (Names | where Id > 2 | project S)", new[] { 1, 2 })]
    [TestCase("S in~ (Names | where Id == 1 | project S)", new[] { 1, 2 })]
    [TestCase("S !in~ (Names | where Id == 1 | project S)", new[] { 3, 4 })]
    [Description("A null string cell is compared as \"\": it is in ('') and in a subquery that returns \"\" or a null string")]
    public void StringIn_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNamesTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Names | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [Test]
    [Description("String in / !in are never null, also for a null string cell")]
    public void StringIn_PerRow_IsNeverNull()
    {
        var kustoDatabase = CreateDatabase(BuildNamesTable());
        const string query = "Names | extend In = S in ('abc'), NotInCi = S !in~ ('abc')";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "In"),
            Is.EqualTo(ExpectedById<bool>(true, false, false, false)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NotInCi"),
            Is.EqualTo(ExpectedById<bool>(false, false, true, true)));
    }

    #endregion

    #region between / !between

    [TestCase("X between (1 .. 3)", new[] { 1, 2 })]
    [TestCase("X !between (1 .. 3)", new[] { 3 })]
    [TestCase("not(X between (1 .. 3))", new[] { 3 })]
    [TestCase("X between (1 .. 3) or isnull(X)", new[] { 1, 2, 4, 5 })]
    [TestCase("X !between (1 .. 3) or isnull(X)", new[] { 3, 4, 5 })]
    [TestCase("X between (long(null) .. 3)", new int[] { })]
    [TestCase("X !between (long(null) .. 3)", new[] { 3 })]
    [TestCase("X between (1 .. long(null))", new int[] { })]
    [TestCase("X !between (1 .. long(null))", new int[] { })]
    [TestCase("X between (long(null) .. 0)", new int[] { })]
    [TestCase("X !between (long(null) .. 0)", new[] { 1, 2, 3 })]
    [TestCase("X between (4 .. long(null))", new int[] { })]
    [TestCase("X !between (4 .. long(null))", new[] { 1, 2 })]
    [TestCase("X between (long(null) .. long(null))", new int[] { })]
    [TestCase("X !between (long(null) .. long(null))", new int[] { })]
    [Description("between / !between drop a null X; with a null bound only the other bound can decide the result: " +
                 "5 !between (null .. 3) is true because 5 > 3")]
    public void Between_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Numbers | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [TestCase("X between (1 .. 3)", true, true, false, null, null)]
    [TestCase("X !between (1 .. 3)", false, false, true, null, null)]
    [TestCase("X between (long(null) .. 3)", null, null, false, null, null)]
    [TestCase("X !between (long(null) .. 3)", null, null, true, null, null)]
    [TestCase("X between (4 .. long(null))", false, false, null, null, null)]
    [TestCase("X !between (4 .. long(null))", true, true, null, null, null)]
    [TestCase("X between (long(null) .. long(null))", null, null, null, null, null)]
    [Description("between / !between per row: (X >= low and X <= high) / (X < low or X > high) " +
                 "with the three-valued logic")]
    public void Between_PerRow(string expression, bool? id1, bool? id2, bool? id3, bool? id4, bool? id5)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetValuesById<bool>(kustoDatabase, $"Numbers | extend R = {expression}", "R");

        Assert.That(actual, Is.EqualTo(ExpectedById<bool>(id1, id2, id3, id4, id5)));
    }

    [Test]
    [Description("between with nullable columns as bounds: a null bound gives null unless the other bound decides")]
    public void Between_ColumnBoundsWithNulls()
    {
        var kustoDatabase = CreateDatabase(BuildRangesTable());
        const string query = "Ranges | extend B = X between (Lo .. Hi), NB = X !between (Lo .. Hi)";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "B"),
            Is.EqualTo(ExpectedById<bool>(true, null, null, null, false, false)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NB"),
            Is.EqualTo(ExpectedById<bool>(false, null, null, null, true, true)));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Ranges | where X between (Lo .. Hi) | project Id"),
            Is.EquivalentTo(new[] { 1 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Ranges | where X !between (Lo .. Hi) | project Id"),
            Is.EquivalentTo(new[] { 5, 6 }));
    }

    [Test]
    [Description("int: between / !between are null for a null value and with a null bound")]
    public void Between_Int()
    {
        AssertBetweenWithNulls(CreateTypedDatabase(1, 2), "1", "int(null)");
    }

    [Test]
    [Description("real: between / !between are null for a null value and with a null bound")]
    public void Between_Real()
    {
        AssertBetweenWithNulls(CreateTypedDatabase(1.5, 2.5), "1.5", "real(null)");
    }

    [Test]
    [Description("datetime: between / !between are null for a null value and with a null bound")]
    public void Between_DateTime()
    {
        AssertBetweenWithNulls(
            CreateTypedDatabase(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
            "datetime(2020-01-01)", "datetime(null)");
    }

    [Test]
    [Description("timespan: between / !between are null for a null value and with a null bound")]
    public void Between_TimeSpan()
    {
        AssertBetweenWithNulls(CreateTypedDatabase(TimeSpan.FromHours(1), TimeSpan.FromHours(2)), "1h",
            "timespan(null)");
    }

    [Test]
    [Description("datetime between (start .. timespan) is null for a null datetime")]
    public void Between_DateTimeWithTimeSpanRange()
    {
        var kustoDatabase = CreateTypedDatabase(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        const string query = "Typed | extend B = V between (datetime(2020-01-01) .. 1d), " +
                             "NB = V !between (datetime(2020-01-01) .. 1d)";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "B"),
            Is.EqualTo(ExpectedById<bool>(true, false, null)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NB"),
            Is.EqualTo(ExpectedById<bool>(false, true, null)));
        Assert.That(ExecuteAndGetIds(kustoDatabase,
                "Typed | where V !between (datetime(2020-01-01) .. 1d) | project Id"),
            Is.EquivalentTo(new[] { 2 }));
    }

    [Test]
    [Description("The difference between !in and !between for a null X: X !in (1, 3) keeps the null rows " +
                 "(a null is not in the list), X !between (1 .. 3) drops them (it is null)")]
    public void NotIn_KeepsNullRows_NotBetween_DropsThem()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, "Numbers | where X !in (1, 3) | project Id"),
            Is.EquivalentTo(new[] { 3, 4, 5 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Numbers | where X !between (1 .. 3) | project Id"),
            Is.EquivalentTo(new[] { 3 }));

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "Numbers | summarize NotIn = countif(X !in (1, 3)), NotBetween = countif(X !between (1 .. 3))");
        Assert.That(GetValue<long>(row, "NotIn"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(row, "NotBetween"), Is.EqualTo(1L));
    }

    #endregion
}
