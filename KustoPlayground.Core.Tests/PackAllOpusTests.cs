namespace KustoPlayground.Core.Tests;

/// <summary>
/// pack_all([ignore_null_empty]) - https://learn.microsoft.com/en-us/kusto/query/pack-all-function
/// Creates a dynamic property bag from all the columns of the row. Keys follow the column order.
/// Expected values (including the exact JSON text produced by tostring) were verified against a real Azure Data Explorer cluster.
/// </summary>
public class PackAllOpusTests
{
    private const string FloodJson =
        """{"StartTime":"2023-03-28T10:30:00.0000000Z","State":"TEXAS","EventType":"Flood","DamageProperty":5000}""";

    private const string TypedRow1Json =
        """{"id":1,"i":7,"r1":1.5,"r2":2.0,"b":true,"dt":"2020-01-02T03:04:05.0000000Z","ts":"01:00:00","g":"6f9619ff-8b86-d011-b42d-00c04fc964ff","s":"x"}""";

    private const string TypedRow2Json =
        """{"id":2,"i":-2147483648,"r1":100000000000000000000.0,"r2":-0.5,"b":false,"dt":"1999-12-31T23:59:59.1230000Z","ts":"-01:30:00","g":"00000000-0000-0000-0000-000000000000","s":""}""";

    private const string TypedRow2NonEmptyJson =
        """{"id":2,"i":-2147483648,"r1":100000000000000000000.0,"r2":-0.5,"b":false,"dt":"1999-12-31T23:59:59.1230000Z","ts":"-01:30:00","g":"00000000-0000-0000-0000-000000000000"}""";

    [Test]
    [Description("pack_all - all columns of the row are packed into a property bag, keys follow the column order")]
    public void PackAll_StormEventsRow_ReturnsAllColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | extend Json = tostring(pack_all()) | project Json");

        OpusTestUtils.AssertTypedValue(actual, FloodJson);
    }

    [Test]
    [Description("pack_all - is evaluated for every row of the table")]
    public void PackAll_AllRows_ReturnsBagPerRow()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Json",
            kustoDatabase,
            "StormEvents | extend Json = tostring(pack_all()) | project Json");

        Assert.That(actual, Is.EquivalentTo(new List<string>
        {
            """{"StartTime":"2025-08-23T06:20:00.0000000Z","State":"FLORIDA","EventType":"Hurricane","DamageProperty":20000}""",
            FloodJson,
            """{"StartTime":"2024-06-01T16:50:30.0000000Z","State":"FLORIDA","EventType":"Tornado","DamageProperty":5000}"""
        }));
    }

    [Test]
    [Description("pack_all - inside project the bag contains the input columns of the project")]
    public void PackAll_InsideProject_ReturnsInputColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | project Json = tostring(pack_all())");

        OpusTestUtils.AssertTypedValue(actual, FloodJson);
    }

    [Test]
    [Description("pack_all - without tostring the result is a dynamic property bag")]
    public void PackAll_WithoutToString_ReturnsDynamicBag()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Bag",
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | project State, DamageProperty | extend Bag = pack_all() | project Bag");

        OpusTestUtils.AssertDynamicEquals(actual, """{"State":"TEXAS","DamageProperty":5000}""");
    }

    [Test]
    [Description("pack_all - JSON format of every scalar type: numbers, real keeps '.0' (1e20 is written in full), "
                 + "bool, datetime and timespan in Kusto format, guid in lower case; pack_all(true) drops only the empty string")]
    public void PackAll_AllScalarTypes_ReturnsKustoJsonFormat()
    {
        var kustoDatabase = CreateTypedDatabase();

        var actual = TestUtils.ExecuteAndGetDataFor3Columns<long, string, string>(
            "id",
            "All",
            "NonEmpty",
            kustoDatabase,
            "Typed | extend All = tostring(pack_all()), NonEmpty = tostring(pack_all(true)) | project id, All, NonEmpty");

        Assert.That(actual, Is.EquivalentTo(new List<(long, string, string)>
        {
            (1L, TypedRow1Json, TypedRow1Json),
            (2L, TypedRow2Json, TypedRow2NonEmptyJson)
        }));
    }

    [Test]
    [Description("pack_all - decimal is written as a JSON string, quotes/backslashes/tabs are escaped, "
                 + "non-ASCII characters are not escaped and column names with spaces are kept")]
    public void PackAll_DecimalAndSpecialCharacters_ReturnsEscapedJson()
    {
        var table = OpusTestUtils.NewTable("Texts")
            .WithColumn<decimal>("d")
            .WithColumn<string>("s")
            .WithColumn<string>("u")
            .WithColumn<long>("my col")
            .WithColumn<int>("i")
            .WithRow(1.50m, "he said \"hi\"\\", "café\tx", 1L, 7)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            kustoDatabase,
            "Texts | extend Json = tostring(pack_all()) | project Json");

        OpusTestUtils.AssertTypedValue(actual, """{"d":"1.5","s":"he said \"hi\"\\","u":"café\tx","my col":1,"i":7}""");
    }

    [Test]
    [Description("pack_all - special real values of a table column are written as the JSON strings \"NaN\"/\"Infinity\"/\"-Infinity\", "
                 + "small reals in exponent form")]
    public void PackAll_SpecialRealValues_ReturnsKustoFormat()
    {
        var table = OpusTestUtils.NewTable("Edges")
            .WithColumn<double>("r3")
            .WithColumn<double>("r4")
            .WithColumn<double>("r5")
            .WithColumn<double>("r6")
            .WithColumn<long>("l")
            .WithRow(1e-7, double.NaN, double.PositiveInfinity, double.NegativeInfinity, long.MaxValue)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            kustoDatabase,
            "Edges | extend Json = tostring(pack_all()) | project Json");

        OpusTestUtils.AssertTypedValue(actual, """{"r3":1e-7,"r4":"NaN","r5":"Infinity","r6":"-Infinity","l":9223372036854775807}""");
    }

    [Test]
    [Description("pack_all - nulls are kept as JSON null; pack_all(true) drops nulls and empty strings "
                 + "but keeps 0, false and the empty guid; pack_all(false) is the same as pack_all()")]
    public void PackAll_NullsAndEmptyStrings_IgnoreNullEmptyFlag()
    {
        var table = OpusTestUtils.NewTable("Sparse")
            .WithColumn<long>("id")
            .WithColumn<string>("s")
            .WithColumn<long>("n")
            .WithColumn<bool>("b")
            .WithColumn<Guid>("g")
            .WithColumn<double>("r")
            .WithColumn<DateTime>("dt")
            .WithColumn<TimeSpan>("ts")
            .WithRow(1L, "", 0L, false, Guid.Empty, 0.0, null, null)
            .WithRow(2L, "", null, null, null, null, null, null)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor4Columns<long, string, string, string>(
            "id",
            "All",
            "NonEmpty",
            "Same",
            kustoDatabase,
            "Sparse | extend All = tostring(pack_all()), NonEmpty = tostring(pack_all(true)), Same = tostring(pack_all(false)) "
            + "| project id, All, NonEmpty, Same");

        const string row1All =
            """{"id":1,"s":"","n":0,"b":false,"g":"00000000-0000-0000-0000-000000000000","r":0.0,"dt":null,"ts":null}""";
        const string row2All =
            """{"id":2,"s":"","n":null,"b":null,"g":null,"r":null,"dt":null,"ts":null}""";

        Assert.That(actual, Is.EquivalentTo(new List<(long, string, string, string)>
        {
            (1L, row1All, """{"id":1,"n":0,"b":false,"g":"00000000-0000-0000-0000-000000000000","r":0.0}""", row1All),
            (2L, row2All, """{"id":2}""", row2All)
        }));
    }

    [TestCase("tostring(pack_all().State)", "TEXAS")]
    [TestCase("tostring(pack_all()['EventType'])", "Flood")]
    [TestCase("tostring(pack_all().DamageProperty)", "5000")]
    [TestCase("tostring(pack_all().StartTime)", "2023-03-28T10:30:00.0000000Z")]
    [TestCase("tostring(pack_all().Missing)", "")]
    [TestCase("gettype(pack_all())", "dictionary")]
    [TestCase("gettype(pack_all().StartTime)", "datetime")]
    [Description("pack_all - properties of the bag can be accessed directly, a missing property is null (empty string)")]
    public void PackAll_PropertyAccess_ReturnsValue(string expression, string expected)
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            kustoDatabase,
            $"StormEvents | where EventType == 'Flood' | extend {OpusTestUtils.ScalarColumn} = {expression} | project {OpusTestUtils.ScalarColumn}");

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [Test]
    [Description("pack_all - a property of the bag converted back to long")]
    public void PackAll_PropertyConvertedToLong_ReturnsLong()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | extend r = tolong(pack_all().DamageProperty) | project r");

        OpusTestUtils.AssertTypedValue(actual, 5000L);
    }

    [Test]
    [Description("pack_all - can be used in where")]
    public void PackAll_InWhere_FiltersRows()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var floridaCount = OpusTestUtils.ExecuteAndGetSingleValue(
            "Count",
            kustoDatabase,
            "StormEvents | where tostring(pack_all().State) == 'FLORIDA' | count");
        var bigEvents = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "EventType",
            kustoDatabase,
            "StormEvents | where tolong(pack_all().DamageProperty) > 10000 | project EventType");

        OpusTestUtils.AssertTypedValue(floridaCount, 2L);
        Assert.That(bigEvents, Is.EqualTo(new List<string> { "Hurricane" }));
    }

    [Test]
    [Description("pack_all - after project only the projected columns are packed, in the projected order")]
    public void PackAll_AfterProject_UsesProjectedColumnOrder()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | project EventType, State | extend Json = tostring(pack_all()) | project Json");

        OpusTestUtils.AssertTypedValue(actual, """{"EventType":"Flood","State":"TEXAS"}""");
    }

    [Test]
    [Description("pack_all - columns added by a previous extend are packed")]
    public void PackAll_AfterExtend_IncludesExtendedColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | project State, EventType "
            + "| extend Label = strcat(State, '/', EventType) | extend Json = tostring(pack_all()) | project Json");

        OpusTestUtils.AssertTypedValue(actual, """{"State":"TEXAS","EventType":"Flood","Label":"TEXAS/Flood"}""");
    }

    [Test]
    [Description("pack_all - columns computed by bin are packed with their types (datetime and long)")]
    public void PackAll_AfterBin_ReturnsBinnedValues()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | extend Day = bin(StartTime, 1d), Bucket = bin(DamageProperty, 1000) "
            + "| project Day, Bucket | extend Json = tostring(pack_all()) | project Json");

        OpusTestUtils.AssertTypedValue(actual, """{"Day":"2023-03-28T00:00:00.0000000Z","Bucket":5000}""");
    }

    [Test]
    [Description("pack_all - all expressions of the same extend see only the input columns")]
    public void PackAll_TwiceInSameExtend_BothSeeInputColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "P1",
            "P2",
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | project State, EventType "
            + "| extend P1 = tostring(pack_all()), P2 = tostring(pack_all()) | project P1, P2");

        const string expected = """{"State":"TEXAS","EventType":"Flood"}""";
        Assert.That(actual, Is.EqualTo(new List<(string, string)> { (expected, expected) }));
    }

    [Test]
    [Description("pack_all - a previously packed dynamic column is nested as an object")]
    public void PackAll_NestedDynamicColumn_IsNestedObject()
    {
        var kustoDatabase = CreatePairsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            kustoDatabase,
            "Pairs | extend p = pack_all() | extend q = pack_all() | extend r = tostring(q) | project r");

        OpusTestUtils.AssertTypedValue(actual, """{"b":"x","a":1,"p":{"b":"x","a":1}}""");
    }

    [Test]
    [Description("pack_all - nested bags can be navigated and have type dictionary")]
    public void PackAll_NestedDynamicColumn_CanBeNavigated()
    {
        var kustoDatabase = CreatePairsDatabase();

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "NestedType",
            "NestedB",
            kustoDatabase,
            "Pairs | extend p = pack_all() | extend q = pack_all() "
            + "| extend NestedType = gettype(q.p), NestedB = tostring(q.p.b) | project NestedType, NestedB");

        Assert.That(actual, Is.EqualTo(new List<(string, string)> { ("dictionary", "x") }));
    }

    [Test]
    [Description("pack_all - three levels of nesting")]
    public void PackAll_ThreeLevels_ReturnsNestedObjects()
    {
        var kustoDatabase = CreatePairsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            kustoDatabase,
            "Pairs | extend p = pack_all() | extend q = pack_all() | extend z = pack_all() | extend r = tostring(z) | project r");

        OpusTestUtils.AssertTypedValue(actual,
            """{"b":"x","a":1,"p":{"b":"x","a":1},"q":{"b":"x","a":1,"p":{"b":"x","a":1}}}""");
    }

    [Test]
    [Description("pack_all - a previously packed string column is packed as an escaped JSON string")]
    public void PackAll_NestedStringColumn_IsEscapedString()
    {
        var kustoDatabase = CreatePairsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "q",
            kustoDatabase,
            "Pairs | extend p = tostring(pack_all()) | extend q = tostring(pack_all()) | project q");

        OpusTestUtils.AssertTypedValue(actual, """{"b":"x","a":1,"p":"{\"b\":\"x\",\"a\":1}"}""");
    }

    [Test]
    [Description("pack_all - a parse_json column is nested as JSON; pack_all(true) keeps nulls inside nested values")]
    public void PackAll_ParseJsonColumn_IsNestedAsJson()
    {
        var kustoDatabase = CreatePairsDatabase();

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "All",
            "NonEmpty",
            kustoDatabase,
            """Pairs | extend d = parse_json('{"k":[1,2],"n":null}') | project b, d """
            + "| extend All = tostring(pack_all()), NonEmpty = tostring(pack_all(true)) | project All, NonEmpty");

        const string expected = """{"b":"x","d":{"k":[1,2],"n":null}}""";
        Assert.That(actual, Is.EqualTo(new List<(string, string)> { (expected, expected) }));
    }

    [Test]
    [Description("pack_all - result type is dynamic, tostring of it is string")]
    public void PackAll_ResultType_IsDynamic()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            "StormEvents | extend Bag = pack_all(), Text = tostring(pack_all()) | project Bag, Text | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Bag", 0, "System.Object", "dynamic"),
            ("Text", 1, "System.String", "string")
        }));
    }

    [Test]
    [Description("pack_all - the JSON text can be parsed back with parse_json")]
    public void PackAll_RoundTripThroughParseJson_ReturnsOriginalValues()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "BackState",
            "BackDamage",
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | extend Json = tostring(pack_all()) | extend Back = parse_json(Json) "
            + "| extend BackState = tostring(Back.State), BackDamage = tolong(Back.DamageProperty) | project BackState, BackDamage");

        Assert.That(actual, Is.EqualTo(new List<(string, long)> { ("TEXAS", 5000L) }));
    }

    [Test]
    [Description("pack_all - columns produced by the parse operator are packed")]
    public void PackAll_AfterParse_IncludesParsedColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            kustoDatabase,
            """StormEvents | where EventType == 'Hurricane' | project State | parse State with "FLO" Rest | extend Json = tostring(pack_all()) | project Json""");

        OpusTestUtils.AssertTypedValue(actual, """{"State":"FLORIDA","Rest":"RIDA"}""");
    }

    [Test]
    [Description("pack_all - strcat converts the bag to its JSON text")]
    public void PackAll_InStrcat_ReturnsJsonText()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Line",
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | project State, DamageProperty | extend Line = strcat('row=', pack_all()) | project Line");

        OpusTestUtils.AssertTypedValue(actual, """row={"State":"TEXAS","DamageProperty":5000}""");
    }

    [Test]
    [Description("pack_all - JSON text of equal rows is equal, so distinct removes duplicates")]
    public void PackAll_Distinct_RemovesDuplicates()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Count",
            kustoDatabase,
            "StormEvents | project State | extend Json = tostring(pack_all()) | distinct Json | count");

        OpusTestUtils.AssertTypedValue(actual, 2L);
    }

    [Test]
    [Description("pack_all - after count the bag contains the Count column")]
    public void PackAll_AfterCount_ReturnsCount()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            kustoDatabase,
            "StormEvents | count | extend Json = tostring(pack_all()) | project Json");

        OpusTestUtils.AssertTypedValue(actual, """{"Count":3}""");
    }

    [Test]
    [Description("pack_all - after sort and take")]
    public void PackAll_AfterSortAndTake_ReturnsTopRow()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "Json",
            kustoDatabase,
            "StormEvents | sort by DamageProperty desc | take 1 | project EventType, DamageProperty "
            + "| extend Json = tostring(pack_all()) | project Json");

        OpusTestUtils.AssertTypedValue(actual, """{"EventType":"Hurricane","DamageProperty":20000}""");
    }

    [Test]
    [Description("pack_all - in print the bag contains the printed columns (an unnamed column is print_N by its ordinal)")]
    public void PackAll_AfterPrint_ReturnsPrintedColumns()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            OpusTestUtils.ScalarColumn,
            new KustoDatabase(),
            "print a = 1, b = 'x', 2.5 | extend r = tostring(pack_all())");

        OpusTestUtils.AssertTypedValue(actual, """{"a":1,"b":"x","print_2":2.5}""");
    }

    [Test]
    [Description("pack_all - empty input produces no rows")]
    public void PackAll_EmptyInput_ReturnsNoRows()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "StormEvents | where State == 'OHIO' | extend Json = tostring(pack_all())");

        Assert.That(rows, Is.Empty);
    }

    [TestCase("StormEvents | extend r = pack_all('x')")]
    [TestCase("StormEvents | extend r = pack_all(true, false)")]
    [TestCase("StormEvents | extend r = pack_all(DamageProperty > 0)")]
    [Description("pack_all - the only argument must be a constant bool")]
    public void PackAll_InvalidArguments_Fails(string query)
    {
        var kustoDatabase = CreateStormEventsDatabase();

        OpusTestUtils.AssertQueryFails(kustoDatabase, query);
    }

    [Test]
    [Description("pack_all - a bool column is not a constant, so it can't be used as ignore_null_empty")]
    public void PackAll_BoolColumnArgument_Fails()
    {
        var table = OpusTestUtils.NewTable("Flags")
            .WithColumn<bool>("Flag")
            .WithRow(true)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        OpusTestUtils.AssertQueryFails(kustoDatabase, "Flags | extend r = pack_all(Flag)");
    }

    private static KustoDatabase CreateStormEventsDatabase()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        return kustoDatabase;
    }

    /// <summary>
    /// Pairs(b: string, a: long) with the single row ("x", 1).
    /// </summary>
    private static KustoDatabase CreatePairsDatabase()
    {
        var table = OpusTestUtils.NewTable("Pairs")
            .WithColumn<string>("b")
            .WithColumn<long>("a")
            .WithRow("x", 1L)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        return kustoDatabase;
    }

    /// <summary>
    /// Typed(id: long, i: int, r1: real, r2: real, b: bool, dt: datetime, ts: timespan, g: guid, s: string) with 2 rows.
    /// </summary>
    private static KustoDatabase CreateTypedDatabase()
    {
        var table = OpusTestUtils.NewTable("Typed")
            .WithColumn<long>("id")
            .WithColumn<int>("i")
            .WithColumn<double>("r1")
            .WithColumn<double>("r2")
            .WithColumn<bool>("b")
            .WithColumn<DateTime>("dt")
            .WithColumn<TimeSpan>("ts")
            .WithColumn<Guid>("g")
            .WithColumn<string>("s")
            .WithRow(1L, 7, 1.5, 2.0, true, OpusTestUtils.ParseDateTime("2020-01-02 03:04:05"), TimeSpan.FromHours(1),
                Guid.Parse("6F9619FF-8B86-D011-B42D-00C04FC964FF"), "x")
            .WithRow(2L, int.MinValue, 1e20, -0.5, false, OpusTestUtils.ParseDateTime("1999-12-31 23:59:59.123"),
                TimeSpan.FromMinutes(-90), Guid.Empty, "")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        return kustoDatabase;
    }
}
