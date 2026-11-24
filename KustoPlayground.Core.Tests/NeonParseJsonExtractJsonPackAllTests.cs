namespace KustoPlayground.Core.Tests;

public class NeonParseJsonExtractJsonPackAllTests
{
    #region parse_json() Tests

    [Test]
    [Description("parse_json() - parses JSON object and supports dot property access and typed casts")]
    public void ParseJson_ObjectDotAccessAndTypedExtraction()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWithColumn(
            new List<string>
            {
                """{"name":"Alan","age":21,"score":98.5,"active":true,"address":{"street":432,"postcode":"JLK32P"}}"""
            },
            columnName: "Raw",
            tableName: "Payloads");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            Payloads
            | extend d = parse_json(Raw)
            | extend Name = tostring(d.name),
                     Age = toint(d.age),
                     Score = toreal(d.score),
                     Active = tobool(d.active),
                     Street = toint(d.address.street),
                     Postcode = tostring(d.address.postcode)
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        var row = rows[0];
        Assert.That(row["Name"], Is.EqualTo("Alan"));
        Assert.That(row["Age"], Is.EqualTo(21));
        Assert.That((double)row["Score"]!, Is.EqualTo(98.5).Within(1e-9));
        Assert.That(row["Active"], Is.EqualTo(true));
        Assert.That(row["Street"], Is.EqualTo(432));
        Assert.That(row["Postcode"], Is.EqualTo("JLK32P"));
    }

    [Test]
    [Description("parse_json() - bracket notation for keys with spaces, dots, hyphens, and leading digits")]
    public void ParseJson_BracketAccessForSpecialKeys()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWithColumn(
            new List<string>
            {
                """{"Log Level":"WARN","a.b":"dotted","x-header":"custom","123key":7}"""
            },
            columnName: "Raw",
            tableName: "SpecialKeys");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            SpecialKeys
            | extend d = parse_json(Raw)
            | extend Level = tostring(d["Log Level"]),
                     Dotted = tostring(d['a.b']),
                     Header = tostring(d['x-header']),
                     NumKey = toint(d["123key"])
            """);

        var row = rows[0];
        Assert.That(row["Level"], Is.EqualTo("WARN"));
        Assert.That(row["Dotted"], Is.EqualTo("dotted"));
        Assert.That(row["Header"], Is.EqualTo("custom"));
        Assert.That(row["NumKey"], Is.EqualTo(7));
    }

    [Test]
    [Description("parse_json() - parses arrays with zero-based, negative, and nested object indexing")]
    public void ParseJson_ArraysAndIndexing()
    {
        var db = new KustoDatabase();

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            print d = parse_json('{"nums":[43,21,65],"items":[{"id":1,"tag":"first"},{"id":2,"tag":"last"}]}')
            | extend FirstNum = toint(d.nums[0]),
                     LastNum = toint(d.nums[-1]),
                     SecondTag = tostring(d.items[1].tag),
                     OutOfBounds = toint(d.nums[99])
            """);

        var row = rows[0];
        Assert.That(row["FirstNum"], Is.EqualTo(43));
        Assert.That(row["LastNum"], Is.EqualTo(65));
        Assert.That(row["SecondTag"], Is.EqualTo("last"));
        Assert.That(row["OutOfBounds"], Is.Null);
    }

    [Test]
    [Description("parse_json() - top-level primitives, empty object, empty array, and null")]
    public void ParseJson_PrimitivesAndEmptyContainers()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(parse_json('21'))"), Is.EqualTo(21));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(parse_json('\"21\"'))"), Is.EqualTo("21"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(parse_json('true'))"), Is.True);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<double>(db, "print toreal(parse_json('3.14'))"), Is.EqualTo(3.14).Within(1e-9));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print parse_json('null')"), Is.Null);

        NeonTestUtils.AssertJsonEquivalent(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(parse_json('{}'))"),
            "{}");
        NeonTestUtils.AssertJsonEquivalent(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(parse_json('[]'))"),
            "[]");
    }

    [Test]
    [Description("parse_json() - missing property returns null; invalid JSON string is preserved as dynamic string")]
    public void ParseJson_MissingPropertyAndInvalidJsonString()
    {
        var db = new KustoDatabase();

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleObject(db, "print d = parse_json('{\"a\":1}') | extend m = d.nonexistent | project m", "m"),
            Is.Null);

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(parse_json('not valid json'))"),
            Is.EqualTo("not valid json"));

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(parse_json('{broken'))"),
            Is.EqualTo("{broken"));

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(parse_json(''))"),
            Is.EqualTo(""));
    }

    [Test]
    [Description("parse_json() - official doc edge case: dynamic input returned as-is; double-encoded JSON requires tostring()")]
    public void ParseJson_DynamicInputPassThroughAndDoubleEncodedJson()
    {
        var db = new KustoDatabase();

        // Passing already-dynamic value returns it unchanged
        var passThrough = NeonTestUtils.ExecuteAndGetSingleValue<int>(
            db,
            "print d = parse_json(parse_json('{\"a\":123}')) | extend Val = toint(d.a) | project Val",
            "Val");
        Assert.That(passThrough, Is.EqualTo(123));

        // Double-encoded JSON: d.b is dynamic containing string '{"c":456}'.
        // parse_json(d.b) returns d.b unchanged (dynamic string), so .c is null.
        // parse_json(tostring(d.b)) parses the string into a dynamic object, so .c is 456!
        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            print raw = '{"a":123, "b":"{\\"c\\":456}"}'
            | extend d = parse_json(raw)
            | extend WrongC = toint(parse_json(d.b).c),
                     RightC = toint(parse_json(tostring(d.b)).c)
            """);

        Assert.That(rows[0]["WrongC"], Is.Null);
        Assert.That(rows[0]["RightC"], Is.EqualTo(456));
    }

    [Test]
    [Description("parse_json() - escaped characters and unicode escapes decoded properly")]
    public void ParseJson_EscapedAndUnicodeStrings()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            """print d = parse_json('{"text":"line1\\nline2 \\"quoted\\" \\u0041"}') | extend t = tostring(d.text) | project t""",
            "t");

        Assert.That(actual, Is.EqualTo("line1\nline2 \"quoted\" A"));
    }

    [Test]
    [Description("parse_json() - aliases parsejson(), todynamic(), and toobject() behave identically")]
    public void ParseJson_AliasesEquivalent()
    {
        var db = new KustoDatabase();

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print d = parsejson('{\"k\":10}') | extend v = toint(d.k) | project v", "v"),
            Is.EqualTo(10));
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print d = todynamic('{\"k\":20}') | extend v = toint(d.k) | project v", "v"),
            Is.EqualTo(20));
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print d = toobject('{\"k\":30}') | extend v = toint(d.k) | project v", "v"),
            Is.EqualTo(30));
    }

    [Test]
    [Description("parse_json() - heterogeneous JSON rows in a table including missing fields and invalid rows")]
    public void ParseJson_HeterogeneousTableRows()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, """{"code":200,"ok":true}"""),
                (2, """{"code":500}"""),
                (3, "plain text"),
                (4, """{"code":null,"ok":false}""")
            },
            columnName1: "Id",
            columnName2: "Payload",
            tableName: "Events");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "Events | extend d = parse_json(Payload) | extend Code = toint(d.code), Ok = tobool(d.ok) | sort by Id asc");

        Assert.That(rows[0]["Code"], Is.EqualTo(200));
        Assert.That(rows[0]["Ok"], Is.EqualTo(true));

        Assert.That(rows[1]["Code"], Is.EqualTo(500));
        Assert.That(rows[1]["Ok"], Is.Null);

        Assert.That(rows[2]["Code"], Is.Null);
        Assert.That(rows[2]["Ok"], Is.Null);

        Assert.That(rows[3]["Code"], Is.Null);
        Assert.That(rows[3]["Ok"], Is.EqualTo(false));
    }

    #endregion

    #region extract_json() Tests

    [Test]
    [Description("extract_json() - 2-arg form returns string for simple, nested, bracketed, and array JSONPath expressions")]
    public void ExtractJson_TwoArgForm_VariousJsonPaths()
    {
        var db = new KustoDatabase();
        var json = """{"Source":"Server-01","Log Level":"INFO","Details":{"Response Code":200,"User":{"Username":"kiana"}},"Tags":["startup","auth"]}""";
        var table = TestUtils.GenerateTableWithColumn(
            new List<string> { json },
            columnName: "Body",
            tableName: "Logs");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            Logs
            | extend Source = extract_json("$.Source", Body),
                     Level = extract_json("$['Log Level']", Body),
                     Code = extract_json("$.Details['Response Code']", Body),
                     User = extract_json("$.Details.User.Username", Body),
                     FirstTag = extract_json("$.Tags[0]", Body),
                     SecondTag = extract_json("$.Tags[1]", Body)
            """);

        var row = rows[0];
        Assert.That(row["Source"], Is.EqualTo("Server-01"));
        Assert.That(row["Level"], Is.EqualTo("INFO"));
        Assert.That(row["Code"], Is.EqualTo("200"));
        Assert.That(row["User"], Is.EqualTo("kiana"));
        Assert.That(row["FirstTag"], Is.EqualTo("startup"));
        Assert.That(row["SecondTag"], Is.EqualTo("auth"));
    }

    [Test]
    [Description("extract_json() - extracting nested object/array or root '$' returns JSON string")]
    public void ExtractJson_NestedObjectArrayAndRoot()
    {
        var db = new KustoDatabase();
        var json = """{"meta":{"a":1,"b":"x"},"nums":[10,20]}""";
        var table = TestUtils.GenerateTableWithColumn(
            new List<string> { json },
            columnName: "Body",
            tableName: "Docs");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            Docs
            | extend Meta = extract_json("$.meta", Body),
                     Nums = extract_json("$.nums", Body),
                     Root = extract_json("$", Body)
            """);

        NeonTestUtils.AssertJsonEquivalent((string)rows[0]["Meta"]!, """{"a":1,"b":"x"}""");
        NeonTestUtils.AssertJsonEquivalent((string)rows[0]["Nums"]!, "[10,20]");
        NeonTestUtils.AssertJsonEquivalent((string)rows[0]["Root"]!, json);
    }

    [Test]
    [Description("extract_json() - 3-arg form with typeof(...) converts extracted value to target KQL type")]
    public void ExtractJson_ThreeArgForm_WithTypeOf()
    {
        var db = new KustoDatabase();
        var json = """{"i":42,"l":3000000000,"r":54.21,"b":true,"s":"ok","dt":"2023-07-25T09:15:32Z","obj":{"k":9}}""";
        var table = TestUtils.GenerateTableWithColumn(
            new List<string> { json },
            columnName: "Body",
            tableName: "TypedJson");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            TypedJson
            | extend I = extract_json("$.i", Body, typeof(int)),
                     L = extract_json("$.l", Body, typeof(long)),
                     R = extract_json("$.r", Body, typeof(real)),
                     D = extract_json("$.r", Body, typeof(double)),
                     B = extract_json("$.b", Body, typeof(bool)),
                     S = extract_json("$.s", Body, typeof(string)),
                     Dt = extract_json("$.dt", Body, typeof(datetime)),
                     Obj = extract_json("$.obj", Body, typeof(dynamic))
            | extend ObjK = toint(Obj.k)
            """);

        var row = rows[0];
        Assert.That(row["I"], Is.EqualTo(42));
        Assert.That(row["L"], Is.EqualTo(3_000_000_000L));
        Assert.That((double)row["R"]!, Is.EqualTo(54.21).Within(1e-9));
        Assert.That((double)row["D"]!, Is.EqualTo(54.21).Within(1e-9));
        Assert.That(row["B"], Is.EqualTo(true));
        Assert.That(row["S"], Is.EqualTo("ok"));
        Assert.That(row["Dt"], Is.EqualTo(new DateTime(2023, 7, 25, 9, 15, 32, DateTimeKind.Utc)));
        Assert.That(row["ObjK"], Is.EqualTo(9));
    }

    [Test]
    [Description("extract_json() - accepts dynamic input directly as well as string input")]
    public void ExtractJson_DynamicDataSource()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<int>(
            db,
            """print d = parse_json('{"count":77}') | extend c = extract_json("$.count", d, typeof(int)) | project c""",
            "c");

        Assert.That(actual, Is.EqualTo(77));
    }

    [Test]
    [Description("extract_json() - missing path, out-of-bounds index, type mismatch, JSON null, and invalid JSON")]
    public void ExtractJson_MissingInvalidAndTypeMismatch()
    {
        var db = new KustoDatabase();

        // Missing path / out-of-bounds / invalid JSON with typed typeof -> null
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, """print extract_json("$.missing", '{"a":1}', typeof(int))"""),
            Is.Null);
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, """print extract_json("$.arr[5]", '{"arr":[1,2]}', typeof(int))"""),
            Is.Null);
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, """print extract_json("$.a", '{"a":"not_an_int"}', typeof(int))"""),
            Is.Null);
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, """print extract_json("$.a", '{"a":null}', typeof(int))"""),
            Is.Null);
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, """print extract_json("$.a", 'not json', typeof(int))"""),
            Is.Null);

        // 2-arg string form returns empty string (or null) when missing/null/invalid
        Assert.That(
            string.IsNullOrEmpty(NeonTestUtils.ExecuteAndGetSingleNullableString(db, """print extract_json("$.missing", '{"a":1}')""")),
            Is.True);
        Assert.That(
            string.IsNullOrEmpty(NeonTestUtils.ExecuteAndGetSingleNullableString(db, """print extract_json("$.a", 'not json')""")),
            Is.True);
    }

    [Test]
    [Description("extractjson() - alias behaves identically to extract_json() and works in where filters")]
    public void ExtractJson_AliasAndWhereFilter()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, """{"level":"INFO","code":200}"""),
                (2, """{"level":"ERROR","code":500}"""),
                (3, """{"level":"ERROR","code":503}""")
            },
            columnName1: "Id",
            columnName2: "Payload",
            tableName: "Alerts");
        db.AddTable(table);

        var ids = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            "Id",
            db,
            """Alerts | where extractjson("$.level", Payload) == "ERROR" and extract_json("$.code", Payload, typeof(int)) >= 503 | project Id""");

        Assert.That(ids, Is.EquivalentTo(new List<int> { 3 }));
    }

    [Test]
    [Description("extract_json() - wrong argument count returns execution error")]
    public void ExtractJson_InvalidArity_ReturnsError()
    {
        var db = new KustoDatabase();

        NeonTestUtils.ExecuteAndExpectError(db, "print extract_json()");
        NeonTestUtils.ExecuteAndExpectError(db, "print extract_json('$.a')");
        NeonTestUtils.ExecuteAndExpectError(db, "print extract_json('$.a', '{}', typeof(int), 'extra')");
    }

    #endregion

    #region pack_all() Tests

    [Test]
    [Description("pack_all() - packs all columns of each row into a dynamic property bag")]
    public void PackAll_Default_PacksAllColumns()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith3Columns(
            new List<(int, string, double)>
            {
                (1, "alpha", 12.5),
                (2, "beta", 0.0)
            },
            columnName1: "Id",
            columnName2: "Name",
            columnName3: "Score",
            tableName: "Items");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            Items
            | extend Packed = pack_all()
            | extend PackedStr = tostring(Packed),
                     FromBagName = tostring(Packed.Name),
                     FromBagId = toint(Packed.Id)
            | sort by Id asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        NeonTestUtils.AssertJsonEquivalent((string)rows[0]["PackedStr"]!, """{"Id":1,"Name":"alpha","Score":12.5}""");
        Assert.That(rows[0]["FromBagName"], Is.EqualTo("alpha"));
        Assert.That(rows[0]["FromBagId"], Is.EqualTo(1));

        NeonTestUtils.AssertJsonEquivalent((string)rows[1]["PackedStr"]!, """{"Id":2,"Name":"beta","Score":0.0}""");
        Assert.That(rows[1]["FromBagName"], Is.EqualTo("beta"));
        Assert.That(rows[1]["FromBagId"], Is.EqualTo(2));
    }

    [Test]
    [Description("pack_all(false) - includes null and empty-string columns just like pack_all()")]
    public void PackAll_False_IncludesNullAndEmptyColumns()
    {
        var db = new KustoDatabase();
        var table = NeonTestUtils.CreateTable(
            "Sparse",
            [
                new Column<int>("Id", isNullable: false),
                new Column<string>("Label", isNullable: true),
                new Column<int>("Count", isNullable: true)
            ],
            [
                new() { ["Id"] = 1, ["Label"] = "", ["Count"] = null }
            ]);
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "Sparse | extend DefaultBag = tostring(pack_all()), ExplicitFalse = tostring(pack_all(false))");

        NeonTestUtils.AssertJsonEquivalent((string)rows[0]["DefaultBag"]!, """{"Id":1,"Label":"","Count":null}""");
        NeonTestUtils.AssertJsonEquivalent((string)rows[0]["ExplicitFalse"]!, """{"Id":1,"Label":"","Count":null}""");
    }

    [Test]
    [Description("pack_all(true) - excludes null and empty-string columns while keeping 0, 0.0, false, and whitespace")]
    public void PackAll_True_ExcludesNullAndEmptyButKeepsFalsyNonNullValues()
    {
        var db = new KustoDatabase();
        var table = NeonTestUtils.CreateTable(
            "MixedNulls",
            [
                new Column<int>("Id", isNullable: false),
                new Column<string>("EmptyStr", isNullable: false),
                new Column<string>("NullStr", isNullable: true),
                new Column<string>("SpaceStr", isNullable: false),
                new Column<int>("ZeroInt", isNullable: true),
                new Column<int>("NullInt", isNullable: true),
                new Column<double>("ZeroReal", isNullable: true),
                new Column<bool>("FalseBool", isNullable: true),
                new Column<bool>("NullBool", isNullable: true)
            ],
            [
                new()
                {
                    ["Id"] = 1,
                    ["EmptyStr"] = "",
                    ["NullStr"] = null,
                    ["SpaceStr"] = " ",
                    ["ZeroInt"] = 0,
                    ["NullInt"] = null,
                    ["ZeroReal"] = 0.0,
                    ["FalseBool"] = false,
                    ["NullBool"] = null
                }
            ]);
        db.AddTable(table);

        var actualJson = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "MixedNulls | extend Bag = tostring(pack_all(true)) | project Bag",
            "Bag");

        NeonTestUtils.AssertJsonEquivalent(
            actualJson,
            """{"Id":1,"SpaceStr":" ","ZeroInt":0,"ZeroReal":0.0,"FalseBool":false}""");
    }

    [Test]
    [Description("pack_all(true) - row where all columns are null or empty produces empty JSON object {}")]
    public void PackAll_True_AllNullOrEmptyRow_ProducesEmptyObject()
    {
        var db = new KustoDatabase();
        var table = NeonTestUtils.CreateTable(
            "AllEmpty",
            [
                new Column<string>("A", isNullable: true),
                new Column<int>("B", isNullable: true),
                new Column<string>("C", isNullable: false)
            ],
            [
                new() { ["A"] = null, ["B"] = null, ["C"] = "" }
            ]);
        db.AddTable(table);

        var actualJson = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "AllEmpty | extend Bag = tostring(pack_all(true)) | project Bag",
            "Bag");

        NeonTestUtils.AssertJsonEquivalent(actualJson, "{}");
    }

    [Test]
    [Description("pack_all() - reflects preceding extend, project subset, and column renames")]
    public void PackAll_RespectsPrecedingExtendAndProject()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith3Columns(
            new List<(int, string, int)>
            {
                (1, "widget", 5)
            },
            columnName1: "Id",
            columnName2: "Item",
            columnName3: "Qty",
            tableName: "Orders");
        db.AddTable(table);

        var afterExtend = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "Orders | extend DoubleQty = toint(Qty * 2) | extend Bag = tostring(pack_all()) | project Bag",
            "Bag");
        NeonTestUtils.AssertJsonEquivalent(afterExtend, """{"Id":1,"Item":"widget","Qty":5,"DoubleQty":10}""");

        var afterProjectRename = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "Orders | project ProductName = Item, Qty | extend Bag = tostring(pack_all()) | project Bag",
            "Bag");
        NeonTestUtils.AssertJsonEquivalent(afterProjectRename, """{"ProductName":"widget","Qty":5}""");
    }

    [Test]
    [Description("pack_all() - roundtrips cleanly with extract_json() / dynamic access and reports dynamic schema in getschema")]
    public void PackAll_RoundtripWithExtractJsonAndGetSchema()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)>
            {
                ("eastus", 42)
            },
            columnName1: "Region",
            columnName2: "Nodes",
            tableName: "Clusters");
        db.AddTable(table);

        var extractedNodes = NeonTestUtils.ExecuteAndGetSingleValue<int>(
            db,
            """Clusters | extend Bag = pack_all() | extend N = extract_json("$.Nodes", tostring(Bag), typeof(int)) | project N""",
            "N");
        Assert.That(extractedNodes, Is.EqualTo(42));

        var directNodes = NeonTestUtils.ExecuteAndGetSingleValue<int>(
            db,
            "Clusters | extend Bag = pack_all() | extend N = toint(Bag.Nodes) | project N",
            "N");
        Assert.That(directNodes, Is.EqualTo(42));

        var schemaRows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "Clusters | extend Bag = pack_all() | project Bag | getschema");
        Assert.That(schemaRows, Has.Count.EqualTo(1));
        Assert.That(schemaRows[0]["ColumnName"], Is.EqualTo("Bag"));
        Assert.That(schemaRows[0]["DataType"], Is.EqualTo("System.Object"));
        Assert.That(schemaRows[0]["ColumnType"], Is.EqualTo("dynamic"));
    }

    [Test]
    [Description("pack_all() - invalid arguments return execution error")]
    public void PackAll_InvalidArguments_ReturnsError()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWithColumn(new List<int> { 1 }, columnName: "A", tableName: "T");
        db.AddTable(table);

        NeonTestUtils.ExecuteAndExpectError(db, "T | extend b = pack_all(true, false)");
        NeonTestUtils.ExecuteAndExpectError(db, "T | extend b = pack_all('yes')");
    }

    #endregion
}
