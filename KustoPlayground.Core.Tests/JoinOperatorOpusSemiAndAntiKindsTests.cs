using static KustoPlayground.Core.Tests.JoinOperatorOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// The semi and anti joins (kind=leftsemi, rightsemi, leftanti, rightanti and their aliases), as in Azure Data Explorer.
/// - leftsemi returns each left row that has a match, once, however many right rows match it, so a filter by the
///   existence of a match never multiplies the rows (an event of a device listed twice in the inventory stays one
///   event); the duplicated left rows are all kept, unlike innerunique;
/// - leftanti returns the left rows without any match, for example the registered devices that sent no heartbeat;
///   anti and leftantisemi are aliases of leftanti;
/// - rightsemi and rightanti are their mirrors on the right side; rightantisemi is an alias of rightanti;
/// - the result has the columns of one side only, in their order and with their names and types: no right key column
///   (DeviceId1) and no renamed column, so a column of the other side cannot be referenced after the join;
/// - the keys follow the rules of the other joins: several keys, keys with different names ($left.A == $right.B),
///   subqueries on both sides, and the result pipes into another join, summarize or union;
/// - with non-null keys, leftsemi returns the rows of 'where Key in (subquery)' and leftanti the rows of
///   'where Key !in (subquery)', and the leftsemi and leftanti results split the left side.
/// The rows of a join come in any order, so the results are compared in any order unless the query sorts them.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class JoinOperatorOpusSemiAndAntiKindsTests
{
    private static readonly string[] EventsSchema =
        { "EventId:long", "DeviceId:string", "EventType:string", "Timestamp:datetime" };

    private static readonly string[] DevicesSchema = { "DeviceId:string", "Model:string", "Active:bool" };

    #region leftsemi and leftanti

    [Test]
    [Description("Devices | join kind=leftsemi Heartbeats: d1 (two heartbeats) and d2 (one heartbeat) are returned " +
                 "once each, with the columns of Devices only; d4 has no heartbeat")]
    public void Join_LeftSemi_ReturnsEachMatchingLeftRowOnceWithItsColumnsOnly()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "Devices | join kind=leftsemi Heartbeats on DeviceId");

        Assert.That(rows.Select(row => FormatRow(row, "DeviceId", "Model", "Active")),
            Is.EquivalentTo(new[] { "d1:Surface:True", "d2:Xbox:False" }));
        AssertColumnsInOrder(rows[0], "DeviceId", "Model", "Active");
    }

    [Test]
    [Description("Keeping the events of known devices: leftsemi returns the events 1, 2, 3 and 5 unchanged, although " +
                 "d1 is listed twice in Inventory; the event 4 of the unknown device d3 is dropped")]
    public void Join_LeftSemi_KeepsTheEventsOfKnownDevicesWithoutMultiplyingThem()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, "Events | join kind=leftsemi Inventory on DeviceId"),
            Is.EquivalentTo(new[]
            {
                "1:d1:boot:2024-01-01 08:00", "2:d1:crash:2024-01-01 09:00", "3:d2:boot:2024-01-01 10:00",
                "5:d2:shutdown:2024-01-02 09:00"
            }));
    }

    [TestCase("leftsemi", new[] { "d1:2", "d2:2" })]
    [TestCase("inner", new[] { "d1:4", "d2:2" })]
    [Description("Events per device: leftsemi keeps the two events of d1, inner multiplies them by the two inventory " +
                 "rows of d1")]
    public void Join_LeftSemiVersusInner_EventsPerDevice(string kind, string[] expectedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();
        string query = $"Events | join kind={kind} Inventory on DeviceId | summarize Rows = count() by DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "DeviceId", "Rows"), Is.EquivalentTo(expectedRows));
    }

    [Test]
    [Description("leftsemi keeps every duplicated left row: the two identical rows (k, x) of Dup are both returned, " +
                 "and rightsemi keeps both rows (k, x) of Dup on the right side")]
    public void Join_SemiKinds_KeepTheDuplicatedRowsOfTheirSide()
    {
        var kustoDatabase = CreateDuplicateRowsDatabase();

        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, "Dup | join kind=leftsemi DupR on K"),
            Is.EquivalentTo(new[] { "k:x", "k:x" }), "leftsemi");
        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, "DupR | join kind=rightsemi Dup on K"),
            Is.EquivalentTo(new[] { "k:x", "k:x" }), "rightsemi");
    }

    [TestCase("leftanti")]
    [TestCase("anti")]
    [TestCase("leftantisemi")]
    [Description("Registered devices that sent no heartbeat: leftanti and its aliases anti and leftantisemi return d4, " +
                 "with the columns of Devices only")]
    public void Join_LeftAntiAndItsAliases_ReturnTheLeftRowsWithoutMatch(string kind)
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyDictionary<string, object?> row =
            ExecuteAndGetSingleRow(kustoDatabase, $"Devices | join kind={kind} Heartbeats on DeviceId");

        AssertColumnsInOrder(row, "DeviceId", "Model", "Active");
        Assert.That(FormatRow(row, "DeviceId", "Model", "Active"), Is.EqualTo("d4:HoloLens:True"));
    }

    [Test]
    [Description("A filtered right side: only d1 sent a heartbeat after 08:00, so d2 and d4 are returned by leftanti")]
    public void Join_LeftAnti_WithAFilteredSubqueryOnTheRight()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Devices " +
                             "| join kind=leftanti (Heartbeats | where Timestamp > datetime(2024-01-01 08:00)) " +
                             "on DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "DeviceId"), Is.EquivalentTo(new[] { "d2", "d4" }));
    }

    [Test]
    [Description("Subqueries on both sides: the boot events (1, 3, 4) of the active devices (d1, d4) are the event 1")]
    public void Join_LeftSemi_WithSubqueriesOnBothSides()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "(Events | where EventType == 'boot') " +
                             "| join kind=leftsemi (Devices | where Active) on DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId"), Is.EqualTo(new[] { "1" }));
    }

    [Test]
    [Description("A leftanti join piped into a leftsemi join: the inventory rows without heartbeat (d4 Austin) of the " +
                 "registered devices")]
    public void Join_LeftAntiThenLeftSemi()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Inventory | join kind=leftanti Heartbeats on DeviceId " +
                             "| join kind=leftsemi Devices on DeviceId";

        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, query), Is.EqualTo(new[] { "d4:Austin" }));
    }

    [Test]
    [Description("A let-bound leftanti result used as the right side of a leftsemi join")]
    public void Join_LeftSemi_WithALetBoundAntiJoinOnTheRight()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "let Silent = Devices | join kind=leftanti Heartbeats on DeviceId;\n" +
                             "Inventory | join kind=leftsemi Silent on DeviceId";

        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, query), Is.EqualTo(new[] { "d4:Austin" }));
    }

    [Test]
    [Description("The leftsemi result pipes into summarize: the events of the known devices per event type")]
    public void Join_LeftSemi_ThenSummarize()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=leftsemi Devices on DeviceId " +
                             "| summarize EventCount = count() by EventType";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventType", "EventCount"),
            Is.EquivalentTo(new[] { "boot:2", "crash:1", "shutdown:1" }));
    }

    #endregion

    #region rightsemi and rightanti

    [Test]
    [Description("Devices | join kind=rightsemi Heartbeats returns the heartbeats of the known devices (both heartbeats " +
                 "of d1 and the one of d2), with the columns of Heartbeats only")]
    public void Join_RightSemi_ReturnsTheMatchingRightRowsWithTheirColumnsOnly()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "Devices | join kind=rightsemi Heartbeats on DeviceId");

        Assert.That(rows.Select(row => FormatRow(row, "DeviceId", "Timestamp")),
            Is.EquivalentTo(new[] { "d1:2024-01-01 08:00", "d1:2024-01-01 08:05", "d2:2024-01-01 08:00" }));
        AssertColumnsInOrder(rows[0], "DeviceId", "Timestamp");
    }

    [Test]
    [Description("Events | join kind=rightsemi Devices returns d1 and d2 once each, although d1 and d2 have two " +
                 "events each")]
    public void Join_RightSemi_ReturnsEachMatchingRightRowOnce()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, "Events | join kind=rightsemi Devices on DeviceId"),
            Is.EquivalentTo(new[] { "d1:Surface:True", "d2:Xbox:False" }));
    }

    [TestCase("rightanti")]
    [TestCase("rightantisemi")]
    [Description("The heartbeat of the unknown device d9: rightanti and its alias rightantisemi return the right rows " +
                 "without match, with the columns of Heartbeats only")]
    public void Join_RightAntiAndItsAlias_ReturnTheRightRowsWithoutMatch(string kind)
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyDictionary<string, object?> row =
            ExecuteAndGetSingleRow(kustoDatabase, $"Devices | join kind={kind} Heartbeats on DeviceId");

        AssertColumnsInOrder(row, "DeviceId", "Timestamp");
        Assert.That(FormatRow(row, "DeviceId", "Timestamp"), Is.EqualTo("d9:2024-01-01 08:00"));
    }

    [TestCase("leftsemi", "Name", new[] { "GET /home", "GET /home (retry)", "POST /cart" })]
    [TestCase("leftanti", "Name", new[] { "GET /about" })]
    [TestCase("rightsemi", "Message", new[] { "start", "query db", "end", "checkout" })]
    [TestCase("rightanti", "Message", new[] { "orphan trace" })]
    [Description("Requests and traces: both op1 requests have traces (leftsemi), op3 has none (leftanti); the op1 and " +
                 "op2 traces have requests (rightsemi), the op4 trace has none (rightanti)")]
    public void Join_SemiAndAntiKinds_OnRequestsAndTraces(string kind, string column, string[] expectedValues)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, $"Requests | join kind={kind} Traces on OperationId", column),
            Is.EquivalentTo(expectedValues));
    }

    #endregion

    #region output schema

    [TestCase("leftsemi")]
    [TestCase("leftanti")]
    [TestCase("anti")]
    [TestCase("leftantisemi")]
    [Description("The left semi and anti kinds return the columns of the left side, with their names, order and types")]
    public void Join_LeftSemiAndAntiKinds_ReturnTheSchemaOfTheLeftSide(string kind)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, $"Events | join kind={kind} Devices on DeviceId"),
            Is.EqualTo(EventsSchema));
    }

    [TestCase("rightsemi")]
    [TestCase("rightanti")]
    [TestCase("rightantisemi")]
    [Description("The right semi and anti kinds return the columns of the right side, not renamed (DeviceId, not " +
                 "DeviceId1)")]
    public void Join_RightSemiAndAntiKinds_ReturnTheSchemaOfTheRightSide(string kind)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, $"Events | join kind={kind} Devices on DeviceId"),
            Is.EqualTo(DevicesSchema));
    }

    [Test]
    [Description("A semi self join keeps the columns of one side without renaming anything (no DeviceId1, Model1 or " +
                 "Active1): the active devices d1 and d4")]
    public void Join_LeftSemi_SelfJoinRenamesNothing()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Devices | join kind=leftsemi (Devices | where Active) on DeviceId";

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query), Is.EqualTo(DevicesSchema));
        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, query),
            Is.EquivalentTo(new[] { "d1:Surface:True", "d4:HoloLens:True" }));
    }

    [Test]
    [Description("The values keep their types: the event 1 after leftsemi and the device d1 after rightsemi")]
    public void Join_SemiKinds_KeepTheValuesAndTypesOfTheirSide()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyDictionary<string, object?> eventRow = ExecuteAndGetSingleRow(kustoDatabase,
            "Events | join kind=leftsemi Devices on DeviceId | where EventId == 1");
        Assert.That(GetValue<long>(eventRow, "EventId"), Is.EqualTo(1L));
        Assert.That(GetValue<string>(eventRow, "DeviceId"), Is.EqualTo("d1"));
        Assert.That(GetValue<string>(eventRow, "EventType"), Is.EqualTo("boot"));
        Assert.That(GetValue<DateTime>(eventRow, "Timestamp"), Is.EqualTo(Utc(2024, 1, 1, 8)));

        IReadOnlyDictionary<string, object?> deviceRow = ExecuteAndGetSingleRow(kustoDatabase,
            "Events | join kind=rightsemi Devices on DeviceId | where DeviceId == 'd1'");
        Assert.That(GetValue<string>(deviceRow, "Model"), Is.EqualTo("Surface"));
        Assert.That(GetValue<bool>(deviceRow, "Active"), Is.True);
    }

    [TestCase("Events | join kind=leftsemi Devices on DeviceId | project DeviceId1")]
    [TestCase("Events | join kind=leftsemi Devices on DeviceId | where Active")]
    [TestCase("Events | join kind=leftanti Devices on DeviceId | project Model")]
    [TestCase("Events | join kind=anti Devices on DeviceId | project DeviceId1")]
    [TestCase("Events | join kind=rightsemi Devices on DeviceId | project EventId")]
    [TestCase("Events | join kind=rightanti Devices on DeviceId | project EventType")]
    [Description("The columns of the other side are not in the result of a semi or anti join, so referencing them is " +
                 "an error (SEM0100)")]
    public void Join_SemiAndAntiKinds_ColumnsOfTheOtherSideCannotBeReferenced(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        AssertQueryFails(kustoDatabase, query);
    }

    #endregion

    #region keys

    [TestCase("leftsemi", new[] { "100:d1", "101:d2" })]
    [TestCase("leftanti", new[] { "102:d9" })]
    [TestCase("anti", new[] { "102:d9" })]
    [TestCase("leftantisemi", new[] { "102:d9" })]
    [TestCase("rightsemi", new[] { "d1:alice", "d2:bob" })]
    [TestCase("rightanti", new[] { "d7:carol" })]
    [TestCase("rightantisemi", new[] { "d7:carol" })]
    [Description("Keys with different names: Tickets | join on $left.DeviceId == $right.Id; the tickets 100 and 101 " +
                 "have an asset, 102 (d9) has none, and the asset d7 has no ticket")]
    public void Join_SemiAndAntiKinds_OnKeysWithDifferentNames(string kind, string[] expectedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetWholeRowTexts(kustoDatabase, $"Tickets | join kind={kind} Assets on $left.DeviceId == $right.Id"),
            Is.EquivalentTo(expectedRows));
    }

    [TestCase("leftsemi", new[] { "t1:d1:boot", "t2:d1:crash" })]
    [TestCase("leftanti", new[] { "t2:d2:boot", "t3:d1:boot" })]
    [TestCase("rightsemi", new[] { "t1:d1:alice", "t2:d1:bob" })]
    [TestCase("rightanti", new[] { "t1:d2:carol" })]
    [Description("Two keys must both match: the device d1 of the tenant t3 and the device d2 of t2 are unknown, " +
                 "although d1 and d2 exist in other tenants")]
    public void Join_SemiAndAntiKinds_OnTwoKeys(string kind, string[] expectedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetWholeRowTexts(kustoDatabase,
                $"TenantEvents | join kind={kind} TenantDevices on TenantId, DeviceId"),
            Is.EquivalentTo(expectedRows));
    }

    [TestCase("leftsemi", new[] { "t1:d1:boot", "t2:d1:crash" })]
    [TestCase("leftanti", new[] { "t2:d2:boot", "t3:d1:boot" })]
    [Description("Two explicit equalities joined by 'and', one of them on keys with different names (DeviceId and Id)")]
    public void Join_SemiAndAntiKinds_OnTwoExplicitEqualities(string kind, string[] expectedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();
        string query = $"TenantEvents | join kind={kind} Registry " +
                       "on $left.TenantId == $right.TenantId and $left.DeviceId == $right.Id";

        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, query), Is.EquivalentTo(expectedRows));
    }

    #endregion

    #region equivalences

    [TestCase("leftsemi", "in", new[] { 1L, 2L, 3L, 5L })]
    [TestCase("leftanti", "!in", new[] { 4L })]
    [Description("With non-null keys leftsemi returns the rows of 'where DeviceId in (subquery)' and leftanti the rows " +
                 "of 'where DeviceId !in (subquery)'")]
    public void Join_LeftSemiAndLeftAnti_MatchInAndNotInWithASubquery(string kind, string inOperator,
        long[] expectedEventIds)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            GetValues<long>(ExecuteAndGetRows(kustoDatabase, $"Events | join kind={kind} Devices on DeviceId"),
                "EventId"),
            Is.EquivalentTo(expectedEventIds), "join");
        Assert.That(
            GetValues<long>(
                ExecuteAndGetRows(kustoDatabase, $"Events | where DeviceId {inOperator} (Devices | project DeviceId)"),
                "EventId"),
            Is.EquivalentTo(expectedEventIds), inOperator);
    }

    [Test]
    [Description("leftsemi and leftanti split the left side: their union has every event exactly once; rightsemi and " +
                 "rightanti split the right side: their union has every device exactly once")]
    public void Join_SemiAndAntiResults_SplitTheirSide()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase,
                "union (Events | join kind=leftsemi Devices on DeviceId), " +
                "(Events | join kind=leftanti Devices on DeviceId)",
                "EventId"),
            Is.EquivalentTo(new[] { "1", "2", "3", "4", "5" }), "left side");
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase,
                "union (Events | join kind=rightsemi Devices on DeviceId), " +
                "(Events | join kind=rightanti Devices on DeviceId)",
                "DeviceId"),
            Is.EquivalentTo(new[] { "d1", "d2", "d4" }), "right side");
    }

    [TestCase("Devices | join kind=leftsemi Heartbeats on DeviceId",
        "Heartbeats | join kind=rightsemi Devices on DeviceId")]
    [TestCase("Devices | join kind=leftanti Heartbeats on DeviceId",
        "Heartbeats | join kind=rightanti Devices on DeviceId")]
    [TestCase("Heartbeats | join kind=leftsemi Devices on DeviceId",
        "Devices | join kind=rightsemi Heartbeats on DeviceId")]
    [Description("A | join kind=leftsemi B returns the rows of B | join kind=rightsemi A, and the same for the anti kinds")]
    public void Join_LeftAndRightKinds_AreMirrors(string leftKindQuery, string mirroredQuery)
    {
        var kustoDatabase = CreateScenarioDatabase();

        List<string> leftKindRows = ExecuteAndGetWholeRowTexts(kustoDatabase, leftKindQuery);

        Assert.That(leftKindRows, Is.Not.Empty);
        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, mirroredQuery), Is.EquivalentTo(leftKindRows));
    }

    #endregion
}
