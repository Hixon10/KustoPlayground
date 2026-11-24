using static KustoPlayground.Core.Tests.JoinOperatorOpusTestUtils;
using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// The outer joins (kind=leftouter, rightouter and fullouter), as in Azure Data Explorer.
/// - leftouter returns every matching pair (no deduplication of the left side) plus each unmatched left row once,
///   so no event is lost: an event of an unknown device gives one row, an event of a device with two inventory rows
///   gives two rows;
/// - rightouter is the mirror of leftouter, and fullouter returns the matching pairs plus the unmatched rows of both
///   sides;
/// - the missing side of an unmatched row is padded: a string column is empty ("", isnull false, isempty true) and a
///   column of any other type (int, long, real, decimal, bool, datetime, timespan, guid, dynamic) is null;
/// - the matched values keep their type, and the result of an outer join pipes into where, extend, project, summarize
///   and another join, where the padded values behave like any other empty string or null (coalesce, iff, case);
/// - typical uses: enrich events without dropping any, find the events of unknown devices, and reconcile two
///   inventory snapshots (added, removed, upgraded and unchanged devices).
/// The rows of a join come in any order, so the results are compared in any order unless the query sorts them.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class JoinOperatorOpusOuterKindsTests
{
    private static readonly Guid SerialIdOfD1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SerialIdOfD8 = new("22222222-2222-2222-2222-222222222222");

    private static readonly string[] DeviceSpecsValueColumns =
        { "Cores", "Weight", "Price", "Warranty", "SerialId", "Retired", "Released", "Storage" };

    // A right side with a dynamic column: Details = {"ram":16} for every device of DeviceSpecs.
    private const string DeviceSpecsWithDetails = "(DeviceSpecs | extend Details = parse_json('{\"ram\":16}'))";

    private static readonly string[] RequestAndTracePairs =
    {
        "op1:GET /home:op1:start", "op1:GET /home:op1:query db", "op1:GET /home:op1:end",
        "op1:GET /home (retry):op1:start", "op1:GET /home (retry):op1:query db", "op1:GET /home (retry):op1:end",
        "op2:POST /cart:op2:checkout"
    };

    /// <summary>
    /// DeviceSpecs(DeviceId: string, Vendor: string, Cores: int, Weight: real, Price: decimal, Warranty: timespan,
    /// SerialId: guid, Retired: bool, Released: datetime, Storage: long), one column per scalar type:
    /// d1 (the device of the events 1 and 2) Contoso, 8, 1.5, 999.99, 730 days, 1111..., false, 2023-06-01, 512 and
    /// d8 (no event) Fabrikam, 4, 2.25, 499.5, 365 days, 2222..., true, 2021-03-15, 256.
    /// </summary>
    private static Table BuildDeviceSpecsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("DeviceSpecs")
            .WithColumn<string>("DeviceId")
            .WithColumn<string>("Vendor")
            .WithColumn<int>("Cores")
            .WithColumn<double>("Weight")
            .WithColumn<decimal>("Price")
            .WithColumn<TimeSpan>("Warranty")
            .WithColumn<Guid>("SerialId")
            .WithColumn<bool>("Retired")
            .WithColumn<DateTime>("Released")
            .WithColumn<long>("Storage")
            .WithRow("d1", "Contoso", 8, 1.5, 999.99m, TimeSpan.FromDays(730), SerialIdOfD1, false, Utc(2023, 6, 1),
                512L)
            .WithRow("d8", "Fabrikam", 4, 2.25, 499.5m, TimeSpan.FromDays(365), SerialIdOfD8, true, Utc(2021, 3, 15),
                256L)
            .Build();
    }

    private static KustoDatabase CreateDatabaseWithDeviceSpecs()
    {
        return CreateScenarioDatabase(BuildDeviceSpecsTable());
    }

    private static void AssertSpecsOfD1(IReadOnlyDictionary<string, object?> row, string deviceIdColumn)
    {
        Assert.That(GetValue<string>(row, deviceIdColumn), Is.EqualTo("d1"));
        Assert.That(GetValue<string>(row, "Vendor"), Is.EqualTo("Contoso"));
        Assert.That(GetValue<int>(row, "Cores"), Is.EqualTo(8));
        Assert.That(GetValue<double>(row, "Weight"), Is.EqualTo(1.5));
        Assert.That(GetValue<decimal>(row, "Price"), Is.EqualTo(999.99m));
        Assert.That(GetValue<TimeSpan>(row, "Warranty"), Is.EqualTo(TimeSpan.FromDays(730)));
        Assert.That(GetValue<Guid>(row, "SerialId"), Is.EqualTo(SerialIdOfD1));
        Assert.That(GetValue<bool>(row, "Retired"), Is.False);
        Assert.That(GetValue<DateTime>(row, "Released"), Is.EqualTo(Utc(2023, 6, 1)));
        Assert.That(GetValue<long>(row, "Storage"), Is.EqualTo(512L));
    }

    private static void AssertSpecsOfD8(IReadOnlyDictionary<string, object?> row, string deviceIdColumn)
    {
        Assert.That(GetValue<string>(row, deviceIdColumn), Is.EqualTo("d8"));
        Assert.That(GetValue<string>(row, "Vendor"), Is.EqualTo("Fabrikam"));
        Assert.That(GetValue<int>(row, "Cores"), Is.EqualTo(4));
        Assert.That(GetValue<double>(row, "Weight"), Is.EqualTo(2.25));
        Assert.That(GetValue<decimal>(row, "Price"), Is.EqualTo(499.5m));
        Assert.That(GetValue<TimeSpan>(row, "Warranty"), Is.EqualTo(TimeSpan.FromDays(365)));
        Assert.That(GetValue<Guid>(row, "SerialId"), Is.EqualTo(SerialIdOfD8));
        Assert.That(GetValue<bool>(row, "Retired"), Is.True);
        Assert.That(GetValue<DateTime>(row, "Released"), Is.EqualTo(Utc(2021, 3, 15)));
        Assert.That(GetValue<long>(row, "Storage"), Is.EqualTo(256L));
    }

    private static void AssertSpecsAreMissing(IReadOnlyDictionary<string, object?> row, string deviceIdColumn)
    {
        Assert.That(GetValue<string>(row, deviceIdColumn), Is.Empty, "a missing string is empty, not null");
        Assert.That(GetValue<string>(row, "Vendor"), Is.Empty, "a missing string is empty, not null");
        foreach (string column in DeviceSpecsValueColumns)
        {
            AssertIsNull(row, column);
        }
    }

    private static void AssertEventIsMissing(IReadOnlyDictionary<string, object?> row, string deviceIdColumn)
    {
        AssertIsNull(row, "EventId");
        Assert.That(GetValue<string>(row, deviceIdColumn), Is.Empty, "a missing string is empty, not null");
        Assert.That(GetValue<string>(row, "EventType"), Is.Empty, "a missing string is empty, not null");
        AssertIsNull(row, "Timestamp");
    }

    #region leftouter

    [Test]
    [Description("leftouter keeps every event: each d1 event is paired with both d1 locations, and the event 4 of the " +
                 "unknown device d3 is kept once with an empty right side")]
    public void Join_LeftOuter_KeepsEveryEventAndPairsItWithEveryMatch()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, "Events | join kind=leftouter Inventory on DeviceId",
                "EventId", "DeviceId1", "Location"),
            Is.EquivalentTo(new[]
            {
                "1:d1:Redmond", "1:d1:Seattle", "2:d1:Redmond", "2:d1:Seattle", "3:d2:Dublin", "4::", "5:d2:Dublin"
            }));
    }

    [Test]
    [Description("Rows per event after leftouter: two for the events of d1 (two inventory rows), one for the others, " +
                 "the unmatched event 4 included")]
    public void Join_LeftOuter_RowsPerLeftRow()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=leftouter Inventory on DeviceId | summarize Rows = count() by EventId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Rows"),
            Is.EquivalentTo(new[] { "1:2", "2:2", "3:1", "4:1", "5:1" }));
    }

    [Test]
    [Description("Events | join kind=leftouter (Devices | where Active) on DeviceId: only d1 is an active device with " +
                 "events, the events of d2 (inactive) and d3 (unknown) keep an empty right side")]
    public void Join_LeftOuter_WithAFilteredSubqueryOnTheRight()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, "Events | join kind=leftouter (Devices | where Active) on DeviceId",
                "EventId", "DeviceId1", "Model", "Active"),
            Is.EquivalentTo(new[] { "1:d1:Surface:True", "2:d1:Surface:True", "3:::", "4:::", "5:::" }));
    }

    [Test]
    [Description("The unmatched event 4 keeps its own values; the missing device has an empty DeviceId1 and Model " +
                 "(strings) and a null Active (bool)")]
    public void Join_LeftOuter_UnmatchedRowHasEmptyStringsAndNullsOnTheRight()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Events | join kind=leftouter Devices on DeviceId | where EventId == 4");

        AssertColumnsInOrder(row, "EventId", "DeviceId", "EventType", "Timestamp", "DeviceId1", "Model", "Active");
        Assert.That(GetValue<long>(row, "EventId"), Is.EqualTo(4L));
        Assert.That(GetValue<string>(row, "DeviceId"), Is.EqualTo("d3"));
        Assert.That(GetValue<string>(row, "EventType"), Is.EqualTo("boot"));
        Assert.That(GetValue<DateTime>(row, "Timestamp"), Is.EqualTo(Utc(2024, 1, 2, 8)));
        Assert.That(GetValue<string>(row, "DeviceId1"), Is.Empty, "a missing string is empty, not null");
        Assert.That(GetValue<string>(row, "Model"), Is.Empty, "a missing string is empty, not null");
        AssertIsNull(row, "Active");
    }

    [Test]
    [Description("The event 3 (device d2) has no specs: the missing strings are empty and the missing int, real, " +
                 "decimal, timespan, guid, bool, datetime and long values are null")]
    public void Join_LeftOuter_MissingValuesOfEveryScalarType()
    {
        var kustoDatabase = CreateDatabaseWithDeviceSpecs();

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            "Events | join kind=leftouter DeviceSpecs on DeviceId | where EventId == 3");

        AssertColumnsInOrder(row, "EventId", "DeviceId", "EventType", "Timestamp", "DeviceId1", "Vendor", "Cores",
            "Weight", "Price", "Warranty", "SerialId", "Retired", "Released", "Storage");
        Assert.That(GetValue<string>(row, "DeviceId"), Is.EqualTo("d2"));
        AssertSpecsAreMissing(row, "DeviceId1");
    }

    [TestCase("inner")]
    [TestCase("leftouter")]
    [TestCase("rightouter")]
    [TestCase("fullouter")]
    [Description("A matched right row keeps its values and their exact types (int, real, decimal, timespan, guid, " +
                 "bool, datetime, long), whatever the kind")]
    public void Join_MatchedRightValues_KeepTheirTypes(string kind)
    {
        var kustoDatabase = CreateDatabaseWithDeviceSpecs();

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase,
            $"Events | join kind={kind} DeviceSpecs on DeviceId | where EventId == 1");

        AssertSpecsOfD1(row, "DeviceId1");
    }

    [Test]
    [Description("isnull is false and isempty is true for the 3 missing Vendor strings, isnull is true for the 3 " +
                 "missing values of every other type")]
    public void Join_LeftOuter_IsNullAndIsEmptyOfTheMissingValues()
    {
        var kustoDatabase = CreateDatabaseWithDeviceSpecs();
        const string query = "Events | join kind=leftouter DeviceSpecs on DeviceId " +
                             "| summarize VendorIsNull = countif(isnull(Vendor)), " +
                             "VendorIsEmpty = countif(isempty(Vendor)), " +
                             "CoresIsNull = countif(isnull(Cores)), WeightIsNull = countif(isnull(Weight)), " +
                             "PriceIsNull = countif(isnull(Price)), WarrantyIsNull = countif(isnull(Warranty)), " +
                             "SerialIdIsNull = countif(isnull(SerialId)), RetiredIsNull = countif(isnull(Retired)), " +
                             "ReleasedIsNull = countif(isnull(Released)), StorageIsNull = countif(isnull(Storage))";

        IReadOnlyDictionary<string, object?> row = ExecuteAndGetSingleRow(kustoDatabase, query);

        Assert.That(GetValue<long>(row, "VendorIsNull"), Is.EqualTo(0L));
        Assert.That(GetValue<long>(row, "VendorIsEmpty"), Is.EqualTo(3L));
        foreach (string column in DeviceSpecsValueColumns)
        {
            Assert.That(GetValue<long>(row, column + "IsNull"), Is.EqualTo(3L), column);
        }
    }

    [Test]
    [Description("A missing dynamic value is null: Details.ram is 16 for the events of d1 and null for the others")]
    public void Join_LeftOuter_MissingDynamicValueIsNull()
    {
        var kustoDatabase = CreateDatabaseWithDeviceSpecs();
        string query = $"Events | join kind=leftouter {DeviceSpecsWithDetails} on DeviceId " +
                       "| extend Ram = tolong(Details.ram), MissingDetails = isnull(Details) " +
                       "| project EventId, Ram, MissingDetails";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Ram", "MissingDetails"),
            Is.EquivalentTo(new[] { "1:16:False", "2:16:False", "3::True", "4::True", "5::True" }));
    }

    [Test]
    [Description("The missing Model of the event 4 is not null but empty, and its missing Active is null; " +
                 "summarize groups the empty Model as one more key")]
    public void Join_LeftOuter_MissingValuesInWhereAndSummarize()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string joined = "Events | join kind=leftouter Devices on DeviceId";

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase,
                joined + " | summarize ModelIsNull = countif(isnull(Model)), " +
                "ModelIsEmpty = countif(isempty(Model)), ActiveIsNull = countif(isnull(Active))",
                "ModelIsNull", "ModelIsEmpty", "ActiveIsNull"),
            Is.EqualTo(new[] { "0:1:1" }));
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, joined + " | where isempty(DeviceId1) | project EventId", "EventId"),
            Is.EqualTo(new[] { "4" }));
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, joined + " | summarize EventCount = count() by Model",
                "Model", "EventCount"),
            Is.EquivalentTo(new[] { "Surface:2", "Xbox:2", ":1" }));
    }

    [Test]
    [Description("Two chained leftouter joins: Events with Devices, then with ModelCatalog on the Model of the first " +
                 "join; the event 4 has no device, so its empty Model matches no category")]
    public void Join_LeftOuter_Chained()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=leftouter Devices on DeviceId " +
                             "| join kind=leftouter ModelCatalog on Model";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Model", "Category"),
            Is.EquivalentTo(new[]
            {
                "1:Surface:laptop", "2:Surface:laptop", "3:Xbox:console", "4::", "5:Xbox:console"
            }));
    }

    [Test]
    [Description("A right side without any match (or empty): leftouter still returns every event once, with an empty " +
                 "right side")]
    public void Join_LeftOuter_WithoutAnyMatch_KeepsEveryLeftRow()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=leftouter (Devices | where DeviceId == 'none') on DeviceId " +
                             "| where isnull(Active) and isempty(Model) and isempty(DeviceId1) | count";

        Assert.That(ExecuteAndGetCount(kustoDatabase, query), Is.EqualTo(5L));
    }

    [Test]
    [Description("Defaults for the missing values after leftouter: coalesce(Active, false) and " +
                 "iff(isempty(Model), 'unknown', Model)")]
    public void Join_LeftOuter_DefaultsForTheMissingValues()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=leftouter Devices on DeviceId " +
                             "| extend IsActive = coalesce(Active, false), " +
                             "Model = iff(isempty(Model), 'unknown', Model) " +
                             "| project EventId, Model, IsActive";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "Model", "IsActive"),
            Is.EquivalentTo(new[]
            {
                "1:Surface:True", "2:Surface:True", "3:Xbox:False", "4:unknown:False", "5:Xbox:False"
            }));
    }

    #endregion

    #region rightouter

    [Test]
    [Description("Inventory | join kind=rightouter Events keeps every event of the right side: the d1 events with both " +
                 "locations and the event 4 with an empty left side; the inventory row of d4 (no event) is dropped")]
    public void Join_RightOuter_KeepsEveryRightRow()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, "Inventory | join kind=rightouter Events on DeviceId",
                "DeviceId", "Location", "EventId"),
            Is.EquivalentTo(new[]
            {
                "d1:Redmond:1", "d1:Seattle:1", "d1:Redmond:2", "d1:Seattle:2", "d2:Dublin:3", "::4", "d2:Dublin:5"
            }));
    }

    [Test]
    [Description("B | join kind=rightouter A returns the same pairs as A | join kind=leftouter B")]
    public void Join_RightOuter_MirrorsLeftOuter()
    {
        var kustoDatabase = CreateScenarioDatabase();
        string[] columns = { "EventId", "EventType", "Location" };

        List<string> leftOuter = ExecuteAndGetRowTexts(kustoDatabase,
            "Events | join kind=leftouter Inventory on DeviceId", columns);
        List<string> rightOuter = ExecuteAndGetRowTexts(kustoDatabase,
            "Inventory | join kind=rightouter Events on DeviceId", columns);

        Assert.That(leftOuter, Has.Count.EqualTo(7));
        Assert.That(rightOuter, Is.EquivalentTo(leftOuter));
    }

    [Test]
    [Description("The device d4 has no event: its rightouter row has a null EventId and Timestamp and an empty " +
                 "DeviceId and EventType, next to its own values")]
    public void Join_RightOuter_UnmatchedRowHasEmptyStringsAndNullsOnTheLeft()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "Events | join kind=rightouter Devices on DeviceId");

        Assert.That(rows, Has.Count.EqualTo(5), "4 matched events and the device d4");
        IReadOnlyDictionary<string, object?> row = GetSingleRowWhere(rows, "DeviceId1", "d4");
        AssertColumnsInOrder(row, "EventId", "DeviceId", "EventType", "Timestamp", "DeviceId1", "Model", "Active");
        AssertEventIsMissing(row, "DeviceId");
        Assert.That(GetValue<string>(row, "Model"), Is.EqualTo("HoloLens"));
        Assert.That(GetValue<bool>(row, "Active"), Is.True);
    }

    [Test]
    [Description("With an empty left side rightouter returns every right row once, with a null EventId and an empty " +
                 "DeviceId")]
    public void Join_RightOuter_WithAnEmptyLeftSide_KeepsEveryRightRow()
    {
        var kustoDatabase = CreateScenarioDatabase();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteAndGetRows(kustoDatabase,
            "Events | where EventId < 0 | join kind=rightouter Devices on DeviceId");

        Assert.That(rows.Select(row => FormatRow(row, "DeviceId1", "Model")),
            Is.EquivalentTo(new[] { "d1:Surface", "d2:Xbox", "d4:HoloLens" }));
        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            AssertEventIsMissing(row, "DeviceId");
        }
    }

    [Test]
    [Description("DeviceSpecs | join kind=rightouter Events: the specs of d1 for the events 1 and 2, missing specs " +
                 "(empty strings, nulls of every other type) for the events 3, 4 and 5")]
    public void Join_RightOuter_MissingValuesOfEveryScalarTypeOnTheLeft()
    {
        var kustoDatabase = CreateDatabaseWithDeviceSpecs();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "DeviceSpecs | join kind=rightouter Events on DeviceId");

        Assert.That(rows, Has.Count.EqualTo(5));
        AssertColumnsInOrder(rows[0], "DeviceId", "Vendor", "Cores", "Weight", "Price", "Warranty", "SerialId",
            "Retired", "Released", "Storage", "EventId", "DeviceId1", "EventType", "Timestamp");
        AssertSpecsOfD1(GetSingleRowWhere(rows, "EventId", 1L), "DeviceId");
        AssertSpecsOfD1(GetSingleRowWhere(rows, "EventId", 2L), "DeviceId");
        foreach (long eventId in new[] { 3L, 4L, 5L })
        {
            AssertSpecsAreMissing(GetSingleRowWhere(rows, "EventId", eventId), "DeviceId");
        }

        Assert.That(GetValue<string>(GetSingleRowWhere(rows, "EventId", 4L), "DeviceId1"), Is.EqualTo("d3"));
    }

    [Test]
    [Description("Events | join kind=rightouter DeviceSpecs: the specs of d8 have no event, the row keeps the typed " +
                 "values of d8 with a missing event")]
    public void Join_RightOuter_UnmatchedRightRowKeepsItsTypedValues()
    {
        var kustoDatabase = CreateDatabaseWithDeviceSpecs();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "Events | join kind=rightouter DeviceSpecs on DeviceId");

        Assert.That(rows.Select(row => FormatRow(row, "EventId", "DeviceId1")),
            Is.EquivalentTo(new[] { "1:d1", "2:d1", ":d8" }));
        IReadOnlyDictionary<string, object?> row = GetSingleRowWhere(rows, "DeviceId1", "d8");
        AssertEventIsMissing(row, "DeviceId");
        AssertSpecsOfD8(row, "DeviceId1");
    }

    #endregion

    #region outer kinds of requests and traces

    [TestCase("leftouter", new[] { "op3:GET /about::" })]
    [TestCase("rightouter", new[] { "::op4:orphan trace" })]
    [TestCase("fullouter", new[] { "op3:GET /about::", "::op4:orphan trace" })]
    [Description("The outer kinds return every request-trace pair (2 x 3 for op1, no deduplication) plus the unmatched " +
                 "rows of their outer side: the request op3 without trace, the trace op4 without request")]
    public void Join_OuterKinds_ReturnEveryPairAndTheUnmatchedRows(string kind, string[] unmatchedRows)
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, $"Requests | join kind={kind} Traces on OperationId",
                "OperationId", "Name", "OperationId1", "Message"),
            Is.EquivalentTo(RequestAndTracePairs.Concat(unmatchedRows)));
    }

    #endregion

    #region fullouter

    [Test]
    [Description("fullouter returns the 4 matched events, the event 4 of the unknown device d3 and the device d4 " +
                 "without events")]
    public void Join_FullOuter_ReturnsTheMatchesAndTheUnmatchedRowsOfBothSides()
    {
        var kustoDatabase = CreateScenarioDatabase();

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, "Events | join kind=fullouter Devices on DeviceId",
                "EventId", "DeviceId", "DeviceId1", "Model"),
            Is.EquivalentTo(new[]
            {
                "1:d1:d1:Surface", "2:d1:d1:Surface", "3:d2:d2:Xbox", "4:d3::", "5:d2:d2:Xbox", "::d4:HoloLens"
            }));
    }

    [Test]
    [Description("Events | join kind=fullouter DeviceSpecs: the events 3, 4 and 5 have missing specs, the specs of d8 " +
                 "have a missing event, and both keep the exact types of their own values")]
    public void Join_FullOuter_PadsBothSidesWithValuesOfTheirTypes()
    {
        var kustoDatabase = CreateDatabaseWithDeviceSpecs();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            ExecuteAndGetRows(kustoDatabase, "Events | join kind=fullouter DeviceSpecs on DeviceId");

        Assert.That(rows.Select(row => FormatRow(row, "EventId", "DeviceId1")),
            Is.EquivalentTo(new[] { "1:d1", "2:d1", "3:", "4:", "5:", ":d8" }));
        AssertSpecsOfD1(GetSingleRowWhere(rows, "EventId", 2L), "DeviceId1");
        AssertSpecsAreMissing(GetSingleRowWhere(rows, "EventId", 5L), "DeviceId1");
        Assert.That(GetValue<string>(GetSingleRowWhere(rows, "EventId", 5L), "EventType"), Is.EqualTo("shutdown"));
        IReadOnlyDictionary<string, object?> rowOfD8 = GetSingleRowWhere(rows, "DeviceId1", "d8");
        AssertEventIsMissing(rowOfD8, "DeviceId");
        AssertSpecsOfD8(rowOfD8, "DeviceId1");
    }

    [Test]
    [Description("Reconciling two inventory snapshots with fullouter: d1 is unchanged, d2 upgraded, d3 only in " +
                 "yesterday's snapshot (removed) and d5 only in today's (added)")]
    public void Join_FullOuter_ReconcilesTwoSnapshots()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "InventoryYesterday | join kind=fullouter InventoryToday on DeviceId " +
                             "| extend Device = iff(isempty(DeviceId), DeviceId1, DeviceId), " +
                             "Change = case(isempty(DeviceId1), 'removed', isempty(DeviceId), 'added', " +
                             "Version != Version1, 'upgraded', 'unchanged')";

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, query,
                "Device", "Change", "DeviceId", "Version", "DeviceId1", "Version1"),
            Is.EquivalentTo(new[]
            {
                "d1:unchanged:d1:1.0:d1:1.0",
                "d2:upgraded:d2:1.0:d2:1.1",
                "d5:added:::d5:1.0",
                "d3:removed:d3:2.0::"
            }));
    }

    [Test]
    [Description("coalesce(DeviceId, DeviceId1) skips the empty key of a one-sided row, so it names the device of " +
                 "every fullouter row")]
    public void Join_FullOuter_CoalesceOfBothKeys()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "InventoryYesterday | join kind=fullouter InventoryToday on DeviceId " +
                             "| project Device = coalesce(DeviceId, DeviceId1), Version, Version1";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Device", "Version", "Version1"),
            Is.EquivalentTo(new[] { "d1:1.0:1.0", "d2:1.0:1.1", "d5::1.0", "d3:2.0:" }));
    }

    [Test]
    [Description("After fullouter an empty right key marks the devices only in yesterday's snapshot (d3), an empty " +
                 "left key the devices only in today's snapshot (d5)")]
    public void Join_FullOuter_DevicesOnlyInOneSnapshot()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string joined = "InventoryYesterday | join kind=fullouter InventoryToday on DeviceId";

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, joined + " | where isempty(DeviceId1) | project DeviceId, Version",
                "DeviceId", "Version"),
            Is.EqualTo(new[] { "d3:2.0" }), "only yesterday");
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, joined + " | where isempty(DeviceId) | project DeviceId1, Version1",
                "DeviceId1", "Version1"),
            Is.EqualTo(new[] { "d5:1.0" }), "only today");
    }

    [Test]
    [Description("The fullouter result pipes into summarize: one device per change, and one row per pair of keys, " +
                 "an empty key standing for the missing side")]
    public void Join_FullOuter_Summarized()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string joined = "InventoryYesterday | join kind=fullouter InventoryToday on DeviceId";

        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase,
                joined + " | extend Change = case(isempty(DeviceId1), 'removed', isempty(DeviceId), 'added', " +
                "Version != Version1, 'upgraded', 'unchanged') | summarize DeviceCount = count() by Change",
                "Change", "DeviceCount"),
            Is.EquivalentTo(new[] { "unchanged:1", "upgraded:1", "added:1", "removed:1" }));
        Assert.That(
            ExecuteAndGetRowTexts(kustoDatabase, joined + " | summarize Rows = count() by DeviceId, DeviceId1",
                "DeviceId", "DeviceId1", "Rows"),
            Is.EquivalentTo(new[] { "d1:d1:1", "d2:d2:1", "d3::1", ":d5:1" }));
    }

    [Test]
    [Description("fullouter with a right side that matches nothing: every event with an empty right side, plus the " +
                 "right row (the heartbeat of d9) with a missing event")]
    public void Join_FullOuter_WithoutAnyMatch()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=fullouter (Heartbeats | where DeviceId == 'd9') on DeviceId";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "EventId", "DeviceId1"),
            Is.EquivalentTo(new[] { "1:", "2:", "3:", "4:", "5:", ":d9" }));
    }

    [Test]
    [Description("The rows of a fullouter join split into matched rows (both keys), left-only rows (empty DeviceId1) " +
                 "and right-only rows (empty DeviceId): 4, 1 and 1")]
    public void Join_FullOuter_MatchedAndUnmatchedRowCounts()
    {
        var kustoDatabase = CreateScenarioDatabase();
        const string query = "Events | join kind=fullouter Devices on DeviceId " +
                             "| summarize Matched = countif(isnotempty(DeviceId) and isnotempty(DeviceId1)), " +
                             "LeftOnly = countif(isempty(DeviceId1)), RightOnly = countif(isempty(DeviceId))";

        Assert.That(ExecuteAndGetRowTexts(kustoDatabase, query, "Matched", "LeftOnly", "RightOnly"),
            Is.EqualTo(new[] { "4:1:1" }));
    }

    #endregion
}
