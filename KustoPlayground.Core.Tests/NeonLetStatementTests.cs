namespace KustoPlayground.Core.Tests;

public class NeonLetStatementTests
{
    #region Scalar Variables & Expressions

    [Test]
    [Description("let - binds scalar variables of all core types and uses them in print")]
    public void Let_ScalarVariables_AllBasicTypes()
    {
        var db = new KustoDatabase();

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            let i = int(10);
            let l = long(3000000000);
            let r = 2.5;
            let s = "hello";
            let b = true;
            let dt = datetime(2025-03-15 12:00:00);
            let ts = time(15m);
            print i = i, l = l, r = r, s = s, b = b, dt = dt, ts = ts
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        var row = rows[0];
        Assert.That(row["i"], Is.EqualTo(10));
        Assert.That(row["l"], Is.EqualTo(3_000_000_000L));
        Assert.That((double)row["r"]!, Is.EqualTo(2.5).Within(1e-9));
        Assert.That(row["s"], Is.EqualTo("hello"));
        Assert.That(row["b"], Is.EqualTo(true));
        Assert.That(row["dt"], Is.EqualTo(new DateTime(2025, 3, 15, 12, 0, 0, DateTimeKind.Utc)));
        Assert.That(row["ts"], Is.EqualTo(TimeSpan.FromMinutes(15)));
    }

    [Test]
    [Description("let - chained scalar expressions referencing earlier let variables")]
    public void Let_ChainedScalarExpressions()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleObject(
            db,
            """
            let a = 10;
            let b = a * 2;
            let c = b + 5;
            print res = c
            """,
            "res");

        NeonTestUtils.AssertNumericEqual(actual, 25L);
    }

    [Test]
    [Description("let - scalar variables used in where, extend, take, and scalar functions")]
    public void Let_ScalarVariablesUsedInTableOperators()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, double>(
            "EventType",
            "AdjustedDamage",
            db,
            """
            let targetState = "FLORIDA";
            let minDamage = 5000;
            let multiplier = 1.5;
            let maxRows = 1;
            StormEvents
            | where State == targetState and DamageProperty >= minDamage
            | extend AdjustedDamage = DamageProperty * multiplier
            | sort by AdjustedDamage desc
            | take maxRows
            | project EventType, AdjustedDamage
            """);

        Assert.That(actual, Is.EqualTo(new List<(string, double)>
        {
            ("Hurricane", 30000.0)
        }));
    }

    [Test]
    [Description("let - escaped bracketed identifier names (official doc syntax)")]
    public void Let_EscapedBracketedVariableNames()
    {
        var db = new KustoDatabase();

        var numResult = NeonTestUtils.ExecuteAndGetSingleObject(
            db,
            """
            let ['some number'] = 20;
            print x = ['some number'] + 5
            """,
            "x");
        NeonTestUtils.AssertNumericEqual(numResult, 25L);

        var strResult = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            """
            let ["greeting text"] = "hello";
            print msg = strcat(["greeting text"], " kusto")
            """,
            "msg");
        Assert.That(strResult, Is.EqualTo("hello kusto"));
    }

    [Test]
    [Description("let - dynamic scalar variable from parse_json() used in downstream query")]
    public void Let_DynamicScalarVariable()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<int>(
            db,
            """
            let cfg = parse_json('{"threshold":42,"label":"high"}');
            print val = toint(cfg.threshold)
            """,
            "val");

        Assert.That(actual, Is.EqualTo(42));
    }

    #endregion

    #region Tabular Variables & View

    [Test]
    [Description("let - tabular variable binds filtered table expression and supports further piping")]
    public void Let_TabularVariable_BasicAndChained()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var actual = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "EventType",
            db,
            """
            let FloridaEvents = StormEvents | where State == "FLORIDA";
            let HighDamageFlorida = FloridaEvents | where DamageProperty > 10000;
            HighDamageFlorida | project EventType
            """);

        Assert.That(actual, Is.EquivalentTo(new List<string> { "Hurricane" }));
    }

    [Test]
    [Description("let - shadowing a table name with a filtered tabular let variable")]
    public void Let_ShadowingTableName()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var count = NeonTestUtils.ExecuteAndGetSingleValue<long>(
            db,
            """
            let StormEvents = StormEvents | where State == "TEXAS";
            StormEvents | count
            """,
            "Count");

        Assert.That(count, Is.EqualTo(1L));
    }

    [Test]
    [Description("let - view () syntax creates a query-defined virtual table")]
    public void Let_ViewSyntax_CreatesVirtualTable()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var actual = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "EventType",
            db,
            """
            let targetState = "FLORIDA";
            let FloridaView = view () { StormEvents | where State == targetState };
            FloridaView | sort by EventType asc | project EventType
            """);

        Assert.That(actual, Is.EqualTo(new List<string> { "Hurricane", "Tornado" }));
    }

    #endregion

    #region Scalar User-Defined Functions (UDFs)

    [Test]
    [Description("let - zero-parameter and single-parameter scalar UDFs")]
    public void Let_ScalarUdf_ZeroAndSingleParameter()
    {
        var db = new KustoDatabase();

        var zeroParam = NeonTestUtils.ExecuteAndGetSingleObject(
            db,
            """
            let GetMagic = () { 42 };
            print res = GetMagic()
            """,
            "res");
        NeonTestUtils.AssertNumericEqual(zeroParam, 42L);

        var singleParam = NeonTestUtils.ExecuteAndGetSingleObject(
            db,
            """
            let DoubleIt = (n:long) { n * 2 };
            print res = DoubleIt(21)
            """,
            "res");
        NeonTestUtils.AssertNumericEqual(singleParam, 42L);
    }

    [Test]
    [Description("let - scalar UDF with multiple parameters applied across table rows in extend and where")]
    public void Let_ScalarUdf_MultiParamInExtendAndWhere()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith3Columns(
            new List<(int, int, int)>
            {
                (1, 10, 5),
                (2, 20, 3),
                (3, 4, 2)
            },
            columnName1: "Id",
            columnName2: "Price",
            columnName3: "Qty",
            tableName: "LineItems");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            let ComputeTotal = (price:int, qty:int) { price * qty };
            LineItems
            | extend Total = ComputeTotal(Price, Qty)
            | where ComputeTotal(Price, Qty) >= 50
            | sort by Id asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Id"], Is.EqualTo(1));
        NeonTestUtils.AssertNumericEqual(rows[0]["Total"], 50L);
        Assert.That(rows[1]["Id"], Is.EqualTo(2));
        NeonTestUtils.AssertNumericEqual(rows[1]["Total"], 60L);
    }

    [Test]
    [Description("let - scalar UDF with default parameter values (official doc pattern)")]
    public void Let_ScalarUdf_DefaultParameterValues()
    {
        var db = new KustoDatabase();

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            let Calc = (a:long, b:long = 10, c:long = 100) { a + b + c };
            print one = Calc(1), two = Calc(1, 2), three = Calc(1, 2, 3)
            """);

        NeonTestUtils.AssertNumericEqual(rows[0]["one"], 111L);
        NeonTestUtils.AssertNumericEqual(rows[0]["two"], 103L);
        NeonTestUtils.AssertNumericEqual(rows[0]["three"], 6L);

        var strRows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            let Wrap = (s:string, prefix:string = "[", suffix:string = "]") { strcat(prefix, s, suffix) };
            print a = Wrap("ok"), b = Wrap("ok", "<", ">")
            """);

        Assert.That(strRows[0]["a"], Is.EqualTo("[ok]"));
        Assert.That(strRows[0]["b"], Is.EqualTo("<ok>"));
    }

    [Test]
    [Description("let - scalar UDF invoked with named arguments and out-of-order named arguments")]
    public void Let_ScalarUdf_NamedArguments()
    {
        var db = new KustoDatabase();

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            let FormatScore = (name:string, points:long = 0, bonus:long = 5) {
                strcat(name, ":", points + bonus)
            };
            print
                namedAll = FormatScore(bonus=10, name="Ada", points=90),
                mixedPositionalAndNamed = FormatScore("Alan", bonus=20)
            """);

        Assert.That(rows[0]["namedAll"], Is.EqualTo("Ada:100"));
        Assert.That(rows[0]["mixedPositionalAndNamed"], Is.EqualTo("Alan:20"));
    }

    [Test]
    [Description("let - nested let inside scalar UDF body, local shadowing without mutating outer scope, and UDF composition")]
    public void Let_ScalarUdf_NestedLetLocalShadowingAndComposition()
    {
        var db = new KustoDatabase();

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            let x = 10;
            let AddLocal = (a:long) {
                let x = 100;
                let doubled = a * 2;
                doubled + x
            };
            let UseOuter = (a:long) { a + x };
            let Composed = (a:long) { UseOuter(AddLocal(a)) };
            print outerX = x, localRes = AddLocal(5), outerRes = UseOuter(5), compRes = Composed(5)
            """);

        NeonTestUtils.AssertNumericEqual(rows[0]["outerX"], 10L);
        NeonTestUtils.AssertNumericEqual(rows[0]["localRes"], 110L);
        NeonTestUtils.AssertNumericEqual(rows[0]["outerRes"], 15L);
        NeonTestUtils.AssertNumericEqual(rows[0]["compRes"], 120L);
    }

    #endregion

    #region Tabular User-Defined Functions (UDFs) & invoke

    [Test]
    [Description("let - zero-parameter and scalar-parameter tabular UDFs")]
    public void Let_TabularUdf_ZeroAndScalarParameters()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var zeroParamCount = NeonTestUtils.ExecuteAndGetSingleValue<long>(
            db,
            """
            let FloridaOnly = () { StormEvents | where State == "FLORIDA" };
            FloridaOnly() | count
            """,
            "Count");
        Assert.That(zeroParamCount, Is.EqualTo(2L));

        var filteredEvents = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "EventType",
            db,
            """
            let ByMinDamage = (minDmg:int) { StormEvents | where DamageProperty > minDmg };
            ByMinDamage(10000) | project EventType
            """);
        Assert.That(filteredEvents, Is.EquivalentTo(new List<string> { "Hurricane" }));
    }

    [Test]
    [Description("let - tabular UDF with explicit table schema parameter and default scalar argument")]
    public void Let_TabularUdf_ExplicitTableSchemaAndDefaultArgument()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var withDefault = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "EventType",
            db,
            """
            let FilterByStateAndDamage = (T:(State:string, EventType:string, DamageProperty:int), state:string, minDmg:int = 5000) {
                T | where State == state and DamageProperty >= minDmg
            };
            FilterByStateAndDamage(StormEvents, "FLORIDA") | sort by EventType asc | project EventType
            """);
        Assert.That(withDefault, Is.EqualTo(new List<string> { "Hurricane", "Tornado" }));

        var withOverride = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "EventType",
            db,
            """
            let FilterByStateAndDamage = (T:(State:string, EventType:string, DamageProperty:int), state:string, minDmg:int = 5000) {
                T | where State == state and DamageProperty >= minDmg
            };
            FilterByStateAndDamage(StormEvents, "FLORIDA", 10000) | project EventType
            """);
        Assert.That(withOverride, Is.EquivalentTo(new List<string> { "Hurricane" }));
    }

    [Test]
    [Description("let - tabular UDF with wildcard schema (*) invoked directly and via invoke operator")]
    public void Let_TabularUdf_WildcardSchemaAndInvokeOperator()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var directCall = NeonTestUtils.ExecuteAndGetSingleValue<long>(
            db,
            """
            let TakeN = (T:(*), n:long) { T | take n };
            TakeN(StormEvents, 2) | count
            """,
            "Count");
        Assert.That(directCall, Is.EqualTo(2L));

        var viaInvoke = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "EventType",
            db,
            """
            let OnlyState = (T:(State:string, EventType:string, DamageProperty:int), st:string) {
                T | where State == st
            };
            let OnlyHighDamage = (T:(State:string, EventType:string, DamageProperty:int), minDmg:int) {
                T | where DamageProperty >= minDmg
            };
            StormEvents
            | invoke OnlyState("FLORIDA")
            | invoke OnlyHighDamage(10000)
            | project EventType
            """);
        Assert.That(viaInvoke, Is.EquivalentTo(new List<string> { "Hurricane" }));
    }

    [Test]
    [Description("let - tabular UDF with nested let inside body and tabular let passed as argument")]
    public void Let_TabularUdf_NestedLetAndTabularVariableArgument()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var actual = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Label",
            db,
            """
            let Base = StormEvents | where State == "FLORIDA";
            let LabelHigh = (T:(EventType:string, DamageProperty:int)) {
                let threshold = 10000;
                T
                | where DamageProperty >= threshold
                | extend Label = strcat("HIGH:", EventType)
            };
            LabelHigh(Base) | project Label
            """);

        Assert.That(actual, Is.EquivalentTo(new List<string> { "HIGH:Hurricane" }));
    }

    #endregion

    #region Error Cases

    [Test]
    [Description("let - missing semicolon, undefined reference, use-before-declaration, wrong arg count, and no final statement return errors")]
    public void Let_InvalidUsage_ReturnsError()
    {
        var db = new KustoDatabase();

        // Missing semicolon
        NeonTestUtils.ExecuteAndExpectError(db, "let x = 10 print x");

        // Undefined variable
        NeonTestUtils.ExecuteAndExpectError(db, "let x = 10; print y");

        // Duplicate variable declaration in the same scope
        NeonTestUtils.ExecuteAndExpectError(db, "let x = 10; let x = x + 5; print x");

        // Use before declaration
        NeonTestUtils.ExecuteAndExpectError(db, "let a = b + 1; let b = 5; print a");

        // Wrong argument counts for UDF
        NeonTestUtils.ExecuteAndExpectError(db, "let f = (a:long, b:long) { a + b }; print f(1)");
        NeonTestUtils.ExecuteAndExpectError(db, "let f = (a:long) { a + 1 }; print f(1, 2)");

        // No final query statement after let
        NeonTestUtils.ExecuteAndExpectError(db, "let x = 10;");
    }

    #endregion

    #region Cross-Feature End-to-End Combination Tests

    [Test]
    [Description("End-to-end combination: let + parse + trim + toint/toreal/tobool + bin + strcat")]
    public void Combination_LetParseTrimConversionsBinAndStrcat()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "[2025-04-10 14:23:11] svc=  --auth--  | latency= 142.8 | ok=TRUE"),
                (2, "[2025-04-10 14:51:02] svc= --billing-- | latency= 58.2 | ok=false"),
                (3, "[2025-04-10 15:07:45] svc=  --search-- | latency= 210.9 | ok=1")
            },
            columnName1: "Id",
            columnName2: "RawLog",
            tableName: "RawLogs");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            let bucketSize = 1h;
            let CleanSvc = (raw:string) {
                let noSpaces = trim(@"\s+", raw);
                trim("-+", noSpaces)
            };
            RawLogs
            | parse RawLog with "[" Ts:datetime "] svc=" RawSvc "| latency=" RawLatency "| ok=" RawOk
            | extend Service = CleanSvc(RawSvc),
                     LatencyInt = toint(toreal(trim(@"\s+", RawLatency))),
                     LatencyBucket = bin(toreal(trim(@"\s+", RawLatency)), 50.0),
                     HourBucket = bin(Ts, bucketSize),
                     IsOk = tobool(trim(@"\s+", RawOk))
            | extend Tag = strcat(Service, "@", LatencyInt, ":", tostring(IsOk))
            | where IsOk == true
            | sort by Id asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));

        Assert.That(rows[0]["Service"], Is.EqualTo("auth"));
        Assert.That(rows[0]["LatencyInt"], Is.EqualTo(142));
        NeonTestUtils.AssertNumericEqual(rows[0]["LatencyBucket"], 100.0);
        Assert.That(rows[0]["HourBucket"], Is.EqualTo(new DateTime(2025, 4, 10, 14, 0, 0, DateTimeKind.Utc)));
        Assert.That(rows[0]["IsOk"], Is.EqualTo(true));
        Assert.That((string)rows[0]["Tag"]!, Is.EqualTo("auth@142:true").IgnoreCase);

        Assert.That(rows[1]["Service"], Is.EqualTo("search"));
        Assert.That(rows[1]["LatencyInt"], Is.EqualTo(210));
        NeonTestUtils.AssertNumericEqual(rows[1]["LatencyBucket"], 200.0);
        Assert.That(rows[1]["HourBucket"], Is.EqualTo(new DateTime(2025, 4, 10, 15, 0, 0, DateTimeKind.Utc)));
        Assert.That(rows[1]["IsOk"], Is.EqualTo(true));
        Assert.That((string)rows[1]["Tag"]!, Is.EqualTo("search@210:true").IgnoreCase);
    }

    [Test]
    [Description("End-to-end combination: let + parse + parse_json + extract_json + pack_all(true) + getschema")]
    public void Combination_LetParseJsonExtractJsonPackAllAndGetSchema()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, """env=prod payload={"user":"ada","retries":3,"note":""}"""),
                (2, """env=staging payload={"user":"alan","retries":0,"note":"retry"}""")
            },
            columnName1: "Id",
            columnName2: "Message",
            tableName: "Ingress");
        db.AddTable(table);

        var dataRows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            let Enrich = (T:(Id:int, Message:string)) {
                T
                | parse Message with "env=" Env " payload=" JsonBody
                | extend Parsed = parse_json(JsonBody)
                | extend User = tostring(Parsed.user),
                         Retries = extract_json("$.retries", JsonBody, typeof(int)),
                         Note = tostring(Parsed.note)
                | project Id, Env, User, Retries, Note
                | extend CompactBag = pack_all(true)
            };
            Enrich(Ingress)
            | extend BagJson = tostring(CompactBag)
            | sort by Id asc
            """);

        Assert.That(dataRows, Has.Count.EqualTo(2));
        // Row 1: Note is "" so pack_all(true) omits it
        NeonTestUtils.AssertJsonEquivalent(
            (string)dataRows[0]["BagJson"]!,
            """{"Id":1,"Env":"prod","User":"ada","Retries":3}""");
        // Row 2: Retries is 0 (kept!) and Note is "retry" (kept!)
        NeonTestUtils.AssertJsonEquivalent(
            (string)dataRows[1]["BagJson"]!,
            """{"Id":2,"Env":"staging","User":"alan","Retries":0,"Note":"retry"}""");

        // Verify schema produced by the tabular let UDF
        var schema = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            """
            let Enrich = (T:(Id:int, Message:string)) {
                T
                | parse Message with "env=" Env " payload=" JsonBody
                | extend Parsed = parse_json(JsonBody)
                | extend User = tostring(Parsed.user),
                         Retries = extract_json("$.retries", JsonBody, typeof(int)),
                         Note = tostring(Parsed.note)
                | project Id, Env, User, Retries, Note
                | extend CompactBag = pack_all(true)
            };
            Enrich(Ingress) | getschema | sort by ColumnOrdinal asc
            """);

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Id", 0, "System.Int32", "int"),
            ("Env", 1, "System.String", "string"),
            ("User", 2, "System.String", "string"),
            ("Retries", 3, "System.Int32", "int"),
            ("Note", 4, "System.String", "string"),
            ("CompactBag", 5, "System.Object", "dynamic")
        }));
    }

    [Test]
    [Description("End-to-end combination: parse kind=relaxed + pack_all(true) omits failed null fields while keeping valid ones")]
    public void Combination_ParseRelaxedWithPackAllIgnoreNullEmpty()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "host=web01 code=200 ms=15"),
                (2, "host=web02 code=invalid ms=40")
            },
            columnName1: "Id",
            columnName2: "Line",
            tableName: "HttpLogs");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            HttpLogs
            | parse kind=relaxed Line with "host=" Host " code=" Code:int " ms=" Ms:int
            | project Id, Host, Code, Ms
            | extend Bag = tostring(pack_all(true))
            | sort by Id asc
            """);

        NeonTestUtils.AssertJsonEquivalent((string)rows[0]["Bag"]!, """{"Id":1,"Host":"web01","Code":200,"Ms":15}""");
        // Row 2: Code failed to parse -> null -> omitted by pack_all(true), while Host and Ms remain
        NeonTestUtils.AssertJsonEquivalent((string)rows[1]["Bag"]!, """{"Id":2,"Host":"web02","Ms":40}""");
    }

    #endregion
}
