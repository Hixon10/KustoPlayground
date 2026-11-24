using InputTable = KustoPlayground.Core.Tests.SummarizeOperatorOpusTableBuilder;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;
using static KustoPlayground.Core.Tests.TestUtils;

namespace KustoPlayground.Core.Tests;

public class JoinKeyMatchingAstraTests
{
    private static KustoDatabase TenantDevices() => CreateDatabase(
        GenerateTableWith3Columns<string, string, int>(
            [("east", "d", 1), ("west", "d", 2), ("east", "x", 3), ("east|west", "d", 4), ("east", "west|d", 5)],
            "TenantId", "DeviceId", "EventId", "Events"),
        GenerateTableWith3Columns<string, string, int>(
            [("d", "west", 20), ("d", "east", 10), ("x", "west", 30), ("d", "east|west", 40), ("west|d", "east", 50)],
            "DeviceId", "TenantId", "AssetRow", "Devices"));

    [TestCase("TenantId, DeviceId")]
    [TestCase("$left.TenantId == $right.TenantId and $left.DeviceId == $right.DeviceId")]
    [TestCase("$left.DeviceId == $right.DeviceId, $left.TenantId == $right.TenantId")]
    [TestCase("TenantId, $left.DeviceId == $right.DeviceId")]
    public void CompositeKeysRequireTheSameTupleNotJustOneComponentOrAJoinedString(string keys)
    {
        var pairs = ExecuteAndGetDataFor2Columns<int, int>("EventId", "AssetRow", TenantDevices(),
            $"Events | join kind=inner Devices on {keys}");

        Assert.That(pairs, Is.EquivalentTo(new[] { (1, 10), (2, 20), (4, 40), (5, 50) }));
    }

    [TestCase("leftsemi",  "EventId",  new[] { 1, 2, 4, 5 })]
    [TestCase("leftanti",  "EventId",  new[] { 3 })]
    [TestCase("rightsemi", "AssetRow", new[] { 10, 20, 40, 50 })]
    [TestCase("rightanti", "AssetRow", new[] { 30 })]
    public void AnInventoryEntryInAnotherTenantDoesNotSatisfyAnExistenceJoin(string kind, string id, int[] expected)
    {
        var ids = ExecuteAndGetDataForOneColumn<int>(id, TenantDevices(),
            $"Events | join kind={kind} Devices on TenantId, DeviceId");

        Assert.That(ids, Is.EquivalentTo(expected));
    }

    [Test]
    public void SelfJoinResolvesManagerIdOnTheLeftAndEmployeeIdOnTheRight()
    {
        var db = CreateDatabase(GenerateTableWith3Columns<int, int, string>(
            [(1, 0, "Ada"), (2, 1, "Bea"), (3, 1, "Cal"), (4, 2, "Dev")],
            "EmployeeId", "ManagerId", "Name", "Employees"));
        var pairs = ExecuteAndGetDataFor2Columns<string, string>("Employee", "Manager", db,
            """
            Employees
            | join kind=inner Employees on $left.ManagerId == $right.EmployeeId
            | project Employee = Name, Manager = Name1
            """);

        Assert.That(pairs, Is.EquivalentTo(new[] { ("Bea", "Ada"), ("Cal", "Ada"), ("Dev", "Bea") }));
    }

    [Test]
    public void StringKeysMatchExactlyIncludingCaseWhitespaceAndTheEmptyString()
    {
        var db = CreateDatabase(
            GenerateTableWith2Columns<string, int>([("edge", 1), ("EDGE", 2), ("edge ", 3), ("", 4), (" ", 5)],
                "DeviceId", "EventId", "Events"),
            GenerateTableWith2Columns<string, int>([("EDGE", 10), ("edge", 11), ("", 12), (" edge", 13)],
                "DeviceId", "AssetRow", "Devices"));
        var pairs = ExecuteAndGetDataFor2Columns<int, int>("EventId", "AssetRow", db,
            "Events | join kind=inner Devices on DeviceId");

        Assert.That(pairs, Is.EquivalentTo(new[] { (1, 11), (2, 10), (4, 12) }));
    }

    [Test]
    public void InnerUniqueDeduplicatesANullDeviceIdWithinItsTenantNotAcrossTenants()
    {
        var db = CreateDatabase(
            new InputTable("Events").WithColumn<string>("TenantId").WithColumn<long>("DeviceId")
                .WithRow("east", null).WithRow("east", null).WithRow("west", null).WithRow("east", 0L).Build(),
            new InputTable("Devices").WithColumn<string>("Tenant").WithColumn<long>("Id").WithColumn<int>("AssetRow")
                .WithRow("east", null, 10).WithRow("east", null, 11).WithRow("west", null, 20)
                .WithRow("east", 0L, 30).WithRow("west", 0L, 40).Build());
        var rows = ExecuteAndGetDataFor3Columns<string, long?, int>("TenantId", "DeviceId", "AssetRow", db,
            "Events | join Devices on $left.TenantId == $right.Tenant and $left.DeviceId == $right.Id");

        Assert.That(rows, Is.EquivalentTo(new (string, long?, int)[]
            { ("east", null, 10), ("east", null, 11), ("west", null, 20), ("east", 0, 30) }));
    }

    [Test]
    public void AdjacentLongIdsBeyondDoublePrecisionDoNotMatchEachOther()
    {
        const long first = 9_007_199_254_740_992L;
        var db = CreateDatabase(
            GenerateTableWithColumn<long>([first, first + 1], "DeviceId", "Events"),
            GenerateTableWithColumn<long>([first + 1, first + 2], "DeviceId", "Devices"));
        var pairs = ExecuteAndGetDataFor2Columns<long, long>("DeviceId", "DeviceId1", db,
            "Events | join kind=inner Devices on DeviceId");

        Assert.That(pairs, Is.EquivalentTo(new[] { (first + 1, first + 1) }));
    }

    private static KustoDatabase MixedKeyTypes() => CreateDatabase(
        GenerateTableWith2Columns<string, string>([("east", "7"), ("east", "007"), ("east", "bad"), ("west", "8")],
            "TenantId", "DeviceId", "Events"),
        GenerateTableWith2Columns<string, long>([("east", 7), ("east", 8), ("west", 7)],
            "TenantId", "DeviceId", "Devices"));

    [TestCase("DeviceId")]
    [TestCase("$left.DeviceId == $right.DeviceId")]
    [TestCase("TenantId, DeviceId")]
    public void StringAndLongKeysAreRejectedEvenIfTheirTextMatches(string keys)
    {
        AstraTestUtils.AssertRejected(MixedKeyTypes(), $"Events | join kind=inner Devices on {keys}");
    }

    [Test]
    public void CastingBeforeTheJoinMatchesNumericIdsWithoutChangingTheOriginalColumn()
    {
        var pairs = ExecuteAndGetDataFor2Columns<string, long>("DeviceId", "DeviceId1", MixedKeyTypes(),
            """
            Events | extend NumericId = tolong(DeviceId)
            | join kind=inner Devices on TenantId, $left.NumericId == $right.DeviceId
            | project DeviceId, DeviceId1
            """);

        Assert.That(pairs, Is.EquivalentTo(new[] { ("007", 7L), ("7", 7L) }));
    }

    private static KustoDatabase JsonDeviceIds() => CreateDatabase(
        GenerateTableWith2Columns<int, string>([(1, """{"id":"7"}"""), (2, """{"id":42}"""), (3, """{"id":8}""")],
            "EventId", "Payload", "RawEvents"),
        GenerateTableWith2Columns<int, string>([(10, """{"id":7}"""), (20, """{"id":"42"}""")],
            "AssetRow", "Payload", "RawDevices"));

    private const string ParseDeviceIds = """
        let E = RawEvents | project EventId, DeviceId = parse_json(Payload).id;
        let D = RawDevices | project AssetRow, DeviceId = parse_json(Payload).id;
        """;

    [TestCase("E | join kind=inner D on DeviceId")]
    [TestCase("E | join kind=inner D on $left.DeviceId == $right.DeviceId")]
    [TestCase("E | join kind=inner (D | extend DeviceId = tolong(DeviceId)) on DeviceId")]
    public void DynamicKeysAreRejectedUnlessBothSidesHaveScalarKeyTypes(string join)
    {
        AstraTestUtils.AssertRejected(JsonDeviceIds(), ParseDeviceIds + "\n" + join);
    }

    [Test]
    public void CastingDynamicPropertiesOnBothSidesAllowsAValueJoin()
    {
        var pairs = ExecuteAndGetDataFor2Columns<int, int>("EventId", "AssetRow", JsonDeviceIds(),
            $"""
            {ParseDeviceIds}
            E | extend Key = tolong(DeviceId)
            | join kind=inner (D | project AssetRow, Key = tolong(DeviceId)) on Key
            """);

        Assert.That(pairs, Is.EquivalentTo(new[] { (1, 10), (2, 20) }));
    }

    [TestCase("Events | join kind=innner Devices on DeviceId")]
    [TestCase("Events | join kind=inner Devices")]
    [TestCase("Events | join kind=inner Devices on EventId")]
    [TestCase("Events | join kind=inner Devices on AssetRow")]
    [TestCase("Events | join kind=inner Devices on $left.DeviceId == $left.DeviceId")]
    [TestCase("Events | join kind=inner Devices on $left.DeviceId != $right.DeviceId")]
    [TestCase("Events | join kind=inner Devices on $left.DeviceId == $right.DeviceId or $left.TenantId == $right.TenantId")]
    public void InvalidJoinClausesAreRejectedRatherThanProducingAPartialOrCartesianJoin(string query)
    {
        AstraTestUtils.AssertRejected(TenantDevices(), query);
    }
}
