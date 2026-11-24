using System.Globalization;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Shared scenario tables and helpers for the join operator tests (JoinOperatorOpus*Tests).
/// - the scenario tables model application monitoring (Requests, Traces), a device fleet (Events, Devices, Inventory,
///   Heartbeats, two inventory snapshots, devices of several tenants) and a few small tables for the key rules
///   (user names that differ in case, keys with different names, JSON payloads);
/// - every table is built with <see cref="SummarizeOperatorOpusTableBuilder"/>, so the input rows are shuffled and a
///   test cannot depend on the input order;
/// - a result row is compared as the text of some of its columns (<see cref="FormatRow"/>), and a result schema as
///   "Name:Type" texts (<see cref="ExecuteAndGetSchemaTexts"/>).
/// </summary>
internal static class JoinOperatorOpusTestUtils
{
    #region helpers

    internal static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Executes the query and returns every result row as the values of the given columns joined by ':'
    /// (see <see cref="FormatRow"/>).
    /// </summary>
    internal static List<string> ExecuteAndGetRowTexts(KustoDatabase kustoDatabase, string query,
        params string[] columnNames)
    {
        return ExecuteAndGetRows(kustoDatabase, query).Select(row => FormatRow(row, columnNames)).ToList();
    }

    /// <summary>
    /// Executes the query and returns every result row as the values of all its columns, in the result column order,
    /// joined by ':' (see <see cref="FormatRow"/>). Used for the semi and anti joins, which return the columns of one
    /// side only.
    /// </summary>
    internal static List<string> ExecuteAndGetWholeRowTexts(KustoDatabase kustoDatabase, string query)
    {
        return ExecuteAndGetRows(kustoDatabase, query).Select(row => FormatRow(row, row.Keys.ToArray())).ToList();
    }

    /// <summary>
    /// Formats the values of the given columns joined by ':', like strcat(EventId, ':', Model) in Kusto:
    /// null and the empty string are both empty (a missing string is "" in Kusto), a datetime is yyyy-MM-dd HH:mm,
    /// a bool is True or False and the numbers use the invariant culture.
    /// For example the row (4, null) of the columns EventId, Model is "4:".
    /// </summary>
    internal static string FormatRow(IReadOnlyDictionary<string, object?> row, params string[] columnNames)
    {
        return string.Join(":", columnNames.Select(columnName => FormatJoinCell(row, columnName)));
    }

    /// <summary>
    /// Executes the query followed by getschema and returns its columns in order as "Name:Type" with the Kusto type
    /// names (long, int, real, decimal, string, bool, datetime, timespan, guid, dynamic), for example "DeviceId1:string".
    /// </summary>
    internal static List<string> ExecuteAndGetSchemaTexts(KustoDatabase kustoDatabase, string query)
    {
        return ExecuteAndGetRows(kustoDatabase, query + " | getschema")
            .OrderBy(row => GetValue<int>(row, "ColumnOrdinal"))
            .Select(row => $"{GetValue<string>(row, "ColumnName")}:{GetValue<string>(row, "ColumnType")}")
            .ToList();
    }

    /// <summary>
    /// Returns the rows whose column equals the value (compared with Equals, so a long value must be passed as 4L).
    /// </summary>
    internal static List<IReadOnlyDictionary<string, object?>> GetRowsWhere(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        string columnName,
        object? expectedValue)
    {
        return rows.Where(row => row.TryGetValue(columnName, out object? value) && Equals(value, expectedValue))
            .ToList();
    }

    /// <summary>
    /// Asserts the innerunique result of one join key: the rows hold a single left row, which may be any of the
    /// candidates because Kusto does not guarantee which of the duplicate left rows survives, and that left row is
    /// paired with every matching right row. The left and the right rows are compared as texts of the given columns
    /// (see <see cref="FormatRow"/>).
    /// </summary>
    internal static void AssertOneLeftRowPairedWithEveryRightRow(
        IReadOnlyCollection<IReadOnlyDictionary<string, object?>> rowsOfOneKey,
        string[] leftColumns,
        string[] candidateLeftRows,
        string[] rightColumns,
        string[] expectedRightRows)
    {
        List<string> leftRows = rowsOfOneKey.Select(row => FormatRow(row, leftColumns)).Distinct().ToList();
        Assert.That(leftRows, Has.Count.EqualTo(1),
            $"innerunique keeps one left row per key, got [{string.Join(", ", leftRows)}]");
        Assert.That(leftRows[0], Is.AnyOf(candidateLeftRows), "the surviving left row is one of the input rows");
        Assert.That(rowsOfOneKey.Select(row => FormatRow(row, rightColumns)), Is.EquivalentTo(expectedRightRows),
            "the surviving left row is paired with every matching right row");
    }

    private static string FormatJoinCell(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        Assert.That(row.Keys, Does.Contain(columnName), $"column '{columnName}' is missing");
        return row[columnName] switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            object value => value.ToString() ?? string.Empty
        };
    }

    #endregion

    #region scenario tables

    /// <summary>
    /// Requests(OperationId: string, Name: string, DurationMs: long): op1 has two requests, op3 has no trace.
    /// op1 "GET /home" 120, op1 "GET /home (retry)" 80, op2 "POST /cart" 300, op3 "GET /about" 50.
    /// </summary>
    internal static Table BuildRequestsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Requests")
            .WithColumn<string>("OperationId")
            .WithColumn<string>("Name")
            .WithColumn<long>("DurationMs")
            .WithRow("op1", "GET /home", 120L)
            .WithRow("op1", "GET /home (retry)", 80L)
            .WithRow("op2", "POST /cart", 300L)
            .WithRow("op3", "GET /about", 50L)
            .Build();
    }

    /// <summary>
    /// Traces(OperationId: string, Message: string, Level: string): op1 has three traces, op4 has no request.
    /// op1 start Info, op1 "query db" Verbose, op1 end Info, op2 checkout Warning, op4 "orphan trace" Error.
    /// </summary>
    internal static Table BuildTracesTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Traces")
            .WithColumn<string>("OperationId")
            .WithColumn<string>("Message")
            .WithColumn<string>("Level")
            .WithRow("op1", "start", "Info")
            .WithRow("op1", "query db", "Verbose")
            .WithRow("op1", "end", "Info")
            .WithRow("op2", "checkout", "Warning")
            .WithRow("op4", "orphan trace", "Error")
            .Build();
    }

    /// <summary>
    /// Events(EventId: long, DeviceId: string, EventType: string, Timestamp: datetime):
    /// 1 d1 boot 2024-01-01 08:00, 2 d1 crash 2024-01-01 09:00, 3 d2 boot 2024-01-01 10:00,
    /// 4 d3 boot 2024-01-02 08:00, 5 d2 shutdown 2024-01-02 09:00. The device d3 is unknown to Devices.
    /// </summary>
    internal static Table BuildEventsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Events")
            .WithColumn<long>("EventId")
            .WithColumn<string>("DeviceId")
            .WithColumn<string>("EventType")
            .WithColumn<DateTime>("Timestamp")
            .WithRow(1L, "d1", "boot", Utc(2024, 1, 1, 8))
            .WithRow(2L, "d1", "crash", Utc(2024, 1, 1, 9))
            .WithRow(3L, "d2", "boot", Utc(2024, 1, 1, 10))
            .WithRow(4L, "d3", "boot", Utc(2024, 1, 2, 8))
            .WithRow(5L, "d2", "shutdown", Utc(2024, 1, 2, 9))
            .Build();
    }

    /// <summary>
    /// Devices(DeviceId: string, Model: string, Active: bool): d1 Surface true, d2 Xbox false, d4 HoloLens true.
    /// The device d4 has no event.
    /// </summary>
    internal static Table BuildDevicesTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Devices")
            .WithColumn<string>("DeviceId")
            .WithColumn<string>("Model")
            .WithColumn<bool>("Active")
            .WithRow("d1", "Surface", true)
            .WithRow("d2", "Xbox", false)
            .WithRow("d4", "HoloLens", true)
            .Build();
    }

    /// <summary>
    /// Inventory(DeviceId: string, Location: string): d1 Seattle, d1 Redmond, d2 Dublin, d4 Austin.
    /// The device d1 is listed twice.
    /// </summary>
    internal static Table BuildInventoryTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Inventory")
            .WithColumn<string>("DeviceId")
            .WithColumn<string>("Location")
            .WithRow("d1", "Seattle")
            .WithRow("d1", "Redmond")
            .WithRow("d2", "Dublin")
            .WithRow("d4", "Austin")
            .Build();
    }

    /// <summary>
    /// Heartbeats(DeviceId: string, Timestamp: datetime):
    /// d1 2024-01-01 08:00, d1 2024-01-01 08:05, d2 2024-01-01 08:00, d9 2024-01-01 08:00.
    /// The device d4 sent no heartbeat and the device d9 is unknown to Devices.
    /// </summary>
    internal static Table BuildHeartbeatsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Heartbeats")
            .WithColumn<string>("DeviceId")
            .WithColumn<DateTime>("Timestamp")
            .WithRow("d1", Utc(2024, 1, 1, 8))
            .WithRow("d1", Utc(2024, 1, 1, 8, 5))
            .WithRow("d2", Utc(2024, 1, 1, 8))
            .WithRow("d9", Utc(2024, 1, 1, 8))
            .Build();
    }

    /// <summary>
    /// InventoryYesterday(DeviceId: string, Version: string): d1 1.0, d2 1.0, d3 2.0.
    /// </summary>
    internal static Table BuildInventoryYesterdayTable()
    {
        return new SummarizeOperatorOpusTableBuilder("InventoryYesterday")
            .WithColumn<string>("DeviceId")
            .WithColumn<string>("Version")
            .WithRow("d1", "1.0")
            .WithRow("d2", "1.0")
            .WithRow("d3", "2.0")
            .Build();
    }

    /// <summary>
    /// InventoryToday(DeviceId: string, Version: string): d1 1.0, d2 1.1, d5 1.0.
    /// Compared with yesterday: d1 is unchanged, d2 is upgraded, d3 is removed and d5 is added.
    /// </summary>
    internal static Table BuildInventoryTodayTable()
    {
        return new SummarizeOperatorOpusTableBuilder("InventoryToday")
            .WithColumn<string>("DeviceId")
            .WithColumn<string>("Version")
            .WithRow("d1", "1.0")
            .WithRow("d2", "1.1")
            .WithRow("d5", "1.0")
            .Build();
    }

    /// <summary>
    /// TenantDevices(TenantId: string, DeviceId: string, Owner: string): t1 d1 alice, t2 d1 bob, t1 d2 carol.
    /// The device id d1 exists in two tenants.
    /// </summary>
    internal static Table BuildTenantDevicesTable()
    {
        return BuildTenantDeviceRows("TenantDevices", "DeviceId");
    }

    /// <summary>
    /// Registry(TenantId: string, Id: string, Owner: string): the rows of TenantDevices with the device column
    /// named Id, for the multi-key joins on columns with different names.
    /// </summary>
    internal static Table BuildRegistryTable()
    {
        return BuildTenantDeviceRows("Registry", "Id");
    }

    /// <summary>
    /// TenantEvents(TenantId: string, DeviceId: string, EventType: string):
    /// t1 d1 boot, t2 d1 crash, t2 d2 boot, t3 d1 boot. Only (t1, d1) and (t2, d1) are devices of TenantDevices.
    /// </summary>
    internal static Table BuildTenantEventsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("TenantEvents")
            .WithColumn<string>("TenantId")
            .WithColumn<string>("DeviceId")
            .WithColumn<string>("EventType")
            .WithRow("t1", "d1", "boot")
            .WithRow("t2", "d1", "crash")
            .WithRow("t2", "d2", "boot")
            .WithRow("t3", "d1", "boot")
            .Build();
    }

    /// <summary>
    /// ModelCatalog(Model: string, Category: string): Surface laptop, Xbox console, HoloLens headset.
    /// </summary>
    internal static Table BuildModelCatalogTable()
    {
        return new SummarizeOperatorOpusTableBuilder("ModelCatalog")
            .WithColumn<string>("Model")
            .WithColumn<string>("Category")
            .WithRow("Surface", "laptop")
            .WithRow("Xbox", "console")
            .WithRow("HoloLens", "headset")
            .Build();
    }

    /// <summary>
    /// ArchivedDevices(DeviceId: string, Model: string, Active: bool): d3 Zune false (the schema of Devices).
    /// </summary>
    internal static Table BuildArchivedDevicesTable()
    {
        return new SummarizeOperatorOpusTableBuilder("ArchivedDevices")
            .WithColumn<string>("DeviceId")
            .WithColumn<string>("Model")
            .WithColumn<bool>("Active")
            .WithRow("d3", "Zune", false)
            .Build();
    }

    /// <summary>
    /// DeviceStatus(DeviceId: string, Timestamp: datetime, Status: string):
    /// d1 2024-01-01 08:00 online, d1 2024-01-01 12:00 offline, d2 2024-01-01 09:00 online.
    /// </summary>
    internal static Table BuildDeviceStatusTable()
    {
        return new SummarizeOperatorOpusTableBuilder("DeviceStatus")
            .WithColumn<string>("DeviceId")
            .WithColumn<DateTime>("Timestamp")
            .WithColumn<string>("Status")
            .WithRow("d1", Utc(2024, 1, 1, 8), "online")
            .WithRow("d1", Utc(2024, 1, 1, 12), "offline")
            .WithRow("d2", Utc(2024, 1, 1, 9), "online")
            .Build();
    }

    /// <summary>
    /// DailyQuota(Day: datetime, Quota: long): 2024-01-01 10, 2024-01-02 20.
    /// </summary>
    internal static Table BuildDailyQuotaTable()
    {
        return new SummarizeOperatorOpusTableBuilder("DailyQuota")
            .WithColumn<DateTime>("Day")
            .WithColumn<long>("Quota")
            .WithRow(Utc(2024, 1, 1), 10L)
            .WithRow(Utc(2024, 1, 2), 20L)
            .Build();
    }

    /// <summary>
    /// Users(UserName: string, Team: string): alice red, Alice blue, ALICE green, bob red, "carol " red
    /// (with a trailing space).
    /// </summary>
    internal static Table BuildUsersTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Users")
            .WithColumn<string>("UserName")
            .WithColumn<string>("Team")
            .WithRow("alice", "red")
            .WithRow("Alice", "blue")
            .WithRow("ALICE", "green")
            .WithRow("bob", "red")
            .WithRow("carol ", "red")
            .Build();
    }

    /// <summary>
    /// Logins(UserName: string, LoginCount: long): alice 1, BOB 2, carol 3.
    /// </summary>
    internal static Table BuildLoginsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Logins")
            .WithColumn<string>("UserName")
            .WithColumn<long>("LoginCount")
            .WithRow("alice", 1L)
            .WithRow("BOB", 2L)
            .WithRow("carol", 3L)
            .Build();
    }

    /// <summary>
    /// Tickets(Id: long, DeviceId: string): 100 d1, 101 d2, 102 d9.
    /// </summary>
    internal static Table BuildTicketsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Tickets")
            .WithColumn<long>("Id")
            .WithColumn<string>("DeviceId")
            .WithRow(100L, "d1")
            .WithRow(101L, "d2")
            .WithRow(102L, "d9")
            .Build();
    }

    /// <summary>
    /// Assets(Id: string, Owner: string): d1 alice, d2 bob, d7 carol. The device id is in the column Id.
    /// </summary>
    internal static Table BuildAssetsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Assets")
            .WithColumn<string>("Id")
            .WithColumn<string>("Owner")
            .WithRow("d1", "alice")
            .WithRow("d2", "bob")
            .WithRow("d7", "carol")
            .Build();
    }

    /// <summary>
    /// Flags(Active: bool, Label: string): true on, false off.
    /// </summary>
    internal static Table BuildFlagsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Flags")
            .WithColumn<bool>("Active")
            .WithColumn<string>("Label")
            .WithRow(true, "on")
            .WithRow(false, "off")
            .Build();
    }

    /// <summary>
    /// Telemetry(TelemetryId: long, Payload: string) with JSON payloads:
    /// 1 {"device":"d1","event":1}, 2 {"device":"d2","event":3}, 3 {"device":"d9","event":9}.
    /// </summary>
    internal static Table BuildTelemetryTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Telemetry")
            .WithColumn<long>("TelemetryId")
            .WithColumn<string>("Payload")
            .WithRow(1L, "{\"device\":\"d1\",\"event\":1}")
            .WithRow(2L, "{\"device\":\"d2\",\"event\":3}")
            .WithRow(3L, "{\"device\":\"d9\",\"event\":9}")
            .Build();
    }

    /// <summary>
    /// The scenario database with every scenario table: Requests, Traces, Events, Devices, Inventory, Heartbeats,
    /// InventoryYesterday, InventoryToday, TenantDevices, Registry, TenantEvents, ModelCatalog, ArchivedDevices,
    /// DeviceStatus, DailyQuota, Users, Logins, Tickets, Assets, Flags and Telemetry, plus the extra tables of a test.
    /// </summary>
    internal static KustoDatabase CreateScenarioDatabase(params Table[] extraTables)
    {
        Table[] scenarioTables =
        {
            BuildRequestsTable(),
            BuildTracesTable(),
            BuildEventsTable(),
            BuildDevicesTable(),
            BuildInventoryTable(),
            BuildHeartbeatsTable(),
            BuildInventoryYesterdayTable(),
            BuildInventoryTodayTable(),
            BuildTenantDevicesTable(),
            BuildRegistryTable(),
            BuildTenantEventsTable(),
            BuildModelCatalogTable(),
            BuildArchivedDevicesTable(),
            BuildDeviceStatusTable(),
            BuildDailyQuotaTable(),
            BuildUsersTable(),
            BuildLoginsTable(),
            BuildTicketsTable(),
            BuildAssetsTable(),
            BuildFlagsTable(),
            BuildTelemetryTable()
        };

        return CreateDatabase(scenarioTables.Concat(extraTables).ToArray());
    }

    private static Table BuildTenantDeviceRows(string tableName, string deviceColumnName)
    {
        return new SummarizeOperatorOpusTableBuilder(tableName)
            .WithColumn<string>("TenantId")
            .WithColumn<string>(deviceColumnName)
            .WithColumn<string>("Owner")
            .WithRow("t1", "d1", "alice")
            .WithRow("t2", "d1", "bob")
            .WithRow("t1", "d2", "carol")
            .Build();
    }

    #endregion

    #region kind matrix, generated and duplicate rows tables

    /// <summary>
    /// The database of the join kind matrix:
    /// L(LeftId: long, Key: string) = 1 a, 2 a, 3 b, 4 c and R(Key: string, RightId: long) = a 10, a 20, a 30, b 40, d 50.
    /// The key a is repeated on both sides (2 x 3 pairs), b matches once, c is only on the left and d only on the right.
    /// </summary>
    internal static KustoDatabase CreateKindMatrixDatabase()
    {
        Table left = new SummarizeOperatorOpusTableBuilder("L")
            .WithColumn<long>("LeftId")
            .WithColumn<string>("Key")
            .WithRow(1L, "a")
            .WithRow(2L, "a")
            .WithRow(3L, "b")
            .WithRow(4L, "c")
            .Build();
        Table right = new SummarizeOperatorOpusTableBuilder("R")
            .WithColumn<string>("Key")
            .WithColumn<long>("RightId")
            .WithRow("a", 10L)
            .WithRow("a", 20L)
            .WithRow("a", 30L)
            .WithRow("b", 40L)
            .WithRow("d", 50L)
            .Build();

        return CreateDatabase(left, right);
    }

    /// <summary>
    /// The database of the generated tables:
    /// BigLeft(LeftId: long, Key: long) with the ids 0..99 and Key = LeftId % 10 (10 rows per key 0..9) and
    /// BigRight(RightId: long, Key: long) with the ids 0..49 and Key = RightId % 5 (10 rows per key 0..4).
    /// </summary>
    internal static KustoDatabase CreateGeneratedDatabase()
    {
        SummarizeOperatorOpusTableBuilder bigLeft = new SummarizeOperatorOpusTableBuilder("BigLeft")
            .WithColumn<long>("LeftId")
            .WithColumn<long>("Key");
        for (long leftId = 0; leftId < 100; leftId++)
        {
            bigLeft.WithRow(leftId, leftId % 10);
        }

        SummarizeOperatorOpusTableBuilder bigRight = new SummarizeOperatorOpusTableBuilder("BigRight")
            .WithColumn<long>("RightId")
            .WithColumn<long>("Key");
        for (long rightId = 0; rightId < 50; rightId++)
        {
            bigRight.WithRow(rightId, rightId % 5);
        }

        return CreateDatabase(bigLeft.Build(), bigRight.Build());
    }

    /// <summary>
    /// The database of the duplicate rows:
    /// Dup(K: string, V: string) = (k, x) twice, DupR(K: string, W: long) = (k, 1), (k, 2),
    /// TL(TenantId: string, DeviceId: string, Note: string) = t1 d1 a, t1 d1 b, t2 d1 c and
    /// TR(TenantId: string, DeviceId: string, Owner: string) = t1 d1 X, t2 d1 Y, t2 d1 Z.
    /// </summary>
    internal static KustoDatabase CreateDuplicateRowsDatabase()
    {
        return CreateDatabase(
            new SummarizeOperatorOpusTableBuilder("Dup")
                .WithColumn<string>("K")
                .WithColumn<string>("V")
                .WithRow("k", "x")
                .WithRow("k", "x")
                .Build(),
            new SummarizeOperatorOpusTableBuilder("DupR")
                .WithColumn<string>("K")
                .WithColumn<long>("W")
                .WithRow("k", 1L)
                .WithRow("k", 2L)
                .Build(),
            new SummarizeOperatorOpusTableBuilder("TL")
                .WithColumn<string>("TenantId")
                .WithColumn<string>("DeviceId")
                .WithColumn<string>("Note")
                .WithRow("t1", "d1", "a")
                .WithRow("t1", "d1", "b")
                .WithRow("t2", "d1", "c")
                .Build(),
            new SummarizeOperatorOpusTableBuilder("TR")
                .WithColumn<string>("TenantId")
                .WithColumn<string>("DeviceId")
                .WithColumn<string>("Owner")
                .WithRow("t1", "d1", "X")
                .WithRow("t2", "d1", "Y")
                .WithRow("t2", "d1", "Z")
                .Build());
    }

    #endregion
}
