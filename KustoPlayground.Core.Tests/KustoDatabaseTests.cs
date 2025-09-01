namespace KustoPlayground.Core.Tests;

public class KustoDatabaseTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void ExecuteQueryWhenUnknownTableTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        string query = @"StormEvents
            | where State == 'FLORIDA' and DamageProperty > 10000
            | project StartTime, EventType, DamageProperty
            | take 10
        ";

        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ResultRows, Is.Null);
        Assert.That(results.ExecutionErrors!, Has.Count.EqualTo(1));
    }

    [Test]
    public void ExecuteQueryWhenEmptyResultWithoutErrorsTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        var startTimeCol = new Column<DateTime>("StartTime", isNullable: false);
        var stormEvents = new Table("StormEvents", [startTimeCol]);
        kustoDatabase.AddTable(stormEvents);

        string query = @"StormEvents
            | take 10
        ";

        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null);
        Assert.That(results.ResultRows!, Is.Empty);
    }

    [Test]
    public void ExecuteQueryWithTableAndDataFromUiTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(BuildTestTable());

        string query = @"StormEvents
            | where State == 'FLORIDA' and DamageProperty > 10000
            | project StartTime, EventType, DamageProperty
            | take 10
        ";
        var results = kustoDatabase.ExecuteQuery(query);
        List<IReadOnlyDictionary<string, object?>> expected =
        [
            new Dictionary<string, object?>
            {
                { "DamageProperty", 20000 },
                { "StartTime", new DateTime(2025, 8, 23, 6, 20, 0) },
                { "EventType", "Hurricane" }
            }.AsReadOnly()
        ];

        Assert.That(results.ExecutionErrors, Is.Null);
        Assert.That(results.ResultRows, Has.Count.EqualTo(expected.Count));
        for (var index = 0; index < results.ResultRows.Count; index++)
        {
            Assert.That(results.ResultRows[index], Is.EquivalentTo(expected[index]));
        }
    }

    [Test]
    public void GetAllRowsForIntTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        HashSet<int> expectedData = [1, 2, 3, 4];
        Table table = TestUtils.GenerateTableWithColumn(expectedData, tableName: "table1");
        kustoDatabase.AddTable(table);

        List<int> actualData =
            TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase, table.Name);
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    public void GetAllRowsForLongTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        HashSet<long> expectedData = [1L, 2L, 3L, 4L];
        Table table = TestUtils.GenerateTableWithColumn(expectedData, tableName: "table1");
        kustoDatabase.AddTable(table);

        List<long> actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            TestUtils.GetColumnNane(table), kustoDatabase, table.Name);
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    public void GetAllRowsWhenTwoTablesRegisteredTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        HashSet<int> table1Data = [1, 2, 3, 4];
        Table table1 = TestUtils.GenerateTableWithColumn(table1Data, tableName: "table1");
        kustoDatabase.AddTable(table1);

        HashSet<int> table2Data = [4, 5, 6, 7];
        Table table2 = TestUtils.GenerateTableWithColumn(table2Data, tableName: "table2");
        kustoDatabase.AddTable(table2);

        List<int> actualData1 = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            TestUtils.GetColumnNane(table1), kustoDatabase, table1.Name);
        Assert.That(actualData1, Is.EquivalentTo(table1Data));

        List<int> actualData2 = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            TestUtils.GetColumnNane(table2), kustoDatabase, table2.Name);
        Assert.That(actualData2, Is.EquivalentTo(table2Data));
    }

    private static Table BuildTestTable()
    {
        var startTimeCol = new Column<DateTime>("StartTime", isNullable: false);
        var stateCol = new Column<string>("State", isNullable: false);
        var eventTypeCol = new Column<string>("EventType", isNullable: false);
        var damagePropertyCol = new Column<int>("DamageProperty", isNullable: false);

        var stormEvents = new Table("StormEvents",
            new ColumnBase[] { startTimeCol, stateCol, eventTypeCol, damagePropertyCol });

        stormEvents.AddRow(new Dictionary<string, object?>
        {
            ["StartTime"] = new DateTime(2025, 8, 23, 6, 20, 0),
            ["State"] = "FLORIDA",
            ["EventType"] = "Hurricane",
            ["DamageProperty"] = 20000
        });

        stormEvents.AddRow(new Dictionary<string, object?>
        {
            ["StartTime"] = new DateTime(2023, 3, 28, 10, 30, 0),
            ["State"] = "TEXAS",
            ["EventType"] = "Flood",
            ["DamageProperty"] = 5000
        });

        stormEvents.AddRow(new Dictionary<string, object?>
        {
            ["StartTime"] = new DateTime(2024, 6, 1, 16, 50, 30),
            ["State"] = "FLORIDA",
            ["EventType"] = "Tornado",
            ["DamageProperty"] = 5000
        });

        return stormEvents;
    }
}