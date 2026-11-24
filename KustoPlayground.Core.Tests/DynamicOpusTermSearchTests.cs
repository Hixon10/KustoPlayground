using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Term search on dynamic values:
/// has - https://learn.microsoft.com/en-us/kusto/query/has-operator,
/// !has - https://learn.microsoft.com/en-us/kusto/query/not-has-operator,
/// has_cs - https://learn.microsoft.com/en-us/kusto/query/has-cs-operator,
/// !has_cs - https://learn.microsoft.com/en-us/kusto/query/not-has-cs-operator,
/// has_any - https://learn.microsoft.com/en-us/kusto/query/has-any-operator
/// and has_all - https://learn.microsoft.com/en-us/kusto/query/has-all-operator
/// (terms - https://learn.microsoft.com/en-us/kusto/query/datatypes-string-operators).
/// <para>
/// A dynamic value has a term when one of its property names or one of its scalar values (strings, numbers, bools,
/// guids, datetimes) contains it, at any depth of nested arrays and property bags. A term is a run of ASCII letters and
/// digits: 'beta-gamma' and 'user_id' have the terms 'beta', 'gamma', 'user' and 'id', but 'Value42' doesn't have
/// 'Value' or '42' and 12345 doesn't have '123'. A needle with several terms ('hello world', 'beta-gamma') must be found
/// with the same delimiters inside one property name or one value ('beta gamma' doesn't match 'beta-gamma'); a match
/// never crosses from a property name to its value ('name hello' doesn't match {"name":"hello world"}) and the JSON text
/// (quotes, colons, the word null) is not searched. has, has_any and has_all are case-insensitive, has_cs and !has_cs
/// are case-sensitive. A null value has no terms (has is false and !has is true).
/// </para>
/// <para>
/// A term search is different from array element membership: dynamic(['beta-gamma']) has 'beta' is true,
/// but 'beta' in (dynamic(['beta-gamma'])) and set_has_element(dynamic(['beta-gamma']), 'beta') are false.
/// </para>
/// Expected values were verified against a real Azure Data Explorer cluster. Real and datetime values are only searched
/// in table rows: the cluster finds their terms in table rows, but not in constant expressions
/// (print dynamic({'r':1.5}) has '1.5' returns false).
/// </summary>
public class DynamicOpusTermSearchTests
{
    private const string SampleLet =
        "let d = dynamic({'name':'hello world','tags':['Alpha','beta-gamma'],'nested':{'deep':'Value42'},'n':12345,'user_id':'x'});";

    // has on a property bag: property names and values at any depth

    [TestCase("d has 'hello'", true, Description = "a term of a string value")]
    [TestCase("d has 'world'", true)]
    [TestCase("d has 'HELLO'", true, Description = "has is case-insensitive")]
    [TestCase("d has 'hello world'", true, Description = "a needle with several terms is found inside one value")]
    [TestCase("d has 'Hello World'", true)]
    [TestCase("d has 'world hello'", false, Description = "the terms of the needle must be in the same order")]
    [TestCase("d has 'name'", true, Description = "property names are searched")]
    [TestCase("d has 'tags'", true, Description = "the name of a property holding an array")]
    [TestCase("d has 'nested'", true, Description = "the name of a property holding a property bag")]
    [TestCase("d has 'deep'", true, Description = "property names of nested bags are searched")]
    [TestCase("d has 'value42'", true, Description = "values of nested bags are searched")]
    [TestCase("d has 'alpha'", true, Description = "array elements are searched")]
    [TestCase("d has 'beta'", true, Description = "'-' separates terms")]
    [TestCase("d has 'gamma'", true)]
    [TestCase("d has 'beta-gamma'", true)]
    [TestCase("d has 'beta gamma'", false, Description = "the delimiters of the needle must match too")]
    [TestCase("d has 'gamma-beta'", false)]
    [TestCase("d has '12345'", true, Description = "numbers are searched")]
    [TestCase("d has '123'", false, Description = "only whole terms match, not prefixes of numbers")]
    [TestCase("d has 'user_id'", true)]
    [TestCase("d has 'user'", true, Description = "'_' separates terms")]
    [TestCase("d has 'id'", true)]
    [TestCase("d has 'x'", true, Description = "a single character term")]
    [TestCase("d has 'hel'", false, Description = "only whole terms match, not prefixes")]
    [TestCase("d has 'Value'", false, Description = "letters followed by digits are one term")]
    [TestCase("d has '42'", false)]
    [TestCase("d has 'name hello'", false, Description = "a match doesn't cross from a property name to its value")]
    [TestCase("d has 'deep Value42'", false)]
    [TestCase("d has 'name\":\"hello'", false, Description = "the JSON text of the value is not searched")]
    [TestCase("d has 'zzz'", false)]
    [Description("has - finds whole terms in the property names and values of a dynamic value at any depth")]
    public void Has_NestedBag_MatchesWholeTermsOfPropertyNamesAndValues(string expression, bool expected)
    {
        OpusTestUtils.AssertTypedValue(EvaluateWithLets(SampleLet, expression), expected);
    }

    [TestCase("d has_cs 'hello'", true)]
    [TestCase("d has_cs 'Alpha'", true)]
    [TestCase("d has_cs 'alpha'", false, Description = "has_cs is case-sensitive")]
    [TestCase("d has_cs 'HELLO'", false)]
    [TestCase("d has_cs 'Hello'", false)]
    [TestCase("d !has_cs 'HELLO'", true)]
    [TestCase("d !has_cs 'hello'", false)]
    [TestCase("d !has 'hello'", false)]
    [TestCase("d !has 'HELLO'", false, Description = "!has is case-insensitive")]
    [TestCase("d !has 'hel'", true, Description = "a prefix is not a term")]
    [TestCase("d !has 'zzz'", true)]
    [Description("has_cs / !has / !has_cs - case-sensitive and negated term search on a dynamic value")]
    public void HasCsAndNegations_NestedBag_ReturnExpectedResult(string expression, bool expected)
    {
        OpusTestUtils.AssertTypedValue(EvaluateWithLets(SampleLet, expression), expected);
    }

    [TestCase("d has_any ('zzz', 'world')", true)]
    [TestCase("d has_any ('zzz', 'wor')", false, Description = "only whole terms match")]
    [TestCase("d has_any ('hel', 'wor', 'alp')", false)]
    [TestCase("d has_any ('HELLO')", true, Description = "has_any is case-insensitive")]
    [TestCase("d has_any ('deep')", true, Description = "property names are searched")]
    [TestCase("d has_any ('12345', 'x')", true)]
    [TestCase("d has_any (dynamic(['zzz', 'alpha']))", true, Description = "the terms can be a dynamic array")]
    [TestCase("d has_all ('hello', 'alpha', 'deep')", true, Description = "every term is found, each one in any property name or value")]
    [TestCase("d has_all ('tags', 'nested', 'n')", true)]
    [TestCase("d has_all ('hello', 'zzz')", false)]
    [TestCase("d has_all (dynamic(['hello', 'beta']))", true, Description = "the terms can be a dynamic array")]
    [Description("has_any / has_all - any / every term of a list is found in the dynamic value")]
    public void HasAnyAndHasAll_NestedBag_MatchAnyOrEveryTerm(string expression, bool expected)
    {
        OpusTestUtils.AssertTypedValue(EvaluateWithLets(SampleLet, expression), expected);
    }

    // has on other kinds of dynamic values

    [TestCase("dynamic(['hello world']) has 'hello'", true, Description = "the terms of an array element")]
    [TestCase("dynamic(['Alpha']) has 'alpha'", true)]
    [TestCase("dynamic([1, 2]) has '1'", true, Description = "a number element")]
    [TestCase("dynamic(12345) has '12345'", true, Description = "a scalar dynamic value")]
    [TestCase("dynamic('plain text') has 'plain'", true, Description = "a string dynamic value")]
    [TestCase("dynamic({'flag':true}) has 'true'", true, Description = "a bool value has the term true or false")]
    [TestCase("dynamic({'flag':false}) has 'false'", true)]
    [TestCase("dynamic({'k':-5}) has '5'", true, Description = "the minus sign is a delimiter")]
    [TestCase("dynamic({'k':-5}) has '-5'", true)]
    [TestCase("dynamic({'k':null}) has 'k'", true, Description = "the name of a property holding null")]
    [TestCase("dynamic({'k':null}) has 'null'", false, Description = "a null value has no terms")]
    [TestCase("dynamic([{'k':'v'}]) has 'k'", true, Description = "property names of bags inside arrays")]
    [TestCase("dynamic({'a':{'b':{'c':['deep-value']}}}) has 'value'", true, Description = "any depth")]
    [TestCase("dynamic({'a.b':1}) has 'a.b'", true, Description = "a property name with a dot")]
    [TestCase("dynamic({'a.b':1}) has 'b'", true)]
    [TestCase("dynamic({'k':'v'}) has 'K'", true, Description = "property names are searched case-insensitively")]
    [TestCase("dynamic({'K':'v'}) has_cs 'k'", false)]
    [TestCase("dynamic({'g':guid(6f9619ff-8b86-d011-b42d-00c04fc964ff)}) has '6f9619ff'", true, Description = "a guid value")]
    [TestCase("parse_json('{\"k\":\"some value\"}') has 'value'", true, Description = "the result of parse_json")]
    [TestCase("dynamic({}) has 'x'", false, Description = "an empty property bag has no terms")]
    [TestCase("dynamic([]) has 'x'", false, Description = "an empty array has no terms")]
    [TestCase("dynamic(null) has 'x'", false, Description = "null has no terms")]
    [TestCase("dynamic(null) !has 'x'", true)]
    [TestCase("dynamic(null) has_any ('x')", false)]
    [TestCase("dynamic({'k':'v'}) has_any (dynamic([]))", false, Description = "an empty list of terms matches nothing")]
    [TestCase("dynamic({'a':'b'}) has ''", true, Description = "the empty needle is found in a value with terms")]
    [TestCase("dynamic(null) has ''", false)]
    [Description("has - searches the terms of every scalar value and property name of arrays, bags and scalar dynamic values")]
    public void Has_DynamicValues_SearchesTermsOfScalarsAndPropertyNames(string expression, bool expected)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    // term search vs element membership

    [TestCase("dynamic(['beta-gamma']) has 'beta'", true, Description = "a term of an element")]
    [TestCase("'beta' in (dynamic(['beta-gamma']))", false, Description = "in compares whole elements")]
    [TestCase("set_has_element(dynamic(['beta-gamma']), 'beta')", false, Description = "set_has_element compares whole elements")]
    [TestCase("set_has_element(dynamic(['Alpha']), 'alpha')", false, Description = "set_has_element is case-sensitive, has is not")]
    [TestCase("'alpha' in~ (dynamic(['Alpha']))", true)]
    [TestCase("dynamic({'key':'value'}) has 'alu'", false, Description = "has doesn't match a part of a term")]
    [TestCase("dynamic({'key':'value'}) contains 'alu'", true, Description = "contains matches any substring")]
    [Description("has is a term search: it is different from the element membership of in, in~ and set_has_element")]
    public void TermSearch_ComparedWithElementMembership_ReturnsExpectedResult(string expression, bool expected)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [Test]
    [Description("array_index_of - an element that only has the term is not found, has finds it")]
    public void ArrayIndexOf_ElementWithTerm_IsNotFound()
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("array_index_of(dynamic(['beta-gamma']), 'beta')"), -1L);
    }

    // term search on a dynamic column

    [TestCase("Logs | where d has 'error'", new[] { 1L, 2L, 4L, 6L, 7L },
        Description = "'error-free' has the term error, 'errors' doesn't; property names and nested arrays are searched case-insensitively")]
    [TestCase("Logs | where d !has 'error'", new[] { 3L, 5L }, Description = "!has is true for null")]
    [TestCase("Logs | where isnotnull(d) and d !has 'error'", new[] { 3L })]
    [TestCase("Logs | where not(d has 'info')", new[] { 1L, 4L, 5L, 6L, 7L }, Description = "has is false (not null) for null")]
    [TestCase("Logs | where d has_cs 'ERROR'", new[] { 7L })]
    [TestCase("Logs | where d !has_cs 'ERROR'", new[] { 1L, 2L, 3L, 4L, 5L, 6L })]
    [TestCase("Logs | where d has 'level'", new[] { 1L, 2L, 3L, 7L }, Description = "a property name")]
    [TestCase("Logs | where d has 'disk full'", new[] { 1L })]
    [TestCase("Logs | where d has 'free run'", new[] { 2L })]
    [TestCase("Logs | where d has_any ('disk', 'happen')", new[] { 1L, 3L })]
    [TestCase("Logs | where d has_any (dynamic(['full', 'warn']))", new[] { 1L, 7L })]
    [TestCase("Logs | where d has_any ('details', 'zzz')", new[] { 7L }, Description = "a nested property name")]
    [TestCase("Logs | where d has_all ('level', 'info')", new[] { 2L, 3L })]
    [TestCase("Logs | where d has_all ('info', 'error')", new[] { 2L }, Description = "the terms can be in different values")]
    [TestCase("Logs | where d.msg has 'error'", new[] { 2L }, Description = "has on an extracted string value")]
    [TestCase("Logs | where d.level has 'info'", new[] { 2L, 3L })]
    [TestCase("Logs | where d.details has 'error'", new[] { 7L }, Description = "has on an extracted property bag")]
    [TestCase("Logs | where d.details.inner has 'error'", new[] { 7L }, Description = "has on an extracted array")]
    [TestCase("Logs | where d has 'error' and d.level == 'info'", new[] { 2L })]
    [TestCase("Logs | where d has 'error' or d has 'warn'", new[] { 1L, 2L, 4L, 6L, 7L })]
    [TestCase("Logs | where tostring(d) has 'error'", new[] { 1L, 2L, 4L, 6L, 7L })]
    [TestCase("Logs | where tostring(d) has 'level\":\"info'", new[] { 2L, 3L },
        Description = "tostring returns the compact JSON text, which is searched like any string")]
    [TestCase("Logs | where tostring(d) contains 'error'", new[] { 1L, 2L, 3L, 4L, 6L, 7L })]
    [TestCase("Logs | where set_has_element(d, 'error')", new[] { 4L }, Description = "membership: only the array with the element 'error'")]
    [TestCase("Logs | where array_index_of(d, 'error') >= 0", new[] { 4L })]
    [Description("has / has_cs / has_any / has_all - filter the rows of a dynamic column")]
    public void Where_TermSearchOnDynamicColumn_ReturnsMatchingRows(string query, long[] expectedIds)
    {
        Assert.That(ExecuteAndGetIds(CreateLogsDatabase(), query), Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("has_any - the terms can be a dynamic array from a let statement")]
    public void Where_HasAnyWithLetArray_ReturnsMatchingRows()
    {
        var ids = ExecuteAndGetIds(CreateLogsDatabase(),
            "let terms = dynamic(['disk', 'happen']);\nLogs | where d has_any (terms)");

        Assert.That(ids, Is.EqualTo(new[] { 1L, 3L }));
    }

    [Test]
    [Description("has - returns a bool for every row, false for null")]
    public void Extend_HasOnDynamicColumn_ReturnsBoolPerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateLogsDatabase(), "Logs | extend hit = d has 'error' | project id, hit");

        AssertTypedById<bool>(SingleById(rows, "hit"),
            (1, true), (2, true), (3, false), (4, true), (5, false), (6, true), (7, true));
    }

    [Test]
    [Description("countif with has - counts the matching rows")]
    public void Summarize_CountIfHas_CountsMatchingRows()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateLogsDatabase(),
            "Logs | summarize withError = countif(d has 'error'), withMsg = countif(d has 'msg'), notInfo = countif(d !has 'info')");

        Assert.That(rows, Has.Count.EqualTo(1));
        OpusTestUtils.AssertTypedValue(Col(rows[0], "withError"), 5L);
        OpusTestUtils.AssertTypedValue(Col(rows[0], "withMsg"), 3L);
        OpusTestUtils.AssertTypedValue(Col(rows[0], "notInfo"), 5L);
    }

    [Test]
    [Description("countif with has - per group of an extracted value, rows without the property are in the '' group")]
    public void Summarize_CountIfHasByExtractedValue_CountsMatchingRowsPerGroup()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateLogsDatabase(),
            "Logs | summarize hits = countif(d has 'error'), total = count() by lvl = tostring(d.level)");

        var hits = SingleByKey(rows, "lvl", "hits");
        var totals = SingleByKey(rows, "lvl", "total");
        Assert.That(hits.Keys, Is.EquivalentTo(new[] { "", "error", "info", "warn" }));
        OpusTestUtils.AssertTypedValue(hits[""], 2L);
        OpusTestUtils.AssertTypedValue(hits["error"], 1L);
        OpusTestUtils.AssertTypedValue(hits["info"], 1L);
        OpusTestUtils.AssertTypedValue(hits["warn"], 1L);
        OpusTestUtils.AssertTypedValue(totals[""], 3L);
        OpusTestUtils.AssertTypedValue(totals["error"], 1L);
        OpusTestUtils.AssertTypedValue(totals["info"], 2L);
        OpusTestUtils.AssertTypedValue(totals["warn"], 1L);
    }

    // typed values in table rows

    [TestCase("1.5", new[] { 1L, 5L, 6L }, Description = "a real value and the string '1.5'")]
    [TestCase("1", new[] { 1L, 5L, 6L }, Description = "'.' separates the terms of a real value, 15 doesn't have the term 1")]
    [TestCase("15", new[] { 3L }, Description = "a long value")]
    [TestCase("2020", new[] { 2L }, Description = "a datetime value")]
    [TestCase("true", new[] { 4L }, Description = "a bool value")]
    [TestCase("z", new[] { 6L })]
    [TestCase("r", new[] { 1L }, Description = "a property name")]
    [TestCase("t", new[] { 2L }, Description = "a property name, not a part of the datetime text or of true")]
    [TestCase("6f9619ff-8b86-d011-b42d-00c04fc964ff", new[] { 7L }, Description = "a guid value")]
    [Description("has - finds the terms of number, datetime, bool and guid values stored in a dynamic column")]
    public void Where_HasOnTypedValuesInRows_FindsTermsOfEveryScalarType(string needle, long[] expectedIds)
    {
        Assert.That(ExecuteAndGetIds(CreateScalarsDatabase(), $"Scalars | where d has '{needle}'"), Is.EqualTo(expectedIds));
    }

    // parse_json of a string column

    [TestCase("RawEvents | where parse_json(raw) has 'error'", new[] { 1L, 2L, 3L, 6L },
        Description = "invalid JSON is a dynamic string, which is searched too")]
    [TestCase("RawEvents | where parse_json(raw) has '500'", new[] { 1L }, Description = "a number from the JSON text")]
    [TestCase("RawEvents | where parse_json(raw) has_any ('timeout', 'zzz')", new[] { 6L })]
    [TestCase("RawEvents | extend p = parse_json(raw) | where p has 'error' and gettype(p) == 'dictionary'", new[] { 1L, 6L })]
    [Description("has - searches the values parsed from JSON text with parse_json")]
    public void Where_HasOnParsedJsonText_ReturnsMatchingRows(string query, long[] expectedIds)
    {
        Assert.That(ExecuteAndGetIds(CreateRawEventsDatabase(), query), Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("has / has_any - a bool per row for parsed JSON text, false for the null parsed from an empty string")]
    public void Extend_HasOnParsedJsonText_ReturnsBoolPerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateRawEventsDatabase(),
            "RawEvents | extend p = parse_json(raw) | extend hit = p has 'error', hitCode = p has '500', "
            + "hitTimeout = p has_any ('timeout', 'zzz'), t = gettype(p)");

        AssertTypedById<bool>(SingleById(rows, "hit"),
            (1, true), (2, true), (3, true), (4, false), (5, false), (6, true));
        AssertTypedById<bool>(SingleById(rows, "hitCode"),
            (1, true), (2, false), (3, false), (4, false), (5, false), (6, false));
        AssertTypedById<bool>(SingleById(rows, "hitTimeout"),
            (1, false), (2, false), (3, false), (4, false), (5, false), (6, true));

        var types = SingleById(rows, "t");
        Assert.That(types[1], Is.EqualTo("dictionary"));
        Assert.That(types[2], Is.EqualTo("string"));
        Assert.That(types[3], Is.EqualTo("array"));
        Assert.That(types[4], Is.EqualTo("null"));
    }

    private static KustoDatabase CreateLogsDatabase()
    {
        var table = OpusTestUtils.NewTable("Logs")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Json("""{"level":"error","msg":"disk full"}"""))
            .WithRow(2L, Json("""{"level":"info","msg":"error-free run"}"""))
            .WithRow(3L, Json("""{"level":"info","msg":"errors happen"}"""))
            .WithRow(4L, Json("""["error"]"""))
            .WithRow(5L, null)
            .WithRow(6L, Json("""{"error":1}"""))
            .WithRow(7L, Json("""{"level":"warn","details":{"inner":["ERROR"]}}"""))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateScalarsDatabase()
    {
        var table = OpusTestUtils.NewTable("Scalars")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Bag(("r", 1.5)))
            .WithRow(2L, Bag(("t", Utc(2020, 1, 2))))
            .WithRow(3L, Bag(("l", 15L)))
            .WithRow(4L, Bag(("b", true)))
            .WithRow(5L, Bag(("s", "1.5")))
            .WithRow(6L, Arr(1.5, "z"))
            .WithRow(7L, Bag(("g", SampleGuid)))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateRawEventsDatabase()
    {
        var table = OpusTestUtils.NewTable("RawEvents")
            .WithColumn<long>("id")
            .WithColumn<string>("raw")
            .WithRow(1L, """{"level":"error","code":500}""")
            .WithRow(2L, "not json error")
            .WithRow(3L, """["warn","error"]""")
            .WithRow(4L, "")
            .WithRow(5L, """{"level":"info"}""")
            .WithRow(6L, """{"nested":{"text":"Error: timeout"}}""")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }
}
