namespace KustoPlayground.Core.Tests;

public class PackAllGetSchemaAstraTests
{
    [TestCase("pack_all()", """{"Blank":"","Missing":null,"Space":"  ","N":0,"Ok":false}""")]
    [TestCase("pack_all(false)", """{"Blank":"","Missing":null,"Space":"  ","N":0,"Ok":false}""")]
    [TestCase("pack_all(true)", """{"Space":"  ","N":0,"Ok":false}""")]
    public void PackAll_DistinguishesEmptyAndNullFromZeroFalseAndWhitespace(string expression, string expectedJson)
    {
        var database = new KustoDatabase();
        var table = new Table("Samples", [
            new Column<string>("Blank"), new Column<long>("Missing"), new Column<string>("Space"),
            new Column<int>("N"), new Column<bool>("Ok")
        ]);
        table.AddRow(new Dictionary<string, object?>
        {
            ["Blank"] = "", ["Missing"] = null, ["Space"] = "  ", ["N"] = 0, ["Ok"] = false
        });
        database.AddTable(table);

        var bags = TestUtils.ExecuteAndGetDataForOneColumn<object?>("Bag", database,
            $"Samples | extend Bag = {expression}");

        Assert.That(bags, Has.Count.EqualTo(1));
        AstraTestUtils.AssertJsonValue(bags[0], expectedJson);
    }

    [Test]
    public void PackAll_IgnoringEveryColumnReturnsAnEmptyBagNotNull()
    {
        var bags = TestUtils.ExecuteAndGetDataForOneColumn<object?>("Bag", new KustoDatabase(),
            "print Label = '', N = long(null) | extend Bag = pack_all(true)");

        Assert.That(bags, Has.Count.EqualTo(1));
        AstraTestUtils.AssertJsonValue(bags[0], "{}");
    }

    [Test]
    public void PackAll_UsesEachRowsCurrentProjectedNamesAndValues()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWith3Columns<long, string, string>(
            [(1, "red", "ignored"), (2, "blue", "also ignored")], "Id", "Label", "Old", "Samples"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<long, object?>("Id", "Bag", database,
            """
            Samples
            | project Id, ['display.name'] = Label
            | extend Bag = pack_all()
            """);

        Assert.That(rows.Select(row => row.Item1), Is.EquivalentTo(new[] { 1L, 2L }));
        AstraTestUtils.AssertJsonValue(rows.Single(row => row.Item1 == 1).Item2, """{"Id":1,"display.name":"red"}""");
        AstraTestUtils.AssertJsonValue(rows.Single(row => row.Item1 == 2).Item2, """{"display.name":"blue","Id":2}""");
    }

    [Test]
    public void PackAll_KeepsNestedDynamicDataStructured()
    {
        var bags = TestUtils.ExecuteAndGetDataForOneColumn<object?>("Bag", new KustoDatabase(),
            """print Child = dynamic({"items":[2,null,1]}), Label = "x" | extend Bag = pack_all()""");

        Assert.That(bags, Has.Count.EqualTo(1));
        AstraTestUtils.AssertJsonValue(bags[0], """{"Label":"x","Child":{"items":[2,null,1]}}""");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GetSchema_ReportsDeclaredTypesAndOrdinalsWithoutNeedingSampleValues(bool addRow)
    {
        var table = new Table("Typed", [
            new Column<DateTime>("Stamp"), new Column<string>("Label"), new Column<long>("Big"),
            new Column<int>("Small"), new Column<bool>("Ok"), new Column<double>("Ratio"),
            new Column<TimeSpan>("Elapsed"), new Column<Guid>("Token")
        ]);
        if (addRow)
        {
            table.AddRow(new Dictionary<string, object?> { ["Label"] = "" });
        }

        var database = new KustoDatabase();
        database.AddTable(table);
        var rows = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName", "ColumnOrdinal", "DataType", "ColumnType", database, "Typed | getschema");

        Assert.That(rows, Is.EquivalentTo(new (string, int, string, string)[]
        {
            ("Stamp", 0, "System.DateTime", "datetime"), ("Label", 1, "System.String", "string"),
            ("Big", 2, "System.Int64", "long"), ("Small", 3, "System.Int32", "int"),
            ("Ok", 4, "System.SByte", "bool"), ("Ratio", 5, "System.Double", "real"),
            ("Elapsed", 6, "System.TimeSpan", "timespan"), ("Token", 7, "System.Guid", "guid")
        }));
    }

    [Test]
    public void GetSchema_ReflectsProjectionOrderRenamesAndComputedTypesAfterAllRowsAreFiltered()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWith2Columns<long, string>([(1, "one")], "Id", "Label", "Samples"));
        var rows = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName", "ColumnOrdinal", "DataType", "ColumnType", database,
            """
            Samples
            | extend Bag = pack_all(), Amount = toreal(Id)
            | project Payload = Bag, Amount, ['code text'] = Label
            | where false
            | getschema
            """);

        Assert.That(rows, Is.EquivalentTo(new (string, int, string, string)[]
        {
            ("Payload", 0, "System.Object", "dynamic"), ("Amount", 1, "System.Double", "real"),
            ("code text", 2, "System.String", "string")
        }));
    }

    [Test]
    public void GetSchema_DistinguishesParsedJsonFromUntypedAndTypedJsonExtraction()
    {
        var rows = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName", "ColumnOrdinal", "DataType", "ColumnType", new KustoDatabase(),
            """
            print Payload = '{"v":7}'
            | extend Parsed = parse_json(Payload), Untyped = extract_json('$.v', Payload),
                     Typed = extract_json('$.v', Payload, typeof(int))
            | project Parsed, Untyped, Typed
            | getschema
            """);

        Assert.That(rows, Is.EquivalentTo(new (string, int, string, string)[]
        {
            ("Parsed", 0, "System.Object", "dynamic"), ("Untyped", 1, "System.String", "string"),
            ("Typed", 2, "System.Int32", "int")
        }));
    }

    [Test]
    public void GetSchema_EmptyParseResultStillDeclaresItsCaptures()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(Array.Empty<string>(), "Message", "Logs"));
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, int, string>(
            "ColumnName", "ColumnOrdinal", "ColumnType", database,
            """Logs | parse Message with "id=" Id:long ";label=" Label | getschema""");

        Assert.That(rows, Is.EquivalentTo(new[] { ("Message", 0, "string"), ("Id", 1, "long"), ("Label", 2, "string") }));
    }

    [Test]
    public void GetSchema_ProducesATableWithExactlyFourMetadataColumns()
    {
        var rows = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName", "ColumnOrdinal", "DataType", "ColumnType", new KustoDatabase(),
            "print Id = 1 | getschema | getschema");

        Assert.That(rows, Is.EquivalentTo(new (string, int, string, string)[]
        {
            ("ColumnName", 0, "System.String", "string"), ("ColumnOrdinal", 1, "System.Int32", "int"),
            ("DataType", 2, "System.String", "string"), ("ColumnType", 3, "System.String", "string")
        }));
    }
}
