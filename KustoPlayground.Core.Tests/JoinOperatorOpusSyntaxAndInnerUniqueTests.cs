using static KustoPlayground.Core.Tests.JoinOperatorOpusTestUtils;
using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// The join syntax and its default kind, innerunique, as in Azure Data Explorer.
/// - 'T | join U on Key' without a kind is innerunique, and kind=innerunique is the same: it keeps one left row per
///   distinct key (which of the duplicate left rows survives is not guaranteed) and pairs it with every matching
///   right row;
/// - kind=inner returns every matching pair: two requests and three traces of one OperationId give six rows, where
///   innerunique gives three;
/// - the surviving left row is a whole input row, never a mix of the columns of several rows;
/// - innerunique removes the duplicate keys of the left side only, so swapping the sides changes the result, and when
///   the left keys are unique it returns the same rows as inner;
/// - the hints (hint.strategy=broadcast, hint.strategy=shuffle, hint.shufflekey) do not change the result, before or
///   after the kind, and neither do line breaks or parentheses around the inputs;
/// - an unknown kind (outer, semi, a kind in the wrong case like Inner), a missing 'on' clause, a condition that is
///   not an equality of a left column and a right column ('&lt;', '=~', 'or', a function call, two columns of one
///   side) and a key column missing on one side (column names are case-sensitive) are errors.
/// The rows of a join come in any order, so the results are compared in any order unless the query sorts them.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class JoinOperatorOpusSyntaxAndInnerUniqueTests
{
    private static readonly string[] RequestColumns = { "OperationId", "Name", "DurationMs" };
    private static readonly string[] TraceColumns = { "OperationId1", "Message", "Level" };

    #region default kind is innerunique

    [TestCase("Requests | join Traces on OperationId")]
    [TestCase("Requests | join kind=innerunique Traces on OperationId")]
    [Description("A join without a kind is innerunique: one of the two op1 requests (either) is paired with the three " +
                 "op1 traces, the op2 request with its trace; op3 (no trace) and op4 (no request) are dropped")]
    public void Join_WithoutKindOrWithInnerUnique_KeepsOneLeftRowPerKeyPairedWithEveryRightRow(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndGetRows(kustoDatabase, query);

        Assert.That(rows, Has.Count.EqualTo(4), "op1: 1 request x 3 traces, op2: 1 request x 1 trace");
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "OperationId", "op1"),
            RequestColumns, new[] { "op1:GET /home:120", "op1:GET /home (retry):80" },
            TraceColumns, new[] { "op1:start:Info", "op1:query db:Verbose", "op1:end:Info" });
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "OperationId", "op2"),
            RequestColumns, new[] { "op2:POST /cart:300" },
            TraceColumns, new[] { "op2:checkout:Warning" });
    }

    [TestCase("", 3L, 4L)]
    [TestCase("kind=innerunique", 3L, 4L)]
    [TestCase("kind=inner", 6L, 7L)]
    [Description("Two requests and three traces share op1: inner returns the 6 pairs, innerunique (the default) " +
                 "returns 3; over all the operations inner returns 7 rows and innerunique 4")]
    public void Join_DuplicateKeysOnBothSides_InnerReturnsEveryPairAndInnerUniqueOneLeftRow(string kindClause,
        long op1Rows, long allRows)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase,
                $"Requests | where OperationId == 'op1' | join {kindClause} Traces on OperationId | count"),
            Is.EqualTo(op1Rows), "rows of op1");
        Assert.That(ExecuteAndGetCount(kustoDatabase, $"Requests | join {kindClause} Traces on OperationId | count"),
            Is.EqualTo(allRows), "rows of every operation");
    }

    [Test]
    [Description("kind=inner pairs every request with every trace of its OperationId: 2 x 3 rows for op1 and 1 for op2")]
    public void Join_Inner_ReturnsEveryMatchingPair()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, "Requests | join kind=inner Traces on OperationId", "Name", "Message"),
            Is.EquivalentTo(new[]
            {
                "GET /home:start", "GET /home:query db", "GET /home:end",
                "GET /home (retry):start", "GET /home (retry):query db", "GET /home (retry):end",
                "POST /cart:checkout"
            }));
    }

    [Test]
    [Description("The surviving left row of innerunique is one whole input row: the EventId, EventType and Timestamp " +
                 "of the d1 row all come from event 1 or all from event 2")]
    public void Join_InnerUnique_KeepsTheWholeSurvivingLeftRow()
    {
        var kustoDatabase = CreateScenarioDatabase();
        string[] eventColumns = { "EventId", "EventType", "Timestamp" };
        string[] deviceColumns = { "DeviceId1", "Model", "Active" };

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "Events | join Devices on DeviceId");

        Assert.That(rows, Has.Count.EqualTo(2), "d3 (event 4) has no device and d4 has no event");
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "DeviceId", "d1"),
            eventColumns, new[] { "1:boot:2024-01-01 08:00", "2:crash:2024-01-01 09:00" },
            deviceColumns, new[] { "d1:Surface:True" });
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "DeviceId", "d2"),
            eventColumns, new[] { "3:boot:2024-01-01 10:00", "5:shutdown:2024-01-02 09:00" },
            deviceColumns, new[] { "d2:Xbox:False" });
    }

    [Test]
    [Description("innerunique deduplicates the left side only: Traces | join Requests keeps one of the three op1 " +
                 "traces (any) and pairs it with both op1 requests")]
    public void Join_InnerUnique_SwappingTheSidesDeduplicatesTheOtherTable()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "Traces | join Requests on OperationId");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "OperationId", "op1"),
            new[] { "Message" }, new[] { "start", "query db", "end" },
            new[] { "Name" }, new[] { "GET /home", "GET /home (retry)" });
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "OperationId", "op2"),
            new[] { "Message" }, new[] { "checkout" },
            new[] { "Name" }, new[] { "POST /cart" });
    }

    [Test]
    [Description("After the default join every OperationId has one distinct request name and duration, and as many " +
                 "rows as traces (op1: 1, 1, 3 and op2: 1, 1, 1)")]
    public void Join_InnerUnique_SummarizeShowsOneLeftRowPerKey()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Requests | join Traces on OperationId " +
                             "| summarize Names = dcount(Name), Durations = dcount(DurationMs), Rows = count() " +
                             "by OperationId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "OperationId", "Names", "Durations", "Rows"),
            Is.EquivalentTo(new[] { "op1:1:1:3", "op2:1:1:1" }));
    }

    [TestCase("")]
    [TestCase("kind=innerunique")]
    [TestCase("kind=inner")]
    [Description("The device ids of Devices are unique, so with Devices on the left innerunique returns every pair, " +
                 "like inner")]
    public void Join_LeftSideWithUniqueKeys_InnerUniqueReturnsTheSameRowsAsInner(string kindClause)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, $"Devices | join {kindClause} Events on DeviceId",
                "DeviceId", "Model", "EventId"),
            Is.EquivalentTo(new[] { "d1:Surface:1", "d1:Surface:2", "d2:Xbox:3", "d2:Xbox:5" }));
    }

    [TestCase("", new[] { "d1:d1:Surface", "d2:d2:Xbox" })]
    [TestCase("kind=inner", new[] { "d1:d1:Surface", "d1:d1:Surface", "d2:d2:Xbox", "d2:d2:Xbox" })]
    [Description("A left side of identical rows (Events | project DeviceId has d1 and d2 twice): innerunique keeps " +
                 "one row per key, inner keeps the duplicates")]
    public void Join_LeftSideWithIdenticalRows(string kindClause, string[] expectedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, $"Events | project DeviceId | join {kindClause} Devices on DeviceId",
                "DeviceId", "DeviceId1", "Model"),
            Is.EquivalentTo(expectedRows));
    }

    [TestCase("", 2L)]
    [TestCase("kind=innerunique", 2L)]
    [TestCase("kind=inner", 4L)]
    [TestCase("kind=leftouter", 4L)]
    [TestCase("kind=leftsemi", 2L)]
    [Description("Dup has the row (k, x) twice and DupR two rows of the key k: innerunique keeps one of the identical " +
                 "left rows (2 rows), inner and leftouter keep both (4 rows), leftsemi returns both left rows once")]
    public void Join_FullyDuplicatedLeftRows_RowCountPerKind(string kindClause, long expectedRows)
    {
        var kustoDatabase = CreateDuplicateRowsDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, $"Dup | join {kindClause} DupR on K | count"),
            Is.EqualTo(expectedRows));
    }

    [Test]
    [Description("innerunique of the duplicated rows pairs one (k, x) row with both right rows")]
    public void Join_InnerUnique_FullyDuplicatedLeftRows_PairsOneOfThemWithEachRightRow()
    {
        var kustoDatabase = CreateDuplicateRowsDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, "Dup | join DupR on K", "K", "V", "K1", "W"),
            Is.EquivalentTo(new[] { "k:x:k:1", "k:x:k:2" }));
    }

    [Test]
    [Description("With two key columns innerunique keeps one left row per key combination: one of the notes a and b " +
                 "of (t1, d1) with X, and the note c of (t2, d1) with Y and Z; inner returns 4 rows")]
    public void Join_InnerUnique_OnTwoKeys_KeepsOneLeftRowPerKeyCombination()
    {
        var kustoDatabase = CreateDuplicateRowsDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "TL | join TR on TenantId, DeviceId");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "TenantId", "t1"),
            new[] { "Note" }, new[] { "a", "b" },
            new[] { "TenantId1", "Owner" }, new[] { "t1:X" });
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "TenantId", "t2"),
            new[] { "Note" }, new[] { "c" },
            new[] { "TenantId1", "Owner" }, new[] { "t2:Y", "t2:Z" });
        Assert.That(ExecuteAndGetCount(kustoDatabase, "TL | join kind=inner TR on TenantId, DeviceId | count"),
            Is.EqualTo(4L), "inner");
    }

    [Test]
    [Description("Joined on DeviceId only, innerunique keeps one of the three d1 events of any tenant and pairs it with " +
                 "both d1 devices (of t1 and t2); the d2 event of t2 is paired with the d2 device of t1")]
    public void Join_InnerUnique_OnOneOfTwoColumns_DeduplicatesByThatColumnOnly()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "TenantEvents | join TenantDevices on DeviceId");

        Assert.That(rows, Has.Count.EqualTo(3));
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "DeviceId", "d1"),
            new[] { "TenantId", "EventType" }, new[] { "t1:boot", "t2:crash", "t3:boot" },
            new[] { "TenantId1", "Owner" }, new[] { "t1:alice", "t2:bob" });
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "DeviceId", "d2"),
            new[] { "TenantId", "EventType" }, new[] { "t2:boot" },
            new[] { "TenantId1", "Owner" }, new[] { "t1:carol" });
    }

    [Test]
    [Description("BigLeft has 10 rows per key 0..9 and BigRight 10 rows per key 0..4: innerunique keeps one left row " +
                 "of each matching key with its 10 right rows (5 distinct left ids, 50 distinct right ids, 50 rows)")]
    public void Join_InnerUnique_OnGeneratedTables_KeepsOneOfTenLeftRowsPerKey()
    {
        var kustoDatabase = CreateGeneratedDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase,
                "BigLeft | join BigRight on Key " +
                "| summarize LeftRows = dcount(LeftId), RightRows = dcount(RightId), Rows = count() by Key",
                "Key", "LeftRows", "RightRows", "Rows"),
            Is.EquivalentTo(new[] { "0:1:10:10", "1:1:10:10", "2:1:10:10", "3:1:10:10", "4:1:10:10" }));
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase,
                "BigLeft | join BigRight on Key | summarize LeftIds = dcount(LeftId), RightIds = dcount(RightId)",
                "LeftIds", "RightIds"),
            Is.EqualTo(new[] { "5:50" }));
    }

    [TestCase("", 5L)]
    [TestCase("kind=inner", 9L)]
    [Description("Self join of Events on DeviceId: innerunique keeps one event per device (2 + 2 + 1 rows), " +
                 "inner pairs every event with every event of its device, itself included (4 + 4 + 1 rows)")]
    public void Join_SelfJoin_RowCountPerKind(string kindClause, long expectedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, $"Events | join {kindClause} Events on DeviceId | count"),
            Is.EqualTo(expectedRows));
    }

    #endregion

    #region hints and layout

    [TestCase("hint.strategy=broadcast", 2L)]
    [TestCase("hint.strategy=shuffle", 2L)]
    [TestCase("hint.shufflekey=DeviceId", 2L)]
    [TestCase("hint.strategy=broadcast kind=inner", 4L)]
    [TestCase("kind=inner hint.strategy=shuffle", 4L)]
    [TestCase("kind=leftouter hint.strategy=shuffle", 5L)]
    [TestCase("kind=leftouter hint.strategy=broadcast", 5L)]
    [TestCase("hint.strategy=broadcast kind=leftouter", 5L)]
    [Description("The hints only tune the execution: a hinted join without a kind is still innerunique (2 rows), " +
                 "and the kind may come before or after the hint (inner 4 rows, leftouter 5 rows)")]
    public void Join_WithHints_ReturnsTheRowsOfTheKind(string parameters, long expectedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, $"Events | join {parameters} Devices on DeviceId | count"),
            Is.EqualTo(expectedRows));
    }

    [TestCase("Events\n| join kind=inner\n    Devices\n    on DeviceId")]
    [TestCase("Events | join kind=inner (Devices) on DeviceId")]
    [TestCase("Events|join kind=inner Devices on DeviceId")]
    [TestCase("(Events) | join kind=inner (Devices) on DeviceId")]
    [TestCase("Events | join kind=inner (\n    Devices\n) on DeviceId")]
    [Description("Line breaks, missing spaces around the pipe and parentheses around a table name do not change the join")]
    public void Join_LayoutVariants_ReturnTheSameRows(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Model"),
            Is.EquivalentTo(new[] { "1:Surface", "2:Surface", "3:Xbox", "5:Xbox" }));
    }

    #endregion

    #region errors

    [TestCase("outer")]
    [TestCase("semi")]
    [TestCase("Inner")]
    [TestCase("LEFTOUTER")]
    [Description("An unknown kind is an error (SEM0001), and the kind names are case-sensitive")]
    public void Join_UnknownKind_Fails(string kind)
    {
        var kustoDatabase = CreateScenarioDatabase();

        AssertQueryFails(kustoDatabase, $"Events | join kind={kind} Devices on DeviceId");
    }

    [Test]
    [Description("The 'on' clause is required (SEM0001)")]
    public void Join_WithoutOnClause_Fails()
    {
        var kustoDatabase = CreateScenarioDatabase();

        AssertQueryFails(kustoDatabase, "Events | join kind=inner Devices");
    }

    [TestCase("Events | join kind=inner Heartbeats on $left.Timestamp < $right.Timestamp")]
    [TestCase("Events | join kind=inner Heartbeats on $left.DeviceId == $right.DeviceId and " +
              "$left.Timestamp < $right.Timestamp")]
    [TestCase("Users | join kind=inner Logins on $left.UserName =~ $right.UserName")]
    [TestCase("Events | join kind=inner Devices on $left.DeviceId == $right.DeviceId or $left.EventType == $right.Model")]
    [TestCase("Users | join kind=inner Logins on $left.UserName == tolower($right.UserName)")]
    [TestCase("Users | join kind=inner Logins on tolower(UserName)")]
    [Description("A join condition is one or more equalities of a left column and a right column: '<', '=~', 'or' " +
                 "and an expression instead of a column are errors (SEM0001, SEM0712)")]
    public void Join_ConditionOtherThanAnEqualityOfColumns_Fails(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        AssertQueryFails(kustoDatabase, query);
    }

    [Test]
    [Description("Both sides of an equality must be qualified by different sides, $left and $right (SEM0001)")]
    public void Join_EqualityOfTwoLeftColumns_Fails()
    {
        var kustoDatabase = CreateScenarioDatabase();

        AssertQueryFails(kustoDatabase, "Events | join kind=inner Devices on $left.DeviceId == $left.DeviceId");
    }

    [TestCase("Events | join kind=inner Devices on EventType")]
    [TestCase("Events | join kind=inner Devices on Model")]
    [TestCase("Events | join kind=inner Devices on deviceid")]
    [TestCase("Events | join kind=inner Devices on $left.Model == $right.Model")]
    [TestCase("Events | join kind=inner Devices on $left.DeviceId == $right.EventId")]
    [TestCase("Events | join kind=inner Devices on DeviceId, Model")]
    [Description("A key column must exist on both sides with this exact case: EventType is missing in Devices, Model in " +
                 "Events, deviceid in both (SEM0100)")]
    public void Join_KeyColumnMissingOnOneSide_Fails(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        AssertQueryFails(kustoDatabase, query);
    }

    #endregion
}
