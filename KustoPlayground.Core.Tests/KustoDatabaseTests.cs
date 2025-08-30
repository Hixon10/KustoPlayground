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
                { "EventType", "Hurricane" },
            }.AsReadOnly(),
        ];

        Assert.That(results.ExecutionErrors, Is.Null);
        Assert.That(results.ResultRows, Has.Count.EqualTo(expected.Count));
        for (var index = 0; index < results.ResultRows.Count; index++)
        {
            Assert.That(results.ResultRows[index], Is.EquivalentTo(expected[index]));
        }
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
            ["DamageProperty"] = 20000,
        });

        stormEvents.AddRow(new Dictionary<string, object?>
        {
            ["StartTime"] = new DateTime(2023, 3, 28, 10, 30, 0),
            ["State"] = "TEXAS",
            ["EventType"] = "Flood",
            ["DamageProperty"] = 5000,
        });

        stormEvents.AddRow(new Dictionary<string, object?>
        {
            ["StartTime"] = new DateTime(2024, 6, 1, 16, 50, 30),
            ["State"] = "FLORIDA",
            ["EventType"] = "Tornado",
            ["DamageProperty"] = 5000,
        });

        return stormEvents;
    }
}