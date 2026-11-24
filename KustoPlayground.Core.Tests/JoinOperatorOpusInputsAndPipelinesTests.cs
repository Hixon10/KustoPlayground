using static KustoPlayground.Core.Tests.JoinOperatorOpusTestUtils;
using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// The inputs of join and the pipelines around it, as in Azure Data Explorer.
/// - either side may be a table name, a parenthesized pipeline (Events | join kind=leftouter (Devices | where Active)
///   on DeviceId), a let-bound table or pipeline, a materialized let, a call of a tabular let function (with or without
///   parameters), a union or a print; the left side is whatever the pipe passes to join;
/// - the result of a join pipes into any operator: where, extend, project, summarize, distinct, sort, take, count, union,
///   another join (a chain of joins, or a join nested inside the right side), toscalar and 'in' subqueries;
/// - join works on the result of summarize, before, between and after other joins: the counts per device enriched with
///   the device, the latest status of every device (arg_max), the events of every day with its quota;
/// - typical pipelines: the events of the boots followed by a crash on the same device, the heartbeats received before
///   an event, the events of the devices of one model through a let function.
/// The rows of a join come in any order, so the results are compared in any order unless the query sorts them.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class JoinOperatorOpusInputsAndPipelinesTests
{
    #region input forms

    [TestCase("Events | join kind=inner (Devices | where Active) on DeviceId")]
    [TestCase("let ActiveDevices = Devices | where Active;\nEvents | join kind=inner ActiveDevices on DeviceId")]
    [TestCase("let ActiveDevices = Devices | where Active;\nEvents | join kind=inner (ActiveDevices) on DeviceId")]
    [TestCase("let ActiveDevices = materialize(Devices | where Active);\n" +
              "Events | join kind=inner ActiveDevices on DeviceId")]
    [TestCase("let ActiveDevices = () { Devices | where Active };\n" +
              "Events | join kind=inner ActiveDevices() on DeviceId")]
    [TestCase("let DevicesIn = (active: bool) { Devices | where Active == active };\n" +
              "Events | join kind=inner DevicesIn(true) on DeviceId")]
    [Description("The right side as a parenthesized pipeline, a let-bound pipeline (with or without parentheses), a " +
                 "materialized let and a call of a let function without and with a parameter: the active devices " +
                 "d1 and d4 match the events 1 and 2")]
    public void Join_RightSideForms(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Model"),
            Is.EquivalentTo(new[] { "1:Surface", "2:Surface" }));
    }

    [TestCase("Events | where EventType == 'boot' | join kind=inner Devices on DeviceId")]
    [TestCase("(Events | where EventType == 'boot') | join kind=inner Devices on DeviceId")]
    [TestCase("let Boots = Events | where EventType == 'boot';\nBoots | join kind=inner Devices on DeviceId")]
    [TestCase("let Boots = () { Events | where EventType == 'boot' };\nBoots() | join kind=inner Devices on DeviceId")]
    [Description("The left side as a pipeline, a parenthesized pipeline, a let-bound pipeline and a call of a let " +
                 "function: the boot events 1 and 3 have a device, the boot event 4 (d3) has none")]
    public void Join_LeftSideForms(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Model"),
            Is.EquivalentTo(new[] { "1:Surface", "3:Xbox" }));
    }

    [Test]
    [Description("A let-bound right side with leftouter: every event is kept, the events of the inactive or unknown " +
                 "devices with an empty Model")]
    public void Join_LeftOuter_WithALetBoundRightSide()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "let ActiveDevices = Devices | where Active;\n" +
                             "Events | join kind=leftouter ActiveDevices on DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Model"),
            Is.EquivalentTo(new[] { "1:Surface", "2:Surface", "3:", "4:", "5:" }));
    }

    [TestCase("Events | join kind=inner (union Devices, ArchivedDevices) on DeviceId")]
    [TestCase("Events | join kind=inner (Devices | union ArchivedDevices) on DeviceId")]
    [Description("A union on the right side: with the archived device d3, every event has a device")]
    public void Join_WithAUnionOnTheRight(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Model"),
            Is.EquivalentTo(new[] { "1:Surface", "2:Surface", "3:Xbox", "4:Zune", "5:Xbox" }));
    }

    [Test]
    [Description("A union on the left side: the current and the archived devices with their events")]
    public void Join_WithAUnionOnTheLeft()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Devices | union ArchivedDevices | join kind=inner Events on DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Model", "EventId"),
            Is.EquivalentTo(new[] { "Surface:1", "Surface:2", "Xbox:3", "Xbox:5", "Zune:4" }));
    }

    [Test]
    [Description("print as the left side: a lookup of one device")]
    public void Join_WithPrintOnTheLeft()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, "print DeviceId = 'd1' | join kind=inner Devices on DeviceId", "Model"),
            Is.EqualTo(new[] { "Surface" }));
    }

    [Test]
    [Description("A self join through a let-bound table: every event with every event of its device (9 rows)")]
    public void Join_SelfJoinThroughALetBoundTable()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "let E = Events;\nE | join kind=inner E on DeviceId | count";

        Assert.That(ExecuteAndGetCount(kustoDatabase, query), Is.EqualTo(9L));
    }

    #endregion

    #region join with summarize

    [Test]
    [Description("summarize before join: the event count of every device, enriched with its model; the unknown " +
                 "device d3 keeps its count with an empty model")]
    public void Join_AfterSummarize()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | summarize EventCount = count() by DeviceId " +
                             "| join kind=leftouter Devices on DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "DeviceId", "EventCount", "Model"),
            Is.EquivalentTo(new[] { "d1:2:Surface", "d2:2:Xbox", "d3:1:" }));
    }

    [Test]
    [Description("summarize between two joins: the distinct locations of the devices with events, then their model")]
    public void Join_SummarizeBetweenTwoJoins()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner Inventory on DeviceId " +
                             "| summarize Locations = dcount(Location) by DeviceId " +
                             "| join kind=inner Devices on DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "DeviceId", "Locations", "Model"),
            Is.EquivalentTo(new[] { "d1:2:Surface", "d2:1:Xbox" }));
    }

    [Test]
    [Description("summarize after join: the events per model")]
    public void Summarize_AfterJoin()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner Devices on DeviceId | summarize EventCount = count() by Model";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Model", "EventCount"),
            Is.EquivalentTo(new[] { "Surface:2", "Xbox:2" }));
    }

    [Test]
    [Description("The latest status of every device: arg_max on the right side of a leftouter join; d4 has no status")]
    public void Join_WithTheLatestStatusOfEveryDevice()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Devices " +
                             "| join kind=leftouter (DeviceStatus | summarize arg_max(Timestamp, Status) by DeviceId) " +
                             "on DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "DeviceId", "Status", "Timestamp"),
            Is.EquivalentTo(new[] { "d1:offline:2024-01-01 12:00", "d2:online:2024-01-01 09:00", "d4::" }));
    }

    [Test]
    [Description("The events of every day joined with the quota of the day on a datetime key computed by bin")]
    public void Join_DailyCountsWithTheQuotaOfTheDay()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | summarize EventCount = count() by Day = bin(Timestamp, 1d) " +
                             "| join kind=inner DailyQuota on Day";

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndGetRows(kustoDatabase, query);

        Assert.That(rows, Has.Count.EqualTo(2));
        IReadOnlyDictionary<string, object?> firstDay = GetSingleRowWhere(rows, "Day", Utc(2024, 1, 1));
        Assert.That(GetValue<long>(firstDay, "EventCount"), Is.EqualTo(3L));
        Assert.That(GetValue<long>(firstDay, "Quota"), Is.EqualTo(10L));
        IReadOnlyDictionary<string, object?> secondDay = GetSingleRowWhere(rows, "Day", Utc(2024, 1, 2));
        Assert.That(GetValue<long>(secondDay, "EventCount"), Is.EqualTo(2L));
        Assert.That(GetValue<long>(secondDay, "Quota"), Is.EqualTo(20L));
    }

    #endregion

    #region chains and nested joins

    [TestCase("Events | join kind=inner Devices on DeviceId | join kind=inner ModelCatalog on Model")]
    [TestCase("let Enriched = Events | join kind=inner Devices on DeviceId;\n" +
              "Enriched | join kind=inner ModelCatalog on Model")]
    [TestCase("Events | join kind=inner (Devices | join kind=inner ModelCatalog on Model) on DeviceId")]
    [Description("The category of the device of every event: a chain of two joins, a join on a let-bound join, and a " +
                 "join nested inside the right side")]
    public void Join_ChainedLetBoundAndNested(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Category"),
            Is.EquivalentTo(new[] { "1:laptop", "2:laptop", "3:console", "5:console" }));
    }

    [Test]
    [Description("leftouter then leftanti: the event whose model (empty, no device) has no category")]
    public void Join_LeftOuterThenLeftAnti()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=leftouter Devices on DeviceId " +
                             "| join kind=leftanti ModelCatalog on Model";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId"), Is.EqualTo(new[] { "4" }));
    }

    [Test]
    [Description("The symmetric difference of the device ids of Events and Devices: leftanti both ways, unioned")]
    public void Union_OfTwoLeftAntiJoins_IsTheSymmetricDifference()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "union (Events | join kind=leftanti Devices on DeviceId | project DeviceId), " +
                             "(Devices | join kind=leftanti Events on DeviceId | project DeviceId)";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "DeviceId"), Is.EquivalentTo(new[] { "d3", "d4" }));
    }

    [Test]
    [Description("A boot followed by a crash on the same device: the boot events joined with the crash events")]
    public void Join_BootsWithCrashesOfTheSameDevice()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | where EventType == 'boot' " +
                             "| join kind=inner (Events | where EventType == 'crash') on DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "DeviceId", "EventId", "EventId1"),
            Is.EqualTo(new[] { "d1:1:2" }));
    }

    [Test]
    [Description("The heartbeats received before each event of the device (an as-of condition applied with where " +
                 "after the join): two for the event 2, one for the events 3 and 5, none for the event 1")]
    public void Where_AfterJoin_AppliesAConditionBetweenTheSides()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner Heartbeats on DeviceId | where Timestamp1 < Timestamp " +
                             "| summarize Heartbeats = count() by EventId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Heartbeats"),
            Is.EquivalentTo(new[] { "2:2", "3:1", "5:1" }));
    }

    #endregion

    #region other operators around join

    [TestCase("Events | where Timestamp >= datetime(2024-01-02) | join kind=inner Devices on DeviceId")]
    [TestCase("let Since = datetime(2024-01-02);\n" +
              "Events | where Timestamp >= Since | join kind=inner Devices on DeviceId")]
    [Description("A time filter before the join: the events of 2024-01-02 with a known device (event 5)")]
    public void Join_AfterATimeFilter(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId"), Is.EqualTo(new[] { "5" }));
    }

    [Test]
    [Description("extend after join computes from the columns of both sides")]
    public void Extend_AfterJoin_UsesTheColumnsOfBothSides()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner Devices on DeviceId " +
                             "| extend Label = strcat(Model, '/', EventType)";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Label"),
            Is.EquivalentTo(new[] { "Surface/boot", "Surface/crash", "Xbox/boot", "Xbox/shutdown" }));
    }

    [Test]
    [Description("sort after join orders the joined rows")]
    public void Sort_AfterJoin()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner Devices on DeviceId | sort by EventId asc | project EventId";

        Assert.That(GetValues<long>(ExecuteAndGetRows(kustoDatabase, query), "EventId"),
            Is.EqualTo(new[] { 1L, 2L, 3L, 5L }));
    }

    [Test]
    [Description("sort and take after join: the latest event of a known device")]
    public void SortAndTake_AfterJoin()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner Devices on DeviceId | sort by Timestamp desc | take 1";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Model"), Is.EqualTo(new[] { "5:Xbox" }));
    }

    [Test]
    [Description("distinct after join removes the copies made by the repeated keys")]
    public void Distinct_AfterJoin()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner Inventory on DeviceId | distinct DeviceId, Location";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "DeviceId", "Location"),
            Is.EquivalentTo(new[] { "d1:Redmond", "d1:Seattle", "d2:Dublin" }));
    }

    [Test]
    [Description("toscalar of a counted join")]
    public void ToScalar_OfAJoin()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "print Rows = toscalar(Events | join kind=inner Devices on DeviceId | count)");

        Assert.That(GetValue<long>(row, "Rows"), Is.EqualTo(4L));
    }

    [Test]
    [Description("A join as the subquery of 'in': the devices that have an event and an inventory row")]
    public void In_WithAJoinSubquery()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Devices " +
                             "| where DeviceId in (Events | join kind=inner Inventory on DeviceId | project DeviceId)";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "DeviceId"), Is.EquivalentTo(new[] { "d1", "d2" }));
    }

    [Test]
    [Description("A let function with a parameter that contains a join: the events of the Xbox devices")]
    public void Join_InsideALetFunctionWithAParameter()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "let EventsOf = (model: string) " +
                             "{ Events | join kind=inner (Devices | where Model == model) on DeviceId };\n" +
                             "EventsOf('Xbox')";

        Assert.That(GetValues<long>(ExecuteAndGetRows(kustoDatabase, query), "EventId"),
            Is.EquivalentTo(new[] { 3L, 5L }));
    }

    #endregion
}
