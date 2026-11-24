using static KustoPlayground.Core.Tests.JoinOperatorOpusTestUtils;
using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// The join keys and their comparison rules, as in Azure Data Explorer.
/// - 'on DeviceId' is the shorthand of 'on $left.DeviceId == $right.DeviceId', and '$left.DeviceId == $right.Id'
///   joins columns with different names without renaming them first; the equality may also be written
///   '$right.Id == $left.DeviceId';
/// - several keys are separated by commas or joined by 'and', in any order and mixing both forms, and a row matches only
///   when every key matches: the same device id in two tenants stays two devices;
/// - string keys are compared exactly: case-sensitive and without trimming (alice is not Alice, carol is not "carol ");
///   to match regardless of case, join on tolower of both sides;
/// - the keys of both sides must have the same type, except int and long which match each other (each key column keeping
///   its own type); string versus long, real versus long, decimal versus real and the other mixes are errors, so the
///   query must cast one side (tolong(Key) or tostring(Key));
/// - a dynamic key is an error, even against another dynamic key: cast it first (tostring(Payload.device),
///   tolong(Payload.event)) instead of relying on an implicit conversion;
/// - keys of every scalar type match equal values (string, long, int, real, decimal, bool, datetime, timespan, guid),
///   and a computed key such as bin(Timestamp, 1d) or toint(EventId) joins like a column;
/// - the empty string of a padded outer join row is an ordinary empty string key: it matches the empty string keys of
///   the next join (the null keys are covered by NullableOpusJoinTests).
/// The rows of a join come in any order, so the results are compared in any order unless the query sorts them.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class JoinOperatorOpusKeysAndTypesTests
{
    private static readonly Guid TypedGuid = new("33333333-3333-3333-3333-333333333333");

    private static readonly string[] TenantJoinColumns =
        { "TenantId", "DeviceId", "EventType", "TenantId1", "DeviceId1", "Owner" };

    // A device id read from the JSON payload of Telemetry, as a dynamic value.
    private const string TelemetryWithDynamicDevice = "Telemetry | extend DeviceId = parse_json(Payload).device";

    /// <summary>
    /// Typed(S: string, L: long, R: real, D: datetime, G: guid, B: bool, I: int, M: decimal, T: timespan) with one row:
    /// "1", 1, 1.0, 2024-01-01, 3333..., true, 1, 1.0, 01:00:00.
    /// </summary>
    private static Table BuildTypedTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Typed")
            .WithColumn<string>("S")
            .WithColumn<long>("L")
            .WithColumn<double>("R")
            .WithColumn<DateTime>("D")
            .WithColumn<Guid>("G")
            .WithColumn<bool>("B")
            .WithColumn<int>("I")
            .WithColumn<decimal>("M")
            .WithColumn<TimeSpan>("T")
            .WithRow("1", 1L, 1.0, Utc(2024, 1, 1), TypedGuid, true, 1, 1m, TimeSpan.FromHours(1))
            .Build();
    }

    /// <summary>
    /// KeyLeft(Key: string, Side: string) = ("1", left) and KeyRight(Key: long, Side: string) = (1, right):
    /// the same key name and the same text, but different types.
    /// </summary>
    private static Table[] BuildKeyTables()
    {
        return new[]
        {
            new SummarizeOperatorOpusTableBuilder("KeyLeft")
                .WithColumn<string>("Key")
                .WithColumn<string>("Side")
                .WithRow("1", "left")
                .Build(),
            new SummarizeOperatorOpusTableBuilder("KeyRight")
                .WithColumn<long>("Key")
                .WithColumn<string>("Side")
                .WithRow(1L, "right")
                .Build()
        };
    }

    /// <summary>
    /// Categories(Model: string, Category: string) = (Surface, laptop), ("", unknown): a category for the empty model.
    /// </summary>
    private static Table BuildCategoriesTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Categories")
            .WithColumn<string>("Model")
            .WithColumn<string>("Category")
            .WithRow("Surface", "laptop")
            .WithRow("", "unknown")
            .Build();
    }

    private static KustoDatabase CreateTypedDatabase()
    {
        return CreateScenarioDatabase(BuildKeyTables().Append(BuildTypedTable()).ToArray());
    }

    #region same-name and explicit keys

    [TestCase("", 2)]
    [TestCase("kind=innerunique", 2)]
    [TestCase("kind=inner", 4)]
    [TestCase("kind=leftouter", 5)]
    [TestCase("kind=rightouter", 5)]
    [TestCase("kind=fullouter", 6)]
    [TestCase("kind=leftsemi", 4)]
    [TestCase("kind=leftanti", 1)]
    [TestCase("kind=rightsemi", 2)]
    [TestCase("kind=rightanti", 1)]
    [Description("'on DeviceId' is the shorthand of 'on $left.DeviceId == $right.DeviceId': both give the same rows " +
                 "for every kind")]
    public void Join_SameNameShorthandAndExplicitEquality_ReturnTheSameRows(string kindClause, int expectedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();

        List<string> shorthandRows = ExecuteAndGetWholeRowTexts(kustoDatabase,
            $"Events | join {kindClause} Devices on DeviceId");
        List<string> explicitRows = ExecuteAndGetWholeRowTexts(kustoDatabase,
            $"Events | join {kindClause} Devices on $left.DeviceId == $right.DeviceId");

        Assert.That(shorthandRows, Has.Count.EqualTo(expectedRows), "on DeviceId");
        if (kindClause is "" or "kind=innerunique")
        {
            Assert.That(explicitRows, Has.Count.EqualTo(expectedRows), "explicit equality");
        }
        else
        {
            Assert.That(explicitRows, Is.EquivalentTo(shorthandRows), "explicit equality");
        }
    }

    [TestCase("inner", new string[0])]
    [TestCase("leftouter", new[] { "4::" })]
    [TestCase("rightouter", new[] { ":d7:carol" })]
    [TestCase("fullouter", new[] { "4::", ":d7:carol" })]
    [Description("Keys with different names: Events | join Assets on $left.DeviceId == $right.Id pairs the events of " +
                 "d1 and d2 with their assets; the event 4 (d3) has no asset and the asset d7 has no event")]
    public void Join_OnKeysWithDifferentNames_PerKind(string kind, string[] unmatchedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();
        string[] matchedRows = { "1:d1:alice", "2:d1:alice", "3:d2:bob", "5:d2:bob" };

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, $"Events | join kind={kind} Assets on $left.DeviceId == $right.Id",
                "EventId", "Id", "Owner"),
            Is.EquivalentTo(matchedRows.Concat(unmatchedRows)));
    }

    [Test]
    [Description("The default kind with keys of different names keeps one event per device: event 1 or 2 with the " +
                 "asset d1, event 3 or 5 with the asset d2")]
    public void Join_InnerUnique_OnKeysWithDifferentNames()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "Events | join Assets on $left.DeviceId == $right.Id");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "DeviceId", "d1"),
            new[] { "EventId" }, new[] { "1", "2" }, new[] { "Id", "Owner" }, new[] { "d1:alice" });
        AssertOneLeftRowPairedWithEveryRightRow(GetRowsWhere(rows, "DeviceId", "d2"),
            new[] { "EventId" }, new[] { "3", "5" }, new[] { "Id", "Owner" }, new[] { "d2:bob" });
    }

    [TestCase("rightouter", new[] { "100:alice", "101:bob", ":carol" })]
    [TestCase("fullouter", new[] { "100:alice", "101:bob", ":carol", "102:" })]
    [Description("Tickets | join Assets on $left.DeviceId == $right.Id: the asset d7 has no ticket (null ticket Id), " +
                 "and with fullouter the ticket 102 (d9) has no asset (empty Owner)")]
    public void Join_OuterKinds_OnKeysWithDifferentNames_KeepTheUnmatchedRows(string kind, string[] expectedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, $"Tickets | join kind={kind} Assets on $left.DeviceId == $right.Id",
                "Id", "Owner"),
            Is.EquivalentTo(expectedRows));
    }

    [TestCase("inner", new[] { "1:d1:alice", "2:d1:alice", "3:d2:bob", "5:d2:bob" })]
    [TestCase("leftouter", new[] { "1:d1:alice", "2:d1:alice", "3:d2:bob", "4::", "5:d2:bob" })]
    [Description("The equality may name the right column first: $right.Id == $left.DeviceId is the same condition")]
    public void Join_EqualityWithTheRightColumnFirst_IsTheSameCondition(string kind, string[] expectedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, $"Events | join kind={kind} Assets on $right.Id == $left.DeviceId",
                "EventId", "Id", "Owner"),
            Is.EquivalentTo(expectedRows));
    }

    #endregion

    #region multiple keys

    [TestCase("TenantId, DeviceId")]
    [TestCase("DeviceId, TenantId")]
    [TestCase("TenantId, $left.DeviceId == $right.DeviceId")]
    [TestCase("$left.TenantId == $right.TenantId, $left.DeviceId == $right.DeviceId")]
    [TestCase("$left.TenantId == $right.TenantId and $left.DeviceId == $right.DeviceId")]
    [Description("Two keys, as names, explicit equalities separated by a comma or joined by 'and', in any order: the " +
                 "device d1 of t1 and the device d1 of t2 are different devices, d2 of t2 and d1 of t3 have no match")]
    public void Join_OnTwoKeys_BothKeysMustMatch(string keys)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, $"TenantEvents | join kind=inner TenantDevices on {keys}",
                TenantJoinColumns),
            Is.EquivalentTo(new[] { "t1:d1:boot:t1:d1:alice", "t2:d1:crash:t2:d1:bob" }));
    }

    [TestCase("TenantId, $left.DeviceId == $right.Id")]
    [TestCase("$left.TenantId == $right.TenantId, $left.DeviceId == $right.Id")]
    [TestCase("$left.TenantId == $right.TenantId and $left.DeviceId == $right.Id")]
    [Description("Two keys, one of them with different names (DeviceId and Id): the result keeps the left key " +
                 "DeviceId and the right key Id, and renames the right TenantId to TenantId1")]
    public void Join_OnTwoKeysWithDifferentNames(string keys)
    {
        var kustoDatabase = CreateScenarioDatabase();
        string query = $"TenantEvents | join kind=inner Registry on {keys}";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "TenantId", "DeviceId", "Id", "Owner"),
            Is.EquivalentTo(new[] { "t1:d1:d1:alice", "t2:d1:d1:bob" }));
        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query),
            Is.EqualTo(new[]
            {
                "TenantId:string", "DeviceId:string", "EventType:string", "TenantId1:string", "Id:string",
                "Owner:string"
            }));
    }

    [Test]
    [Description("Joined on DeviceId only, the device d1 of every tenant matches the d1 devices of both tenants: " +
                 "7 rows, 5 of them pairing different tenants, which the join on both keys avoids")]
    public void Join_OnOneOfTwoKeys_MixesTheTenants()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string joined = "TenantEvents | join kind=inner TenantDevices on DeviceId";

        Assert.That(ExecuteAndGetCount(kustoDatabase, joined + " | count"), Is.EqualTo(7L));
        Assert.That(ExecuteAndGetCount(kustoDatabase, joined + " | where TenantId != TenantId1 | count"),
            Is.EqualTo(5L));
    }

    [Test]
    [Description("leftouter on two keys keeps the events whose tenant and device do not match together (t2 d2, t3 d1) " +
                 "with an empty owner")]
    public void Join_LeftOuter_OnTwoKeys()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, "TenantEvents | join kind=leftouter TenantDevices on TenantId, DeviceId",
                "TenantId", "DeviceId", "EventType", "Owner"),
            Is.EquivalentTo(new[] { "t1:d1:boot:alice", "t2:d1:crash:bob", "t2:d2:boot:", "t3:d1:boot:" }));
    }

    [Test]
    [Description("fullouter on two keys also keeps the device d2 of t1, which has no event in t1")]
    public void Join_FullOuter_OnTwoKeys()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, "TenantEvents | join kind=fullouter TenantDevices on TenantId, DeviceId",
                TenantJoinColumns),
            Is.EquivalentTo(new[]
            {
                "t1:d1:boot:t1:d1:alice", "t2:d1:crash:t2:d1:bob", "t2:d2:boot:::", "t3:d1:boot:::",
                ":::t1:d2:carol"
            }));
    }

    [Test]
    [Description("A string key and a datetime key: only the event 1 has a heartbeat of its device at its exact time")]
    public void Join_OnAStringAndADatetimeKey()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, "Events | join kind=inner Heartbeats on DeviceId, Timestamp",
                "EventId", "DeviceId1", "Timestamp1"),
            Is.EqualTo(new[] { "1:d1:2024-01-01 08:00" }));
    }

    #endregion

    #region key types

    [TestCase("S")]
    [TestCase("L")]
    [TestCase("R")]
    [TestCase("D")]
    [TestCase("G")]
    [TestCase("B")]
    [TestCase("I")]
    [TestCase("M")]
    [TestCase("T")]
    [Description("A key of each scalar type (string, long, real, datetime, guid, bool, int, decimal, timespan) matches " +
                 "the equal value of the same type")]
    public void Join_OnAKeyOfEachScalarType_MatchesTheEqualValue(string column)
    {
        var kustoDatabase = CreateTypedDatabase();
        string query = $"Typed | project K = {column} | join kind=inner (Typed | project K = {column}) on K | count";

        Assert.That(ExecuteAndGetCount(kustoDatabase, query), Is.EqualTo(1L));
    }

    [Test]
    [Description("int and long keys match each other, and each key column keeps its own type: K is the int of the " +
                 "left side and K1 the long of the right side")]
    public void Join_IntKeyAndLongKey_MatchAndKeepTheirTypes()
    {
        var kustoDatabase = CreateTypedDatabase();
        const string query = "Typed | project K = I | join kind=inner (Typed | project K = L) on K";

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase, query);

        Assert.That(GetValue<int>(row, "K"), Is.EqualTo(1));
        Assert.That(GetValue<long>(row, "K1"), Is.EqualTo(1L));
        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query), Is.EqualTo(new[] { "K:int", "K1:long" }));
    }

    [Test]
    [Description("A long key on the left and an int key on the right match too: K is long and K1 is int")]
    public void Join_LongKeyAndIntKey_MatchAndKeepTheirTypes()
    {
        var kustoDatabase = CreateTypedDatabase();
        const string query = "Typed | project K = L | join kind=inner (Typed | project K = I) on K";

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase, query);

        Assert.That(GetValue<long>(row, "K"), Is.EqualTo(1L));
        Assert.That(GetValue<int>(row, "K1"), Is.EqualTo(1));
        Assert.That(ExecuteAndGetSchemaTexts(kustoDatabase, query), Is.EqualTo(new[] { "K:long", "K1:int" }));
    }

    [Test]
    [Description("An explicit equality of an int column and a long column matches too")]
    public void Join_ExplicitEqualityOfIntAndLongColumns_Matches()
    {
        var kustoDatabase = CreateTypedDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, "Typed | join kind=inner Typed on $left.I == $right.L | count"),
            Is.EqualTo(1L));
    }

    [Test]
    [Description("A computed int key (toint(EventId)) matches the long TelemetryId: the events 1, 2 and 3")]
    public void Join_OnAComputedIntKeyAndALongKey()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | extend K = toint(EventId) " +
                             "| join kind=inner (Telemetry | extend K = TelemetryId) on K";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "TelemetryId"),
            Is.EquivalentTo(new[] { "1:1", "2:2", "3:3" }));
    }

    [TestCase("S", "L")]
    [TestCase("R", "L")]
    [TestCase("D", "S")]
    [TestCase("G", "S")]
    [TestCase("B", "L")]
    [TestCase("I", "R")]
    [TestCase("M", "R")]
    [TestCase("M", "L")]
    [TestCase("I", "S")]
    [TestCase("T", "L")]
    [TestCase("D", "T")]
    [Description("Keys of incompatible types are an error (SEM0714), even when the values look equal: string and long, " +
                 "real and long, decimal and real, datetime and timespan and the other mixes except int and long")]
    public void Join_OnKeysOfIncompatibleTypes_Fails(string leftColumn, string rightColumn)
    {
        var kustoDatabase = CreateTypedDatabase();

        AssertQueryFails(kustoDatabase,
            $"Typed | project K = {leftColumn} | join kind=inner (Typed | project K = {rightColumn}) on K");
    }

    [TestCase("")]
    [TestCase("kind=inner")]
    [TestCase("kind=leftouter")]
    [TestCase("kind=fullouter")]
    [TestCase("kind=leftsemi")]
    [TestCase("kind=leftanti")]
    [Description("A string key and a long key with the same name are an error for every kind; the join does not " +
                 "convert \"1\" to 1 and does not silently return no match")]
    public void Join_OnAStringKeyAndALongKey_FailsForEveryKind(string kindClause)
    {
        var kustoDatabase = CreateTypedDatabase();

        AssertQueryFails(kustoDatabase, $"KeyLeft | join {kindClause} KeyRight on Key");
    }

    [Test]
    [Description("An explicit equality of a string column and a long column is an error too")]
    public void Join_ExplicitEqualityOfAStringAndALongColumn_Fails()
    {
        var kustoDatabase = CreateTypedDatabase();

        AssertQueryFails(kustoDatabase, "Typed | join kind=inner Typed on $left.S == $right.L");
    }

    [TestCase("KeyLeft | extend Key = tolong(Key) | join kind=inner KeyRight on Key")]
    [TestCase("KeyLeft | join kind=inner (KeyRight | extend Key = tostring(Key)) on Key")]
    [Description("Casting either key to the type of the other makes the keys comparable: \"1\" and 1 match")]
    public void Join_AfterCastingOneKey_Matches(string query)
    {
        var kustoDatabase = CreateTypedDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Key", "Side", "Key1", "Side1"),
            Is.EqualTo(new[] { "1:left:1:right" }));
    }

    [TestCase(TelemetryWithDynamicDevice + " | join kind=inner Devices on DeviceId")]
    [TestCase(TelemetryWithDynamicDevice + " | join kind=leftouter Devices on DeviceId")]
    [TestCase(TelemetryWithDynamicDevice + " | join kind=leftsemi Devices on DeviceId")]
    [TestCase(TelemetryWithDynamicDevice + " | join kind=inner (" + TelemetryWithDynamicDevice + ") on DeviceId")]
    [TestCase("Telemetry | extend Device = parse_json(Payload).device " +
              "| join kind=inner Devices on $left.Device == $right.DeviceId")]
    [Description("A dynamic key is an error, against a string key or another dynamic key: the join does not compare " +
                 "the JSON values or their texts (SEM0713)")]
    public void Join_OnADynamicKey_Fails(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        AssertQueryFails(kustoDatabase, query);
    }

    [Test]
    [Description("tostring of the dynamic device id is a string key: the telemetry 1 and 2 match the devices d1 and " +
                 "d2, the telemetry 3 (d9) has no device")]
    public void Join_OnADynamicValueCastToString_Matches()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Telemetry | extend DeviceId = tostring(parse_json(Payload).device) " +
                             "| join kind=inner Devices on DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "TelemetryId", "DeviceId", "Model"),
            Is.EquivalentTo(new[] { "1:d1:Surface", "2:d2:Xbox" }));
    }

    [Test]
    [Description("tolong of the dynamic event number is a long key: the telemetry 1 and 2 match the events 1 and 3")]
    public void Join_OnADynamicValueCastToLong_Matches()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Telemetry | extend EventId = tolong(parse_json(Payload).event) " +
                             "| join kind=inner Events on EventId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "TelemetryId", "EventId", "EventType"),
            Is.EquivalentTo(new[] { "1:1:boot", "2:3:boot" }));
    }

    [Test]
    [Description("Casting the dynamic event number to the wrong type (string) against the long EventId is an error")]
    public void Join_OnADynamicValueCastToTheWrongType_Fails()
    {
        var kustoDatabase = CreateScenarioDatabase();

        AssertQueryFails(kustoDatabase,
            "Telemetry | extend EventId = tostring(parse_json(Payload).event) | join kind=inner Events on EventId");
    }

    [Test]
    [Description("A bool key: the active devices d1 and d4 match the flag on, the inactive d2 the flag off")]
    public void Join_OnABoolKey()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, "Devices | join kind=inner Flags on Active", "DeviceId", "Label"),
            Is.EquivalentTo(new[] { "d1:on", "d2:off", "d4:on" }));
    }

    [Test]
    [Description("A computed datetime key: bin(Timestamp, 1d) matches the day of the quota")]
    public void Join_OnADayComputedWithBin()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | extend Day = bin(Timestamp, 1d) | join kind=inner DailyQuota on Day";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Quota"),
            Is.EquivalentTo(new[] { "1:10", "2:10", "3:10", "4:20", "5:20" }));
    }

    #endregion

    #region string comparison

    [TestCase("Users | join kind=inner Logins on UserName")]
    [TestCase("Users | join kind=inner Logins on $left.UserName == $right.UserName")]
    [Description("String keys are case-sensitive: only alice matches alice; Alice and ALICE do not match alice, bob " +
                 "does not match BOB and \"carol \" (trailing space) does not match carol")]
    public void Join_StringKeys_AreCaseSensitive(string query)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "UserName", "Team", "LoginCount"),
            Is.EqualTo(new[] { "alice:red:1" }));
    }

    [TestCase("Users | join kind=leftanti Logins on UserName", new[] { "Alice", "ALICE", "bob", "carol " })]
    [TestCase("Logins | join kind=leftanti Users on UserName", new[] { "BOB", "carol" })]
    [Description("leftanti shows the names that differ only in case or in a trailing space as missing")]
    public void Join_LeftAnti_OnStringKeysThatDifferInCase(string query, string[] expectedNames)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "UserName"), Is.EquivalentTo(expectedNames));
    }

    [Test]
    [Description("Joining on tolower of both sides matches regardless of case: alice, Alice and ALICE match alice, " +
                 "bob matches BOB; \"carol \" still does not match carol")]
    public void Join_OnLowerCasedKeys_MatchesRegardlessOfCase()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Users | extend Key = tolower(UserName) " +
                             "| join kind=inner (Logins | extend Key = tolower(UserName)) on Key";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "UserName", "Team", "LoginCount"),
            Is.EquivalentTo(new[] { "alice:red:1", "Alice:blue:1", "ALICE:green:1", "bob:red:2" }));
    }

    #endregion

    #region empty string keys

    [Test]
    [Description("The empty Model of the unmatched event 4 after leftouter is an ordinary empty string: the next join " +
                 "matches it to the category of the empty model; the Xbox events have no category")]
    public void Join_EmptyStringOfAnUnmatchedRow_MatchesAnEmptyStringKeyInTheNextJoin()
    {
        var kustoDatabase = CreateScenarioDatabase(BuildCategoriesTable());
        const string query = "Events | join kind=leftouter Devices on DeviceId | join kind=inner Categories on Model";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Model", "Category"),
            Is.EquivalentTo(new[] { "1:Surface:laptop", "2:Surface:laptop", "4::unknown" }));
    }

    #endregion
}
