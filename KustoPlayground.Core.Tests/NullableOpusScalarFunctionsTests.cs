using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Scalar functions over nullable columns of every type, as in Azure Data Explorer:
/// - isnull() is true only for a null: 0, false, 0.0, 00:00:00 and the empty guid are values;
/// - tostring() of a null is "" (empty, but not null);
/// - toreal() / todouble() / todatetime() / totimespan(), bin() / bin_at(), make_timespan(), ago() and now()
///   of a null (or with a null argument) are null;
/// - pack_array() / bag_pack() keep a null as a JSON null, and parse_json() of "" is null.
/// The last region tests functions that the README does not list yet (isempty, isnotempty, coalesce, iff, case),
/// because they are the usual way to handle nulls in KQL.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class NullableOpusScalarFunctionsTests
{
    private static readonly Guid Guid1 = new("00000000-0000-0000-0000-000000000001");
    private static readonly DateTime Day1 = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2At10 = new(2020, 1, 2, 10, 0, 0, DateTimeKind.Utc);

    // AllTypes(Id: int, I: int?, L: long?, R: real?, Dc: decimal?, B: bool?, D: datetime?, T: timespan?, G: guid?) =
    // 1: (1, 7, 1.5, 2.5, true, 2020-01-02 10:00, 01:30:00, 00000000-0000-0000-0000-000000000001)
    // 2: all null
    // 3: (0, 0, 0.0, 0, false, 2020-01-01, 00:00:00, 00000000-0000-0000-0000-000000000000)
    private static Table BuildAllTypesTable()
    {
        return new SummarizeOperatorOpusTableBuilder("AllTypes")
            .WithColumn<int>("Id")
            .WithColumn<int>("I")
            .WithColumn<long>("L")
            .WithColumn<double>("R")
            .WithColumn<decimal>("Dc")
            .WithColumn<bool>("B")
            .WithColumn<DateTime>("D")
            .WithColumn<TimeSpan>("T")
            .WithColumn<Guid>("G")
            .WithRow(1, 1, 7L, 1.5, 2.5m, true, Day2At10, TimeSpan.FromMinutes(90), Guid1)
            .WithRow(2, null, null, null, null, null, null, null, null)
            .WithRow(3, 0, 0L, 0.0, 0m, false, Day1, TimeSpan.Zero, Guid.Empty)
            .Build();
    }

    // Bins(Id: int, L: long?, R: real?, D: datetime?, T: timespan?) = 1: (7, 0.7, 2020-01-02 10:00, 01:30:00), 2: all null
    private static Table BuildBinsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Bins")
            .WithColumn<int>("Id")
            .WithColumn<long>("L")
            .WithColumn<double>("R")
            .WithColumn<DateTime>("D")
            .WithColumn<TimeSpan>("T")
            .WithRow(1, 7L, 0.7, Day2At10, TimeSpan.FromMinutes(90))
            .WithRow(2, null, null, null, null)
            .Build();
    }

    // Clock(Id: int, H: int?, M: int?) = 1: (7, 30), 2: (null, 30), 3: (7, null), 4: (null, null)
    private static Table BuildClockTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Clock")
            .WithColumn<int>("Id")
            .WithColumn<int>("H")
            .WithColumn<int>("M")
            .WithRow(1, 7, 30)
            .WithRow(2, null, 30)
            .WithRow(3, 7, null)
            .WithRow(4, null, null)
            .Build();
    }

    // Mixed(Id: int, X: long?, S: string?, D: datetime?, B: bool?) =
    // 1: (1, "abc", 2020-01-01, true), 2: all null, 3: (5, "", 2021-01-01, false)
    private static Table BuildMixedTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Mixed")
            .WithColumn<int>("Id")
            .WithColumn<long>("X")
            .WithColumn<string>("S")
            .WithColumn<DateTime>("D")
            .WithColumn<bool>("B")
            .WithRow(1, 1L, "abc", Day1, true)
            .WithRow(2, null, null, null, null)
            .WithRow(3, 5L, "", new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), false)
            .Build();
    }

    #region isnull / isnotnull

    [TestCase("I")]
    [TestCase("L")]
    [TestCase("R")]
    [TestCase("Dc")]
    [TestCase("B")]
    [TestCase("D")]
    [TestCase("T")]
    [TestCase("G")]
    [Description("isnull() / isnotnull() of a nullable column of every type: only row 2 is null; " +
                 "0, 0.0, false, 00:00:00 and the empty guid of row 3 are values")]
    public void IsNullAndIsNotNull_PerType(string columnName)
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());
        string query = $"AllTypes | project Id, IsNull = isnull({columnName}), IsNotNull = isnotnull({columnName})";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "IsNull"),
            Is.EqualTo(ExpectedById<bool>(false, true, false)), "isnull");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "IsNotNull"),
            Is.EqualTo(ExpectedById<bool>(true, false, true)), "isnotnull");
    }

    [TestCase("I")]
    [TestCase("L")]
    [TestCase("R")]
    [TestCase("Dc")]
    [TestCase("B")]
    [TestCase("D")]
    [TestCase("T")]
    [TestCase("G")]
    [Description("where isnull() / isnotnull() of a nullable column of every type")]
    public void IsNullAndIsNotNull_Where_PerType(string columnName)
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, $"AllTypes | where isnull({columnName}) | project Id"),
            Is.EqualTo(new[] { 2 }), "isnull");
        Assert.That(ExecuteAndGetIds(kustoDatabase, $"AllTypes | where isnotnull({columnName}) | project Id"),
            Is.EquivalentTo(new[] { 1, 3 }), "isnotnull");
    }

    #endregion

    #region tostring

    [TestCase("I", "1", "0")]
    [TestCase("L", "7", "0")]
    [TestCase("Dc", "2.5", "0")]
    [TestCase("B", "True", "False")]
    [TestCase("T", "01:30:00", "00:00:00")]
    [TestCase("G", "00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000000")]
    [Description("tostring() of a null is an empty string; zero-like values are converted to their text " +
                 "(Azure Data Explorer writes a bool as True / False)")]
    public void ToString_PerType(string columnName, string textOfId1, string textOfId3)
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());

        var actual = ExecuteAndGetStringsById(kustoDatabase,
            $"AllTypes | project Id, Text = tostring({columnName})", "Text");

        Assert.That(actual, Is.EqualTo(ExpectedStringsById(textOfId1, "", textOfId3)));
    }

    [TestCase("I")]
    [TestCase("L")]
    [TestCase("R")]
    [TestCase("Dc")]
    [TestCase("B")]
    [TestCase("D")]
    [TestCase("T")]
    [TestCase("G")]
    [Description("tostring() of a null is \"\": == '' and strlen() == 0 match it, but isnull() does not, " +
                 "because the result of tostring() is never null")]
    public void ToString_OfNull_IsEmptyButNotNull_Where(string columnName)
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, $"AllTypes | where tostring({columnName}) == '' | project Id"),
            Is.EqualTo(new[] { 2 }), "tostring() == ''");
        Assert.That(
            ExecuteAndGetIds(kustoDatabase, $"AllTypes | where strlen(tostring({columnName})) == 0 | project Id"),
            Is.EqualTo(new[] { 2 }), "strlen(tostring()) == 0");
        Assert.That(ExecuteAndGetIds(kustoDatabase, $"AllTypes | where isnull(tostring({columnName})) | project Id"),
            Is.Empty, "isnull(tostring())");
        Assert.That(
            ExecuteAndGetIds(kustoDatabase, $"AllTypes | where isnotnull(tostring({columnName})) | project Id"),
            Is.EquivalentTo(new[] { 1, 2, 3 }), "isnotnull(tostring())");
    }

    #endregion

    #region conversions

    [TestCase("toreal(I)", 1.0, 0.0)]
    [TestCase("toreal(L)", 7.0, 0.0)]
    [TestCase("todouble(L)", 7.0, 0.0)]
    [TestCase("toreal(R)", 1.5, 0.0)]
    [TestCase("toreal(Dc)", 2.5, 0.0)]
    [TestCase("toreal(B)", 1.0, 0.0)]
    [Description("toreal() / todouble() of a nullable column is a real, and null for a null")]
    public void ToReal_OfNullableColumns(string expression, double valueOfId1, double valueOfId3)
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());

        var actual = ExecuteAndGetValuesById<double>(kustoDatabase, $"AllTypes | project Id, V = {expression}", "V");

        Assert.That(actual, Is.EqualTo(ExpectedById<double>(valueOfId1, null, valueOfId3)));
    }

    [Test]
    [Description("todatetime() of a datetime column and totimespan() of a timespan column return the value itself, " +
                 "and null for a null")]
    public void ToDateTimeAndToTimeSpan_OfTypedColumns()
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());
        const string query = "AllTypes | project Id, ToD = todatetime(D), ToT = totimespan(T)";

        Assert.That(ExecuteAndGetValuesById<DateTime>(kustoDatabase, query, "ToD"),
            Is.EqualTo(ExpectedById<DateTime>(Day2At10, null, Day1)), "todatetime(D)");
        Assert.That(ExecuteAndGetValuesById<TimeSpan>(kustoDatabase, query, "ToT"),
            Is.EqualTo(ExpectedById<TimeSpan>(TimeSpan.FromMinutes(90), null, TimeSpan.Zero)), "totimespan(T)");
    }

    [TestCase("isnull(toreal(I))", new[] { 2 })]
    [TestCase("toreal(L) > 1", new[] { 1 })]
    [TestCase("toreal(L) != 7", new[] { 2, 3 })]
    [TestCase("not(toreal(L) > 1)", new[] { 3 })]
    [TestCase("todatetime(D) > datetime(2020-01-01)", new[] { 1 })]
    [TestCase("todatetime(D) != datetime(2020-01-01)", new[] { 1, 2 })]
    [TestCase("totimespan(T) < 1h", new[] { 3 })]
    [TestCase("isnull(totimespan(T))", new[] { 2 })]
    [Description("A conversion of a null is null, and 'where' applies the null rules to it: " +
                 "an ordering comparison drops the null row, while != keeps it")]
    public void Conversions_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"AllTypes | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    #endregion

    #region ago / now

    [Test]
    [Description("ago() and now() with a null timespan argument are null")]
    public void AgoAndNow_OfNullTimeSpan_AreNull()
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());
        const string query = "AllTypes | project Id, AgoIsNull = isnull(ago(T)), NowIsNull = isnull(now(T))";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "AgoIsNull"),
            Is.EqualTo(ExpectedById<bool>(false, true, false)), "isnull(ago(T))");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NowIsNull"),
            Is.EqualTo(ExpectedById<bool>(false, true, false)), "isnull(now(T))");
    }

    [Test]
    [Description("ago() of a null is neither before nor after now(): the null row is dropped by the comparison " +
                 "and by its negation")]
    public void Ago_OfNullTimeSpan_Where()
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, "AllTypes | where ago(T) <= now() | project Id"),
            Is.EquivalentTo(new[] { 1, 3 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "AllTypes | where not(ago(T) <= now()) | project Id"), Is.Empty);
        Assert.That(ExecuteAndGetIds(kustoDatabase, "AllTypes | where isnull(ago(T)) | project Id"),
            Is.EqualTo(new[] { 2 }));
    }

    #endregion

    #region bin / bin_at

    [Test]
    [Description("bin() / bin_at() of a null value is null for every type")]
    public void Bin_OfNullValue_IsNull()
    {
        var kustoDatabase = CreateDatabase(BuildBinsTable());
        const string query = "Bins | project Id, BL = bin(L, 5), BR = bin(R, 0.5), BD = bin(D, 1d), BT = bin(T, 1h), " +
                             "AL = bin_at(L, 10, 3), AD = bin_at(D, 1d, datetime(2020-01-01 06:00))";

        Assert.That(ExecuteAndGetValuesById<long>(kustoDatabase, query, "BL"),
            Is.EqualTo(ExpectedById<long>(5, null)), "bin(L, 5)");
        Assert.That(ExecuteAndGetValuesById<double>(kustoDatabase, query, "BR"),
            Is.EqualTo(ExpectedById<double>(0.5, null)), "bin(R, 0.5)");
        Assert.That(ExecuteAndGetValuesById<DateTime>(kustoDatabase, query, "BD"),
            Is.EqualTo(ExpectedById<DateTime>(new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc), null)),
            "bin(D, 1d)");
        Assert.That(ExecuteAndGetValuesById<TimeSpan>(kustoDatabase, query, "BT"),
            Is.EqualTo(ExpectedById<TimeSpan>(TimeSpan.FromHours(1), null)), "bin(T, 1h)");
        Assert.That(ExecuteAndGetValuesById<long>(kustoDatabase, query, "AL"),
            Is.EqualTo(ExpectedById<long>(3, null)), "bin_at(L, 10, 3)");
        Assert.That(ExecuteAndGetValuesById<DateTime>(kustoDatabase, query, "AD"),
            Is.EqualTo(ExpectedById<DateTime>(new DateTime(2020, 1, 2, 6, 0, 0, DateTimeKind.Utc), null)),
            "bin_at(D, 1d, datetime(2020-01-01 06:00))");
    }

    [TestCase("bin(L, long(null))")]
    [TestCase("bin(R, real(null))")]
    [TestCase("bin(D, timespan(null))")]
    [TestCase("bin(T, timespan(null))")]
    [Description("bin() with a null bin size is null, also for a non-null value")]
    public void Bin_WithNullBinSize_IsNull(string expression)
    {
        var kustoDatabase = CreateDatabase(BuildBinsTable());

        var actual = ExecuteAndGetValuesById<bool>(kustoDatabase, $"Bins | project Id, IsNull = isnull({expression})",
            "IsNull");

        Assert.That(actual, Is.EqualTo(ExpectedById<bool>(true, true)));
    }

    [TestCase("bin(L, 5) == 5", new[] { 1 })]
    [TestCase("bin(L, 5) != 5", new[] { 2 })]
    [TestCase("isnull(bin(D, 1d))", new[] { 2 })]
    [TestCase("bin(D, 1d) != datetime(2020-01-02)", new[] { 2 })]
    [TestCase("bin(T, 1h) < 2h", new[] { 1 })]
    [Description("bin() of a null in 'where': null != value is true, an ordering comparison drops the row")]
    public void Bin_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildBinsTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Bins | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    #endregion

    #region make_timespan

    [Test]
    [Description("make_timespan() is null when any of its arguments is null")]
    public void MakeTimeSpan_WithNullArgument_IsNull()
    {
        var kustoDatabase = CreateDatabase(BuildClockTable());

        var actual = ExecuteAndGetValuesById<TimeSpan>(kustoDatabase,
            "Clock | project Id, Span = make_timespan(0, H, M, 0)", "Span");

        Assert.That(actual, Is.EqualTo(ExpectedById<TimeSpan>(new TimeSpan(7, 30, 0), null, null, null)));
    }

    [Test]
    [Description("where over make_timespan() with null arguments")]
    public void MakeTimeSpan_WithNullArgument_Where()
    {
        var kustoDatabase = CreateDatabase(BuildClockTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, "Clock | where isnull(make_timespan(0, H, M, 0)) | project Id"),
            Is.EquivalentTo(new[] { 2, 3, 4 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Clock | where make_timespan(0, H, M, 0) > 1h | project Id"),
            Is.EqualTo(new[] { 1 }));
    }

    #endregion

    #region dynamic values

    [Test]
    [Description("pack_array() keeps a null value as a JSON null")]
    public void PackArray_KeepsNullsAsJsonNulls()
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "AllTypes | project Id, A = pack_array(I, B)");
        var actual = rows.ToDictionary(row => GetValue<int>(row, IdColumn), row => GetArrayItemsAsJson(row, "A"));

        Assert.That(actual, Is.EqualTo(new Dictionary<int, List<string>>
        {
            [1] = ["1", "true"],
            [2] = ["null", "null"],
            [3] = ["0", "false"]
        }));
    }

    [Test]
    [Description("bag_pack() keeps a null value as a property with a JSON null value")]
    public void BagPack_KeepsNullsAsJsonNulls()
    {
        var kustoDatabase = CreateDatabase(BuildAllTypesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "AllTypes | project Id, Bag = bag_pack('i', I, 'b', B)");
        var actual = rows.ToDictionary(row => GetValue<int>(row, IdColumn), row => GetBagPropertiesAsJson(row, "Bag"));

        Assert.That(actual, Is.EqualTo(new Dictionary<int, Dictionary<string, string>>
        {
            [1] = new() { ["i"] = "1", ["b"] = "true" },
            [2] = new() { ["i"] = "null", ["b"] = "null" },
            [3] = new() { ["i"] = "0", ["b"] = "false" }
        }));
    }

    [Test]
    [Description("parse_json() of an empty string (and of a null string cell, which is \"\" in Kusto) is null")]
    public void ParseJson_OfEmptyOrNullString_IsNull()
    {
        var kustoDatabase = CreateDatabase(new SummarizeOperatorOpusTableBuilder("Texts")
            .WithColumn<int>("Id")
            .WithColumn<string>("S")
            .WithRow(1, "{\"a\":1}")
            .WithRow(2, "")
            .WithRow(3, null)
            .Build());

        var actual = ExecuteAndGetValuesById<bool>(kustoDatabase,
            "Texts | project Id, IsNull = isnull(parse_json(S))", "IsNull");

        Assert.That(actual, Is.EqualTo(ExpectedById<bool>(false, true, true)));
    }

    #endregion

    #region functions not listed in the README yet (isempty, isnotempty, coalesce, iff, case)

    [Test]
    [Description("isempty() is true for a null of any type and for \"\"; 0 and false are not empty; " +
                 "isnotempty() is its negation")]
    public void IsEmptyAndIsNotEmpty()
    {
        var kustoDatabase = CreateDatabase(BuildMixedTable());
        const string query = "Mixed | project Id, EX = isempty(X), ES = isempty(S), ED = isempty(D), " +
                             "EB = isempty(B), NX = isnotempty(X), NS = isnotempty(S)";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "EX"),
            Is.EqualTo(ExpectedById<bool>(false, true, false)), "isempty(X)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "ES"),
            Is.EqualTo(ExpectedById<bool>(false, true, true)), "isempty(S)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "ED"),
            Is.EqualTo(ExpectedById<bool>(false, true, false)), "isempty(D)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "EB"),
            Is.EqualTo(ExpectedById<bool>(false, true, false)), "isempty(B)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NX"),
            Is.EqualTo(ExpectedById<bool>(true, false, true)), "isnotempty(X)");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NS"),
            Is.EqualTo(ExpectedById<bool>(true, false, false)), "isnotempty(S)");
    }

    [Test]
    [Description("countif(isempty()) counts the nulls of a numeric column, not its zeros")]
    public void IsEmpty_InCountIf_CountsNullsNotZeros()
    {
        var kustoDatabase = CreateDatabase(new SummarizeOperatorOpusTableBuilder("Zeros")
            .WithColumn<long>("Z")
            .WithRow(0L)
            .WithRow(null)
            .WithRow(0L)
            .WithRow(null)
            .Build());

        var row = ExecuteAndGetSingleRow(kustoDatabase, "Zeros | summarize Empty = countif(isempty(Z))");

        Assert.That(GetValue<long>(row, "Empty"), Is.EqualTo(2L));
    }

    [Test]
    [Description("coalesce() returns its first argument that is not null (for strings: not empty)")]
    public void Coalesce_ReplacesNulls()
    {
        var kustoDatabase = CreateDatabase(BuildMixedTable());
        const string query = "Mixed | project Id, CX = coalesce(X, 0), CS = coalesce(S, 'missing'), " +
                             "CD = coalesce(D, datetime(2000-01-01)), CN = coalesce(X, long(null))";

        Assert.That(ExecuteAndGetNumbersById(kustoDatabase, query, "CX"),
            Is.EqualTo(ExpectedById<double>(1, 0, 5)), "coalesce(X, 0)");
        Assert.That(ExecuteAndGetStringsById(kustoDatabase, query, "CS"),
            Is.EqualTo(ExpectedStringsById("abc", "missing", "missing")), "coalesce(S, 'missing')");
        Assert.That(ExecuteAndGetValuesById<DateTime>(kustoDatabase, query, "CD"),
            Is.EqualTo(ExpectedById<DateTime>(Day1, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc))), "coalesce(D, datetime(2000-01-01))");
        Assert.That(ExecuteAndGetNumbersById(kustoDatabase, query, "CN"),
            Is.EqualTo(ExpectedById<double>(1, null, 5)), "coalesce(X, long(null))");
    }

    [Test]
    [Description("iff() takes the 'else' branch when its condition is null")]
    public void Iff_NullConditionTakesElseBranch()
    {
        var kustoDatabase = CreateDatabase(BuildMixedTable());
        const string query = "Mixed | project Id, A = iff(X > 1, X, -X), Y = iff(B, 'yes', 'no'), " +
                             "Z = iff(isnull(X), 0, X)";

        Assert.That(ExecuteAndGetNumbersById(kustoDatabase, query, "A"),
            Is.EqualTo(ExpectedById<double>(-1, null, 5)), "iff(X > 1, X, -X)");
        Assert.That(ExecuteAndGetStringsById(kustoDatabase, query, "Y"),
            Is.EqualTo(ExpectedStringsById("yes", "no", "no")), "iff(B, 'yes', 'no')");
        Assert.That(ExecuteAndGetNumbersById(kustoDatabase, query, "Z"),
            Is.EqualTo(ExpectedById<double>(1, 0, 5)), "iff(isnull(X), 0, X)");

        var row = ExecuteAndGetSingleRow(kustoDatabase, "print R = iff(bool(null), 1, 2)");
        Assert.That(GetNumber(row, "R"), Is.EqualTo(2), "iff(bool(null), 1, 2)");
    }

    [Test]
    [Description("case() skips a branch whose condition is null, so a null value falls through to the 'else' value")]
    public void Case_NullConditionsFallThroughToElse()
    {
        var kustoDatabase = CreateDatabase(BuildMixedTable());

        var actual = ExecuteAndGetStringsById(kustoDatabase,
            "Mixed | project Id, C = case(X > 3, 'high', X > 0, 'mid', 'low')", "C");

        Assert.That(actual, Is.EqualTo(ExpectedStringsById("mid", "low", "high")));
    }

    [TestCase("isempty(X)", new[] { 2 })]
    [TestCase("isempty(S)", new[] { 2, 3 })]
    [TestCase("isnotempty(S)", new[] { 1 })]
    [TestCase("coalesce(X, 0) < 3", new[] { 1, 2 })]
    [TestCase("iff(isnull(B), true, B)", new[] { 1, 2 })]
    [TestCase("case(X > 3, 'high', X > 0, 'mid', 'low') == 'low'", new[] { 2 })]
    [Description("isempty / coalesce / iff / case in 'where' select or replace the null rows explicitly")]
    public void NullHandlingFunctions_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildMixedTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Mixed | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    #endregion
}
