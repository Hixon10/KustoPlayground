using static KustoPlayground.Core.Tests.JoinOperatorOpusTestUtils;
using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// The output columns of join, as in Azure Data Explorer.
/// - innerunique, inner, leftouter, rightouter and fullouter return the left columns followed by the right columns,
///   both in their input order and with their types, the key columns of both sides included: a join on DeviceId returns
///   DeviceId (left) and DeviceId1 (right), and the queries after the join can reference either;
/// - a right column whose name is already taken gets the first free suffix 1, 2, ...: DeviceId1, then DeviceId2 in a
///   second join, Key2 when the right side already has Key1, Value11 for a second Value1; the non-key columns are
///   renamed the same way (Timestamp1, Model1, Active1), and a left column is never renamed;
/// - keys with different names ($left.DeviceId == $right.Id) keep both names, and a right column is renamed only when it
///   collides with a left column (the Id of Tickets and the Id of Assets give Id and Id1);
/// - the semi and anti kinds return the columns of one side only (see JoinOperatorOpusSemiAndAntiKindsTests);
/// - the schema does not depend on the rows: an empty result has the same columns.
/// The rows of a join come in any order, so the results are compared in any order unless the query sorts them.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class JoinOperatorOpusOutputSchemaTests
{
    private static readonly string[] EventsJoinDevicesSchema =
    {
        "EventId:long", "DeviceId:string", "EventType:string", "Timestamp:datetime", "DeviceId1:string",
        "Model:string", "Active:bool"
    };

    private static readonly string[] EventsJoinDevicesColumns =
        { "EventId", "DeviceId", "EventType", "Timestamp", "DeviceId1", "Model", "Active" };

    /// <summary>
    /// X(Key, Key1, V) = (k, k1, v), Y(Key, W) = (k, w) and S(Key, Value1) = (k, s1), all strings:
    /// tables whose column names collide with the suffixed names of a join.
    /// </summary>
    private static KustoDatabase CreateCollisionDatabase()
    {
        return CreateDatabase(
            new SummarizeOperatorOpusTableBuilder("X")
                .WithColumn<string>("Key")
                .WithColumn<string>("Key1")
                .WithColumn<string>("V")
                .WithRow("k", "k1", "v")
                .Build(),
            new SummarizeOperatorOpusTableBuilder("Y")
                .WithColumn<string>("Key")
                .WithColumn<string>("W")
                .WithRow("k", "w")
                .Build(),
            new SummarizeOperatorOpusTableBuilder("S")
                .WithColumn<string>("Key")
                .WithColumn<string>("Value1")
                .WithRow("k", "s1")
                .Build());
    }

    private static List<string> ExecuteAndGetNamedCells(KustoDatabase kustoDatabase, string query)
    {
        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase, query);
        return row.Keys.Select(column => $"{column}={FormatRow(row, column)}").ToList();
    }

    #region columns per kind

    [TestCase("")]
    [TestCase("kind=innerunique")]
    [TestCase("kind=inner")]
    [TestCase("kind=leftouter")]
    [TestCase("kind=rightouter")]
    [TestCase("kind=fullouter")]
    [Description("The kinds that return both sides return the left columns, then the right columns, both keys " +
                 "included: the right DeviceId becomes DeviceId1")]
    public void Join_KindsReturningBothSides_ReturnTheLeftThenTheRightColumns(string kindClause)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, $"Events | join {kindClause} Devices on DeviceId"),
            Is.EqualTo(EventsJoinDevicesSchema));
    }

    [TestCase("innerunique", new[] { "EventId", "DeviceId", "EventType", "Timestamp", "DeviceId1", "Model", "Active" })]
    [TestCase("inner", new[] { "EventId", "DeviceId", "EventType", "Timestamp", "DeviceId1", "Model", "Active" })]
    [TestCase("leftouter", new[] { "EventId", "DeviceId", "EventType", "Timestamp", "DeviceId1", "Model", "Active" })]
    [TestCase("rightouter", new[] { "EventId", "DeviceId", "EventType", "Timestamp", "DeviceId1", "Model", "Active" })]
    [TestCase("fullouter", new[] { "EventId", "DeviceId", "EventType", "Timestamp", "DeviceId1", "Model", "Active" })]
    [TestCase("leftsemi", new[] { "EventId", "DeviceId", "EventType", "Timestamp" })]
    [TestCase("leftanti", new[] { "EventId", "DeviceId", "EventType", "Timestamp" })]
    [TestCase("rightsemi", new[] { "DeviceId", "Model", "Active" })]
    [TestCase("rightanti", new[] { "DeviceId", "Model", "Active" })]
    [Description("Every result row has the columns of its kind, in order")]
    public void Join_ResultRows_HaveTheColumnsOfTheKind(string kind, string[] expectedColumns)
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, $"Events | join kind={kind} Devices on DeviceId");

        Assert.That(rows, Is.Not.Empty);
        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            AssertColumnsInOrder(row, expectedColumns);
        }
    }

    [TestCase("Events | join kind=inner (Devices | where DeviceId == 'none') on DeviceId")]
    [TestCase("Events | where EventId < 0 | join kind=inner Devices on DeviceId")]
    [TestCase("Events | join kind=fullouter Devices on DeviceId | where EventId < 0 and isempty(DeviceId1)")]
    [Description("An empty result has the same columns as a non-empty one")]
    public void Join_EmptyResult_HasTheSameColumns(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, query + " | count"), Is.EqualTo(0L));
        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query), Is.EqualTo(EventsJoinDevicesSchema));
    }

    [Test]
    [Description("The values of a row are under the names of their side: the event 1 under the Events columns, its " +
                 "device under DeviceId1, Model and Active, with their types")]
    public void Join_Inner_PutsTheValuesUnderTheColumnsOfTheirSide()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Events | join kind=inner Devices on DeviceId | where EventId == 1");

        AssertColumnsInOrder(row, EventsJoinDevicesColumns);
        Assert.That(GetValue<long>(row, "EventId"), Is.EqualTo(1L));
        Assert.That(GetValue<string>(row, "DeviceId"), Is.EqualTo("d1"));
        Assert.That(GetValue<string>(row, "EventType"), Is.EqualTo("boot"));
        Assert.That(GetValue<DateTime>(row, "Timestamp"), Is.EqualTo(Utc(2024, 1, 1, 8)));
        Assert.That(GetValue<string>(row, "DeviceId1"), Is.EqualTo("d1"));
        Assert.That(GetValue<string>(row, "Model"), Is.EqualTo("Surface"));
        Assert.That(GetValue<bool>(row, "Active"), Is.True);
    }

    #endregion

    #region both key columns downstream

    [Test]
    [Description("After an inner join both key columns hold the same value: DeviceId == DeviceId1 for the 4 rows, " +
                 "DeviceId != DeviceId1 for none")]
    public void Where_AfterInnerJoin_BothKeyColumnsAreEqual()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string joined = "Events | join kind=inner Devices on DeviceId";

        Assert.That(ExecuteAndGetCount(kustoDatabase, joined + " | where DeviceId == DeviceId1 | count"),
            Is.EqualTo(4L));
        Assert.That(ExecuteAndGetCount(kustoDatabase, joined + " | where DeviceId != DeviceId1 | count"),
            Is.EqualTo(0L));
    }

    [Test]
    [Description("After leftouter the right key is empty for the unmatched event 4: where DeviceId == DeviceId1 keeps " +
                 "the 4 matched rows, and summarize by DeviceId1 puts the unmatched event in the empty group")]
    public void WhereAndSummarize_AfterLeftOuterJoin_OnTheRightKey()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string joined = "Events | join kind=leftouter Devices on DeviceId";

        Assert.That(ExecuteAndGetCount(kustoDatabase, joined + " | where DeviceId == DeviceId1 | count"),
            Is.EqualTo(4L));
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, joined + " | summarize EventCount = count() by DeviceId1",
                "DeviceId1", "EventCount"),
            Is.EquivalentTo(new[] { "d1:2", "d2:2", ":1" }));
    }

    [Test]
    [Description("project can keep either key and rename the right one: DeviceKey = DeviceId1")]
    public void Project_AfterJoin_RenamesTheRightKey()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner Devices on DeviceId " +
                             "| project EventId, DeviceId, DeviceKey = DeviceId1, Model";

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(new[] { "EventId:long", "DeviceId:string", "DeviceKey:string", "Model:string" }));
        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "DeviceKey"),
            Is.EquivalentTo(new[] { "1:d1", "2:d1", "3:d2", "5:d2" }));
    }

    [Test]
    [Description("A left side of the key column only: the result is DeviceId, then the Events columns with the right " +
                 "key renamed to DeviceId1")]
    public void Join_LeftSideWithTheKeyColumnOnly()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Devices | project DeviceId | join kind=inner Events on DeviceId";

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(new[]
            {
                "DeviceId:string", "EventId:long", "DeviceId1:string", "EventType:string", "Timestamp:datetime"
            }));
        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "DeviceId", "EventId", "DeviceId1"),
            Is.EquivalentTo(new[] { "d1:1:d1", "d1:2:d1", "d2:3:d2", "d2:5:d2" }));
    }

    #endregion

    #region renamed columns

    [TestCase("Events | join kind=inner Devices on DeviceId | join kind=inner Inventory on DeviceId")]
    [TestCase("Events | join kind=inner Devices on DeviceId " +
              "| join kind=inner Inventory on $left.DeviceId1 == $right.DeviceId")]
    [Description("A second join renames its right key to DeviceId2, because DeviceId1 is taken by the first join")]
    public void Join_ChainOfThreeTables_SuffixesTheThirdKeyWith2(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(EventsJoinDevicesSchema.Concat(new[] { "DeviceId2:string", "Location:string" })));
        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "DeviceId1", "DeviceId2", "Location"),
            Is.EquivalentTo(new[]
            {
                "1:d1:d1:Seattle", "1:d1:d1:Redmond", "2:d1:d1:Seattle", "2:d1:d1:Redmond", "3:d2:d2:Dublin",
                "5:d2:d2:Dublin"
            }));
    }

    [Test]
    [Description("A nested join on the right side: its right side already has DeviceId1 (from the inner join), so its " +
                 "DeviceId becomes DeviceId2 and DeviceId1 keeps its name")]
    public void Join_WithANestedJoinOnTheRight_SuffixesTheKeyWithTheFirstFreeNumber()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner (Devices | join kind=inner Inventory on DeviceId) on DeviceId";

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(new[]
            {
                "EventId:long", "DeviceId:string", "EventType:string", "Timestamp:datetime", "DeviceId2:string",
                "Model:string", "Active:bool", "DeviceId1:string", "Location:string"
            }));
        Assert.That(ExecuteAndGetCount(kustoDatabase, query + " | count"), Is.EqualTo(6L));
    }

    [Test]
    [Description("A non-key column present on both sides is renamed too: the Timestamp of Heartbeats is Timestamp1")]
    public void Join_RenamesANonKeyColumnOfTheRightSide()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner Heartbeats on DeviceId";

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(new[]
            {
                "EventId:long", "DeviceId:string", "EventType:string", "Timestamp:datetime", "DeviceId1:string",
                "Timestamp1:datetime"
            }));
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, query + " | where EventId == 1", "Timestamp", "Timestamp1"),
            Is.EquivalentTo(new[] { "2024-01-01 08:00:2024-01-01 08:00", "2024-01-01 08:00:2024-01-01 08:05" }));
    }

    [Test]
    [Description("Joined on Model, the right Model becomes Model1")]
    public void Join_OnANonFirstColumn_RenamesTheRightKey()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, "Devices | join kind=inner ModelCatalog on Model"),
            Is.EqualTo(new[]
            {
                "DeviceId:string", "Model:string", "Active:bool", "Model1:string", "Category:string"
            }));
    }

    [Test]
    [Description("Two tables with the same columns: every right column is renamed, with its type kept " +
                 "(DeviceId1, Model1, Active1)")]
    public void Join_TablesWithTheSameColumns_RenameEveryRightColumn()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Devices | join kind=fullouter ArchivedDevices on DeviceId";

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(new[]
            {
                "DeviceId:string", "Model:string", "Active:bool", "DeviceId1:string", "Model1:string", "Active1:bool"
            }));
        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, query),
            Is.EquivalentTo(new[]
            {
                "d1:Surface:True:::", "d2:Xbox:False:::", "d4:HoloLens:True:::", ":::d3:Zune:False"
            }));
    }

    [Test]
    [Description("A self join renames every right column: EventId1, DeviceId1, EventType1, Timestamp1; " +
                 "EventId < EventId1 pairs the events of a device in order")]
    public void Join_SelfJoin_RenamesEveryRightColumn()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner Events on DeviceId";

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(new[]
            {
                "EventId:long", "DeviceId:string", "EventType:string", "Timestamp:datetime", "EventId1:long",
                "DeviceId1:string", "EventType1:string", "Timestamp1:datetime"
            }));
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, query + " | where EventId < EventId1", "EventId", "EventId1"),
            Is.EquivalentTo(new[] { "1:2", "3:5" }));
    }

    [TestCase("X | join kind=inner Y on Key", new[] { "Key=k", "Key1=k1", "V=v", "Key2=k", "W=w" })]
    [TestCase("Y | join kind=inner X on Key", new[] { "Key=k", "W=w", "Key2=k", "Key1=k1", "V=v" })]
    [TestCase("S | join kind=inner S on Key", new[] { "Key=k", "Value1=s1", "Key1=k", "Value11=s1" })]
    [Description("A right column takes the first free suffix: Key2 when Key1 is a left column (X | join Y) or a right " +
                 "column (Y | join X), and Value1 of the right side becomes Value11")]
    public void Join_ColumnsCollidingWithSuffixedNames_TakeTheFirstFreeSuffix(string query, string[] expectedCells)
    {
        var kustoDatabase = CreateCollisionDatabase();

        Assert.That(ExecuteAndGetNamedCells(kustoDatabase, query), Is.EqualTo(expectedCells));
    }

    #endregion

    #region keys with different names

    [Test]
    [Description("Keys with different names keep both names: DeviceId from Events and Id from Assets, nothing renamed")]
    public void Join_OnKeysWithDifferentNames_KeepsBothNames()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetSchemaTexts(kustoDatabase, "Events | join kind=inner Assets on $left.DeviceId == $right.Id"),
            Is.EqualTo(new[]
            {
                "EventId:long", "DeviceId:string", "EventType:string", "Timestamp:datetime", "Id:string",
                "Owner:string"
            }));
    }

    [Test]
    [Description("The right key Id collides with the left column Id (a long ticket id): it becomes Id1 and stays a " +
                 "string")]
    public void Join_OnKeysWithDifferentNames_RenamesARightKeyThatCollidesWithALeftColumn()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Tickets | join kind=inner Assets on $left.DeviceId == $right.Id";

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(new[] { "Id:long", "DeviceId:string", "Id1:string", "Owner:string" }));
        Assert.That(ExecuteAndGetWholeRowTexts(kustoDatabase, query),
            Is.EquivalentTo(new[] { "100:d1:d1:alice", "101:d2:d2:bob" }));
    }

    [Test]
    [Description("A key added to the right side with extend comes last on the right side and is renamed DeviceId1")]
    public void Join_WithAKeyAddedByExtendOnTheRight()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=inner (Assets | extend DeviceId = Id) on DeviceId";

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(new[]
            {
                "EventId:long", "DeviceId:string", "EventType:string", "Timestamp:datetime", "Id:string",
                "Owner:string", "DeviceId1:string"
            }));
        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Id", "Owner", "DeviceId1"),
            Is.EquivalentTo(new[] { "1:d1:alice:d1", "2:d1:alice:d1", "3:d2:bob:d2", "5:d2:bob:d2" }));
    }

    [Test]
    [Description("A key renamed by project on the right side keeps the position of the projection")]
    public void Join_WithAKeyRenamedByProjectOnTheRight()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Tickets | join kind=inner (Assets | project DeviceId = Id, Owner) on DeviceId";

        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(new[] { "Id:long", "DeviceId:string", "DeviceId1:string", "Owner:string" }));
        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Id", "DeviceId1", "Owner"),
            Is.EquivalentTo(new[] { "100:d1:alice", "101:d2:bob" }));
    }

    #endregion
}
