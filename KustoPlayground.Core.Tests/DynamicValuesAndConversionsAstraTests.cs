using System.Text.Json;

namespace KustoPlayground.Core.Tests;

public class DynamicValuesAndConversionsAstraTests
{
    [Test]
    public void DeclaredDynamicColumnIngestsMixedJsonRowsWithoutStringifyingThem()
    {
        const string definition = """
            {
              "Name":"Events",
              "Columns":[{"Name":"Seq","Type":"long"},{"Name":"Payload","Type":"dynamic","Nullable":true}],
              "Rows":[
                {"Seq":1,"Payload":null}, {"Seq":2,"Payload":false}, {"Seq":3,"Payload":"plain text"},
                {"Seq":4,"Payload":9007199254740993}, {"Seq":5,"Payload":0.125},
                {"Seq":6,"Payload":[3,"three",null,3,{"ids":[2,1]}]},
                {"Seq":7,"Payload":{"a.b":{},"A":[],"a":{"value":true}}}
              ]
            }
            """;
        var db = new KustoDatabase();
        db.AddTable(TableBuilderFromJson.Build(JsonSerializer.Deserialize(definition, TableDefJsonContext.Default.TableDef)!));

        var values = TestUtils.ExecuteAndGetDataForOneColumn<object?>("Payload", db, "Events | sort by Seq asc");
        string[] expected = ["null", "false", "\"plain text\"", "9007199254740993", "0.125",
            """[3,"three",null,3,{"ids":[2,1]}]""", """{"a":{"value":true},"a.b":{},"A":[]}"""];
        Assert.That(values, Has.Count.EqualTo(expected.Length));
        for (int i = 0; i < expected.Length; i++)
            AstraTestUtils.AssertJsonValue(values[i], expected[i]);
    }

    [Test]
    public void GetTypeInspectsEachStoredValueRatherThanReportingTheColumnType()
    {
        var db = new KustoDatabase();
        db.AddTable(OpusTestUtils.NewTable("Events").WithColumn<long>("Id").WithColumn<object>("P")
            .WithRow(1L, null).WithRow(2L, false).WithRow(3L, 7).WithRow(4L, 9007199254740993L)
            .WithRow(5L, 0.25).WithRow(6L, "plain").WithRow(7L, new DateTime(2024, 2, 29))
            .WithRow(8L, TimeSpan.FromSeconds(90)).WithRow(9L, Guid.Empty)
            .WithRow(10L, new object?[] { null }).WithRow(11L, new Dictionary<string, object?>()).Build());

        var rows = TestUtils.ExecuteAndGetDataFor3Columns<long, string, bool>("Id", "Kind", "Missing", db,
            "Events | project Id, Kind=gettype(P), Missing=isnull(P)");
        Assert.That(rows, Is.EquivalentTo(new (long, string, bool)[]
        {
            // Kusto widens an int held in a dynamic value to long, so gettype reports "long" for it too.
            (1, "null", true), (2, "bool", false), (3, "long", false), (4, "long", false),
            (5, "double", false), (6, "string", false), (7, "datetime", false), (8, "timespan", false),
            (9, "guid", false), (10, "array", false), (11, "dictionary", false)
        }));
    }

    private static IEnumerable<TestCaseData> TypedLiterals() =>
    [
        new("9007199254740993", "long", "tolong", 9007199254740993L),
        new("datetime(2024-02-29T12:34:56.1234567Z)", "datetime", "todatetime",
            new DateTime(2024, 2, 29, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567)),
        // Kusto stores a timespan inside a dynamic literal as its tick count (a long); totimespan still recovers it exactly.
        new("time(00:01:30.125)", "long", "totimespan", TimeSpan.FromMilliseconds(90125)),
        new("guid(73c22437-33ba-4f69-8c68-4f3e8b53878b)", "guid", "toguid", Guid.Parse("73c22437-33ba-4f69-8c68-4f3e8b53878b"))
    ];

    [TestCaseSource(nameof(TypedLiterals))]
    public void NestedDynamicLiteralRetainsKustoTypeAndExactValue(string literal, string kind, string cast, object expected)
    {
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, object?>("Kind", "Scalar", new KustoDatabase(),
            $$"""
            print P=dynamic({"entries":[{"v":{{literal}}}]})
            | extend V=P.entries[0].v
            | project Kind=gettype(V), Scalar={{cast}}(V)
            """);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Item1, Is.EqualTo(kind));
        Assert.That(rows[0].Item2, Is.TypeOf(expected.GetType()).And.EqualTo(expected));
    }

    [TestCase("p.user.id", "13")]
    [TestCase("""p["a.b"]""", "\"literal dot\"")]
    [TestCase("p.a.b", "\"nested\"")]
    [TestCase("p['space key']['0']", "false")]
    [TestCase("p.items[0].tags[1]", "\"second\"")]
    [TestCase("p.items[(-1)]", "42")]
    [TestCase("p.items[(-3)].tags[0]", "\"first\"")]
    [TestCase("p.items[1]", "null")]
    [TestCase("p.items[3]", "null")]
    [TestCase("p.items[(-4)]", "null")]
    [TestCase("p.User", "null")]
    [TestCase("p.unknown.child", "null")]
    public void SubscriptsDistinguishLiteralKeysPathsAndArrayBounds(string access, string expectedJson)
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("V", new KustoDatabase(),
            $$"""
            print p=dynamic({"user":{"id":13},"a.b":"literal dot","a":{"b":"nested"},
                             "space key":{"0":false},"items":[{"tags":["first","second"]},null,42]})
            | project V={{access}}
            """);
        AstraTestUtils.AssertJsonValue(value, expectedJson);
    }

    [TestCase("p[Key][Offset]", "b0", "r0")]
    [TestCase("p[Key][(Offset + 1)]", "b1", "r1")]
    public void ComputedPropertyNamesAndIndexesAreEvaluatedForEachRow(string access, string blue, string red)
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWith2Columns<string, long>(
            [("blue", 0), ("red", -2), ("absent", 1), ("red", 5)], "Key", "Offset", "Choices"));
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, long, string?>("Key", "Offset", "V", db,
            $$"""
            Choices | extend p=dynamic({"blue":["b0","b1"],"red":["r0","r1"],"Key":["wrong","key"]})
            | project Key, Offset, V={{access}}
            """);
        Assert.That(rows, Is.EquivalentTo(new (string, long, string?)[]
            { ("blue", 0, blue), ("red", -2, red), ("absent", 1, null), ("red", 5, null) }));
    }

    [Test]
    public void ExtractedSlotsAndArrayItemsRemainDynamicUntilExplicitlyCast()
    {
        var db = new KustoDatabase();
        db.AddTable(OpusTestUtils.NewTable("Events").WithColumn<object>("P").Build());
        var columns = TestUtils.ExecuteAndGetDataFor2Columns<string, string>("ColumnName", "ColumnType", db,
            "Events | project P, Slot=P.user.id, Item=P.items[0], N=tolong(P.count) | getschema");
        Assert.That(columns, Is.EquivalentTo(new[]
            { ("P", "dynamic"), ("Slot", "dynamic"), ("Item", "dynamic"), ("N", "long") }));
    }

    [TestCase("9007199254740993", "long", "9007199254740993")]
    [TestCase("-1.25e2", "double", "-125.0")]
    [TestCase("false", "bool", "false")]
    [TestCase("null", "null", "null")]
    [TestCase("\"broken [\"", "string", "\"broken [\"")]
    [TestCase("  [1] trailing text ", "string", "\"  [1] trailing text \"")]
    [TestCase("""{"x":[null,{"y":true}]}""", "dictionary", """{"x":[null,{"y":true}]}""")]
    [TestCase("""[{"k":7},null,"7"]""", "array", """[{"k":7},null,"7"]""")]
    public void ParseJsonReportsTheRuntimeTypeAndConsumesTheWholeInput(string json, string kind, string expectedJson)
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWithColumn([json], "Text", "Docs"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, object?>("Kind", "P", db,
            "Docs | extend P=parse_json(Text) | project Kind=gettype(P), P");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Item1, Is.EqualTo(kind));
        AstraTestUtils.AssertJsonValue(rows[0].Item2, expectedJson);
    }

    [TestCase("toint", "-7.75", -7)]
    [TestCase("tolong", "\"9007199254740993\"", 9007199254740993L)]
    [TestCase("toreal", "\"-1.25e2\"", -125.0)]
    [TestCase("todouble", "\"0.125\"", 0.125)]
    [TestCase("tobool", "\"FALSE\"", false)]
    [TestCase("tostring", "null", "")]
    // Kusto's toint of a dynamic slot wraps the parsed value to 32 bits instead of returning null.
    [TestCase("toint", "\"2147483648\"", int.MinValue)]
    [TestCase("tolong", "\"9223372036854775808\"", null)]
    [TestCase("toreal", "\"bad\"", null)]
    [TestCase("todouble", "null", null)]
    [TestCase("tobool", "\"unknown\"", null)]
    [TestCase("todatetime", "\"not-a-date\"", null)]
    [TestCase("totimespan", "\"no-duration\"", null)]
    [TestCase("toguid", "\"not-an-id\"", null)]
    public void CastsOfExtractedSlotsReturnTypedScalarsOrNull(string function, string literal, object? expected)
    {
        var value = AstraTestUtils.Print<object?>($$"""{{function}}(dynamic({"v":{{literal}}}).v)""");
        if (expected is not null)
            Assert.That(value, Is.TypeOf(expected.GetType()));
        Assert.That(value, Is.EqualTo(expected));
    }

    private static IEnumerable<TestCaseData> JsonStringCasts() =>
    [
        new("todatetime", "2024-02-29T03:04:05Z", new DateTime(2024, 2, 29, 3, 4, 5, DateTimeKind.Utc)),
        new("totimespan", "01:02:03", new TimeSpan(1, 2, 3)),
        new("toguid", "73c22437-33ba-4f69-8c68-4f3e8b53878b", Guid.Parse("73c22437-33ba-4f69-8c68-4f3e8b53878b"))
    ];

    [TestCaseSource(nameof(JsonStringCasts))]
    public void CastsRecoverTimeAndGuidValuesFromJsonStrings(string cast, string input, object expected)
    {
        var value = AstraTestUtils.Print<object?>($$"""{{cast}}(parse_json('{"v":"{{input}}"}').v)""");
        Assert.That(value, Is.TypeOf(expected.GetType()).And.EqualTo(expected));
    }

    [Test]
    public void ParsedEventFieldsCanFilterGroupAndSumWithoutDroppingOtherValidRows()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWithColumn<string>(
            ["""{"site":"east","ok":true,"samples":["12"]}""", """{"site":"east","ok":"true","samples":[8]}""",
             """{"site":"east","ok":false,"samples":[99]}""", """{"site":"east","ok":true,"samples":["bad"]}""",
             """{"site":"west","ok":true,"samples":[0]}""", "{unfinished"], "Json", "Events"));
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>("Site", "Total", "Samples", db,
            """
            Events | extend P=parse_json(Json)
            | where tobool(P.ok)
            | extend Site=tostring(P.site), N=tolong(P.samples[0])
            | where isnotnull(N)
            | summarize Total=sum(N), Samples=count() by Site
            """);
        Assert.That(rows, Is.EquivalentTo(new[] { ("east", 20L, 2L), ("west", 0L, 1L) }));
    }
}
