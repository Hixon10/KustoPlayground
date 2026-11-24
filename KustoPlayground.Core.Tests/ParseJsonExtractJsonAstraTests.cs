using System.Text.Json.Nodes;

namespace KustoPlayground.Core.Tests;

public class ParseJsonExtractJsonAstraTests
{
    private static KustoDatabase Documents(params string[] payloads)
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(payloads, "Payload", "Docs"));
        return database;
    }

    [TestCase(""" { "parts": [1, null, false], "meta": {"ok":true} } """, """{"meta":{"ok":true},"parts":[1,null,false]}""")]
    [TestCase("""[2,"a",1]""", """[2,"a",1]""")]
    [TestCase("[]", "[]")]
    [TestCase("{}", "{}")]
    [TestCase("12.5", "12.5")]
    [TestCase("false", "false")]
    [TestCase("\"blue\"", "\"blue\"")]
    [TestCase("null", "null")]
    [TestCase("", "null")]
    [TestCase(" not JSON ", "\" not JSON \"")]
    [TestCase("{unfinished", "\"{unfinished\"")]
    public void ParseJson_ProducesStructuredValuesOrPreservesMalformedInput(string payload, string expectedJson)
    {
        var values = TestUtils.ExecuteAndGetDataForOneColumn<object?>("Parsed", Documents(payload),
            "Docs | extend Parsed = parse_json(Payload)");

        Assert.That(values, Has.Count.EqualTo(1));
        AstraTestUtils.AssertJsonValue(values[0], expectedJson);
    }

    [TestCase("""dynamic({"list":[1,2]})""", """{"list":[1,2]}""")]
    [TestCase("dynamic([2,1])", "[2,1]")]
    [TestCase("""dynamic('{"n":1}')""", "\"{\\\"n\\\":1}\"")]
    [TestCase("dynamic(null)", "null")]
    public void ParseJson_DynamicOperandIsNotParsedAgain(string operand, string expectedJson)
    {
        AstraTestUtils.AssertJsonValue(AstraTestUtils.Print<object?>($"parse_json({operand})"), expectedJson);
    }

    [Test]
    public void ParseJson_NestedJsonStringNeedsAStringCastBeforeTheSecondParse()
    {
        var database = Documents("""{"inner":"{\"metrics\":[2,7]}"}""");
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, long>("Raw", "Second", database,
            """
            Docs
            | extend OuterBag = parse_json(Payload)
            | extend Raw = tostring(parse_json(OuterBag.inner)),
                     Second = tolong(parse_json(tostring(OuterBag.inner)).metrics[1])
            """);

        Assert.That(rows, Is.EqualTo(new[] { ("""{"metrics":[2,7]}""", 7L) }));
    }

    [TestCase("""extract_json("$.n", Payload)""", "42")]
    [TestCase("""extract_json("$.name", Payload)""", "blue")]
    [TestCase("""extract_json("$['odd.key'][0]", Payload)""", "first")]
    [TestCase("""extract_json("$['odd.key'][1]", Payload, typeof(string))""", "second")]
    public void ExtractJson_UsesStringByDefaultAndSupportsQuotedKeysAndArrayIndices(string expression, string expected)
    {
        var database = Documents("""{"n":42,"name":"blue","odd.key":["first","second"]}""");
        var values = TestUtils.ExecuteAndGetDataForOneColumn<string>("Extracted", database,
            $"Docs | extend Extracted = {expression}");

        Assert.That(values, Is.EqualTo(new[] { expected }));
    }

    [Test]
    public void ExtractJson_TypedPathsReturnRequestedScalarTypes()
    {
        var database = Documents("""[{"id":"9007199254740993","ratio":0.125,"ok":true,"at":"2024-02-29T00:00:00Z"}]""");
        var rows = TestUtils.ExecuteAndGetDataFor4Columns<long, double, bool, DateTime>("Id", "Ratio", "Ok", "At", database,
            """
            Docs
            | extend Id = extract_json("$[0].id", Payload, typeof(long)),
                     Ratio = extract_json("$[0].ratio", Payload, typeof(real)),
                     Ok = extract_json("$[0].ok", Payload, typeof(bool)),
                     At = extract_json("$[0].at", Payload, typeof(datetime))
            """);

        Assert.That(rows, Is.EqualTo(new[] { (9007199254740993L, 0.125, true, new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc)) }));
    }

    [Test]
    public void ExtractJson_RootPathSelectsTheWholeDocument()
    {
        var values = TestUtils.ExecuteAndGetDataForOneColumn<long>("N", Documents("17"),
            """Docs | extend N = extract_json("$", Payload, typeof(long))""");

        Assert.That(values, Is.EqualTo(new[] { 17L }));
    }

    [Test]
    public void ExtractJson_WithoutTypeReturnsJsonTextEvenForAnArray()
    {
        var text = AstraTestUtils.Print<string>("""extract_json('$.v', '{"v":[3,null,1]}')""");
        AstraTestUtils.AssertJsonValue(JsonNode.Parse(text), "[3,null,1]");
    }

    [Test]
    public void ExtractJson_PathCanBeALetConstantButNotARowColumn()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWith2Columns<string, string>(
            [("$.v", """{"v":7}""")], "Path", "Payload", "PathDocs"));

        var values = TestUtils.ExecuteAndGetDataForOneColumn<long>("N", database,
            "let path = '$.v'; PathDocs | extend N = extract_json(path, Payload, typeof(long))");
        Assert.That(values, Is.EqualTo(new[] { 7L }));
        AstraTestUtils.AssertRejected(database, "PathDocs | extend N = extract_json(Path, Payload, typeof(long))");
    }

    [Test]
    public void ExtractJson_MissingNullMalformedAndUnconvertibleValuesDoNotLoseRows()
    {
        var database = Documents("""{"v":[0]}""", """{"v":["bad"]}""", """{"v":[null]}""", """{"v":[]}""",
            """{"V":[1]}""", "{}", "{broken");
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, long?>("Payload", "N", database,
            """Docs | extend N = extract_json("$.v[0]", Payload, typeof(long))""");

        Assert.That(rows, Is.EquivalentTo(new (string, long?)[]
        {
            ("""{"v":[0]}""", 0), ("""{"v":["bad"]}""", null), ("""{"v":[null]}""", null),
            ("""{"v":[]}""", null), ("""{"V":[1]}""", null), ("{}", null), ("{broken", null)
        }));
    }
}
