namespace KustoPlayground.Core.Tests;

/// <summary>
/// parse_json() (todynamic, parsejson, toobject) - https://learn.microsoft.com/en-us/kusto/query/parse-json-function
/// extract_json() (extractjson)                  - https://learn.microsoft.com/en-us/kusto/query/extract-json-function
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class ParseJsonExtractJsonOpusTests
{
    private const string PersonJson =
        """{"name":"John","age":30,"tags":["a","b","c"],"addr":{"city":"Seattle","zip":"98101"},"score":4.5,"ok":true,"none":null,"key with space":1}""";

    // {"a":123, "b":"{\"c\":456}"} - property b is a string which contains JSON
    private const string NestedJson = """{"a":123, "b":"{\"c\":456}"}""";

    [TestCase("tostring(d.name)", "John")]
    [TestCase("tostring(d['name'])", "John")]
    [TestCase("tostring(d.age)", "30")]
    [TestCase("tostring(d.score)", "4.5")]
    [TestCase("tostring(d.tags[1])", "b")]
    [TestCase("tostring(d.tags[-1])", "c")]
    [TestCase("tostring(d.addr.city)", "Seattle")]
    [TestCase("""tostring(d["addr"]["zip"])""", "98101")]
    [TestCase("""tostring(d["key with space"])""", "1")]
    [TestCase("tostring(d.tags)", """["a","b","c"]""")]
    [TestCase("tostring(d.addr)", """{"city":"Seattle","zip":"98101"}""")]
    [Description("parse_json - property, bracket and array index access (negative index counts from the end)")]
    public void ParseJson_PropertyAccess_ReturnsValue(string expression, string expected)
    {
        var actual = EvaluateOnPerson(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("tostring(d.none)")]
    [TestCase("tostring(d.absent)")]
    [TestCase("tostring(d.tags[10])")]
    [TestCase("tostring(d.name.child)")]
    [TestCase("tostring(d.age.x)")]
    [Description("parse_json - JSON null, missing property, index out of range and property of a non-object are null (empty string)")]
    public void ParseJson_MissingOrNullValue_ReturnsEmptyString(string expression)
    {
        var actual = EvaluateOnPerson(expression);

        OpusTestUtils.AssertTypedValue(actual, "");
    }

    [Test]
    [Description("parse_json - dynamic values are converted to typed scalars with tolong/toint/toreal/tobool")]
    public void ParseJson_ConvertedToScalarTypes_ReturnsTypedValues()
    {
        OpusTestUtils.AssertTypedValue(EvaluateOnPerson("tolong(d.age)"), 30L);
        OpusTestUtils.AssertTypedValue(EvaluateOnPerson("toint(d.age)"), 30);
        OpusTestUtils.AssertTypedValue(EvaluateOnPerson("toreal(d.score)"), 4.5);
        OpusTestUtils.AssertTypedValue(EvaluateOnPerson("tobool(d.ok)"), true);
    }

    [Test]
    [Description("parse_json - dynamic bool is converted to a lower case string (unlike tostring(true) which is 'True')")]
    public void ParseJson_DynamicBoolToString_IsLowerCase()
    {
        OpusTestUtils.AssertTypedValue(EvaluateOnPerson("tostring(d.ok)"), "true");
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("tostring(parse_json('false'))"), "false");
        OpusTestUtils.AssertTypedValue(EvaluateOnPerson("gettype(d.ok)"), "bool");
    }

    [Test]
    [Description("parse_json - whole document is a dynamic value")]
    public void ParseJson_WholeDocument_ReturnsDynamic()
    {
        var actual = EvaluateOnPerson("d");

        OpusTestUtils.AssertDynamicEquals(actual, PersonJson);
    }

    [Test]
    [Description("parse_json - nested object and array are dynamic values")]
    public void ParseJson_NestedObjectAndArray_ReturnsDynamic()
    {
        OpusTestUtils.AssertDynamicEquals(EvaluateOnPerson("d.addr"), """{"city":"Seattle","zip":"98101"}""");
        OpusTestUtils.AssertDynamicEquals(EvaluateOnPerson("d['tags']"), """["a","b","c"]""");
    }

    [Test]
    [Description("parse_json - result type is dynamic")]
    public void ParseJson_ResultType_IsDynamic()
    {
        var kustoDatabase = CreateDocumentsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            "Documents | project d = parse_json(Doc), City = tostring(parse_json(Doc).addr.city) | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("d", 0, "System.Object", "dynamic"),
            ("City", 1, "System.String", "string")
        }));
    }

    [TestCase("todynamic")]
    [TestCase("parsejson")]
    [TestCase("toobject")]
    [Description("parse_json - todynamic, parsejson and toobject are aliases")]
    public void ParseJson_Aliases_BehaveTheSame(string functionName)
    {
        var kustoDatabase = CreateDocumentsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            kustoDatabase,
            $"Documents | project r = tostring({functionName}(Doc).addr.city)");

        OpusTestUtils.AssertTypedValue(actual, "Seattle");
    }

    [TestCase("tostring(parse_json('{}'))", "{}")]
    [TestCase("tostring(parse_json('[]'))", "[]")]
    [TestCase("""tostring(parse_json('"str"'))""", "str")]
    [TestCase("tostring(parse_json('abc'))", "abc")]
    [TestCase("tostring(parse_json('null'))", "")]
    [TestCase("tostring(parse_json('12'))", "12")]
    [TestCase("tostring(parse_json('1.0'))", "1.0")]
    [TestCase("""tostring(parse_json('{"a":1.0}'))""", """{"a":1.0}""")]
    [TestCase("""tostring(parse_json('{"b":2, "a":[1,"x",null,true,1.5]}'))""", """{"b":2,"a":[1,"x",null,true,1.5]}""")]
    [TestCase("""tostring(parse_json('  {"a": 1}  ').a)""", "1")]
    [Description("parse_json - top level values; tostring returns compact JSON keeping the property order")]
    public void ParseJson_TopLevelValues_ToString(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("tostring(parse_json('[1,2,3]')[0])", "1")]
    [TestCase("tostring(parse_json('[[1,2],[3,4]]')[1][0])", "3")]
    [TestCase("""tostring(parse_json('{"s":"\\u0041b"}').s)""", "Ab")]
    [TestCase("""tostring(parse_json('{"r":1e2}').r)""", "100.0")]
    [TestCase("""tostring(dynamic(["a", "b"])[1])""", "b")]
    [Description("parse_json - nested arrays, JSON unicode escapes, exponent numbers and dynamic literals")]
    public void ParseJson_SpecialValues_ToString(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [Test]
    [Description("parse_json - top level and nested scalars are converted to typed values")]
    public void ParseJson_ScalarValues_ConvertedToTypedValues()
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("tolong(parse_json('123'))"), 123L);
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("tobool(parse_json('true'))"), true);
        OpusTestUtils.AssertTypedValue(
            OpusTestUtils.EvaluateScalar("""tolong(parse_json('{"v":9223372036854775807}').v)"""),
            long.MaxValue);
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("""toreal(parse_json('{"r":1e2}').r)"""), 100.0);
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("""tolong(parse_json('{"v":"2"}').v)"""), 2L);
    }

    [Test]
    [Description("parse_json - documentation example: a JSON string inside JSON has to be converted with tostring before parsing it again")]
    public void ParseJson_DocExampleNestedJsonString_RequiresToString()
    {
        var table = TestUtils.GenerateTableWithColumn<string>([NestedJson], "Doc", "Nested");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            """
            Nested
            | project
                c1 = tolong(parse_json(tostring(parse_json(Doc).b)).c),
                c2 = tostring(parse_json(parse_json(Doc).b).c),
                bType = gettype(parse_json(Doc).b),
                bText = tostring(parse_json(Doc).b)
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        OpusTestUtils.AssertTypedValue(rows[0]["c1"], 456L);
        OpusTestUtils.AssertTypedValue(rows[0]["c2"], "");
        OpusTestUtils.AssertTypedValue(rows[0]["bType"], "string");
        OpusTestUtils.AssertTypedValue(rows[0]["bText"], """{"c":456}""");
    }

    [Test]
    [Description("parse_json - JSON column can be filtered by a property in where")]
    public void ParseJson_InWhere_FiltersByProperty()
    {
        var kustoDatabase = CreateEventsDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Id",
            kustoDatabase,
            "Events | where tostring(parse_json(Payload).level) == 'error' | project Id");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 1, 3 }));
    }

    [Test]
    [Description("parse_json - dynamic column created by extend, filtered by a numeric property, nested property projected")]
    public void ParseJson_ExtendThenWhere_ProjectsNestedProperty()
    {
        var kustoDatabase = CreateEventsDatabase();

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, string>(
            "Id",
            "User",
            kustoDatabase,
            """
            Events
            | extend p = parse_json(Payload)
            | where tolong(p.code) >= 400
            | project Id, User = tostring(p.user.name)
            """);

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string)>
        {
            (1, "ann"),
            (3, "")
        }));
    }

    [Test]
    [Description("parse_json - invalid JSON and missing properties produce nulls when converted to long")]
    public void ParseJson_TypedPropertyOfAllRows_NullForMissing()
    {
        var kustoDatabase = CreateEventsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Events | project Id, Code = tolong(parse_json(Payload).code)");

        var actualData = rows.Select(row => ((long)row["Id"]!, (long?)row["Code"])).ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long?)>
        {
            (1, 500),
            (2, 200),
            (3, 404),
            (4, null)
        }));
    }

    [Test]
    [Description("parse_json - extracted property combined with distinct")]
    public void ParseJson_WithDistinct_ReturnsDistinctLevels()
    {
        var kustoDatabase = CreateEventsDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Level",
            kustoDatabase,
            "Events | project Level = tostring(parse_json(Payload).level) | distinct Level");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "error", "info", "" }));
    }

    [Test]
    [Description("parse_json - extracted properties combined with strcat")]
    public void ParseJson_WithStrcat_BuildsMessage()
    {
        var kustoDatabase = CreateEventsDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Message",
            kustoDatabase,
            """
            Events
            | extend p = parse_json(Payload)
            | where tostring(p.level) == 'info'
            | project Message = strcat(p.level, ':', p.code, ':', p.user.name)
            """);

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "info:200:bob" }));
    }

    [TestCase("extract_json('$.name', Doc)", "John")]
    [TestCase("extract_json('$.age', Doc)", "30")]
    [TestCase("extract_json('$.score', Doc)", "4.5")]
    [TestCase("extract_json('$.ok', Doc)", "true")]
    [TestCase("extract_json('$.none', Doc)", "null")]
    [TestCase("extract_json('$.addr.city', Doc)", "Seattle")]
    [TestCase("extract_json('$.tags[1]', Doc)", "b")]
    [TestCase("""extract_json("$['key with space']", Doc)""", "1")]
    [TestCase("extract_json('$.addr', Doc)", """{"city":"Seattle","zip":"98101"}""")]
    [TestCase("extract_json('$.tags', Doc)", """["a","b","c"]""")]
    [TestCase("extract_json('$.absent', Doc)", "")]
    [TestCase("extract_json('$.tags[10]', Doc)", "")]
    [TestCase("extractjson('$.name', Doc)", "John")]
    [Description("extract_json - without a type the result is a string (JSON null is 'null', missing path is empty)")]
    public void ExtractJson_Untyped_ReturnsString(string expression, string expected)
    {
        var actual = EvaluateOnDocument(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("""extract_json('$', '"x"')""", "x")]
    [TestCase("extract_json('$.a', 'oops')", "")]
    [TestCase("extract_json('$.a', '')", "")]
    [Description("extract_json - root path of a JSON string, invalid JSON returns an empty string")]
    public void ExtractJson_SpecialDocuments_ReturnsString(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [Test]
    [Description("extract_json - typeof(T) converts the extracted value to the requested type")]
    public void ExtractJson_Typed_ReturnsTypedValue()
    {
        OpusTestUtils.AssertTypedValue(EvaluateOnDocument("extract_json('$.age', Doc, typeof(long))"), 30L);
        OpusTestUtils.AssertTypedValue(EvaluateOnDocument("extract_json('$.age', Doc, typeof(int))"), 30);
        OpusTestUtils.AssertTypedValue(EvaluateOnDocument("extract_json('$.score', Doc, typeof(real))"), 4.5);
        OpusTestUtils.AssertTypedValue(EvaluateOnDocument("extract_json('$.ok', Doc, typeof(bool))"), true);
        OpusTestUtils.AssertTypedValue(EvaluateOnDocument("extract_json('$.age', Doc, typeof(string))"), "30");
        OpusTestUtils.AssertTypedValue(EvaluateOnDocument("""extract_json("$['addr']['zip']", Doc, typeof(long))"""), 98101L);
        OpusTestUtils.AssertTypedValue(
            OpusTestUtils.EvaluateScalar("""extract_json('$.t', '{"t":"2020-01-02T03:04:05Z"}', typeof(datetime))"""),
            OpusTestUtils.ParseDateTime("2020-01-02 03:04:05"));
    }

    [TestCase("extract_json('$.absent', Doc, typeof(long))")]
    [TestCase("extract_json('$.name', Doc, typeof(long))")]
    [TestCase("extract_json('$.name', Doc, typeof(bool))")]
    [TestCase("extract_json('$.none', Doc, typeof(real))")]
    [Description("extract_json - typed result is null when the path is missing or the value can't be converted")]
    public void ExtractJson_TypedMissingOrInvalid_ReturnsNull(string expression)
    {
        var actual = EvaluateOnDocument(expression);

        Assert.That(actual, Is.Null);
    }

    [TestCase("extract_json('$.a', '{}')", "System.String", "string")]
    [TestCase("extract_json('$.a', '{}', typeof(long))", "System.Int64", "long")]
    [TestCase("extract_json('$.a', '{}', typeof(int))", "System.Int32", "int")]
    [TestCase("extract_json('$.a', '{}', typeof(real))", "System.Double", "real")]
    [TestCase("extract_json('$.a', '{}', typeof(bool))", "System.SByte", "bool")]
    [TestCase("extract_json('$.a', '{}', typeof(datetime))", "System.DateTime", "datetime")]
    [Description("extract_json - result type is string or the type passed with typeof()")]
    public void ExtractJson_ResultType_DependsOnTypeArgument(string expression, string expectedDataType, string expectedColumnType)
    {
        var schema = OpusTestUtils.ExecuteGetSchema(new KustoDatabase(), $"print r = {expression} | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("r", 0, expectedDataType, expectedColumnType)
        }));
    }

    [Test]
    [Description("extract_json - applied to every row: missing values and invalid JSON give empty strings / nulls")]
    public void ExtractJson_TableRows_ExtractsPerRow()
    {
        var table = TestUtils.GenerateTableWith2Columns<long, string>(
            [
                (1L, """{"addr":{"city":"Seattle","zip":98101}}"""),
                (2L, """{"addr":{"city":"Paris"}}"""),
                (3L, "{}"),
                (4L, "oops")
            ],
            "Id",
            "Payload",
            "Addresses");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Addresses | project Id, City = extract_json('$.addr.city', Payload), Zip = extract_json('$.addr.zip', Payload, typeof(long))");

        var actualData = rows.Select(row => ((long)row["Id"]!, (string)row["City"]!, (long?)row["Zip"])).ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string, long?)>
        {
            (1, "Seattle", 98101),
            (2, "Paris", null),
            (3, "", null),
            (4, "", null)
        }));
    }

    [Test]
    [Description("extract_json - typed value used inside where")]
    public void ExtractJson_InWhere_FiltersRows()
    {
        var kustoDatabase = CreateEventsDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Id",
            kustoDatabase,
            "Events | where extract_json('$.code', Payload, typeof(long)) == 404 | project Id");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 3 }));
    }

    [Test]
    [Description("extract_json - string value extracted from every row")]
    public void ExtractJson_LevelOfAllRows_ReturnsStrings()
    {
        var kustoDatabase = CreateEventsDatabase();

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, string>(
            "Id",
            "Level",
            kustoDatabase,
            "Events | project Id, Level = extract_json('$.level', Payload)");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string)>
        {
            (1, "error"),
            (2, "info"),
            (3, "error"),
            (4, "")
        }));
    }

    [Test]
    [Description("extract_json and parse_json - return the same values for the same path")]
    public void ExtractJson_ComparedToParseJson_ReturnsSameValues()
    {
        var kustoDatabase = CreateEventsDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Count",
            kustoDatabase,
            """
            Events
            | where extract_json('$.user.name', Payload) == tostring(parse_json(Payload).user.name)
            | count
            """);

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 4 }));
    }

    private static KustoDatabase CreateDocumentsDatabase()
    {
        var table = TestUtils.GenerateTableWithColumn<string>([PersonJson], "Doc", "Documents");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        return kustoDatabase;
    }

    /// <summary>
    /// Evaluates the expression with <c>d = parse_json(Doc)</c> for the single document <see cref="PersonJson"/>.
    /// </summary>
    private static object? EvaluateOnPerson(string expression)
    {
        return OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            CreateDocumentsDatabase(),
            $"Documents | extend d = parse_json(Doc) | project {OpusTestUtils.ScalarColumn} = {expression}");
    }

    /// <summary>
    /// Evaluates the expression on the single document <see cref="PersonJson"/> stored in the string column <c>Doc</c>.
    /// </summary>
    private static object? EvaluateOnDocument(string expression)
    {
        return OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            CreateDocumentsDatabase(),
            $"Documents | project {OpusTestUtils.ScalarColumn} = {expression}");
    }

    private static KustoDatabase CreateEventsDatabase()
    {
        var table = TestUtils.GenerateTableWith2Columns<long, string>(
            [
                (1L, """{"level":"error","code":500,"user":{"name":"ann"}}"""),
                (2L, """{"level":"info","code":200,"user":{"name":"bob"}}"""),
                (3L, """{"level":"error","code":404}"""),
                (4L, "not json")
            ],
            "Id",
            "Payload",
            "Events");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        return kustoDatabase;
    }
}
