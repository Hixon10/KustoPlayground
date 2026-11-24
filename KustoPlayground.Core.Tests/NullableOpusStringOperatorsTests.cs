using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Null semantics of the string operators (contains, !contains, startswith, !startswith, endswith, !endswith,
/// matches regex, ==, =~, !=, !~) and of the string functions.
/// Azure Data Explorer has no null strings: a missing string is the empty string "", a string comparison is never
/// null, and isnull() of a string is always false (https://learn.microsoft.com/en-us/kusto/query/scalar-data-types/null-values).
/// The playground can hold a null in a string column (the CSV and JSON loaders turn an empty cell into null), so the
/// string operators and functions must treat such a null exactly like "": S == '' is true, S contains 'a' is false,
/// strlen(S) is 0, toupper(S) is "". How summarize groups and aggregates null strings is covered by the summarize
/// tests and is not repeated here.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net), with "" for the null cell.
/// </summary>
public class NullableOpusStringOperatorsTests
{
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

    // StringPairs(Id: int, L: string?, R: string?) =
    // 1: ("x", "x"), 2: ("x", ""), 3: ("x", null), 4: ("", null), 5: (null, null), 6: ("", ""), 7: ("", "x"), 8: (null, "x")
    private static Table BuildStringPairsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("StringPairs")
            .WithColumn<int>("Id")
            .WithColumn<string>("L")
            .WithColumn<string>("R")
            .WithRow(1, "x", "x")
            .WithRow(2, "x", "")
            .WithRow(3, "x", null)
            .WithRow(4, "", null)
            .WithRow(5, null, null)
            .WithRow(6, "", "")
            .WithRow(7, "", "x")
            .WithRow(8, null, "x")
            .Build();
    }

    // Texts(Id: int, D: string?, T: string?, N: string?) =
    // 1: ("2020-01-01", "01:00:00", "2"), 2: ("", "", ""), 3: (null, null, null)
    private static Table BuildTextsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Texts")
            .WithColumn<int>("Id")
            .WithColumn<string>("D")
            .WithColumn<string>("T")
            .WithColumn<string>("N")
            .WithRow(1, "2020-01-01", "01:00:00", "2")
            .WithRow(2, "", "", "")
            .WithRow(3, null, null, null)
            .Build();
    }

    #region string operators with a constant

    [TestCase("S contains 'a'", new[] { 1, 2 })]
    [TestCase("S !contains 'a'", new[] { 3, 4 })]
    [TestCase("S startswith 'a'", new[] { 1, 2 })]
    [TestCase("S !startswith 'a'", new[] { 3, 4 })]
    [TestCase("S endswith 'c'", new[] { 1, 2 })]
    [TestCase("S !endswith 'c'", new[] { 3, 4 })]
    [TestCase("S contains ''", new[] { 1, 2, 3, 4 })]
    [TestCase("S !contains ''", new int[] { })]
    [TestCase("S startswith ''", new[] { 1, 2, 3, 4 })]
    [TestCase("S !startswith ''", new int[] { })]
    [TestCase("S endswith ''", new[] { 1, 2, 3, 4 })]
    [TestCase("S !endswith ''", new int[] { })]
    [TestCase("S matches regex '^$'", new[] { 3, 4 })]
    [TestCase("S matches regex '.*'", new[] { 1, 2, 3, 4 })]
    [TestCase("S matches regex '^a'", new[] { 1 })]
    [TestCase("not(S contains 'a')", new[] { 3, 4 })]
    [TestCase("not(S matches regex '^$')", new[] { 1, 2 })]
    [TestCase("S contains 'a' or S startswith ''", new[] { 1, 2, 3, 4 })]
    [TestCase("S !contains 'a' and S !contains 'b'", new[] { 3, 4 })]
    [Description("contains / startswith / endswith / matches regex treat a null string like \"\": it contains only \"\", " +
                 "so the negated operators keep it")]
    public void ContainsStartsWithEndsWithMatchesRegex_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNamesTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Names | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [TestCase("S == ''", new[] { 3, 4 })]
    [TestCase("'' == S", new[] { 3, 4 })]
    [TestCase("S =~ ''", new[] { 3, 4 })]
    [TestCase("S != ''", new[] { 1, 2 })]
    [TestCase("'' != S", new[] { 1, 2 })]
    [TestCase("S !~ ''", new[] { 1, 2 })]
    [TestCase("S == 'abc'", new[] { 1 })]
    [TestCase("S != 'abc'", new[] { 2, 3, 4 })]
    [TestCase("S =~ 'abc'", new[] { 1, 2 })]
    [TestCase("S !~ 'abc'", new[] { 3, 4 })]
    [TestCase("S == S", new[] { 1, 2, 3, 4 })]
    [TestCase("S != S", new int[] { })]
    [TestCase("S =~ S", new[] { 1, 2, 3, 4 })]
    [TestCase("not(S == '')", new[] { 1, 2 })]
    [TestCase("not(S != 'abc')", new[] { 1 })]
    [TestCase("S == '' or S == 'abc'", new[] { 1, 3, 4 })]
    [Description("==, !=, =~ and !~ treat a null string like \"\": it equals '' and itself, and is different from 'abc'")]
    public void EqualityOperators_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNamesTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Names | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [TestCase("S contains 'a'", true, true, false, false)]
    [TestCase("S !contains 'a'", false, false, true, true)]
    [TestCase("S startswith ''", true, true, true, true)]
    [TestCase("S !endswith 'c'", false, false, true, true)]
    [TestCase("S matches regex '^$'", false, false, true, true)]
    [TestCase("S == ''", false, false, true, true)]
    [TestCase("S != 'abc'", false, true, true, true)]
    [TestCase("S =~ 'ABC'", true, true, false, false)]
    [TestCase("S !~ ''", true, true, false, false)]
    [TestCase("S == S", true, true, true, true)]
    [TestCase("S != S", false, false, false, false)]
    [Description("A string operator is never null, not even for a null string cell (row 4), because it is compared as \"\"")]
    public void StringOperators_PerRow_AreNeverNull(string expression, bool? id1, bool? id2, bool? id3, bool? id4)
    {
        var kustoDatabase = CreateDatabase(BuildNamesTable());

        var actual = ExecuteAndGetValuesById<bool>(kustoDatabase, $"Names | extend R = {expression}", "R");

        Assert.That(actual, Is.EqualTo(ExpectedById<bool>(id1, id2, id3, id4)));
    }

    #endregion

    #region string operators with two nullable string columns

    [TestCase("L == R", new[] { 1, 4, 5, 6 })]
    [TestCase("L != R", new[] { 2, 3, 7, 8 })]
    [TestCase("L =~ R", new[] { 1, 4, 5, 6 })]
    [TestCase("L !~ R", new[] { 2, 3, 7, 8 })]
    [TestCase("L contains R", new[] { 1, 2, 3, 4, 5, 6 })]
    [TestCase("L !contains R", new[] { 7, 8 })]
    [TestCase("R contains L", new[] { 1, 4, 5, 6, 7, 8 })]
    [TestCase("L startswith R", new[] { 1, 2, 3, 4, 5, 6 })]
    [TestCase("R endswith L", new[] { 1, 4, 5, 6, 7, 8 })]
    [TestCase("not(L == R)", new[] { 2, 3, 7, 8 })]
    [Description("Two string columns: a null on either side is compared as \"\", so (null, null), (\"\", null) and " +
                 "(\"\", \"\") are equal, and every string contains a null string")]
    public void TwoStringColumns_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildStringPairsTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"StringPairs | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [Test]
    [Description("Two string columns, per row: == and contains are never null, also for (null, null)")]
    public void TwoStringColumns_PerRow_AreNeverNull()
    {
        var kustoDatabase = CreateDatabase(BuildStringPairsTable());
        const string query = "StringPairs | extend Eq = L == R, Contains = L contains R";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Eq"),
            Is.EqualTo(ExpectedById<bool>(true, false, false, true, true, true, false, false)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Contains"),
            Is.EqualTo(ExpectedById<bool>(true, true, true, true, true, true, false, false)));
    }

    #endregion

    #region isnull of a string

    [Test]
    [Description("isnull() of a string is always false and isnotnull() always true, also for a null string cell: " +
                 "Azure Data Explorer has no null strings (use isempty() to find a missing string)")]
    public void IsNullOfString_IsAlwaysFalse()
    {
        var kustoDatabase = CreateDatabase(BuildNamesTable());
        const string query = "Names | extend N = isnull(S), NN = isnotnull(S)";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "N"),
            Is.EqualTo(ExpectedById<bool>(false, false, false, false)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NN"),
            Is.EqualTo(ExpectedById<bool>(true, true, true, true)));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Names | where isnull(S) | project Id"), Is.Empty);
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Names | where isnotnull(S) | project Id"),
            Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
    }

    [Test]
    [Description("A null string cell is not dropped by project / extend, and it stays a missing string " +
                 "(Azure Data Explorer shows it as \"\")")]
    public void NullStringCell_PassesThroughProjectAndExtend()
    {
        var kustoDatabase = CreateDatabase(BuildNamesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Names | project Id, S | extend Copy = S | where Id == 4");

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(GetStringOrNull(rows[0], "S"), Is.Null.Or.Empty);
        Assert.That(GetStringOrNull(rows[0], "Copy"), Is.Null.Or.Empty);
    }

    #endregion

    #region string functions

    [Test]
    [Description("strlen() of a null string cell is 0, like strlen(\"\"), and never null")]
    public void StrLen_OfNullString_IsZero()
    {
        var kustoDatabase = CreateDatabase(BuildNamesTable());

        var actual = ExecuteAndGetNumbersById(kustoDatabase, "Names | extend Len = strlen(S)", "Len");

        Assert.That(actual, Is.EqualTo(ExpectedById<double>(3, 3, 0, 0)));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Names | where strlen(S) == 0 | project Id"),
            Is.EquivalentTo(new[] { 3, 4 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Names | where strlen(S) > 0 | project Id"),
            Is.EquivalentTo(new[] { 1, 2 }));
    }

    [TestCase("toupper(S)", "ABC", "ABC", "", "")]
    [TestCase("tolower(S)", "abc", "abc", "", "")]
    [TestCase("url_encode(S)", "abc", "ABC", "", "")]
    [TestCase("url_decode(S)", "abc", "ABC", "", "")]
    [TestCase("base64_encode_tostring(S)", "YWJj", "QUJD", "", "")]
    [TestCase("base64_decode_tostring(base64_encode_tostring(S))", "abc", "ABC", "", "")]
    [TestCase("tostring(S)", "abc", "ABC", "", "")]
    [TestCase("toupper(tolower(S))", "ABC", "ABC", "", "")]
    [Description("A string function returns \"\" (not null) for a null string cell, like for \"\"")]
    public void StringFunctions_OfNullString_ReturnEmptyString(string expression,
        string id1, string id2, string id3, string id4)
    {
        var kustoDatabase = CreateDatabase(BuildNamesTable());

        var actual = ExecuteAndGetStringsById(kustoDatabase, $"Names | extend R = {expression}", "R");

        Assert.That(actual, Is.EqualTo(ExpectedStringsById(id1, id2, id3, id4)));
    }

    [Test]
    [Description("A string function result can be compared and filtered like any string: toupper(S) == '' " +
                 "matches the empty and the null string")]
    public void StringFunctions_OfNullString_InWhere()
    {
        var kustoDatabase = CreateDatabase(BuildNamesTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, "Names | where toupper(S) == '' | project Id"),
            Is.EquivalentTo(new[] { 3, 4 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Names | where tolower(S) == 'abc' | project Id"),
            Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Names | where base64_encode_tostring(S) != '' | project Id"),
            Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    [Description("todatetime(), totimespan() and toreal() of \"\" or of a null string are null (not an error)")]
    public void Conversions_OfEmptyAndNullString_AreNull()
    {
        var kustoDatabase = CreateDatabase(BuildTextsTable());

        Assert.That(ExecuteAndGetValuesById<DateTime>(kustoDatabase, "Texts | extend R = todatetime(D)", "R"),
            Is.EqualTo(ExpectedById<DateTime>(new DateTime(2020, 1, 1), null, null)), "todatetime");
        Assert.That(ExecuteAndGetValuesById<TimeSpan>(kustoDatabase, "Texts | extend R = totimespan(T)", "R"),
            Is.EqualTo(ExpectedById<TimeSpan>(TimeSpan.FromHours(1), null, null)), "totimespan");
        Assert.That(ExecuteAndGetValuesById<double>(kustoDatabase, "Texts | extend R = toreal(N)", "R"),
            Is.EqualTo(ExpectedById<double>(2.0, null, null)), "toreal");
        Assert.That(ExecuteAndGetValuesById<double>(kustoDatabase, "Texts | extend R = todouble(N)", "R"),
            Is.EqualTo(ExpectedById<double>(2.0, null, null)), "todouble");
    }

    [Test]
    [Description("A null conversion result behaves like any null in 'where': isnull() finds it, comparisons drop it")]
    public void Conversions_OfEmptyAndNullString_InWhere()
    {
        var kustoDatabase = CreateDatabase(BuildTextsTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, "Texts | where isnull(todatetime(D)) | project Id"),
            Is.EquivalentTo(new[] { 2, 3 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase,
                "Texts | where todatetime(D) < datetime(2030-01-01) | project Id"),
            Is.EquivalentTo(new[] { 1 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Texts | where totimespan(T) != 1h | project Id"),
            Is.EquivalentTo(new[] { 2, 3 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Texts | where toreal(N) > 0 | project Id"),
            Is.EquivalentTo(new[] { 1 }));
    }

    [Test]
    [Description("Typed nulls converted to string: tostring() of a null of any type is \"\", " +
                 "so strlen() of it is 0 and string functions return \"\"")]
    public void StringFunctions_OfTypedNullLiterals()
    {
        var kustoDatabase = CreateDatabase();

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "print Len = strlen(tostring(long(null))), Upper = toupper(tostring(int(null))), " +
            "Encoded = url_encode(tostring(datetime(null))), Base64 = base64_encode_tostring(tostring(real(null))), " +
            "IsEmptyText = tostring(bool(null)) == ''");

        Assert.That(GetNumber(row, "Len"), Is.EqualTo(0));
        Assert.That(GetValue<string>(row, "Upper"), Is.Empty);
        Assert.That(GetValue<string>(row, "Encoded"), Is.Empty);
        Assert.That(GetValue<string>(row, "Base64"), Is.Empty);
        Assert.That(GetValue<bool>(row, "IsEmptyText"), Is.True);
    }

    #endregion
}
