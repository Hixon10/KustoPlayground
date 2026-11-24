using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;
using static KustoPlayground.Core.Tests.TestUtils;

namespace KustoPlayground.Core.Tests;

public class JoinSchemaAndPipelineAstraTests
{
    [Test]
    public void RightKeyAndPayloadCollisionsRespectNamesAlreadyTakenOnTheLeft()
    {
        var db = CreateDatabase(
            GenerateTableWith3Columns<long, string, string>([(7, "already named", "event")],
                "DeviceId", "DeviceId1", "Status", "Events"),
            GenerateTableWith3Columns<int, long, string>([(200, 7, "rack-a")],
                "Status", "DeviceId", "Rack", "Devices"));

        var row = ExecuteAndGetSingleRow(db, "Events | join kind=inner Devices on DeviceId");

        AssertColumnsInOrder(row, "DeviceId", "DeviceId1", "Status", "Status1", "DeviceId2", "Rack");
        Assert.That(row.Values, Is.EqualTo(new object[] { 7L, "already named", "event", 200, 7L, "rack-a" }));
    }

    [TestCase("fullouter", new[] { "EventId", "DeviceId", "DeviceId1", "Location" }, new[] { "int", "long", "long", "string" })]
    [TestCase("leftanti",  new[] { "EventId", "DeviceId" }, new[] { "int", "long" })]
    [TestCase("rightsemi", new[] { "DeviceId", "Location" }, new[] { "long", "string" })]
    public void TwoEmptyInputsReturnNoRowsButStillExposeTheJoinSchema(string kind, string[] names, string[] types)
    {
        var db = CreateDatabase(
            GenerateTableWith2Columns<int, long>([], "EventId", "DeviceId", "Events"),
            GenerateTableWith2Columns<long, string>([], "DeviceId", "Location", "Devices"));
        string join = $"Events | join kind={kind} Devices on DeviceId";

        Assert.That(ExecuteAndGetRows(db, join), Is.Empty);
        var schema = ExecuteAndGetDataFor3Columns<string, int, string>("ColumnName", "ColumnOrdinal", "ColumnType",
            db, join + " | getschema");
        Assert.That(schema, Is.EquivalentTo(names.Select((name, ordinal) => (name, ordinal, types[ordinal]))));
    }

    [Test]
    public void FullOuterSnapshotKeepsBothKeysSoAdditionsAndRemovalsAreDistinguishable()
    {
        var db = CreateDatabase(
            GenerateTableWith2Columns<long, string>([(1, "removed"), (2, "before")], "DeviceId", "Status", "Yesterday"),
            GenerateTableWith2Columns<long, string>([(2, "after"), (3, "added")], "DeviceId", "Status", "Today"));

        var rows = ExecuteAndGetDataFor4Columns<long?, string, long?, string>("OldId", "OldStatus", "NewId", "NewStatus", db,
            """
            Yesterday | join kind=fullouter Today on DeviceId
            | project OldId = DeviceId, OldStatus = Status, NewId = DeviceId1, NewStatus = Status1
            """);

        Assert.That(rows, Is.EquivalentTo(new (long?, string, long?, string)[]
            { (1, "removed", null, ""), (2, "before", 2, "after"), (null, "", 3, "added") }));
    }

    private static KustoDatabase EventInventory() => CreateDatabase(
        GenerateTableWith2Columns<int, long>([(1, 7), (2, 8), (3, 9), (4, 7), (5, 10)], "EventId", "DeviceId", "Events"),
        GenerateTableWith3Columns<long, bool, string>(
            [(7, true, "rack-a"), (7, true, "rack-b"), (7, false, "retired"), (8, false, "inactive"),
             (10, true, "rack-c"), (11, true, "spare")],
            "DeviceId", "Active", "Label", "Devices"));

    [Test]
    public void LeftOuterEnrichmentFiltersInventoryBeforeMatchingAndKeepsEventsWithoutActiveDevices()
    {
        var rows = ExecuteAndGetDataFor4Columns<int, long, long?, string>("EventId", "DeviceId", "InventoryId", "Label",
            EventInventory(),
            """
            Events | join kind=leftouter (Devices | where Active) on DeviceId
            | project EventId, DeviceId, InventoryId = DeviceId1, Label
            """);

        Assert.That(rows, Is.EquivalentTo(new (int, long, long?, string)[]
        {
            (1, 7, 7, "rack-a"), (1, 7, 7, "rack-b"), (2, 8, null, ""), (3, 9, null, ""),
            (4, 7, 7, "rack-a"), (4, 7, 7, "rack-b"), (5, 10, 10, "rack-c")
        }));
    }

    [Test]
    public void RightSubqueryCanGroupAndRenameItsKeyBeforeJoining()
    {
        var rows = ExecuteAndGetDataFor2Columns<int, long>("EventId", "Entries", EventInventory(),
            """
            Events | join kind=inner (
                Devices | where Active | summarize Entries = count() by AssetId = DeviceId
            ) on $left.DeviceId == $right.AssetId
            | project EventId, Entries
            """);

        Assert.That(rows, Is.EquivalentTo(new[] { (1, 2L), (4, 2L), (5, 1L) }));
    }

    [Test]
    public void LetBoundInputsFeedASecondJoinOnTheSuffixedKeyAndThenSummarize()
    {
        var db = EventInventory();
        db.AddTable(GenerateTableWith2Columns<long, string>([(7, "red"), (8, "inactive"), (9, "missing"), (10, "blue")],
            "Id", "Team", "Owners"));

        var totals = ExecuteAndGetDataFor2Columns<string, long>("Team", "Pairs", db,
            """
            let SelectedEvents = Events | where EventId != 4;
            let ActiveDevices = Devices | where Active;
            SelectedEvents
            | join kind=leftouter ActiveDevices on DeviceId
            | join kind=inner Owners on $left.DeviceId1 == $right.Id
            | summarize Pairs = count() by Team
            """);

        Assert.That(totals, Is.EquivalentTo(new[] { ("red", 2L), ("blue", 1L) }));
    }

    [TestCase("Events | join kind=inner (Devices | project AssetId = DeviceId) on DeviceId")]
    [TestCase("Events | join kind=leftsemi Devices on DeviceId | project Label")]
    [TestCase("Events | join kind=rightanti Devices on DeviceId | project EventId")]
    public void ColumnsRemovedByTheRightPipelineOrJoinFlavorAreNotInScope(string query)
    {
        AstraTestUtils.AssertRejected(EventInventory(), query);
    }
}
