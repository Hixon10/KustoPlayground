using System.Text.Json;
using static KustoPlayground.Core.Tests.JoinNeonTestUtils;
using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

public class JoinNeonSubqueriesAndPipelinesTests
{
    private static KustoDatabase CreateTelemetryDatabase()
    {
        Table events = new NullableTableBuilder("Events")
            .WithColumn<string>("EventId", false)
            .WithColumn<string>("DeviceId", false)
            .WithColumn<string>("Kind", false)
            .WithColumn<long>("Bytes", false)
            .WithRow("e1", "dev-1", "Http", 100L)
            .WithRow("e2", "dev-1", "Http", 250L)
            .WithRow("e3", "dev-2", "Disk", 500L)
            .WithRow("e4", "dev-3", "Http", 50L)
            .WithRow("e5", "dev-4", "Auth", 10L)
            .Build();

        Table devices = new NullableTableBuilder("Devices")
            .WithColumn<string>("DeviceId", false)
            .WithColumn<string>("Owner", false)
            .WithColumn<string>("Region", false)
            .WithColumn<bool>("Active", false)
            .WithColumn<int>("Version", false)
            .WithRow("dev-1", "Alice", "eastus", true, 3)
            .WithRow("dev-2", "Bob", "westus", false, 1)
            .WithRow("dev-3", "Carol", "eastus", true, 2)
            .WithRow("dev-5", "Dave", "europenorth", true, 3)
            .Build();

        Table regions = new NullableTableBuilder("Regions")
            .WithColumn<string>("Region", false)
            .WithColumn<string>("Geo", false)
            .WithRow("eastus", "US")
            .WithRow("westus", "US")
            .WithRow("europenorth", "EU")
            .Build();

        return CreateDatabase(events, devices, regions);
    }

    #region Right-side subqueries (parenthesized pipelines)

    [Test]
    [Description("Right side accepts a parenthesized table name (Devices) and a parenthesized filtered pipeline (Devices | where Active).")]
    public void RightSideSubquery_ParenthesizedTableAndFilteredPipeline()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        var unfiltered = ExecuteAndGetNullable2Columns<string, string>(
            "EventId", "Owner", db, "Events | join kind=leftouter (Devices) on DeviceId");
        Assert.That(unfiltered, Is.EquivalentTo(new[]
        {
            ("e1", "Alice"),
            ("e2", "Alice"),
            ("e3", "Bob"),
            ("e4", "Carol"),
            ("e5", "")
        }));

        // dev-2 is Active == false, so e3 must remain in leftouter with empty Owner.
        var activeOnly = ExecuteAndGetNullable2Columns<string, string>(
            "EventId", "Owner", db, "Events | join kind=leftouter (Devices | where Active) on DeviceId");
        Assert.That(activeOnly, Is.EquivalentTo(new[]
        {
            ("e1", "Alice"),
            ("e2", "Alice"),
            ("e3", ""),
            ("e4", "Carol"),
            ("e5", "")
        }));
    }

    [Test]
    [Description("Right-side subquery with where, project, and extend computes renamed join keys and narrows emitted columns.")]
    public void RightSideSubquery_FilterProjectAndExtend()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        string query = """
            Events
            | join kind=inner (
                Devices
                | where Active and Version >= 2
                | extend Label = strcat(Owner, "@", Region)
                | project TargetDevice = DeviceId, Label
            ) on $left.DeviceId == $right.TargetDevice
            """;

        var rows = ExecuteJoin(db, query);
        AssertExactColumnOrder(rows, "EventId", "DeviceId", "Kind", "Bytes", "TargetDevice", "Label");

        var actual = rows.Select(r => ((string)r["EventId"]!, (string)r["Label"]!)).ToList();
        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("e1", "Alice@eastus"),
            ("e2", "Alice@eastus"),
            ("e4", "Carol@eastus")
        }));
    }

    [Test]
    [Description("Right-side summarize subquery enriches raw events with per-device aggregate metrics.")]
    public void RightSideSubquery_SummarizeAggregationEnrichment()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        string query = """
            Events
            | join kind=leftouter (
                Events
                | summarize DeviceEvents = count(), DeviceTotalBytes = sum(Bytes) by DeviceId
            ) on DeviceId
            | project EventId, Bytes, DeviceEvents, DeviceTotalBytes
            """;

        var actual = ExecuteAndGetNullable4Columns<string, long, long, long>(
            "EventId", "Bytes", "DeviceEvents", "DeviceTotalBytes", db, query);

        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("e1", 100L, 2L, 350L),
            ("e2", 250L, 2L, 350L),
            ("e3", 500L, 1L, 500L),
            ("e4",  50L, 1L,  50L),
            ("e5",  10L, 1L,  10L)
        }));
    }

    [Test]
    [Description("Right-side distinct subquery deduplicates right keys before a leftouter join so left rows are not multiplied.")]
    public void RightSideSubquery_DistinctPreventsRowMultiplication()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("Events").WithColumn<string>("EventId", false).WithColumn<string>("DeviceId", false)
                .WithRow("e1", "dev-1").WithRow("e2", "dev-2").Build(),
            new NullableTableBuilder("Owners").WithColumn<string>("DeviceId", false).WithColumn<string>("Team", false)
                .WithRow("dev-1", "Core").WithRow("dev-1", "Core").WithRow("dev-2", "Edge").Build());

        var actual = ExecuteAndGetNullable2Columns<string, string>(
            "EventId", "Team", db, "Events | join kind=leftouter (Owners | distinct DeviceId, Team) on DeviceId");
        Assert.That(actual, Is.EquivalentTo(new[] { ("e1", "Core"), ("e2", "Edge") }));
    }

    [Test]
    [Description("Right-side sort and take subquery joins only against the top-N devices.")]
    public void RightSideSubquery_SortAndTakeTopN()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        string query = """
            Events
            | join kind=inner (
                Devices
                | where Active
                | sort by Version desc, DeviceId asc
                | take 1
            ) on DeviceId
            | project EventId, Owner
            """;

        // Among active devices (dev-1 v3, dev-5 v3, dev-3 v2), sort by Version desc, DeviceId asc picks dev-1 first.
        var actual = ExecuteAndGetNullable2Columns<string, string>("EventId", "Owner", db, query);
        Assert.That(actual, Is.EquivalentTo(new[] { ("e1", "Alice"), ("e2", "Alice") }));
    }

    [Test]
    [Description("Right-side union subquery combines multiple lookup tables before joining.")]
    public void RightSideSubquery_UnionCombinesLookupSources()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("Events").WithColumn<string>("EventId", false).WithColumn<string>("DeviceId", false)
                .WithRow("e1", "d1").WithRow("e2", "d2").WithRow("e3", "d3").Build(),
            new NullableTableBuilder("PrimaryDevices").WithColumn<string>("DeviceId", false).WithColumn<string>("Pool", false)
                .WithRow("d1", "primary").Build(),
            new NullableTableBuilder("BackupDevices").WithColumn<string>("DeviceId", false).WithColumn<string>("Pool", false)
                .WithRow("d2", "backup").Build());

        var actual = ExecuteAndGetNullable2Columns<string, string>(
            "EventId", "Pool", db, "Events | join kind=leftouter (PrimaryDevices | union BackupDevices) on DeviceId");

        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("e1", "primary"),
            ("e2", "backup"),
            ("e3", "")
        }));
    }

    [Test]
    [Description("Right-side parse subquery extracts a join key from raw log text on the fly.")]
    public void RightSideSubquery_ParseExtractsJoinKey()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("Devices").WithColumn<string>("DeviceId", false).WithColumn<string>("Owner", false)
                .WithRow("dev-1", "Alice").WithRow("dev-2", "Bob").Build(),
            new NullableTableBuilder("RawLogs").WithColumn<string>("Line", false)
                .WithRow("host=dev-1 state=healthy")
                .WithRow("host=dev-2 state=degraded")
                .Build());

        string query = """
            Devices
            | join kind=inner (
                RawLogs
                | parse Line with "host=" DeviceId " state=" Health
                | project DeviceId, Health
            ) on DeviceId
            | project DeviceId, Owner, Health
            """;

        var actual = ExecuteAndGetNullable3Columns<string, string, string>("DeviceId", "Owner", "Health", db, query);
        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("dev-1", "Alice", "healthy"),
            ("dev-2", "Bob", "degraded")
        }));
    }

    [Test]
    [Description("Right-side print subquery and left-side print input both participate in joins.")]
    public void PrintSubquery_OnRightAndLeftSides_JoinsCorrectly()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        var rightPrint = ExecuteAndGetNullable2Columns<string, string>(
            "EventId", "Flag", db,
            "Events | join kind=inner (print DeviceId = 'dev-1', Flag = 'spotlight') on DeviceId");
        Assert.That(rightPrint, Is.EquivalentTo(new[] { ("e1", "spotlight"), ("e2", "spotlight") }));

        var leftPrint = ExecuteAndGetNullable2Columns<string, string>(
            "DeviceId", "Owner", db,
            "print DeviceId = 'dev-3' | join kind=inner Devices on DeviceId");
        Assert.That(leftPrint, Is.EquivalentTo(new[] { ("dev-3", "Carol") }));
    }

    [Test]
    [Description("Nested parenthesized join on the right side (Events | join (Devices | join Regions on Region) on DeviceId) evaluates inner join first and joins result cleanly.")]
    public void NestedJoinInsideRightSubquery_EvaluatesInnerJoinThenOuterJoin()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        string query = """
            Events
            | join kind=leftouter (
                Devices
                | where Active
                | join kind=inner Regions on Region
                | project DeviceId, Owner, Geo
            ) on DeviceId
            | project EventId, Owner, Geo
            """;

        var actual = ExecuteAndGetNullable3Columns<string, string, string>("EventId", "Owner", "Geo", db, query);
        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("e1", "Alice", "US"),
            ("e2", "Alice", "US"),
            ("e3", "",      ""),
            ("e4", "Carol", "US"),
            ("e5", "",      "")
        }));
    }

    #endregion

    #region let bindings, views, tabular functions, and materialize

    [Test]
    [Description("let-bound tabular variables work on the right side, on the left side, and on both sides of a join.")]
    public void LetBoundTables_WorkOnLeftRightAndBothSides()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        string rightLet = """
            let ActiveDevices = Devices | where Active;
            Events | join kind=leftouter ActiveDevices on DeviceId | project EventId, Owner
            """;
        Assert.That(
            ExecuteAndGetNullable2Columns<string, string>("EventId", "Owner", db, rightLet),
            Is.EquivalentTo(new[] { ("e1", "Alice"), ("e2", "Alice"), ("e3", ""), ("e4", "Carol"), ("e5", "") }));

        string bothLet = """
            let HttpEvents = Events | where Kind == "Http";
            let ActiveDevices = Devices | where Active;
            HttpEvents | join kind=inner ActiveDevices on DeviceId | project EventId, Owner
            """;
        Assert.That(
            ExecuteAndGetNullable2Columns<string, string>("EventId", "Owner", db, bothLet),
            Is.EquivalentTo(new[] { ("e1", "Alice"), ("e2", "Alice"), ("e4", "Carol") }));
    }

    [Test]
    [Description("let scalar variables can parameterize filtering inside a right-side join subquery.")]
    public void LetScalarVariable_UsedInsideRightJoinSubquery()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        string query = """
            let minVersion = 3;
            Events
            | join kind=inner (Devices | where Active and Version >= minVersion) on DeviceId
            | project EventId, Owner
            """;

        var actual = ExecuteAndGetNullable2Columns<string, string>("EventId", "Owner", db, query);
        Assert.That(actual, Is.EquivalentTo(new[] { ("e1", "Alice"), ("e2", "Alice") }));
    }

    [Test]
    [Description("let view() and let tabular functions can serve as join inputs, and materialize() supports reusing a filtered result across both sides of a self-join.")]
    public void LetViewTabularFunctionAndMaterialize_WorkAsJoinInputs()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        string viewQuery = """
            let ActiveView = view () { Devices | where Active };
            Events | join kind=leftsemi ActiveView on DeviceId | project EventId
            """;
        Assert.That(
            ExecuteAndGetNullableColumn<string>("EventId", db, viewQuery),
            Is.EquivalentTo(new[] { "e1", "e2", "e4" }));

        string udfQuery = """
            let DevicesByRegion = (targetRegion:string) { Devices | where Region == targetRegion };
            Events | join kind=inner (DevicesByRegion("eastus")) on DeviceId | project EventId, Owner
            """;
        Assert.That(
            ExecuteAndGetNullable2Columns<string, string>("EventId", "Owner", db, udfQuery),
            Is.EquivalentTo(new[] { ("e1", "Alice"), ("e2", "Alice"), ("e4", "Carol") }));

        string materializeQuery = """
            let EastEvents = materialize(Events | where Kind == "Http");
            EastEvents
            | join kind=inner (EastEvents | summarize TotalBytes = sum(Bytes) by DeviceId) on DeviceId
            | project EventId, TotalBytes
            """;
        Assert.That(
            ExecuteAndGetNullable2Columns<string, long>("EventId", "TotalBytes", db, materializeQuery),
            Is.EquivalentTo(new[] { ("e1", 350L), ("e2", 350L), ("e4", 50L) }));
    }

    #endregion

    #region Chained joins, self-joins, and downstream pipelines

    [Test]
    [Description("Join results pipe cleanly into another join, project, extend, summarize, sort, take, distinct, and count.")]
    public void ChainedMultiTableJoins_PipeIntoSummarizeSortAndTake()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        string query = """
            Events
            | join kind=leftouter (Devices | where Active) on DeviceId
            | join kind=leftouter Regions on Region
            | extend EffectiveGeo = iff(isempty(Geo), "UNKNOWN", Geo)
            | summarize EventCount = count(), TotalBytes = sum(Bytes) by EffectiveGeo
            | sort by TotalBytes desc, EffectiveGeo asc
            """;

        var actual = ExecuteAndGetNullable3Columns<string, long, long>(
            "EffectiveGeo", "EventCount", "TotalBytes", db, query);

        Assert.That(actual, Is.EqualTo(new List<(string, long, long)>
        {
            ("UNKNOWN", 2L, 510L),
            ("US",      3L, 400L)
        }));
    }

    [Test]
    [Description("Chaining leftouter enrichment followed by leftanti or leftsemi composes cleanly.")]
    public void ChainedOuterFollowedBySemiAndAnti_ComposesCorrectly()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        string outerThenAnti = """
            Events
            | join kind=leftouter Devices on DeviceId
            | join kind=leftanti (Regions | where Geo == "US") on Region
            | project EventId, DeviceId
            """;
        // e1, e2, e3, e4 have US regions (eastus/westus); e5 has empty Region so it is unmatched by US regions.
        Assert.That(
            ExecuteAndGetNullable2Columns<string, string>("EventId", "DeviceId", db, outerThenAnti),
            Is.EquivalentTo(new[] { ("e5", "dev-4") }));

        string outerThenSemi = """
            Events
            | join kind=leftouter Devices on DeviceId
            | join kind=leftsemi (Regions | where Geo == "US") on Region
            | distinct DeviceId
            | sort by DeviceId asc
            """;
        Assert.That(
            ExecuteAndGetNullableColumn<string>("DeviceId", db, outerThenSemi),
            Is.EqualTo(new[] { "dev-1", "dev-2", "dev-3" }));
    }

    [Test]
    [Description("Self-join pairs Start and Stop activity rows to compute elapsed duration, and parent-child self-join resolves parent names and roots.")]
    public void SelfJoins_ActivityDurationPairingAndParentChildHierarchy()
    {
        DateTime t0 = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("ActivityLogs")
                .WithColumn<string>("SessionId", false)
                .WithColumn<string>("Phase", false)
                .WithColumn<DateTime>("Timestamp", false)
                .WithRow("s1", "Start", t0)
                .WithRow("s1", "Stop",  t0.AddMinutes(5))
                .WithRow("s2", "Start", t0.AddMinutes(10))
                .WithRow("s2", "Stop",  t0.AddMinutes(12))
                .WithRow("s3", "Start", t0.AddMinutes(20))
                .Build(),
            new NullableTableBuilder("OrgNodes")
                .WithColumn<int>("Id", false)
                .WithColumn<int>("ParentId", true)
                .WithColumn<string>("Name", false)
                .WithRow(1, null, "Root")
                .WithRow(2, 1, "Platform")
                .WithRow(3, 2, "Storage")
                .Build());

        string durationQuery = """
            ActivityLogs
            | where Phase == "Start"
            | join kind=inner (ActivityLogs | where Phase == "Stop") on SessionId
            | project SessionId, Duration = Timestamp1 - Timestamp
            """;
        var durations = ExecuteAndGetNullable2Columns<string, TimeSpan>("SessionId", "Duration", db, durationQuery);
        Assert.That(durations, Is.EquivalentTo(new[]
        {
            ("s1", TimeSpan.FromMinutes(5)),
            ("s2", TimeSpan.FromMinutes(2))
        }));

        string hierarchyQuery = """
            OrgNodes
            | join kind=leftouter OrgNodes on $left.ParentId == $right.Id
            | project NodeName = Name, ParentName = Name1
            """;
        var hierarchy = ExecuteAndGetNullable2Columns<string, string>("NodeName", "ParentName", db, hierarchyQuery);
        Assert.That(hierarchy, Is.EquivalentTo(new[]
        {
            ("Root", ""),
            ("Platform", "Root"),
            ("Storage", "Platform")
        }));

        // Leaf nodes: nodes whose Id never appears as another node's ParentId.
        string leafQuery = """
            OrgNodes
            | join kind=leftanti OrgNodes on $left.Id == $right.ParentId
            | project Name
            """;
        Assert.That(ExecuteAndGetNullableColumn<string>("Name", db, leafQuery), Is.EquivalentTo(new[] { "Storage" }));
    }

    #endregion

    #region CSV/JSON table ingestion and database immutability

    [Test]
    [Description("Tables ingested via TableBuilderFromCsv and TableBuilderFromJson join seamlessly in KustoDatabase.")]
    public void CsvAndJsonIngestedTables_JoinCorrectlyInDatabase()
    {
        Table csvEvents = TableBuilderFromCsv.Build(new TableDef
        {
            Name = "CsvEvents",
            Columns = [new ColumnDef { Name = "DeviceId" }, new ColumnDef { Name = "ErrorCode" }],
            Rows =
            [
                new Dictionary<string, object?> { ["DeviceId"] = "dev-1", ["ErrorCode"] = "500" },
                new Dictionary<string, object?> { ["DeviceId"] = "dev-2", ["ErrorCode"] = "404" },
                new Dictionary<string, object?> { ["DeviceId"] = "dev-9", ["ErrorCode"] = "503" }
            ]
        });

        string jsonInventory = """
            {
              "Name": "JsonDevices",
              "Columns": [
                { "Name": "Id", "Type": "string", "Nullable": false },
                { "Name": "Owner", "Type": "string", "Nullable": false },
                { "Name": "Active", "Type": "bool", "Nullable": false }
              ],
              "Rows": [
                { "Id": "dev-1", "Owner": "Alice", "Active": true },
                { "Id": "dev-2", "Owner": "Bob", "Active": false }
              ]
            }
            """;
        TableDef? tableDef = JsonSerializer.Deserialize<TableDef>(jsonInventory);
        Assert.That(tableDef, Is.Not.Null);
        Table jsonDevices = TableBuilderFromJson.Build(tableDef!);

        KustoDatabase db = CreateDatabase(csvEvents, jsonDevices);

        string query = """
            CsvEvents
            | join kind=leftouter (JsonDevices | where Active) on $left.DeviceId == $right.Id
            | project DeviceId, ErrorCode, Owner
            """;

        var actual = ExecuteAndGetNullable3Columns<string, int, string>("DeviceId", "ErrorCode", "Owner", db, query);
        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("dev-1", 500, "Alice"),
            ("dev-2", 404, ""),
            ("dev-9", 503, "")
        }));
    }

    [Test]
    [Description("Executing multiple joins (inner, leftouter, fullouter, anti) on the same database never mutates stored table rows or schemas.")]
    public void RepeatedJoinsOnSameDatabase_DoNotMutateStoredTables()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        ExecuteJoin(db, "Events | join kind=fullouter Devices on DeviceId");
        ExecuteJoin(db, "Events | join kind=leftanti Devices on DeviceId");
        ExecuteJoin(db, "Events | join Devices on DeviceId");

        Assert.That(ExecuteJoin(db, "Events"), Has.Count.EqualTo(5));
        Assert.That(ExecuteJoin(db, "Devices"), Has.Count.EqualTo(4));

        var recheck = ExecuteAndGetNullable2Columns<string, string>(
            "EventId", "Owner", db, "Events | join kind=inner Devices on DeviceId");
        Assert.That(recheck, Is.EquivalentTo(new[]
        {
            ("e1", "Alice"),
            ("e2", "Alice"),
            ("e3", "Bob"),
            ("e4", "Carol")
        }));
    }

    [Test]
    [Description("Binding a join result to a let variable for subsequent joins and combining two joins via union both compose cleanly.")]
    public void JoinResultBoundToLetAndCombinedWithUnion_ComposesCleanly()
    {
        KustoDatabase db = CreateTelemetryDatabase();

        string letJoinQuery = """
            let Enriched = Events | join kind=leftouter Devices on DeviceId | project EventId, DeviceId, Owner, Kind;
            let OwnerRegions = Devices | distinct Owner, Region;
            Enriched
            | join kind=leftouter OwnerRegions on Owner
            | project EventId, Owner, Region
            """;

        var letActual = ExecuteAndGetNullable3Columns<string, string, string>("EventId", "Owner", "Region", db, letJoinQuery);
        Assert.That(letActual, Is.EquivalentTo(new[]
        {
            ("e1", "Alice", "eastus"),
            ("e2", "Alice", "eastus"),
            ("e3", "Bob", "westus"),
            ("e4", "Carol", "eastus"),
            ("e5", "", "")
        }));

        string unionOfJoinsQuery = """
            union
                (Events | where Kind == "Http" | join kind=inner Devices on DeviceId | project EventId, Owner),
                (Events | join kind=leftanti Devices on DeviceId | extend Owner = "UNREGISTERED" | project EventId, Owner)
            """;

        var unionActual = ExecuteAndGetNullable2Columns<string, string>("EventId", "Owner", db, unionOfJoinsQuery);
        Assert.That(unionActual, Is.EquivalentTo(new[]
        {
            ("e1", "Alice"),
            ("e2", "Alice"),
            ("e4", "Carol"),
            ("e5", "UNREGISTERED")
        }));
    }

    #endregion
}
