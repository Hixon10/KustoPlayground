namespace KustoPlayground.Core.Tests;

/// <summary>
/// getschema operator - https://learn.microsoft.com/en-us/kusto/query/getschema-operator
/// Produces one row per input column: ColumnName (string), ColumnOrdinal (int), DataType (string, .NET type name)
/// and ColumnType (string, Kusto type name). Rows follow the column order.
/// Note that a Kusto bool is reported as System.SByte and decimal as System.Data.SqlTypes.SqlDecimal.
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class GetSchemaOpusTests
{
    private static readonly List<(string, int, string, string)> StormEventsSchema =
    [
        ("StartTime", 0, "System.DateTime", "datetime"),
        ("State", 1, "System.String", "string"),
        ("EventType", 2, "System.String", "string"),
        ("DamageProperty", 3, "System.Int32", "int")
    ];

    [Test]
    [Description("getschema - schema of a table, rows follow the column order")]
    public void GetSchema_StormEvents_ReturnsColumnsInOrder()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(kustoDatabase, "StormEvents | getschema");

        Assert.That(schema, Is.EqualTo(StormEventsSchema));
    }

    [Test]
    [Description("getschema - .NET and Kusto type names of all scalar column types")]
    public void GetSchema_AllScalarColumnTypes_ReturnsTypeNames()
    {
        var table = OpusTestUtils.NewTable("AllTypes")
            .WithColumn<bool>("b")
            .WithColumn<int>("i")
            .WithColumn<long>("l")
            .WithColumn<double>("r")
            .WithColumn<decimal>("d")
            .WithColumn<string>("s")
            .WithColumn<DateTime>("dt")
            .WithColumn<TimeSpan>("ts")
            .WithColumn<Guid>("g")
            .WithRow(true, 1, 2L, 3.5, 4.5m, "x", OpusTestUtils.ParseDateTime("2020-01-01 00:00:00"), TimeSpan.FromHours(1),
                Guid.Empty)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var schema = OpusTestUtils.ExecuteGetSchema(kustoDatabase, "AllTypes | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("b", 0, "System.SByte", "bool"),
            ("i", 1, "System.Int32", "int"),
            ("l", 2, "System.Int64", "long"),
            ("r", 3, "System.Double", "real"),
            ("d", 4, "System.Data.SqlTypes.SqlDecimal", "decimal"),
            ("s", 5, "System.String", "string"),
            ("dt", 6, "System.DateTime", "datetime"),
            ("ts", 7, "System.TimeSpan", "timespan"),
            ("g", 8, "System.Guid", "guid")
        }));
    }

    [TestCase("1", "System.Int64", "long")]
    [TestCase("long(1)", "System.Int64", "long")]
    [TestCase("int(1)", "System.Int32", "int")]
    [TestCase("1.5", "System.Double", "real")]
    [TestCase("real(1)", "System.Double", "real")]
    [TestCase("decimal(1.5)", "System.Data.SqlTypes.SqlDecimal", "decimal")]
    [TestCase("'x'", "System.String", "string")]
    [TestCase("true", "System.SByte", "bool")]
    [TestCase("1 > 0", "System.SByte", "bool")]
    [TestCase("datetime(2020-01-01)", "System.DateTime", "datetime")]
    [TestCase("1h", "System.TimeSpan", "timespan")]
    [TestCase("guid(6F9619FF-8B86-D011-B42D-00C04FC964FF)", "System.Guid", "guid")]
    [TestCase("dynamic([1,2])", "System.Object", "dynamic")]
    [TestCase("parse_json('{}')", "System.Object", "dynamic")]
    [TestCase("toint('1')", "System.Int32", "int")]
    [TestCase("tolong('1')", "System.Int64", "long")]
    [TestCase("toreal('1.5')", "System.Double", "real")]
    [TestCase("tostring(1)", "System.String", "string")]
    [TestCase("tobool('true')", "System.SByte", "bool")]
    [TestCase("strcat('a', 1)", "System.String", "string")]
    [TestCase("trim(' ', ' a ')", "System.String", "string")]
    [TestCase("bin(7, 2)", "System.Int64", "long")]
    [TestCase("bin(7, 2.5)", "System.Double", "real")]
    [TestCase("""extract_json('$.a', '{"a":1}', typeof(long))""", "System.Int64", "long")]
    [Description("getschema - type of literals and function results in print")]
    public void GetSchema_PrintExpression_ReturnsExpressionType(string expression, string dataType, string columnType)
    {
        var schema = OpusTestUtils.ExecuteGetSchema(new KustoDatabase(), $"print r = {expression} | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("r", 0, dataType, columnType)
        }));
    }

    [Test]
    [Description("getschema - unnamed print columns are named print_N where N is the column ordinal")]
    public void GetSchema_PrintUnnamedColumns_NamedByOrdinal()
    {
        var schema = OpusTestUtils.ExecuteGetSchema(new KustoDatabase(), "print 1, 'a', x = 2.5, 3 | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("print_0", 0, "System.Int64", "long"),
            ("print_1", 1, "System.String", "string"),
            ("x", 2, "System.Double", "real"),
            ("print_3", 3, "System.Int64", "long")
        }));
    }

    [Test]
    [Description("getschema - escaped column name with a space")]
    public void GetSchema_EscapedColumnName_ReturnsNameWithoutBrackets()
    {
        var schema = OpusTestUtils.ExecuteGetSchema(new KustoDatabase(), "print ['my col'] = 1, 2 | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("my col", 0, "System.Int64", "long"),
            ("print_1", 1, "System.Int64", "long")
        }));
    }

    [Test]
    [Description("getschema - after project, columns are in the projected order")]
    public void GetSchema_AfterProject_ReturnsProjectedColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(kustoDatabase, "StormEvents | project EventType, State | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("EventType", 0, "System.String", "string"),
            ("State", 1, "System.String", "string")
        }));
    }

    [Test]
    [Description("getschema - after project with computed columns")]
    public void GetSchema_AfterProjectWithExpressions_ReturnsComputedTypes()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            "StormEvents | project Day = bin(StartTime, 1d), Label = strcat(State, '-', EventType), Damage = tolong(DamageProperty) | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Day", 0, "System.DateTime", "datetime"),
            ("Label", 1, "System.String", "string"),
            ("Damage", 2, "System.Int64", "long")
        }));
    }

    [Test]
    [Description("getschema - extend appends new columns after the existing ones")]
    public void GetSchema_AfterExtend_AppendsColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            "StormEvents | extend Label = strcat(State, '-', EventType), IsBig = DamageProperty > 10000, "
            + "Day = bin(StartTime, 1d), Damage = tolong(DamageProperty) | getschema");

        var expected = new List<(string, int, string, string)>(StormEventsSchema)
        {
            ("Label", 4, "System.String", "string"),
            ("IsBig", 5, "System.SByte", "bool"),
            ("Day", 6, "System.DateTime", "datetime"),
            ("Damage", 7, "System.Int64", "long")
        };
        Assert.That(schema, Is.EqualTo(expected));
    }

    [Test]
    [Description("getschema - extend of an existing column replaces it in place (same ordinal, new type)")]
    public void GetSchema_ExtendExistingColumn_ReplacesInPlace()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            "StormEvents | extend State = strcat(State, '!'), DamageProperty = toreal(DamageProperty) | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("StartTime", 0, "System.DateTime", "datetime"),
            ("State", 1, "System.String", "string"),
            ("EventType", 2, "System.String", "string"),
            ("DamageProperty", 3, "System.Double", "real")
        }));
    }

    [Test]
    [Description("getschema - types of constant columns")]
    public void GetSchema_ExtendConstants_ReturnsLiteralTypes()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            "StormEvents | extend One = 1, Name = 'x', Flag = true, Rate = 0.5, When = datetime(2020-01-01), Span = 1h "
            + "| project One, Name, Flag, Rate, When, Span | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("One", 0, "System.Int64", "long"),
            ("Name", 1, "System.String", "string"),
            ("Flag", 2, "System.SByte", "bool"),
            ("Rate", 3, "System.Double", "real"),
            ("When", 4, "System.DateTime", "datetime"),
            ("Span", 5, "System.TimeSpan", "timespan")
        }));
    }

    [Test]
    [Description("getschema - result types of trim, bin, strcat, to* conversions, pack_all, parse_json and typed extract_json")]
    public void GetSchema_FunctionResults_ReturnsFunctionTypes()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            "StormEvents | project Trimmed = trim(' ', State), Bucket = bin(DamageProperty, 1000), Day = bin(StartTime, 1d), "
            + "Joined = strcat(State, DamageProperty), AsLong = tolong(DamageProperty), AsInt = toint(DamageProperty), "
            + "AsReal = toreal(DamageProperty), AsString = tostring(DamageProperty), AsBool = tobool(DamageProperty), "
            + "Bag = pack_all(), Json = parse_json(State), Extracted = extract_json('$.a', State, typeof(long)) | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Trimmed", 0, "System.String", "string"),
            ("Bucket", 1, "System.Int64", "long"),
            ("Day", 2, "System.DateTime", "datetime"),
            ("Joined", 3, "System.String", "string"),
            ("AsLong", 4, "System.Int64", "long"),
            ("AsInt", 5, "System.Int32", "int"),
            ("AsReal", 6, "System.Double", "real"),
            ("AsString", 7, "System.String", "string"),
            ("AsBool", 8, "System.SByte", "bool"),
            ("Bag", 9, "System.Object", "dynamic"),
            ("Json", 10, "System.Object", "dynamic"),
            ("Extracted", 11, "System.Int64", "long")
        }));
    }

    [Test]
    [Description("getschema - after count")]
    public void GetSchema_AfterCount_ReturnsCountColumn()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(kustoDatabase, "StormEvents | count | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Count", 0, "System.Int64", "long")
        }));
    }

    [Test]
    [Description("getschema - after distinct, columns are in the listed order")]
    public void GetSchema_AfterDistinct_ReturnsDistinctColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(kustoDatabase, "StormEvents | distinct EventType, State | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("EventType", 0, "System.String", "string"),
            ("State", 1, "System.String", "string")
        }));
    }

    [TestCase("StormEvents | where State == 'OHIO' | getschema")]
    [TestCase("StormEvents | take 0 | getschema")]
    [TestCase("StormEvents | where DamageProperty > 5000 | getschema")]
    [TestCase("StormEvents | sort by DamageProperty desc | take 1 | getschema")]
    [Description("getschema - schema doesn't depend on the number of rows (also for an empty result)")]
    public void GetSchema_FilteredRows_ReturnsFullSchema(string query)
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(kustoDatabase, query);

        Assert.That(schema, Is.EqualTo(StormEventsSchema));
    }

    [Test]
    [Description("getschema - schema of an empty table")]
    public void GetSchema_EmptyTable_ReturnsSchema()
    {
        var table = OpusTestUtils.NewTable("EmptyTable")
            .WithColumn<string>("Name")
            .WithColumn<long>("Value")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var schema = OpusTestUtils.ExecuteGetSchema(kustoDatabase, "EmptyTable | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Name", 0, "System.String", "string"),
            ("Value", 1, "System.Int64", "long")
        }));
    }

    [Test]
    [Description("getschema - parse appends string columns by default, typed columns get the declared type")]
    public void GetSchema_AfterParse_ReturnsParsedColumnTypes()
    {
        var table = OpusTestUtils.NewTable("Logs")
            .WithColumn<string>("Line")
            .WithRow("id=5 dur=1.5 at=2020-01-01")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var typed = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            "Logs | parse Line with 'id=' Id:long ' dur=' Duration:real ' at=' At:datetime | getschema");
        var untyped = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            "Logs | parse Line with 'id=' Id ' dur=' Duration | getschema");

        Assert.That(typed, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Line", 0, "System.String", "string"),
            ("Id", 1, "System.Int64", "long"),
            ("Duration", 2, "System.Double", "real"),
            ("At", 3, "System.DateTime", "datetime")
        }));
        Assert.That(untyped, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Line", 0, "System.String", "string"),
            ("Id", 1, "System.String", "string"),
            ("Duration", 2, "System.String", "string")
        }));
    }

    [Test]
    [Description("getschema - schema of the getschema output itself (ColumnOrdinal is int)")]
    public void GetSchema_OfGetSchema_ReturnsOutputSchema()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(kustoDatabase, "StormEvents | getschema | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("ColumnName", 0, "System.String", "string"),
            ("ColumnOrdinal", 1, "System.Int32", "int"),
            ("DataType", 2, "System.String", "string"),
            ("ColumnType", 3, "System.String", "string")
        }));
    }

    [Test]
    [Description("getschema - ColumnOrdinal values are int")]
    public void GetSchema_ColumnOrdinal_IsInt()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var ordinals = OpusTestUtils.ExecuteAndGetValues(
            "ColumnOrdinal",
            kustoDatabase,
            "StormEvents | getschema | project ColumnOrdinal");

        Assert.That(ordinals, Is.All.TypeOf<int>());
        Assert.That(ordinals, Is.EquivalentTo(new object[] { 0, 1, 2, 3 }));
    }

    [Test]
    [Description("getschema - output can be filtered with where")]
    public void GetSchema_ThenWhere_FiltersColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var stringColumns = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "ColumnName",
            kustoDatabase,
            "StormEvents | getschema | where ColumnType == 'string' | project ColumnName");
        var lastColumns = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "ColumnName",
            kustoDatabase,
            "StormEvents | getschema | where ColumnOrdinal >= 2 | project ColumnName");

        Assert.That(stringColumns, Is.EquivalentTo(new List<string> { "State", "EventType" }));
        Assert.That(lastColumns, Is.EquivalentTo(new List<string> { "EventType", "DamageProperty" }));
    }

    [Test]
    [Description("getschema - output can be counted")]
    public void GetSchema_ThenCount_ReturnsNumberOfColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = OpusTestUtils.ExecuteAndGetSingleValue("Count", kustoDatabase, "StormEvents | getschema | count");

        OpusTestUtils.AssertTypedValue(actual, 4L);
    }

    [Test]
    [Description("getschema - output can be sorted")]
    public void GetSchema_ThenSort_ReturnsSortedColumns()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, int>(
            "ColumnName",
            "ColumnOrdinal",
            kustoDatabase,
            "StormEvents | getschema | sort by ColumnName asc | project ColumnName, ColumnOrdinal");

        Assert.That(actual, Is.EqualTo(new List<(string, int)>
        {
            ("DamageProperty", 3),
            ("EventType", 2),
            ("StartTime", 0),
            ("State", 1)
        }));
    }

    [Test]
    [Description("getschema - distinct column types")]
    public void GetSchema_ThenDistinct_ReturnsDistinctTypes()
    {
        var kustoDatabase = CreateStormEventsDatabase();

        var actual = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "ColumnType",
            kustoDatabase,
            "StormEvents | getschema | distinct ColumnType");

        Assert.That(actual, Is.EquivalentTo(new List<string> { "datetime", "string", "int" }));
    }

    private static KustoDatabase CreateStormEventsDatabase()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        return kustoDatabase;
    }
}
