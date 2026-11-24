using static KustoPlayground.Core.Tests.DynamicNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// TDD specification tests for:
/// - First-class dynamic column type and database support (nulls, scalars, arrays, bags, nesting, Kusto types)
/// - dynamic(...) literals and parse_json() (including malformed JSON fallback and aliases)
/// - Nested property and array access (dot, bracket, dynamic key/index expressions, negative indexes, missing/out-of-range)
/// </summary>
public class DynamicNeonDataTypesLiteralsParsingAccessTests
{
    private static readonly DateTime SampleUtc = new(2025, 8, 23, 6, 20, 0, DateTimeKind.Utc);
    private static readonly TimeSpan SampleSpan = new(1, 2, 3, 4);
    private static readonly Guid SampleGuid = new("74be27de-1e4e-49d9-b579-fe0b331d3642");

    // =========================================================================
    // 1. Dynamic Column Type & Database Support
    // =========================================================================

    [Test]
    public void DynamicColumn_StoresNullsScalarsArraysBagsAndNestedKustoTypes()
    {
        var nestedBag = Bag(
            ("longVal", 9_000_000_000_000L),
            ("dtVal", SampleUtc),
            ("tsVal", SampleSpan),
            ("guidVal", SampleGuid),
            ("nestedArr", Arr(1, "two", true, null, Bag(("leaf", 3.5))))
        );

        var table = CreateKeyedDynamicTable("Events", [
            (1, null),
            (2, "hello"),
            (3, 9_000_000_000_000L),
            (4, 3.14),
            (5, true),
            (6, SampleUtc),
            (7, SampleSpan),
            (8, SampleGuid),
            (9, Arr(1, "mixed", false, null, SampleUtc, SampleGuid)),
            (10, nestedBag)
        ], shuffle: false);

        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Events
            | extend Kind = gettype(Payload), IsNullVal = isnull(Payload)
            | sort by Id asc
            | project Id, Kind, IsNullVal
            """);

        Assert.That(rows, Has.Count.EqualTo(10));
        Assert.That(rows[0]["Kind"], Is.EqualTo("null"));
        Assert.That(rows[0]["IsNullVal"], Is.EqualTo(true));
        Assert.That(rows[1]["Kind"], Is.EqualTo("string"));
        Assert.That(rows[1]["IsNullVal"], Is.EqualTo(false));
        Assert.That(rows[2]["Kind"], Is.EqualTo("long"));
        Assert.That(rows[3]["Kind"], Is.EqualTo("double"));
        Assert.That(rows[4]["Kind"], Is.EqualTo("bool"));
        Assert.That(rows[5]["Kind"], Is.EqualTo("datetime"));
        Assert.That(rows[6]["Kind"], Is.EqualTo("timespan"));
        Assert.That(rows[7]["Kind"], Is.EqualTo("guid"));
        Assert.That(rows[8]["Kind"], Is.EqualTo("array"));
        Assert.That(rows[9]["Kind"], Is.EqualTo("dictionary"));

        var nestedRow = ExecuteSingleRow(db, """
            Events
            | where Id == 10
            | project
                LongType = gettype(Payload.longVal),
                LongVal = tolong(Payload.longVal),
                DtType = gettype(Payload.dtVal),
                DtVal = todatetime(Payload.dtVal),
                TsType = gettype(Payload.tsVal),
                TsVal = totimespan(Payload.tsVal),
                GuidType = gettype(Payload.guidVal),
                GuidVal = toguid(Payload.guidVal),
                LeafVal = toreal(Payload.nestedArr[4].leaf)
            """);

        Assert.That(nestedRow["LongType"], Is.EqualTo("long"));
        AssertLongEqual(nestedRow["LongVal"], 9_000_000_000_000L);
        Assert.That(nestedRow["DtType"], Is.EqualTo("datetime"));
        AssertDateTimeEqual(nestedRow["DtVal"], SampleUtc);
        Assert.That(nestedRow["TsType"], Is.EqualTo("timespan"));
        AssertTimeSpanEqual(nestedRow["TsVal"], SampleSpan);
        Assert.That(nestedRow["GuidType"], Is.EqualTo("guid"));
        AssertGuidEqual(nestedRow["GuidVal"], SampleGuid);
        AssertNumericEqual(nestedRow["LeafVal"], 3.5);
    }

    [Test]
    public void DynamicColumn_GetSchema_ReportsDynamicColumnTypeForColumnLiteralParseAndAccess()
    {
        var table = CreateKeyedDynamicTable("T", [
            (1, Bag(("user", Bag(("id", 10L))), ("items", Arr("a", "b"))))
        ]);
        var db = CreateDatabase(table);

        var schemaRows = Execute(db, """
            T
            | extend
                Lit = dynamic({"k": 1}),
                Parsed = parse_json('{"x": 2}'),
                Prop = Payload.user,
                Elem = Payload.items[0]
            | getschema
            """);

        AssertSchemaColumn(schemaRows, "Id", "int");
        AssertSchemaColumn(schemaRows, "Payload", "dynamic", "System.Object");
        AssertSchemaColumn(schemaRows, "Lit", "dynamic", "System.Object");
        AssertSchemaColumn(schemaRows, "Parsed", "dynamic", "System.Object");
        AssertSchemaColumn(schemaRows, "Prop", "dynamic", "System.Object");
        AssertSchemaColumn(schemaRows, "Elem", "dynamic", "System.Object");
    }

    [Test]
    public void DynamicColumn_TableBuilderFromJson_LoadsDynamicColumnWithObjectsArraysScalarsAndNull()
    {
        string json = """
            {
              "Name": "Telemetry",
              "Columns": [
                { "Name": "Id", "Type": "int", "Nullable": false },
                { "Name": "Payload", "Type": "dynamic", "Nullable": true }
              ],
              "Rows": [
                { "Id": 1, "Payload": { "user": { "name": "Alice" }, "scores": [10, 20, 30] } },
                { "Id": 2, "Payload": [1, "two", true, null] },
                { "Id": 3, "Payload": "raw-scalar" },
                { "Id": 4, "Payload": null }
              ]
            }
            """;

        Table table = BuildTableFromJson(json);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Telemetry
            | sort by Id asc
            | project
                Id,
                Kind = gettype(Payload),
                UserName = tostring(Payload.user.name),
                LastScore = tolong(Payload.scores[-1]),
                SecondElem = tostring(Payload[1])
            """);

        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows[0]["Kind"], Is.EqualTo("dictionary"));
        Assert.That(rows[0]["UserName"], Is.EqualTo("Alice"));
        AssertLongEqual(rows[0]["LastScore"], 30L);

        Assert.That(rows[1]["Kind"], Is.EqualTo("array"));
        Assert.That(rows[1]["SecondElem"], Is.EqualTo("two"));

        Assert.That(rows[2]["Kind"], Is.EqualTo("string"));
        Assert.That(rows[3]["Kind"], Is.EqualTo("null"));
    }

    [Test]
    public void DynamicColumn_DatatableWithDynamicLiterals_PreservesMixedRowsAndNestedKustoTypes()
    {
        var db = CreateDatabase();

        var rows = Execute(db, """
            datatable(Id:int, Payload:dynamic)
            [
                1, dynamic({"ts": datetime(2025-08-23T06:20:00Z), "dur": timespan(01:02:03), "id": guid(74be27de-1e4e-49d9-b579-fe0b331d3642)}),
                2, dynamic([10, 20, 30]),
                3, dynamic(null),
                4, dynamic("just a string")
            ]
            | sort by Id asc
            | project
                Id,
                Kind = gettype(Payload),
                Ts = todatetime(Payload.ts),
                Dur = totimespan(Payload.dur),
                G = toguid(Payload.id),
                First = tolong(Payload[0])
            """);

        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows[0]["Kind"], Is.EqualTo("dictionary"));
        AssertDateTimeEqual(rows[0]["Ts"], SampleUtc);
        AssertTimeSpanEqual(rows[0]["Dur"], new TimeSpan(1, 2, 3));
        AssertGuidEqual(rows[0]["G"], SampleGuid);

        Assert.That(rows[1]["Kind"], Is.EqualTo("array"));
        AssertLongEqual(rows[1]["First"], 10L);

        Assert.That(rows[2]["Kind"], Is.EqualTo("null"));
        Assert.That(rows[3]["Kind"], Is.EqualTo("string"));
    }

    [Test]
    public void DynamicColumn_PreservedThroughUnionAndProjectAndSort()
    {
        var left = CreateKeyedDynamicTable("LeftT", [
            (2, Bag(("env", "dev"), ("tags", Arr("worker")))),
            (1, Bag(("env", "prod"), ("tags", Arr("api", "web"))))
        ]);
        var right = CreateKeyedDynamicTable("RightT", [
            (3, Bag(("env", "stage"), ("tags", Arr("batch"))))
        ], dynamicColumn: "Meta");

        var db = CreateDatabase(left, right);

        var unionRows = Execute(db, """
            LeftT
            | project Id, Data = Payload
            | union (RightT | project Id, Data = Meta)
            | sort by Id asc
            | project Id, Env = tostring(Data.env), Tag0 = tostring(Data.tags[0])
            """);

        Assert.That(unionRows, Has.Count.EqualTo(3));
        AssertLongEqual(unionRows[0]["Id"], 1);
        Assert.That(unionRows[0]["Env"], Is.EqualTo("prod"));
        Assert.That(unionRows[0]["Tag0"], Is.EqualTo("api"));
        AssertLongEqual(unionRows[1]["Id"], 2);
        Assert.That(unionRows[1]["Env"], Is.EqualTo("dev"));
        Assert.That(unionRows[1]["Tag0"], Is.EqualTo("worker"));
        AssertLongEqual(unionRows[2]["Id"], 3);
        Assert.That(unionRows[2]["Env"], Is.EqualTo("stage"));
        Assert.That(unionRows[2]["Tag0"], Is.EqualTo("batch"));
    }

    [Test]
    public void DynamicColumn_PreservedThroughJoin()
    {
        var left = CreateKeyedDynamicTable("LeftT", [
            (1, Bag(("env", "prod"), ("tags", Arr("api", "web")))),
            (2, Bag(("env", "dev"), ("tags", Arr("worker"))))
        ]);
        var right = CreateKeyedDynamicTable("RightT", [
            (1, Bag(("owner", "team-a"))),
            (3, Bag(("owner", "team-c")))
        ], dynamicColumn: "Meta");

        var db = CreateDatabase(left, right);

        var joined = ExecuteSingleRow(db, """
            LeftT
            | join kind=inner RightT on Id
            | project Id, Env = tostring(Payload.env), Owner = tostring(Meta.owner), Tag0 = tostring(Payload.tags[0])
            """);

        AssertLongEqual(joined["Id"], 1);
        Assert.That(joined["Env"], Is.EqualTo("prod"));
        Assert.That(joined["Owner"], Is.EqualTo("team-a"));
        Assert.That(joined["Tag0"], Is.EqualTo("api"));
    }

    // =========================================================================
    // 2. Dynamic Literals (dynamic(...))
    // =========================================================================

    [Test]
    public void DynamicLiterals_AllPrimitiveAndEmptyContainers_PreserveTypesAndNullSemantics()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                DNull = dynamic(null),
                DBoolT = dynamic(true),
                DBoolF = dynamic(false),
                DNum = dynamic(42),
                DLong = dynamic(long(9000000000000)),
                DReal = dynamic(3.25),
                DStr = dynamic("hello"),
                DDt = dynamic(datetime(2025-08-23T06:20:00Z)),
                DTs = dynamic(timespan(1d)),
                DGuid = dynamic(guid(74be27de-1e4e-49d9-b579-fe0b331d3642)),
                DEmptyArr = dynamic([]),
                DEmptyBag = dynamic({})
            | project
                TNull = gettype(DNull), IsNullDNull = isnull(DNull),
                TBoolT = gettype(DBoolT), VBoolT = tobool(DBoolT),
                VBoolF = tobool(DBoolF),
                TNum = gettype(DNum), VNum = toint(DNum),
                TLong = gettype(DLong), VLong = tolong(DLong),
                TReal = gettype(DReal), VReal = toreal(DReal),
                TStr = gettype(DStr), VStr = tostring(DStr),
                TDt = gettype(DDt), VDt = todatetime(DDt),
                TTs = gettype(DTs), VTs = totimespan(DTs),
                TGuid = gettype(DGuid), VGuid = toguid(DGuid),
                TEmptyArr = gettype(DEmptyArr), IsNullArr = isnull(DEmptyArr),
                TEmptyBag = gettype(DEmptyBag), IsNullBag = isnull(DEmptyBag)
            """);

        Assert.That(row["TNull"], Is.EqualTo("null"));
        Assert.That(row["IsNullDNull"], Is.EqualTo(true));
        Assert.That(row["TBoolT"], Is.EqualTo("bool"));
        Assert.That(row["VBoolT"], Is.EqualTo(true));
        Assert.That(row["VBoolF"], Is.EqualTo(false));
        Assert.That(row["TNum"], Is.AnyOf("int", "long"));
        AssertLongEqual(row["VNum"], 42);
        Assert.That(row["TLong"], Is.EqualTo("long"));
        AssertLongEqual(row["VLong"], 9_000_000_000_000L);
        Assert.That(row["TReal"], Is.EqualTo("double"));
        AssertNumericEqual(row["VReal"], 3.25);
        Assert.That(row["TStr"], Is.EqualTo("string"));
        Assert.That(row["VStr"], Is.EqualTo("hello"));
        Assert.That(row["TDt"], Is.EqualTo("datetime"));
        AssertDateTimeEqual(row["VDt"], SampleUtc);
        Assert.That(row["TTs"], Is.EqualTo("timespan"));
        AssertTimeSpanEqual(row["VTs"], TimeSpan.FromDays(1));
        Assert.That(row["TGuid"], Is.EqualTo("guid"));
        AssertGuidEqual(row["VGuid"], SampleGuid);
        Assert.That(row["TEmptyArr"], Is.EqualTo("array"));
        Assert.That(row["IsNullArr"], Is.EqualTo(false));
        Assert.That(row["TEmptyBag"], Is.EqualTo("dictionary"));
        Assert.That(row["IsNullBag"], Is.EqualTo(false));
    }

    [Test]
    public void DynamicLiterals_DeepRecursiveNestingAndSingleQuotedKeysAndLetBinding()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            let cfg = dynamic({
                'service': 'billing',
                "limits": [
                    {"region": "eastus", "quotas": [100, 200, {"burst": 500, "window": timespan(5m)}]},
                    {"region": "westus", "quotas": [50]}
                ],
                "created": datetime(2025-08-23T06:20:00Z),
                "traceId": guid(74be27de-1e4e-49d9-b579-fe0b331d3642)
            });
            print
                Service = tostring(cfg.service),
                Burst = tolong(cfg.limits[0].quotas[2].burst),
                Window = totimespan(cfg.limits[0].quotas[2].window),
                WindowType = gettype(cfg.limits[0].quotas[2].window),
                SecondRegion = tostring(cfg.limits[1].region),
                CreatedType = gettype(cfg.created),
                TraceType = gettype(cfg.traceId)
            """);

        Assert.That(row["Service"], Is.EqualTo("billing"));
        AssertLongEqual(row["Burst"], 500L);
        AssertTimeSpanEqual(row["Window"], TimeSpan.FromMinutes(5));
        Assert.That(row["WindowType"], Is.EqualTo("timespan"));
        Assert.That(row["SecondRegion"], Is.EqualTo("westus"));
        Assert.That(row["CreatedType"], Is.EqualTo("datetime"));
        Assert.That(row["TraceType"], Is.EqualTo("guid"));
    }

    // =========================================================================
    // 3. parse_json() and Aliases
    // =========================================================================

    [Test]
    public void ParseJson_ValidObjectsArraysNestedPrimitivesAndNull()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                Obj = parse_json('{"a": 1, "b": 2.5, "c": true, "d": "text", "e": null, "f": [10, {"g": 9007199254740993}]}'),
                ArrVal = parse_json('[1, "two", false, null, 1.5e2]'),
                NumVal = parse_json('42'),
                BoolVal = parse_json('false'),
                StrVal = parse_json('"quoted"'),
                NullJson = parse_json('null'),
                EmptyStr = parse_json('')
            | project
                A = tolong(Obj.a),
                B = toreal(Obj.b),
                C = tobool(Obj.c),
                D = tostring(Obj.d),
                EIsNull = isnull(Obj.e),
                BigLong = tolong(Obj.f[1].g),
                Sci = toreal(ArrVal[4]),
                Num = tolong(NumVal),
                BoolV = tobool(BoolVal),
                StrV = tostring(StrVal),
                NullJsonIsNull = isnull(NullJson),
                NullJsonType = gettype(NullJson),
                EmptyIsNull = isnull(EmptyStr),
                EmptyType = gettype(EmptyStr),
                EmptyText = tostring(EmptyStr)
            """);

        AssertLongEqual(row["A"], 1L);
        AssertNumericEqual(row["B"], 2.5);
        Assert.That(row["C"], Is.EqualTo(true));
        Assert.That(row["D"], Is.EqualTo("text"));
        Assert.That(row["EIsNull"], Is.EqualTo(true));
        AssertLongEqual(row["BigLong"], 9007199254740993L);
        AssertNumericEqual(row["Sci"], 150.0);
        AssertLongEqual(row["Num"], 42L);
        Assert.That(row["BoolV"], Is.EqualTo(false));
        Assert.That(row["StrV"], Is.EqualTo("quoted"));
        Assert.That(row["NullJsonIsNull"], Is.EqualTo(true));
        Assert.That(row["NullJsonType"], Is.EqualTo("null"));
        Assert.That(row["EmptyIsNull"], Is.EqualTo(true));
        Assert.That(row["EmptyType"], Is.EqualTo("null"));
        Assert.That(row["EmptyText"], Is.EqualTo(string.Empty));
    }

    [TestCase("{broken json")]
    [TestCase("[1, 2")]
    [TestCase("not-json-at-all")]
    [TestCase("[1, 2,]")]
    [TestCase("undefined")]
    [TestCase("{\"a\": }")]
    public void ParseJson_MalformedJson_ReturnsOriginalStringAsDynamicWithoutError(string malformed)
    {
        var table = CreateTable("Raw", [
            new Column<string>("Text", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Text", malformed } }
        ]);
        var db = CreateDatabase(table);

        var row = ExecuteSingleRow(db, """
            Raw
            | extend Parsed = parse_json(Text)
            | project
                IsNullVal = isnull(Parsed),
                Kind = gettype(Parsed),
                RoundTrip = tostring(Parsed),
                MissingProp = Parsed.a
            """);

        Assert.That(row["IsNullVal"], Is.EqualTo(false), $"Malformed JSON '{malformed}' must not produce null.");
        Assert.That(row["Kind"], Is.EqualTo("string"), $"Malformed JSON '{malformed}' must be dynamic string.");
        Assert.That(row["RoundTrip"], Is.EqualTo(malformed), "Original malformed text must be preserved verbatim.");
        Assert.That(row["MissingProp"], Is.Null, "Property access on a dynamic string must return null.");
    }

    [Test]
    public void ParseJson_ColumnWithMixedValidMalformedEmptyAndNullValues()
    {
        var table = CreateTable("Logs", [
            new Column<int>("Id", isNullable: false),
            new Column<string>("Raw", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Raw", """{"ok": true, "code": 200}""" } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Raw", "[10, 20]" } },
            new Dictionary<string, object?> { { "Id", 3 }, { "Raw", "{truncated" } },
            new Dictionary<string, object?> { { "Id", 4 }, { "Raw", "" } },
            new Dictionary<string, object?> { { "Id", 5 }, { "Raw", null } },
            new Dictionary<string, object?> { { "Id", 6 }, { "Raw", "null" } }
        ], shuffle: true);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Logs
            | extend D = parse_json(Raw)
            | sort by Id asc
            | project Id, Kind = gettype(D), IsNullVal = isnull(D), Text = tostring(D), Code = tolong(D.code)
            """);

        Assert.That(rows, Has.Count.EqualTo(6));
        Assert.That(rows[0]["Kind"], Is.EqualTo("dictionary"));
        AssertLongEqual(rows[0]["Code"], 200L);
        Assert.That(rows[1]["Kind"], Is.EqualTo("array"));
        Assert.That(rows[2]["Kind"], Is.EqualTo("string"));
        Assert.That(rows[2]["Text"], Is.EqualTo("{truncated"));
        Assert.That(rows[3]["Kind"], Is.EqualTo("null"));
        Assert.That(rows[3]["IsNullVal"], Is.EqualTo(true));
        Assert.That(rows[4]["Kind"], Is.EqualTo("null"));
        Assert.That(rows[4]["IsNullVal"], Is.EqualTo(true));
        Assert.That(rows[5]["Kind"], Is.EqualTo("null"));
        Assert.That(rows[5]["IsNullVal"], Is.EqualTo(true));
    }

    [Test]
    public void ParseJson_AlreadyDynamicInput_IsReturnedAsIs_AndDoubleEncodedRequiresToString()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print outer = parse_json('{"a": 1, "inner": "{\\"nested\\": 99}"}')
            | extend
                PassThrough = parse_json(outer),
                InnerNoToString = parse_json(outer.inner),
                InnerParsed = parse_json(tostring(outer.inner))
            | project
                PassThroughA = tolong(PassThrough.a),
                InnerNoToStringType = gettype(InnerNoToString),
                InnerParsedType = gettype(InnerParsed),
                InnerNested = tolong(InnerParsed.nested)
            """);

        AssertLongEqual(row["PassThroughA"], 1L);
        Assert.That(row["InnerNoToStringType"], Is.EqualTo("string"),
            "Passing a dynamic value directly to parse_json should return it as-is without re-parsing.");
        Assert.That(row["InnerParsedType"], Is.EqualTo("dictionary"));
        AssertLongEqual(row["InnerNested"], 99L);
    }

    [Test]
    public void ParseJson_SpecialCharactersUnicodeEscapesAndAliases()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                Special = parse_json('{"a.b": "line1\\nline2", "uni": "\\u0041\\u0042", "": "empty-key", "quote": "he said \\"hi\\""}'),
                ViaParseJsonAlias = parsejson('{"v": 10}'),
                ViaToDynamic = todynamic('{"v": 20}'),
                ViaToObject = toobject('{"v": 30}')
            | project
                DotKey = tostring(Special["a.b"]),
                Uni = tostring(Special.uni),
                EmptyKey = tostring(Special[""]),
                Quote = tostring(Special.quote),
                V1 = tolong(ViaParseJsonAlias.v),
                V2 = tolong(ViaToDynamic.v),
                V3 = tolong(ViaToObject.v)
            """);

        Assert.That(row["DotKey"], Is.EqualTo("line1\nline2"));
        Assert.That(row["Uni"], Is.EqualTo("AB"));
        Assert.That(row["EmptyKey"], Is.EqualTo("empty-key"));
        Assert.That(row["Quote"], Is.EqualTo("he said \"hi\""));
        AssertLongEqual(row["V1"], 10L);
        AssertLongEqual(row["V2"], 20L);
        AssertLongEqual(row["V3"], 30L);
    }

    // =========================================================================
    // 4. Nested Property & Array Access
    // =========================================================================

    [Test]
    public void DynamicAccess_DotBracketQuotedSpecialKeysAndCaseSensitivity()
    {
        var table = CreateKeyedDynamicTable("Docs", [
            (1, Bag(
                ("user", Bag(("id", 42L), ("Name", "Upper"), ("name", "lower"))),
                ("a.b", Bag(("c.d", "dotted"))),
                ("key with spaces", "spaced"),
                ("where", "reserved-word-value"),
                ("", "empty-key-value"),
                ("0", "string-zero-key")
            ))
        ]);
        var db = CreateDatabase(table);

        var row = ExecuteSingleRow(db, """
            Docs
            | project
                DotNested = tolong(Payload.user.id),
                CaseUpper = tostring(Payload.user.Name),
                CaseLower = tostring(Payload.user.name),
                CaseMiss = Payload.user.NAME,
                BracketDouble = tostring(Payload["a.b"]["c.d"]),
                BracketSingle = tostring(Payload['a.b']['c.d']),
                DotBracket = tostring(Payload.['where']),
                Spaces = tostring(Payload["key with spaces"]),
                EmptyKey = tostring(Payload[""]),
                StringZeroKey = tostring(Payload["0"]),
                IntIndexOnBag = Payload[0]
            """);

        AssertLongEqual(row["DotNested"], 42L);
        Assert.That(row["CaseUpper"], Is.EqualTo("Upper"));
        Assert.That(row["CaseLower"], Is.EqualTo("lower"));
        Assert.That(row["CaseMiss"], Is.Null, "Property bag keys must be case-sensitive.");
        Assert.That(row["BracketDouble"], Is.EqualTo("dotted"));
        Assert.That(row["BracketSingle"], Is.EqualTo("dotted"));
        Assert.That(row["DotBracket"], Is.EqualTo("reserved-word-value"));
        Assert.That(row["Spaces"], Is.EqualTo("spaced"));
        Assert.That(row["EmptyKey"], Is.EqualTo("empty-key-value"));
        Assert.That(row["StringZeroKey"], Is.EqualTo("string-zero-key"));
        Assert.That(row["IntIndexOnBag"], Is.Null, "Numeric array indexing on a property bag must return null.");
    }

    [Test]
    public void DynamicAccess_DynamicKeyColumnAndComputedKeyExpressions()
    {
        var table = CreateTable("Lookup", [
            new Column<int>("Id", isNullable: false),
            new Column<string>("KeyCol", isNullable: true),
            new Column<object>("Bag", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "KeyCol", "alpha" }, { "Bag", Bag(("alpha", 100L), ("k1", "v1")) } },
            new Dictionary<string, object?> { { "Id", 2 }, { "KeyCol", "missing" }, { "Bag", Bag(("alpha", 100L), ("k2", "v2")) } },
            new Dictionary<string, object?> { { "Id", 3 }, { "KeyCol", null }, { "Bag", Bag(("alpha", 100L), ("k3", "v3")) } },
            new Dictionary<string, object?> { { "Id", 4 }, { "KeyCol", "a.b" }, { "Bag", Bag(("a.b", 400L), ("k4", "v4")) } }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Lookup
            | sort by Id asc
            | project
                Id,
                ByCol = Bag[KeyCol],
                ByComputed = tostring(Bag[strcat("k", tostring(Id))])
            """);

        Assert.That(rows, Has.Count.EqualTo(4));
        AssertLongEqual(rows[0]["ByCol"], 100L);
        Assert.That(rows[0]["ByComputed"], Is.EqualTo("v1"));

        Assert.That(rows[1]["ByCol"], Is.Null);
        Assert.That(rows[1]["ByComputed"], Is.EqualTo("v2"));

        Assert.That(rows[2]["ByCol"], Is.Null);
        Assert.That(rows[2]["ByComputed"], Is.EqualTo("v3"));

        AssertLongEqual(rows[3]["ByCol"], 400L);
        Assert.That(rows[3]["ByComputed"], Is.EqualTo("v4"));
    }

    [Test]
    public void DynamicAccess_ArrayIndexing_PositiveNegativeComputedAndOutOfRange()
    {
        var table = CreateTable("Arrs", [
            new Column<int>("Id", isNullable: false),
            new Column<int>("Idx", isNullable: true),
            new Column<object>("Items", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Idx", 0 }, { "Items", Arr("a", "b", "c", "d") } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Idx", 2 }, { "Items", Arr("a", "b", "c", "d") } },
            new Dictionary<string, object?> { { "Id", 3 }, { "Idx", -1 }, { "Items", Arr("a", "b", "c", "d") } },
            new Dictionary<string, object?> { { "Id", 4 }, { "Idx", -4 }, { "Items", Arr("a", "b", "c", "d") } },
            new Dictionary<string, object?> { { "Id", 5 }, { "Idx", 4 }, { "Items", Arr("a", "b", "c", "d") } },
            new Dictionary<string, object?> { { "Id", 6 }, { "Idx", -5 }, { "Items", Arr("a", "b", "c", "d") } },
            new Dictionary<string, object?> { { "Id", 7 }, { "Idx", null }, { "Items", Arr("a", "b", "c", "d") } }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Arrs
            | sort by Id asc
            | project
                Id,
                First = tostring(Items[0]),
                Last = tostring(Items[-1]),
                LastParen = tostring(Items[(-1)]),
                SecondFromEnd = tostring(Items[-2]),
                FirstViaNeg = tostring(Items[-4]),
                OutHigh = Items[99],
                OutLow = Items[-99],
                ByCol = Items[Idx],
                NextIdx = Items[(Idx + 1)]
            """);

        Assert.That(rows, Has.Count.EqualTo(7));
        Assert.That(rows[0]["First"], Is.EqualTo("a"));
        Assert.That(rows[0]["Last"], Is.EqualTo("d"));
        Assert.That(rows[0]["LastParen"], Is.EqualTo("d"));
        Assert.That(rows[0]["SecondFromEnd"], Is.EqualTo("c"));
        Assert.That(rows[0]["FirstViaNeg"], Is.EqualTo("a"));
        Assert.That(rows[0]["OutHigh"], Is.Null);
        Assert.That(rows[0]["OutLow"], Is.Null);

        Assert.That(rows[0]["ByCol"], Is.EqualTo("a"));
        Assert.That(rows[0]["NextIdx"], Is.EqualTo("b"));

        Assert.That(rows[1]["ByCol"], Is.EqualTo("c"));
        Assert.That(rows[1]["NextIdx"], Is.EqualTo("d"));

        Assert.That(rows[2]["ByCol"], Is.EqualTo("d"));
        Assert.That(rows[2]["NextIdx"], Is.EqualTo("a")); // (-1 + 1) == 0

        Assert.That(rows[3]["ByCol"], Is.EqualTo("a"));
        Assert.That(rows[3]["NextIdx"], Is.EqualTo("b")); // (-4 + 1) == -3 -> "b"

        Assert.That(rows[4]["ByCol"], Is.Null); // index 4 out of bounds
        Assert.That(rows[5]["ByCol"], Is.Null); // index -5 out of bounds
        Assert.That(rows[6]["ByCol"], Is.Null); // null index
        Assert.That(rows[6]["NextIdx"], Is.Null);
    }

    [Test]
    public void DynamicAccess_ChainedNestedArraysAndBags_AndNullSafetyOnWrongTypes()
    {
        var table = CreateKeyedDynamicTable("Complex", [
            (1, Bag(
                ("users", Arr(
                    Bag(("name", "Alice"), ("roles", Arr("reader", "admin"))),
                    Bag(("name", "Bob"), ("roles", Arr()))
                )),
                ("matrix", Arr(Arr(10L, 20L), Arr(30L, 40L))),
                ("emptyVal", null)
            )),
            (2, null),
            (3, "plain-scalar")
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Complex
            | sort by Id asc
            | project
                Id,
                AliceLastRole = tostring(Payload.users[0].roles[-1]),
                BobRole0 = Payload.users[1].roles[0],
                MatrixVal = tolong(Payload.matrix[1][-2]),
                MissingDeep = Payload.users[5].roles[0].deep,
                ArrayAsBag = Payload.users.name,
                NullProp = Payload.emptyVal,
                NullPropChild = Payload.emptyVal.child,
                ScalarIndex = Payload[0]
            """);

        Assert.That(rows, Has.Count.EqualTo(3));

        Assert.That(rows[0]["AliceLastRole"], Is.EqualTo("admin"));
        Assert.That(rows[0]["BobRole0"], Is.Null);
        AssertLongEqual(rows[0]["MatrixVal"], 30L);
        Assert.That(rows[0]["MissingDeep"], Is.Null);
        Assert.That(rows[0]["ArrayAsBag"], Is.Null);
        Assert.That(rows[0]["NullProp"], Is.Null);
        Assert.That(rows[0]["NullPropChild"], Is.Null);
        Assert.That(rows[0]["ScalarIndex"], Is.Null);

        // Row 2: Payload is null
        Assert.That(rows[1]["AliceLastRole"], Is.EqualTo(string.Empty));
        Assert.That(rows[1]["MatrixVal"], Is.Null);
        Assert.That(rows[1]["ScalarIndex"], Is.Null);

        // Row 3: Payload is scalar string
        Assert.That(rows[2]["AliceLastRole"], Is.EqualTo(string.Empty));
        Assert.That(rows[2]["MatrixVal"], Is.Null);
        Assert.That(rows[2]["ScalarIndex"], Is.Null);
    }
}
