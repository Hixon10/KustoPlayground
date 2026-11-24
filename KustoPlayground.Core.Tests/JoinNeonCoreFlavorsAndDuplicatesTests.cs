using static KustoPlayground.Core.Tests.JoinNeonTestUtils;
using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

public class JoinNeonCoreFlavorsAndDuplicatesTests
{
    private static KustoDatabase CreateRequestsAndTracesDatabase()
    {
        // op-1: 2 requests x 3 traces (tests 6 rows in inner vs 3 in innerunique)
        // op-2: 1 request  x 2 traces (tests right-side duplicates preserved in innerunique)
        // op-3: 3 requests x 1 trace  (tests left-side duplicates collapsed to 1 in innerunique)
        // op-only-left:  2 unmatched requests
        // op-only-right: 2 unmatched traces
        Table requests = new NullableTableBuilder("Requests")
            .WithColumn<string>("OperationId", isNullable: false)
            .WithColumn<string>("ReqId", isNullable: false)
            .WithColumn<int>("StatusCode", isNullable: false)
            .WithRow("op-1", "r1", 200)
            .WithRow("op-1", "r2", 201)
            .WithRow("op-2", "r3", 200)
            .WithRow("op-3", "r4", 500)
            .WithRow("op-3", "r5", 502)
            .WithRow("op-3", "r6", 503)
            .WithRow("op-only-left", "r7", 404)
            .WithRow("op-only-left", "r8", 404)
            .Build();

        Table traces = new NullableTableBuilder("Traces")
            .WithColumn<string>("OperationId", isNullable: false)
            .WithColumn<string>("TraceId", isNullable: false)
            .WithColumn<string>("Message", isNullable: false)
            .WithRow("op-1", "t1", "start")
            .WithRow("op-1", "t2", "db")
            .WithRow("op-1", "t3", "end")
            .WithRow("op-2", "t4", "cache")
            .WithRow("op-2", "t5", "render")
            .WithRow("op-3", "t6", "timeout")
            .WithRow("op-only-right", "t7", "orphan-a")
            .WithRow("op-only-right", "t8", "orphan-b")
            .Build();

        return CreateDatabase(requests, traces);
    }

    private static KustoDatabase CreateEventsAndDevicesDatabase()
    {
        Table events = new NullableTableBuilder("Events")
            .WithColumn<string>("EventId", isNullable: false)
            .WithColumn<string>("DeviceId", isNullable: false)
            .WithColumn<string>("Severity", isNullable: false)
            .WithRow("e1", "dev-1", "Info")
            .WithRow("e2", "dev-1", "Warn")
            .WithRow("e3", "dev-2", "Error")
            .WithRow("e4", "dev-missing", "Critical")
            .WithRow("e5", "dev-missing", "Warn")
            .Build();

        // dev-1 appears twice in Devices (duplicate inventory entries); dev-2 appears once; dev-idle has no events.
        Table devices = new NullableTableBuilder("Devices")
            .WithColumn<string>("DeviceId", isNullable: false)
            .WithColumn<string>("Owner", isNullable: false)
            .WithColumn<int>("Tier", isNullable: false)
            .WithRow("dev-1", "Alice", 1)
            .WithRow("dev-1", "Bob", 2)
            .WithRow("dev-2", "Carol", 1)
            .WithRow("dev-idle", "Dave", 3)
            .WithRow("dev-idle", "Eve", 3)
            .Build();

        return CreateDatabase(events, devices);
    }

    #region Default bare join and innerunique

    [TestCase("Requests | join Traces on OperationId")]
    [TestCase("Requests | join kind=innerunique Traces on OperationId")]
    [TestCase("Requests | join kind = innerunique Traces on OperationId")]
    [Description("Bare join means innerunique: keeps one left row per distinct join key and pairs it with every matching right row without guaranteeing which duplicate left row wins.")]
    public void BareJoinAndExplicitInnerUnique_DeduplicateLeftOnlyAndPairWithAllMatchingRightRows(string query)
    {
        KustoDatabase db = CreateRequestsAndTracesDatabase();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(db, query);

        // op-1: 1 surviving left x 3 traces = 3
        // op-2: 1 surviving left x 2 traces = 2
        // op-3: 1 surviving left x 1 trace  = 1
        // Total = 6 rows (NOT 2*3 + 1*2 + 3*1 = 11 rows from inner).
        Assert.That(rows, Has.Count.EqualTo(6));
        AssertExactColumnOrder(rows, "OperationId", "ReqId", "StatusCode", "OperationId1", "TraceId", "Message");

        var byOp = rows.GroupBy(r => (string)r["OperationId"]!).ToDictionary(g => g.Key, g => g.ToList());
        Assert.That(byOp.Keys, Is.EquivalentTo(new[] { "op-1", "op-2", "op-3" }));

        // op-1: exactly one surviving ReqId from {r1, r2}, paired with ALL 3 traces {t1, t2, t3}
        var op1ReqIds = byOp["op-1"].Select(r => (string)r["ReqId"]!).Distinct().ToList();
        Assert.That(op1ReqIds, Has.Count.EqualTo(1));
        Assert.That(op1ReqIds[0], Is.AnyOf("r1", "r2"));
        Assert.That(byOp["op-1"].Select(r => (string)r["TraceId"]!), Is.EquivalentTo(new[] { "t1", "t2", "t3" }));
        int expectedOp1Status = op1ReqIds[0] == "r1" ? 200 : 201;
        Assert.That(byOp["op-1"].Select(r => ReadCell<int>(r, "StatusCode")).Distinct(), Is.EqualTo(new[] { expectedOp1Status }));

        // op-2: unique left row r3 paired with BOTH traces {t4, t5}
        Assert.That(byOp["op-2"].Select(r => ((string)r["ReqId"]!, (string)r["TraceId"]!)),
            Is.EquivalentTo(new[] { ("r3", "t4"), ("r3", "t5") }));

        // op-3: exactly one surviving ReqId from {r4, r5, r6}, paired with t6
        Assert.That(byOp["op-3"], Has.Count.EqualTo(1));
        Assert.That((string)byOp["op-3"][0]["ReqId"]!, Is.AnyOf("r4", "r5", "r6"));
        Assert.That((string)byOp["op-3"][0]["TraceId"]!, Is.EqualTo("t6"));
    }

    [Test]
    [Description("Two requests and three traces sharing one key return 3 rows for bare join (innerunique) and 6 rows for kind=inner.")]
    public void TwoRequestsAndThreeTracesOnSingleKey_InnerUniqueReturnsThreeWhileInnerReturnsSix()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("Requests")
                .WithColumn<string>("OperationId", isNullable: false)
                .WithColumn<string>("ReqId", isNullable: false)
                .WithRow("op-42", "req-A")
                .WithRow("op-42", "req-B")
                .Build(),
            new NullableTableBuilder("Traces")
                .WithColumn<string>("OperationId", isNullable: false)
                .WithColumn<string>("TraceId", isNullable: false)
                .WithRow("op-42", "tr-1")
                .WithRow("op-42", "tr-2")
                .WithRow("op-42", "tr-3")
                .Build());

        var innerUniqueRows = ExecuteAndGetNullable2Columns<string, string>(
            "ReqId", "TraceId", db, "Requests | join Traces on OperationId");
        var explicitInnerUniqueRows = ExecuteAndGetNullable2Columns<string, string>(
            "ReqId", "TraceId", db, "Requests | join kind=innerunique Traces on OperationId");
        var innerRows = ExecuteAndGetNullable2Columns<string, string>(
            "ReqId", "TraceId", db, "Requests | join kind=inner Traces on OperationId");

        Assert.That(innerUniqueRows, Has.Count.EqualTo(3));
        Assert.That(innerUniqueRows.Select(r => r.Item1).Distinct().ToList(), Has.Count.EqualTo(1));
        Assert.That(innerUniqueRows[0].Item1, Is.AnyOf("req-A", "req-B"));
        Assert.That(innerUniqueRows.Select(r => r.Item2), Is.EquivalentTo(new[] { "tr-1", "tr-2", "tr-3" }));

        Assert.That(explicitInnerUniqueRows, Has.Count.EqualTo(3));
        Assert.That(explicitInnerUniqueRows.Select(r => r.Item1).Distinct().ToList(), Has.Count.EqualTo(1));
        Assert.That(explicitInnerUniqueRows.Select(r => r.Item2), Is.EquivalentTo(new[] { "tr-1", "tr-2", "tr-3" }));

        Assert.That(innerRows, Is.EquivalentTo(new[]
        {
            ("req-A", "tr-1"), ("req-A", "tr-2"), ("req-A", "tr-3"),
            ("req-B", "tr-1"), ("req-B", "tr-2"), ("req-B", "tr-3")
        }));
    }

    [Test]
    [Description("innerunique deduplicates only the left table, never the right table: 1 left row x 4 right rows produces 4 rows.")]
    public void InnerUnique_DoesNotDeduplicateRightSide()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<string>("K", false).WithColumn<int>("LV", false)
                .WithRow("k1", 10).Build(),
            new NullableTableBuilder("R").WithColumn<string>("K", false).WithColumn<int>("RV", false)
                .WithRow("k1", 1).WithRow("k1", 2).WithRow("k1", 3).WithRow("k1", 4).Build());

        var rows = ExecuteAndGetNullable2Columns<int, int>("LV", "RV", db, "L | join R on K");
        Assert.That(rows, Is.EquivalentTo(new[] { (10, 1), (10, 2), (10, 3), (10, 4) }));
    }

    #endregion

    #region inner

    [TestCase("Requests | join kind=inner Traces on OperationId")]
    [TestCase("Requests | join kind = inner Traces on OperationId")]
    [Description("kind=inner returns every matching left-right pair across all keys and excludes unmatched rows from both sides.")]
    public void InnerJoin_ReturnsFullCartesianProductPerMatchingKey(string query)
    {
        KustoDatabase db = CreateRequestsAndTracesDatabase();
        var actual = ExecuteAndGetNullable3Columns<string, string, string>(
            "OperationId", "ReqId", "TraceId", db, query);

        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("op-1", "r1", "t1"), ("op-1", "r1", "t2"), ("op-1", "r1", "t3"),
            ("op-1", "r2", "t1"), ("op-1", "r2", "t2"), ("op-1", "r2", "t3"),
            ("op-2", "r3", "t4"), ("op-2", "r3", "t5"),
            ("op-3", "r4", "t6"), ("op-3", "r5", "t6"), ("op-3", "r6", "t6")
        }));
    }

    [Test]
    [Description("Inner join preserves multiset multiplicity even when rows have completely identical values across all columns.")]
    public void InnerJoin_IdenticalDuplicateRows_PreservesFullMultisetCount()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<string>("K", false).WithColumn<int>("V", false)
                .WithRow("dup", 1).WithRow("dup", 1).Build(),
            new NullableTableBuilder("R").WithColumn<string>("K", false).WithColumn<int>("W", false)
                .WithRow("dup", 9).WithRow("dup", 9).WithRow("dup", 9).Build());

        var rows = ExecuteAndGetNullable3Columns<string, int, int>("K", "V", "W", db, "L | join kind=inner R on K");
        Assert.That(rows, Has.Count.EqualTo(6));
        Assert.That(rows, Is.All.EqualTo(("dup", 1, 9)));
    }

    #endregion

    #region leftouter, rightouter, fullouter

    [Test]
    [Description("kind=leftouter enriches events with device inventory without dropping unmatched events; duplicate matches multiply rows and unmatched right rows are excluded.")]
    public void LeftOuterJoin_EnrichesEventsPreservesUnmatchedLeftAndMultipliesDuplicateMatches()
    {
        KustoDatabase db = CreateEventsAndDevicesDatabase();

        var actual = ExecuteAndGetNullable5Columns<string, string, string, string, int?>(
            "EventId", "DeviceId", "DeviceId1", "Owner", "Tier",
            db, "Events | join kind=leftouter Devices on DeviceId");

        Assert.That(actual, Is.EquivalentTo(new (string, string, string, string, int?)[]
        {
            ("e1", "dev-1", "dev-1", "Alice", 1),
            ("e1", "dev-1", "dev-1", "Bob", 2),
            ("e2", "dev-1", "dev-1", "Alice", 1),
            ("e2", "dev-1", "dev-1", "Bob", 2),
            ("e3", "dev-2", "dev-2", "Carol", 1),
            ("e4", "dev-missing", "", "", null),
            ("e5", "dev-missing", "", "", null)
        }));
    }

    [Test]
    [Description("kind=rightouter keeps every right row (including duplicate unmatched right rows), multiplies matched pairs, and pads unmatched left columns.")]
    public void RightOuterJoin_PreservesAllRightRowsAndPadsMissingLeftSide()
    {
        KustoDatabase db = CreateEventsAndDevicesDatabase();

        var actual = ExecuteAndGetNullable5Columns<string, string, string, string, int>(
            "EventId", "DeviceId", "DeviceId1", "Owner", "Tier",
            db, "Events | join kind=rightouter Devices on DeviceId");

        Assert.That(actual, Is.EquivalentTo(new (string, string, string, string, int)[]
        {
            ("e1", "dev-1", "dev-1", "Alice", 1),
            ("e1", "dev-1", "dev-1", "Bob", 2),
            ("e2", "dev-1", "dev-1", "Alice", 1),
            ("e2", "dev-1", "dev-1", "Bob", 2),
            ("e3", "dev-2", "dev-2", "Carol", 1),
            ("", "", "dev-idle", "Dave", 3),
            ("", "", "dev-idle", "Eve", 3)
        }));
    }

    [Test]
    [Description("kind=fullouter returns every matching pair plus unmatched rows from both left and right sides.")]
    public void FullOuterJoin_ReturnsMatchedPairsPlusUnmatchedRowsFromBothSides()
    {
        KustoDatabase db = CreateEventsAndDevicesDatabase();

        var actual = ExecuteAndGetNullable5Columns<string, string, string, string, int?>(
            "EventId", "DeviceId", "DeviceId1", "Owner", "Tier",
            db, "Events | join kind=fullouter Devices on DeviceId");

        Assert.That(actual, Is.EquivalentTo(new (string, string, string, string, int?)[]
        {
            ("e1", "dev-1", "dev-1", "Alice", 1),
            ("e1", "dev-1", "dev-1", "Bob", 2),
            ("e2", "dev-1", "dev-1", "Alice", 1),
            ("e2", "dev-1", "dev-1", "Bob", 2),
            ("e3", "dev-2", "dev-2", "Carol", 1),
            ("e4", "dev-missing", "", "", null),
            ("e5", "dev-missing", "", "", null),
            ("", "", "dev-idle", "Dave", 3),
            ("", "", "dev-idle", "Eve", 3)
        }));
    }

    [Test]
    [Description("Fullouter snapshot reconciliation exposes devices present only in yesterday's snapshot, only in today's snapshot, changed, or unchanged.")]
    public void FullOuterJoin_InventorySnapshotReconciliation_ExposesAddedRemovedChangedAndUnchanged()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("Yesterday")
                .WithColumn<string>("DeviceId", isNullable: false)
                .WithColumn<string>("Firmware", isNullable: false)
                .WithRow("d1", "1.0")
                .WithRow("d2", "1.0")
                .WithRow("d3", "1.0")
                .Build(),
            new NullableTableBuilder("Today")
                .WithColumn<string>("DeviceId", isNullable: false)
                .WithColumn<string>("Firmware", isNullable: false)
                .WithRow("d1", "1.0")
                .WithRow("d2", "2.0")
                .WithRow("d4", "2.0")
                .Build());

        string query = """
            Yesterday
            | join kind=fullouter Today on DeviceId
            | extend
                EffectiveDevice = iff(isempty(DeviceId), DeviceId1, DeviceId),
                Change = case(
                    isempty(DeviceId), "added",
                    isempty(DeviceId1), "removed",
                    Firmware != Firmware1, "upgraded",
                    "unchanged")
            | project EffectiveDevice, Change, OldFw = Firmware, NewFw = Firmware1
            """;

        var actual = ExecuteAndGetNullable4Columns<string, string, string, string>(
            "EffectiveDevice", "Change", "OldFw", "NewFw", db, query);

        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("d1", "unchanged", "1.0", "1.0"),
            ("d2", "upgraded",  "1.0", "2.0"),
            ("d3", "removed",   "1.0", ""),
            ("d4", "added",     "",    "2.0")
        }));
    }

    #endregion

    #region leftsemi and rightsemi

    [Test]
    [Description("kind=leftsemi keeps each matching left row once with only left columns, without multiplying when right has duplicate keys, while preserving duplicate left rows.")]
    public void LeftSemiJoin_KeepsEachMatchingLeftRowOnceWithoutMultiplyingOnDuplicateRightMatches()
    {
        KustoDatabase db = CreateEventsAndDevicesDatabase();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(
            db, "Events | join kind=leftsemi Devices on DeviceId");

        // dev-1 has 2 rows in Devices, yet e1 and e2 each appear ONLY ONCE (not 2 times each).
        // Both e1 and e2 survive (left duplicates are NOT collapsed).
        AssertExactColumnOrder(rows, "EventId", "DeviceId", "Severity");
        var actual = rows.Select(r => ((string)r["EventId"]!, (string)r["DeviceId"]!, (string)r["Severity"]!)).ToList();
        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("e1", "dev-1", "Info"),
            ("e2", "dev-1", "Warn"),
            ("e3", "dev-2", "Error")
        }));
    }

    [Test]
    [Description("kind=rightsemi keeps each matching right row once with only right columns, without multiplying when left has duplicate keys, while preserving duplicate right rows.")]
    public void RightSemiJoin_KeepsEachMatchingRightRowOnceWithoutMultiplyingOnDuplicateLeftMatches()
    {
        KustoDatabase db = CreateEventsAndDevicesDatabase();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(
            db, "Events | join kind=rightsemi Devices on DeviceId");

        // dev-1 has 2 events on the left, yet Alice and Bob each appear ONLY ONCE (not twice each).
        AssertExactColumnOrder(rows, "DeviceId", "Owner", "Tier");
        var actual = rows.Select(r => ((string)r["DeviceId"]!, (string)r["Owner"]!, ReadCell<int>(r, "Tier"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("dev-1", "Alice", 1),
            ("dev-1", "Bob", 2),
            ("dev-2", "Carol", 1)
        }));
    }

    #endregion

    #region leftanti, rightanti, and anti-join aliases

    [TestCase("leftanti")]
    [TestCase("anti")]
    [TestCase("leftantisemi")]
    [Description("kind=leftanti and its aliases (anti, leftantisemi) return all unmatched left rows (including duplicates) with only left columns.")]
    public void LeftAntiAndAliases_ReturnUnmatchedLeftRowsWithLeftSchemaOnly(string kind)
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("RegisteredDevices")
                .WithColumn<string>("DeviceId", isNullable: false)
                .WithColumn<string>("Region", isNullable: false)
                .WithRow("dev-1", "eastus")
                .WithRow("dev-2", "westus")
                .WithRow("dev-silent", "eastus")
                .WithRow("dev-silent", "europenorth")
                .WithRow("dev-offline", "westus")
                .Build(),
            new NullableTableBuilder("Heartbeats")
                .WithColumn<string>("DeviceId", isNullable: false)
                .WithColumn<long>("Seq", isNullable: false)
                .WithRow("dev-1", 1L)
                .WithRow("dev-1", 2L)
                .WithRow("dev-2", 1L)
                .WithRow("dev-unregistered", 99L)
                .Build());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(
            db, $"RegisteredDevices | join kind={kind} Heartbeats on DeviceId");

        AssertExactColumnOrder(rows, "DeviceId", "Region");
        var actual = rows.Select(r => ((string)r["DeviceId"]!, (string)r["Region"]!)).ToList();
        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("dev-silent", "eastus"),
            ("dev-silent", "europenorth"),
            ("dev-offline", "westus")
        }));
    }

    [TestCase("rightanti")]
    [TestCase("rightantisemi")]
    [Description("kind=rightanti and its alias rightantisemi return all unmatched right rows (including duplicates) with only right columns.")]
    public void RightAntiAndAlias_ReturnUnmatchedRightRowsWithRightSchemaOnly(string kind)
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("RegisteredDevices")
                .WithColumn<string>("DeviceId", isNullable: false)
                .WithColumn<string>("Region", isNullable: false)
                .WithRow("dev-1", "eastus")
                .WithRow("dev-offline", "westus")
                .Build(),
            new NullableTableBuilder("Heartbeats")
                .WithColumn<string>("DeviceId", isNullable: false)
                .WithColumn<long>("Seq", isNullable: false)
                .WithRow("dev-1", 1L)
                .WithRow("dev-rogue", 10L)
                .WithRow("dev-rogue", 11L)
                .WithRow("dev-unknown", 20L)
                .Build());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(
            db, $"RegisteredDevices | join kind={kind} Heartbeats on DeviceId");

        AssertExactColumnOrder(rows, "DeviceId", "Seq");
        var actual = rows.Select(r => ((string)r["DeviceId"]!, ReadCell<long>(r, "Seq"))).ToList();
        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("dev-rogue", 10L),
            ("dev-rogue", 11L),
            ("dev-unknown", 20L)
        }));
    }

    #endregion

    #region Empty tables, disjoint keys, and all-matching keys

    [TestCase("", 0)]
    [TestCase("kind=innerunique", 0)]
    [TestCase("kind=inner", 0)]
    [TestCase("kind=leftouter", 0)]
    [TestCase("kind=rightouter", 2)]
    [TestCase("kind=fullouter", 2)]
    [TestCase("kind=leftsemi", 0)]
    [TestCase("kind=rightsemi", 0)]
    [TestCase("kind=leftanti", 0)]
    [TestCase("kind=anti", 0)]
    [TestCase("kind=leftantisemi", 0)]
    [TestCase("kind=rightanti", 2)]
    [TestCase("kind=rightantisemi", 2)]
    [Description("Empty left table with populated right table produces expected row counts across every join kind.")]
    public void EmptyLeftTable_AllJoinKinds_ProduceExpectedRowCounts(string kindClause, int expectedCount)
    {
        KustoDatabase db = CreateDatabase(
            CreateEmptyNullableTable("L", ("K", typeof(string)), ("LV", typeof(int))),
            new NullableTableBuilder("R").WithColumn<string>("K", false).WithColumn<int>("RV", false)
                .WithRow("a", 10).WithRow("a", 20).Build());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(db, $"L | join {kindClause} R on K");
        Assert.That(rows, Has.Count.EqualTo(expectedCount));

        if (kindClause is "kind=rightouter" or "kind=fullouter")
        {
            Assert.That(rows.Select(r => ((string)r["K"]!, ReadCell<int?>(r, "LV"), (string)r["K1"]!, ReadCell<int>(r, "RV"))),
                Is.EquivalentTo(new (string, int?, string, int)[] { ("", null, "a", 10), ("", null, "a", 20) }));
        }
        else if (kindClause is "kind=rightanti" or "kind=rightantisemi")
        {
            Assert.That(rows.Select(r => ((string)r["K"]!, ReadCell<int>(r, "RV"))),
                Is.EquivalentTo(new[] { ("a", 10), ("a", 20) }));
        }
    }

    [TestCase("", 0)]
    [TestCase("kind=innerunique", 0)]
    [TestCase("kind=inner", 0)]
    [TestCase("kind=leftouter", 2)]
    [TestCase("kind=rightouter", 0)]
    [TestCase("kind=fullouter", 2)]
    [TestCase("kind=leftsemi", 0)]
    [TestCase("kind=rightsemi", 0)]
    [TestCase("kind=leftanti", 2)]
    [TestCase("kind=anti", 2)]
    [TestCase("kind=leftantisemi", 2)]
    [TestCase("kind=rightanti", 0)]
    [TestCase("kind=rightantisemi", 0)]
    [Description("Populated left table with empty right table produces expected row counts across every join kind.")]
    public void EmptyRightTable_AllJoinKinds_ProduceExpectedRowCounts(string kindClause, int expectedCount)
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<string>("K", false).WithColumn<int>("LV", false)
                .WithRow("a", 1).WithRow("a", 2).Build(),
            CreateEmptyNullableTable("R", ("K", typeof(string)), ("RV", typeof(int))));

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(db, $"L | join {kindClause} R on K");
        Assert.That(rows, Has.Count.EqualTo(expectedCount));

        if (kindClause is "kind=leftouter" or "kind=fullouter")
        {
            Assert.That(rows.Select(r => ((string)r["K"]!, ReadCell<int>(r, "LV"), (string)r["K1"]!, ReadCell<int?>(r, "RV"))),
                Is.EquivalentTo(new (string, int, string, int?)[] { ("a", 1, "", null), ("a", 2, "", null) }));
        }
        else if (kindClause is "kind=leftanti" or "kind=anti" or "kind=leftantisemi")
        {
            Assert.That(rows.Select(r => ((string)r["K"]!, ReadCell<int>(r, "LV"))),
                Is.EquivalentTo(new[] { ("a", 1), ("a", 2) }));
        }
    }

    [TestCase("")]
    [TestCase("kind=innerunique")]
    [TestCase("kind=inner")]
    [TestCase("kind=leftouter")]
    [TestCase("kind=rightouter")]
    [TestCase("kind=fullouter")]
    [TestCase("kind=leftsemi")]
    [TestCase("kind=rightsemi")]
    [TestCase("kind=leftanti")]
    [TestCase("kind=anti")]
    [TestCase("kind=leftantisemi")]
    [TestCase("kind=rightanti")]
    [TestCase("kind=rightantisemi")]
    [Description("Joining two empty tables returns zero rows without error across every join kind.")]
    public void BothTablesEmpty_AllJoinKinds_ReturnEmptyResult(string kindClause)
    {
        KustoDatabase db = CreateDatabase(
            CreateEmptyNullableTable("L", ("K", typeof(string)), ("LV", typeof(int))),
            CreateEmptyNullableTable("R", ("K", typeof(string)), ("RV", typeof(int))));

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(db, $"L | join {kindClause} R on K");
        Assert.That(rows, Is.Empty);
    }

    [TestCase("", 0)]
    [TestCase("kind=innerunique", 0)]
    [TestCase("kind=inner", 0)]
    [TestCase("kind=leftouter", 2)]
    [TestCase("kind=rightouter", 3)]
    [TestCase("kind=fullouter", 5)]
    [TestCase("kind=leftsemi", 0)]
    [TestCase("kind=rightsemi", 0)]
    [TestCase("kind=leftanti", 2)]
    [TestCase("kind=anti", 2)]
    [TestCase("kind=leftantisemi", 2)]
    [TestCase("kind=rightanti", 3)]
    [TestCase("kind=rightantisemi", 3)]
    [Description("Non-empty tables with completely disjoint keys produce exact expected counts across all join kinds.")]
    public void DisjointKeys_AllJoinKinds_ProduceExpectedRowCounts(string kindClause, int expectedCount)
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<string>("K", false).WithColumn<int>("LV", false)
                .WithRow("left-1", 1).WithRow("left-2", 2).Build(),
            new NullableTableBuilder("R").WithColumn<string>("K", false).WithColumn<int>("RV", false)
                .WithRow("right-1", 10).WithRow("right-2", 20).WithRow("right-3", 30).Build());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(db, $"L | join {kindClause} R on K");
        Assert.That(rows, Has.Count.EqualTo(expectedCount));
    }

    [TestCase("", 2)]
    [TestCase("kind=innerunique", 2)]
    [TestCase("kind=inner", 2)]
    [TestCase("kind=leftouter", 2)]
    [TestCase("kind=rightouter", 2)]
    [TestCase("kind=fullouter", 2)]
    [TestCase("kind=leftsemi", 2)]
    [TestCase("kind=rightsemi", 2)]
    [TestCase("kind=leftanti", 0)]
    [TestCase("kind=anti", 0)]
    [TestCase("kind=leftantisemi", 0)]
    [TestCase("kind=rightanti", 0)]
    [TestCase("kind=rightantisemi", 0)]
    [Description("One-to-one fully matching tables return identical matched rows for inner/outer/semi joins and zero rows for anti joins.")]
    public void OneToOneAllMatchingKeys_AllJoinKinds_ProduceExpectedRowCounts(string kindClause, int expectedCount)
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<string>("K", false).WithColumn<int>("LV", false)
                .WithRow("a", 1).WithRow("b", 2).Build(),
            new NullableTableBuilder("R").WithColumn<string>("K", false).WithColumn<int>("RV", false)
                .WithRow("a", 10).WithRow("b", 20).Build());

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(db, $"L | join {kindClause} R on K");
        Assert.That(rows, Has.Count.EqualTo(expectedCount));
    }

    #endregion

    #region Hints, syntax forms, fan-out, and cross-join idiom

    [TestCase("L | join kind=inner hint.strategy=broadcast R on K")]
    [TestCase("L | join kind=inner hint.strategy=shuffle R on K")]
    [TestCase("L | join kind=inner hint.strategy=shuffle hint.shufflekey=K R on K")]
    [TestCase("L | join kind=inner hint.num_partitions=4 R on K")]
    [TestCase("L | join hint.strategy=broadcast kind=inner R on K")]
    [TestCase("L | join hint.strategy=broadcast R on K")]
    [Description("Standard KQL join execution hints are accepted as no-op query hints without changing logical results.")]
    public void JoinExecutionHints_AreAcceptedAndPreserveLogicalResults(string query)
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<string>("K", false).WithColumn<int>("LV", false)
                .WithRow("a", 1).WithRow("b", 2).Build(),
            new NullableTableBuilder("R").WithColumn<string>("K", false).WithColumn<int>("RV", false)
                .WithRow("a", 10).WithRow("c", 30).Build());

        var actual = ExecuteAndGetNullable3Columns<string, int, int>("K", "LV", "RV", db, query);
        Assert.That(actual, Is.EquivalentTo(new[] { ("a", 1, 10) }));
    }

    [Test]
    [Description("Cross-join idiom using an extended constant placeholder key returns the full Cartesian product of both tables.")]
    public void CrossJoinIdiom_UsingPlaceholderKey_ReturnsFullCartesianProduct()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("Colors").WithColumn<string>("Color", false)
                .WithRow("Red").WithRow("Blue").Build(),
            new NullableTableBuilder("Sizes").WithColumn<string>("Size", false)
                .WithRow("S").WithRow("M").WithRow("L").Build());

        string query = """
            Colors
            | extend __cross = 1
            | join kind=inner (Sizes | extend __cross = 1) on __cross
            | project Color, Size
            """;

        var actual = ExecuteAndGetNullable2Columns<string, string>("Color", "Size", db, query);
        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("Red", "S"), ("Red", "M"), ("Red", "L"),
            ("Blue", "S"), ("Blue", "M"), ("Blue", "L")
        }));
    }

    [Test]
    [Description("Larger duplicate fan-out (5 left x 4 right on one key) returns 20 for inner, 4 for innerunique, 5 for leftsemi, and 4 for rightsemi.")]
    public void LargeDuplicateFanOut_MultiplicityMatchesEveryJoinFlavor()
    {
        var leftBuilder = new NullableTableBuilder("L").WithColumn<string>("K", false).WithColumn<int>("LId", false);
        for (int i = 1; i <= 5; i++)
        {
            leftBuilder.WithRow("hot", i);
        }

        var rightBuilder = new NullableTableBuilder("R").WithColumn<string>("K", false).WithColumn<int>("RId", false);
        for (int j = 1; j <= 4; j++)
        {
            rightBuilder.WithRow("hot", j * 10);
        }

        KustoDatabase db = CreateDatabase(leftBuilder.Build(), rightBuilder.Build());

        Assert.That(ExecuteJoin(db, "L | join kind=inner R on K"), Has.Count.EqualTo(20));
        Assert.That(ExecuteJoin(db, "L | join R on K"), Has.Count.EqualTo(4));
        Assert.That(ExecuteJoin(db, "L | join kind=leftsemi R on K"), Has.Count.EqualTo(5));
        Assert.That(ExecuteJoin(db, "L | join kind=rightsemi R on K"), Has.Count.EqualTo(4));
        Assert.That(ExecuteJoin(db, "L | join kind=leftanti R on K"), Is.Empty);
        Assert.That(ExecuteJoin(db, "L | join kind=rightanti R on K"), Is.Empty);
    }

    [Test]
    [Description("Single-column (key-only) tables with no payload columns produce valid schemas and expected rows across all join kinds.")]
    public void SingleColumnKeyOnlyTables_AllJoinKinds_ProduceExpectedSchemaAndRows()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<string>("K", false)
                .WithRow("a").WithRow("a").WithRow("b").Build(),
            new NullableTableBuilder("R").WithColumn<string>("K", false)
                .WithRow("a").WithRow("c").Build());

        var inner = ExecuteJoin(db, "L | join kind=inner R on K");
        AssertExactColumnOrder(inner, "K", "K1");
        Assert.That(inner, Has.Count.EqualTo(2));

        var innerUnique = ExecuteJoin(db, "L | join R on K");
        AssertExactColumnOrder(innerUnique, "K", "K1");
        Assert.That(innerUnique, Has.Count.EqualTo(1));

        var fullOuter = ExecuteJoin(db, "L | join kind=fullouter R on K");
        AssertExactColumnOrder(fullOuter, "K", "K1");
        Assert.That(
            fullOuter.Select(r => ((string)r["K"]!, (string)r["K1"]!)),
            Is.EquivalentTo(new[] { ("a", "a"), ("a", "a"), ("b", ""), ("", "c") }));

        var leftSemi = ExecuteJoin(db, "L | join kind=leftsemi R on K");
        AssertExactColumnOrder(leftSemi, "K");
        Assert.That(leftSemi.Select(r => (string)r["K"]!), Is.EquivalentTo(new[] { "a", "a" }));

        var leftAnti = ExecuteJoin(db, "L | join kind=leftanti R on K");
        AssertExactColumnOrder(leftAnti, "K");
        Assert.That(leftAnti.Select(r => (string)r["K"]!), Is.EquivalentTo(new[] { "b" }));

        var rightAnti = ExecuteJoin(db, "L | join kind=rightanti R on K");
        AssertExactColumnOrder(rightAnti, "K");
        Assert.That(rightAnti.Select(r => (string)r["K"]!), Is.EquivalentTo(new[] { "c" }));
    }

    #endregion
}
