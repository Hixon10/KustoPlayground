namespace KustoPlayground.Core.Tests;

/// <summary>
/// let statement - https://learn.microsoft.com/en-us/kusto/query/let-statement
/// Binds a name to a scalar value, a tabular expression or a user-defined function (lambda) for the rest of the query.
/// The tests also combine let with bin, trim, strcat, to* conversions, parse, parse_json, extract_json, pack_all and getschema.
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class LetStatementOpusTests
{
    #region scalar let

    [Test]
    [Description("let - long scalar used in take")]
    public void Let_LongScalarInTake_ReturnsRows()
    {
        var actual = ExecuteCount("let n = 2; StormEvents | take n | count");

        OpusTestUtils.AssertTypedValue(actual, 2L);
    }

    [Test]
    [Description("let - long scalar compared with an int column")]
    public void Let_LongScalarInWhere_FiltersRows()
    {
        var actual = ExecuteCount("let threshold = 5000; StormEvents | where DamageProperty > threshold | count");

        OpusTestUtils.AssertTypedValue(actual, 1L);
    }

    [Test]
    [Description("let - string scalar used in where")]
    public void Let_StringScalarInWhere_FiltersRows()
    {
        var actual = ExecuteEventTypes("let targetState = 'TEXAS'; StormEvents | where State == targetState | project EventType");

        Assert.That(actual, Is.EqualTo(new List<string> { "Flood" }));
    }

    [Test]
    [Description("let - datetime scalar used in where")]
    public void Let_DatetimeScalarInWhere_FiltersRows()
    {
        var actual = ExecuteEventTypes("let cutoff = datetime(2024-01-01); StormEvents | where StartTime < cutoff | project EventType");

        Assert.That(actual, Is.EqualTo(new List<string> { "Flood" }));
    }

    [Test]
    [Description("let - two datetime scalars define a time window")]
    public void Let_DatetimeWindow_FiltersRows()
    {
        var actual = ExecuteEventTypes(
            "let windowStart = datetime(2024-01-01); let windowEnd = datetime(2025-01-01); "
            + "StormEvents | where StartTime >= windowStart and StartTime < windowEnd | project EventType");

        Assert.That(actual, Is.EqualTo(new List<string> { "Tornado" }));
    }

    [Test]
    [Description("let - scalar computed with datetime arithmetic")]
    public void Let_DatetimeArithmetic_FiltersRows()
    {
        var actual = ExecuteCount("let cutoff = datetime(2024-06-01) - 1d; StormEvents | where StartTime > cutoff | count");

        OpusTestUtils.AssertTypedValue(actual, 2L);
    }

    [Test]
    [Description("let - scalar computed by a conversion function")]
    public void Let_ScalarFromToInt_FiltersRows()
    {
        var actual = ExecuteCount("let limit = toint('6000'); StormEvents | where DamageProperty < limit | count");

        OpusTestUtils.AssertTypedValue(actual, 2L);
    }

    [Test]
    [Description("let - printing a let name without alias produces column print_0")]
    public void Let_PrintLetName_DefaultColumnName()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue("print_0", new KustoDatabase(), "let x = 5; print x");

        OpusTestUtils.AssertTypedValue(actual, 5L);
    }

    [Test]
    [Description("let - let value used in an expression")]
    public void Let_UsedInExpression_ReturnsValue()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(OpusTestUtils.ScalarColumn, new KustoDatabase(), "let x = 5; print r = x * 2");

        OpusTestUtils.AssertNumericValue(actual, 10);
    }

    [Test]
    [Description("let - a let can reference a previously declared let")]
    public void Let_ReferencesPreviousLet_ReturnsValue()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let a = 2; let b = a * 3; print r = b");

        OpusTestUtils.AssertNumericValue(actual, 6);
    }

    [Test]
    [Description("let - names are case-sensitive")]
    public void Let_NamesAreCaseSensitive()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let N = 1; let n = 2; print r = N + n");

        OpusTestUtils.AssertNumericValue(actual, 3);
    }

    [Test]
    [Description("let - escaped name with a space")]
    public void Let_EscapedName_ReturnsValue()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let ['my var'] = 7; print r = ['my var']");

        OpusTestUtils.AssertTypedValue(actual, 7L);
    }

    [Test]
    [Description("let - dynamic array scalar")]
    public void Let_DynamicArray_IndexAccess()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            """let names = dynamic(["a","b"]); print r = tostring(names[1])""");

        OpusTestUtils.AssertTypedValue(actual, "b");
    }

    [Test]
    [Description("let - extend with a bare let name creates a column named after the let")]
    public void Let_ExtendWithBareLetName_CreatesColumn()
    {
        var actual = OpusTestUtils.ExecuteAndGetValues(
            "seCount",
            CreateStormEventsDatabase(),
            "let seCount = 3; StormEvents | extend seCount | project seCount");

        Assert.That(actual, Is.All.TypeOf<long>());
        Assert.That(actual, Is.EquivalentTo(new object[] { 3L, 3L, 3L }));
    }

    [Test]
    [Description("let - inside row context a column wins over a let with the same name")]
    public void Let_SameNameAsColumn_ColumnWins()
    {
        var actual = ExecuteCount("let State = 'OHIO'; StormEvents | where State == 'TEXAS' | count");

        OpusTestUtils.AssertTypedValue(actual, 1L);
    }

    [Test]
    [Description("let - statements on separate lines with comments")]
    public void Let_MultiLineQueryWithComments_ReturnsRows()
    {
        const string query = """
                             // threshold for big events
                             let threshold = 10000;
                             // only big events
                             StormEvents
                             | where DamageProperty > threshold // inline comment
                             | project EventType
                             """;

        var actual = ExecuteEventTypes(query);

        Assert.That(actual, Is.EqualTo(new List<string> { "Hurricane" }));
    }

    [Test]
    [Description("let - a trailing semicolon after the last statement is allowed")]
    public void Let_TrailingSemicolon_IsAllowed()
    {
        var actual = ExecuteCount("let n = 1; StormEvents | take n | count;");

        OpusTestUtils.AssertTypedValue(actual, 1L);
    }

    #endregion

    #region tabular let

    [Test]
    [Description("let - tabular expression")]
    public void Let_TabularExpression_ReturnsRows()
    {
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "State",
            CreateStormEventsDatabase(),
            "let floods = StormEvents | where EventType == 'Flood'; floods | project State");

        Assert.That(actual, Is.EqualTo(new List<string> { "TEXAS" }));
    }

    [Test]
    [Description("let - tabular expression which uses a scalar let")]
    public void Let_TabularExpressionUsingScalarLet_ReturnsRows()
    {
        var actual = ExecuteEventTypes(
            "let cutoff = datetime(2024-01-01); let floods = StormEvents | where StartTime < cutoff; floods | project EventType");

        Assert.That(actual, Is.EqualTo(new List<string> { "Flood" }));
    }

    [Test]
    [Description("let - a let can shadow a table name and reference the table itself")]
    public void Let_ShadowsTableName_UsesFilteredTable()
    {
        var actual = ExecuteCount("let StormEvents = StormEvents | where State == 'FLORIDA'; StormEvents | count");

        OpusTestUtils.AssertTypedValue(actual, 2L);
    }

    [Test]
    [Description("let - materialize")]
    public void Let_Materialize_ReturnsRows()
    {
        var all = ExecuteCount("let florida = materialize(StormEvents | where State == 'FLORIDA'); florida | count");
        var tornadoes = ExecuteCount(
            "let florida = materialize(StormEvents | where State == 'FLORIDA'); florida | where EventType == 'Tornado' | count");

        OpusTestUtils.AssertTypedValue(all, 2L);
        OpusTestUtils.AssertTypedValue(tornadoes, 1L);
    }

    [Test]
    [Description("let - view")]
    public void Let_View_ReturnsRows()
    {
        var actual = ExecuteCount("let v = view() { StormEvents | where State == 'TEXAS' }; v | count");

        OpusTestUtils.AssertTypedValue(actual, 1L);
    }

    [Test]
    [Description("let - toscalar of a tabular expression")]
    public void Let_Toscalar_ReturnsScalar()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            CreateStormEventsDatabase(),
            "let total = toscalar(StormEvents | count); print r = total");

        OpusTestUtils.AssertTypedValue(actual, 3L);
    }

    [Test]
    [Description("let - toscalar result used in where")]
    public void Let_ToscalarInWhere_FiltersRows()
    {
        var actual = ExecuteEventTypes(
            "let maxDamage = toscalar(StormEvents | sort by DamageProperty desc | take 1 | project DamageProperty); "
            + "StormEvents | where DamageProperty == maxDamage | project EventType");

        Assert.That(actual, Is.EqualTo(new List<string> { "Hurricane" }));
    }

    #endregion

    #region scalar functions

    [TestCase("f(2)", 20.0)]
    [TestCase("f(2, 3)", 6.0)]
    [Description("let function - parameter with a default value")]
    public void LetFunction_DefaultParameter_ReturnsValue(string call, double expected)
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            $"let f = (a:long, b:long = 10) {{ a * b }}; print r = {call}");

        OpusTestUtils.AssertNumericValue(actual, expected);
    }

    [Test]
    [Description("let function - named arguments in any order")]
    public void LetFunction_NamedArguments_ReturnsValue()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let n = (a:long, b:long = 10) { a - b }; print r = n(b = 1, a = 5)");

        OpusTestUtils.AssertNumericValue(actual, 4);
    }

    [TestCase("f(12, c=7)", "12-b.default-7")]
    [TestCase("f(3, c=33)", "3-b.default-33")]
    [TestCase("""f(1, "x", 2)""", "1-x-2")]
    [TestCase("f(5)", "5-b.default-0")]
    [Description("let function - mix of positional, named and default arguments (example from the docs)")]
    public void LetFunction_DefaultAndNamedArguments_ReturnsValue(string call, string expected)
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            $$"""let f = (a:long, b:string = "b.default", c:long = 0) { strcat(a, "-", b, "-", c) }; print r = {{call}}""");

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [Test]
    [Description("let function - string parameter")]
    public void LetFunction_StringParameter_ReturnsValue()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let greet = (name:string) { strcat('Hello, ', name) }; print r = greet('World')");

        OpusTestUtils.AssertTypedValue(actual, "Hello, World");
    }

    [Test]
    [Description("let function - a let statement inside the function body")]
    public void LetFunction_LetInsideBody_ReturnsValue()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let g = (x:long) { let y = x * 2; y + 1 }; print r = g(3)");

        OpusTestUtils.AssertNumericValue(actual, 7);
    }

    [Test]
    [Description("let function - the body can reference an outer let")]
    public void LetFunction_ReferencesOuterLet_ReturnsValue()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let factor = 3; let h = (x:long) { x * factor }; print r = h(2)");

        OpusTestUtils.AssertNumericValue(actual, 6);
    }

    [Test]
    [Description("let function - without parameters")]
    public void LetFunction_NoParameters_ReturnsValue()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let k = () { 42 }; print r = k()");

        OpusTestUtils.AssertTypedValue(actual, 42L);
    }

    [Test]
    [Description("let function - a parameter shadows an outer let with the same name")]
    public void LetFunction_ParameterShadowsOuterLet_UsesParameter()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let shadow = 100; let m = (shadow:long) { shadow + 1 }; print r = m(1)");

        OpusTestUtils.AssertNumericValue(actual, 2);
    }

    [Test]
    [Description("let function - int parameter keeps the int type")]
    public void LetFunction_IntParameter_ReturnsInt()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let iv = (v:int) { v }; print r = iv(toint(4))");

        OpusTestUtils.AssertTypedValue(actual, 4);
    }

    [Test]
    [Description("let function - a function calling another function")]
    public void LetFunction_CallsOtherFunction_ReturnsValue()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "let twice = (x:long) { x * 2 }; let quad = (x:long) { twice(twice(x)) }; print r = quad(3)");

        OpusTestUtils.AssertNumericValue(actual, 12);
    }

    [Test]
    [Description("let function - called with column arguments for every row")]
    public void LetFunction_ColumnArguments_ReturnsValuePerRow()
    {
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Text",
            CreateStormEventsDatabase(),
            "let describe = (s:string, e:string) { strcat(e, ' in ', s) }; StormEvents | extend Text = describe(State, EventType) | project Text");

        Assert.That(actual, Is.EquivalentTo(new List<string> { "Hurricane in FLORIDA", "Flood in TEXAS", "Tornado in FLORIDA" }));
    }

    [Test]
    [Description("let function - wraps bin, an int column argument is converted to the long parameter")]
    public void LetFunction_WrapsBin_ReturnsLongBuckets()
    {
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "EventType",
            "Bucket",
            CreateStormEventsDatabase(),
            "let bucket = (v:long) { bin(v, 10000) }; StormEvents | extend Bucket = bucket(DamageProperty) | project EventType, Bucket");

        Assert.That(actual, Is.EquivalentTo(new List<(string, long)>
        {
            ("Hurricane", 20000L),
            ("Flood", 0L),
            ("Tornado", 0L)
        }));
    }

    [Test]
    [Description("let function - datetime parameter, the body uses an outer timespan let")]
    public void LetFunction_DatetimeParameterWithOuterBinSize_ReturnsDay()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Day",
            CreateStormEventsDatabase(),
            "let bucketSize = 1d; let toDay = (t:datetime) { bin(t, bucketSize) }; "
            + "StormEvents | where EventType == 'Tornado' | extend Day = toDay(StartTime) | project Day");

        OpusTestUtils.AssertTypedValue(actual, OpusTestUtils.ParseDateTime("2024-06-01 00:00:00"));
    }

    [Test]
    [Description("let function - TrimOnes example from the docs (trim removes one regex match on each side)")]
    public void LetFunction_TrimOnesDocsExample_ReturnsTrimmedValues()
    {
        var table = TestUtils.GenerateTableWithColumn<long>([10L, 11L, 12L, 13L, 14L, 15L], "x", "Numbers");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<long, string>(
            "x",
            "result",
            kustoDatabase,
            """let TrimOnes = (s:string) { trim("1", s) }; Numbers | extend result = TrimOnes(tostring(x)) | project x, result""");

        Assert.That(actual, Is.EquivalentTo(new List<(long, string)>
        {
            (10L, "0"),
            (11L, ""),
            (12L, "2"),
            (13L, "3"),
            (14L, "4"),
            (15L, "5")
        }));
    }

    #endregion

    #region tabular functions

    [Test]
    [Description("let function - tabular result with a scalar parameter")]
    public void LetFunction_TabularResult_ReturnsRows()
    {
        var actual = ExecuteCount("let byState = (s:string) { StormEvents | where State == s }; byState('FLORIDA') | count");

        OpusTestUtils.AssertTypedValue(actual, 2L);
    }

    [TestCase("let tf = (D:(DamageProperty:int)) { D | where DamageProperty > 5000 }; tf(StormEvents) | count")]
    [TestCase("let tf = (D:(DamageProperty:int)) { D | where DamageProperty > 5000 }; StormEvents | invoke tf() | count")]
    [Description("let function - tabular parameter, called directly or with invoke")]
    public void LetFunction_TabularParameter_ReturnsRows(string query)
    {
        var actual = ExecuteCount(query);

        OpusTestUtils.AssertTypedValue(actual, 1L);
    }

    [TestCase("let rowCount = (D:(*)) { D | count }; rowCount(StormEvents)")]
    [TestCase("let rowCount = (D:(*)) { D | count }; StormEvents | invoke rowCount()")]
    [Description("let function - tabular parameter with a wildcard schema")]
    public void LetFunction_WildcardTabularParameter_ReturnsCount(string query)
    {
        var actual = ExecuteCount(query);

        OpusTestUtils.AssertTypedValue(actual, 3L);
    }

    [Test]
    [Description("let function - tabular and scalar parameters, with invoke the first argument is the piped table")]
    public void LetFunction_TabularAndScalarParameters_ReturnsRows()
    {
        const string function =
            "let expensive = (D:(DamageProperty:int), minDamage:int) { D | where DamageProperty >= minDamage }; ";

        var invoked = ExecuteCount(function + "StormEvents | invoke expensive(5000) | count");
        var called = ExecuteEventTypes(function + "expensive(StormEvents, 10000) | project EventType");

        OpusTestUtils.AssertTypedValue(invoked, 3L);
        Assert.That(called, Is.EqualTo(new List<string> { "Hurricane" }));
    }

    #endregion

    #region let combined with other functions and operators

    [Test]
    [Description("let - bin size from a let")]
    public void Let_WithBin_ReturnsBuckets()
    {
        var actual = ExecuteCount(
            "let bucketSize = 10000; StormEvents | extend Bucket = bin(DamageProperty, bucketSize) | distinct Bucket | count");

        OpusTestUtils.AssertTypedValue(actual, 2L);
    }

    [Test]
    [Description("let - trim regex from a let constant")]
    public void Let_WithTrimRegex_ReturnsTrimmedValues()
    {
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "EventType",
            "Trimmed",
            CreateStormEventsDatabase(),
            "let p = 'F|A'; StormEvents | extend Trimmed = trim(p, State) | project EventType, Trimmed");

        Assert.That(actual, Is.EquivalentTo(new List<(string, string)>
        {
            ("Hurricane", "LORID"),
            ("Flood", "TEXAS"),
            ("Tornado", "LORID")
        }));
    }

    [Test]
    [Description("let - separator for strcat from a let")]
    public void Let_WithStrcatSeparator_ReturnsJoinedValue()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Text",
            CreateStormEventsDatabase(),
            "let sep = ' | '; StormEvents | where EventType == 'Flood' | extend Text = strcat(State, sep, EventType) | project Text");

        OpusTestUtils.AssertTypedValue(actual, "TEXAS | Flood");
    }

    [Test]
    [Description("let - tabular let with the parse operator")]
    public void Let_WithParse_ReturnsParsedRows()
    {
        var actual = ExecuteCount(
            """let parsed = StormEvents | parse State with "FLO" Rest; parsed | where Rest != '' | count""");

        OpusTestUtils.AssertTypedValue(actual, 2L);
    }

    [Test]
    [Description("let - configuration object from parse_json")]
    public void Let_WithParseJsonConfig_FiltersRows()
    {
        var actual = ExecuteEventTypes(
            """let config = parse_json('{"minDamage":10000}'); StormEvents | where DamageProperty > toint(config.minDamage) | project EventType""");

        Assert.That(actual, Is.EqualTo(new List<string> { "Hurricane" }));
    }

    [Test]
    [Description("let - JSON document for extract_json")]
    public void Let_WithExtractJson_FiltersRows()
    {
        var actual = ExecuteEventTypes(
            """let doc = '{"state":"TEXAS"}'; StormEvents | where State == extract_json('$.state', doc) | project EventType""");

        Assert.That(actual, Is.EqualTo(new List<string> { "Flood" }));
    }

    [Test]
    [Description("let - tabular let with pack_all")]
    public void Let_WithPackAll_ReturnsJson()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            CreateStormEventsDatabase(),
            "let packed = StormEvents | where EventType == 'Flood' | project State, EventType | extend Json = tostring(pack_all()); "
            + "packed | project Json");

        OpusTestUtils.AssertTypedValue(actual, """{"State":"TEXAS","EventType":"Flood"}""");
    }

    [Test]
    [Description("let - getschema of a tabular let")]
    public void Let_WithGetSchema_ReturnsSchema()
    {
        var schema = OpusTestUtils.ExecuteGetSchema(
            CreateStormEventsDatabase(),
            "let projected = StormEvents | project State, DamageProperty; projected | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("State", 0, "System.String", "string"),
            ("DamageProperty", 1, "System.Int32", "int")
        }));
    }

    #endregion

    #region errors

    [TestCase("let x = State; StormEvents | extend y = x", Description = "a let can't reference a column (SEM0100)")]
    [TestCase("let f = (a:long) { let doubled = a * 2; doubled }; print r = doubled",
        Description = "a let inside a function body isn't visible outside (SEM0100)")]
    [TestCase("let a = 1;", Description = "a query must end with a tabular expression (SEM0002)")]
    [TestCase("let a = 1; let a = 2; print r = a", Description = "duplicate let name (SEM0079)")]
    [TestCase("let a = b; let b = 1; print r = a", Description = "a let can't reference a later let (SEM0100)")]
    [TestCase("let x = 1; print r = y", Description = "unknown name (SEM0100)")]
    [TestCase("let f = (a:long) { a }; print r = f(1, 2)", Description = "too many arguments (SEM0219)")]
    [TestCase("let f = (a:long, b:long) { a + b }; print r = f(1)", Description = "missing required argument (SEM0219)")]
    public void Let_InvalidQuery_Fails(string query)
    {
        OpusTestUtils.AssertQueryFails(CreateStormEventsDatabase(), query);
    }

    #endregion

    private static KustoDatabase CreateStormEventsDatabase()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        return kustoDatabase;
    }

    private static object? ExecuteCount(string query)
    {
        return OpusTestUtils.ExecuteAndGetSingleValue("Count", CreateStormEventsDatabase(), query);
    }

    private static List<string> ExecuteEventTypes(string query)
    {
        return TestUtils.ExecuteAndGetDataForOneColumn<string>("EventType", CreateStormEventsDatabase(), query);
    }
}
