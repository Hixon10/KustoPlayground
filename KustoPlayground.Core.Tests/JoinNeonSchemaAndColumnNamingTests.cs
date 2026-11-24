using System.Text.Json;
using static KustoPlayground.Core.Tests.JoinNeonTestUtils;
using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

public class JoinNeonSchemaAndColumnNamingTests
{
    private static KustoDatabase CreateSchemaDatabase(bool empty = false)
    {
        var events = new NullableTableBuilder("Events")
            .WithColumn<string>("DeviceId", isNullable: false)
            .WithColumn<string>("Name", isNullable: false)
            .WithColumn<int>("Code", isNullable: false);

        var devices = new NullableTableBuilder("Devices")
            .WithColumn<string>("DeviceId", isNullable: false)
            .WithColumn<string>("Name", isNullable: false)
            .WithColumn<bool>("Active", isNullable: false);

        if (!empty)
        {
            events.WithRow("dev-1", "cpu-high", 500)
                  .WithRow("dev-only-left", "disk-full", 507);
            devices.WithRow("dev-1", "web-01", true)
                   .WithRow("dev-only-right", "db-02", false);
        }

        return CreateDatabase(events.Build(), devices.Build());
    }

    #region Output column order and collision renaming

    [TestCase("")]
    [TestCase("kind=innerunique")]
    [TestCase("kind=inner")]
    [TestCase("kind=leftouter")]
    [TestCase("kind=rightouter")]
    [TestCase("kind=fullouter")]
    [Description("Inner and outer joins return left columns in order followed by right columns in order, suffixing colliding key and non-key columns with '1'.")]
    public void InnerAndOuterJoins_ReturnLeftColumnsThenRightColumnsWithSuffixOnCollisions(string kindClause)
    {
        KustoDatabase db = CreateSchemaDatabase();
        var rows = ExecuteJoin(db, $"Events | join {kindClause} Devices on DeviceId");

        AssertExactColumnOrder(rows, "DeviceId", "Name", "Code", "DeviceId1", "Name1", "Active");

        var schema = ExecuteGetSchema(db, $"Events | join {kindClause} Devices on DeviceId");
        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("DeviceId",  0, "System.String", "string"),
            ("Name",      1, "System.String", "string"),
            ("Code",      2, "System.Int32",  "int"),
            ("DeviceId1", 3, "System.String", "string"),
            ("Name1",     4, "System.String", "string"),
            ("Active",    5, "System.SByte",  "bool")
        }));
    }

    [TestCase("leftsemi")]
    [TestCase("leftanti")]
    [TestCase("anti")]
    [TestCase("leftantisemi")]
    [Description("Left semi and left anti joins return only left columns with exact names, order, and zero suffixing.")]
    public void LeftSemiAndLeftAnti_ReturnOnlyLeftColumnsWithoutSuffixing(string kind)
    {
        KustoDatabase db = CreateSchemaDatabase();
        var rows = ExecuteJoin(db, $"Events | join kind={kind} Devices on DeviceId");

        AssertExactColumnOrder(rows, "DeviceId", "Name", "Code");

        var schema = ExecuteGetSchema(db, $"Events | join kind={kind} Devices on DeviceId");
        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("DeviceId", 0, "System.String", "string"),
            ("Name",     1, "System.String", "string"),
            ("Code",     2, "System.Int32",  "int")
        }));
    }

    [TestCase("rightsemi")]
    [TestCase("rightanti")]
    [TestCase("rightantisemi")]
    [Description("Right semi and right anti joins return only right columns with exact names, order, and zero suffixing.")]
    public void RightSemiAndRightAnti_ReturnOnlyRightColumnsWithoutSuffixing(string kind)
    {
        KustoDatabase db = CreateSchemaDatabase();
        var rows = ExecuteJoin(db, $"Events | join kind={kind} Devices on DeviceId");

        AssertExactColumnOrder(rows, "DeviceId", "Name", "Active");

        var schema = ExecuteGetSchema(db, $"Events | join kind={kind} Devices on DeviceId");
        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("DeviceId", 0, "System.String", "string"),
            ("Name",     1, "System.String", "string"),
            ("Active",   2, "System.SByte",  "bool")
        }));
    }

    [TestCase("")]
    [TestCase("kind=innerunique")]
    [TestCase("kind=inner")]
    [TestCase("kind=leftouter")]
    [TestCase("kind=rightouter")]
    [TestCase("kind=fullouter")]
    [Description("getschema returns full left-then-right schema with suffixed collisions even when both input tables are empty.")]
    public void EmptyJoin_GetSchema_PreservesFullOutputSchemaForInnerAndOuterKinds(string kindClause)
    {
        KustoDatabase db = CreateSchemaDatabase(empty: true);
        var schema = ExecuteGetSchema(db, $"Events | join {kindClause} Devices on DeviceId");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("DeviceId",  0, "System.String", "string"),
            ("Name",      1, "System.String", "string"),
            ("Code",      2, "System.Int32",  "int"),
            ("DeviceId1", 3, "System.String", "string"),
            ("Name1",     4, "System.String", "string"),
            ("Active",    5, "System.SByte",  "bool")
        }));
    }

    [TestCase("leftsemi",      new[] { "DeviceId", "Name", "Code" })]
    [TestCase("leftanti",      new[] { "DeviceId", "Name", "Code" })]
    [TestCase("anti",          new[] { "DeviceId", "Name", "Code" })]
    [TestCase("leftantisemi",  new[] { "DeviceId", "Name", "Code" })]
    [TestCase("rightsemi",     new[] { "DeviceId", "Name", "Active" })]
    [TestCase("rightanti",     new[] { "DeviceId", "Name", "Active" })]
    [TestCase("rightantisemi", new[] { "DeviceId", "Name", "Active" })]
    [Description("getschema returns the exact single-side schema on empty semi/anti joins.")]
    public void EmptyJoin_GetSchema_PreservesSingleSideSchemaForSemiAndAntiKinds(string kind, string[] expectedNames)
    {
        KustoDatabase db = CreateSchemaDatabase(empty: true);
        var schema = ExecuteGetSchema(db, $"Events | join kind={kind} Devices on DeviceId");
        Assert.That(schema.Select(c => c.ColumnName).ToArray(), Is.EqualTo(expectedNames));
    }

    [Test]
    [Description("Downstream project, extend, where, and summarize can reference both DeviceId and DeviceId1 (as well as Name and Name1).")]
    public void DownstreamOperators_CanReferenceBothLeftAndSuffixedRightColumns()
    {
        KustoDatabase db = CreateSchemaDatabase();

        string query = """
            Events
            | join kind=fullouter Devices on DeviceId
            | extend
                ResolvedDevice = iff(isempty(DeviceId), DeviceId1, DeviceId),
                Summary = strcat(Name, "->", Name1)
            | where isnotempty(ResolvedDevice)
            | project ResolvedDevice, LeftKey = DeviceId, RightKey = DeviceId1, Summary
            """;

        var actual = ExecuteAndGetNullable4Columns<string, string, string, string>(
            "ResolvedDevice", "LeftKey", "RightKey", "Summary", db, query);

        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("dev-1",          "dev-1",         "dev-1",          "cpu-high->web-01"),
            ("dev-only-left",  "dev-only-left", "",               "disk-full->"),
            ("dev-only-right", "",              "dev-only-right", "->db-02")
        }));
    }

    [Test]
    [Description("When left already contains Id and Id1, a colliding right Id becomes Id2; when left contains Id, Id1, Id2 and right has Id and Id1, right columns become Id3 and Id11.")]
    public void PreExistingSuffixedColumnsOnLeft_PickNextAvailableKustoSuffix()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L1")
                .WithColumn<int>("Id", false)
                .WithColumn<int>("Id1", false)
                .WithRow(1, 10)
                .Build(),
            new NullableTableBuilder("R1")
                .WithColumn<int>("Id", false)
                .WithColumn<string>("Tag", false)
                .WithRow(1, "ok")
                .Build(),
            new NullableTableBuilder("L2")
                .WithColumn<int>("Id", false)
                .WithColumn<int>("Id1", false)
                .WithColumn<int>("Id2", false)
                .WithRow(1, 10, 100)
                .Build(),
            new NullableTableBuilder("R2")
                .WithColumn<int>("Id", false)
                .WithColumn<int>("Id1", false)
                .WithRow(1, 999)
                .Build());

        var rows1 = ExecuteJoin(db, "L1 | join kind=inner R1 on Id");
        AssertExactColumnOrder(rows1, "Id", "Id1", "Id2", "Tag");
        Assert.That((ReadCell<int>(rows1[0], "Id"), ReadCell<int>(rows1[0], "Id1"), ReadCell<int>(rows1[0], "Id2"), (string)rows1[0]["Tag"]!),
            Is.EqualTo((1, 10, 1, "ok")));

        var rows2 = ExecuteJoin(db, "L2 | join kind=inner R2 on Id");
        AssertExactColumnOrder(rows2, "Id", "Id1", "Id2", "Id3", "Id11");
        Assert.That(
            (ReadCell<int>(rows2[0], "Id"), ReadCell<int>(rows2[0], "Id1"), ReadCell<int>(rows2[0], "Id2"),
             ReadCell<int>(rows2[0], "Id3"), ReadCell<int>(rows2[0], "Id11")),
            Is.EqualTo((1, 10, 100, 1, 999)));
    }

    [Test]
    [Description("Explicit join with different key names preserves both names without suffixing when disjoint, and suffixes the right key only if its name collides with a left column.")]
    public void ExplicitJoinKey_SuffixesRightKeyOnlyWhenCollidingWithAnyLeftColumn()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("Events")
                .WithColumn<string>("DeviceId", false)
                .WithColumn<string>("Id", false)
                .WithRow("dev-1", "evt-99")
                .Build(),
            new NullableTableBuilder("Devices")
                .WithColumn<string>("Id", false)
                .WithColumn<string>("Owner", false)
                .WithRow("dev-1", "Alice")
                .Build());

        // Left already has a non-key column named "Id", so right key "Id" must become "Id1".
        var rows = ExecuteJoin(db, "Events | join kind=inner Devices on $left.DeviceId == $right.Id");
        AssertExactColumnOrder(rows, "DeviceId", "Id", "Id1", "Owner");
        Assert.That(
            ((string)rows[0]["DeviceId"]!, (string)rows[0]["Id"]!, (string)rows[0]["Id1"]!, (string)rows[0]["Owner"]!),
            Is.EqualTo(("dev-1", "evt-99", "dev-1", "Alice")));
    }

    [Test]
    [Description("Chained 3-way join on DeviceId produces DeviceId, DeviceId1, DeviceId2; self-join suffixes every right-side column.")]
    public void ChainedThreeWayJoinAndSelfJoin_SuffixAllRepeatedColumnsPredictably()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("T1").WithColumn<string>("DeviceId", false).WithColumn<int>("A", false)
                .WithRow("d1", 1).Build(),
            new NullableTableBuilder("T2").WithColumn<string>("DeviceId", false).WithColumn<int>("B", false)
                .WithRow("d1", 2).Build(),
            new NullableTableBuilder("T3").WithColumn<string>("DeviceId", false).WithColumn<int>("C", false)
                .WithRow("d1", 3).Build());

        var threeWay = ExecuteJoin(db, "T1 | join kind=inner T2 on DeviceId | join kind=inner T3 on DeviceId");
        AssertExactColumnOrder(threeWay, "DeviceId", "A", "DeviceId1", "B", "DeviceId2", "C");
        Assert.That(
            ((string)threeWay[0]["DeviceId"]!, (string)threeWay[0]["DeviceId1"]!, (string)threeWay[0]["DeviceId2"]!,
             ReadCell<int>(threeWay[0], "A"), ReadCell<int>(threeWay[0], "B"), ReadCell<int>(threeWay[0], "C")),
            Is.EqualTo(("d1", "d1", "d1", 1, 2, 3)));

        var selfJoin = ExecuteJoin(db, "T1 | join kind=inner T1 on DeviceId");
        AssertExactColumnOrder(selfJoin, "DeviceId", "A", "DeviceId1", "A1");
        Assert.That(
            ((string)selfJoin[0]["DeviceId"]!, ReadCell<int>(selfJoin[0], "A"),
             (string)selfJoin[0]["DeviceId1"]!, ReadCell<int>(selfJoin[0], "A1")),
            Is.EqualTo(("d1", 1, "d1", 1)));
    }

    #endregion

    #region Unmatched outer-join typed padding and non-key dynamic payloads

    [Test]
    [Description("Unmatched leftouter rows pad string columns with empty string and all other types (int, long, real, decimal, bool, datetime, timespan, guid, dynamic) with null, even when right columns are non-nullable.")]
    public void UnmatchedLeftOuterRow_PadsEveryKqlTypeCorrectlyEvenFromNonNullableRightTable()
    {
        DateTime dt = new(2025, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        TimeSpan ts = TimeSpan.FromMinutes(30);
        Guid g = Guid.Parse("22222222-3333-4444-5555-666666666666");
        using JsonDocument bagDoc = JsonDocument.Parse("""{"env":"prod","tags":["a","b"]}""");

        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<string>("K", isNullable: false)
                .WithRow("matched")
                .WithRow("missing")
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<string>("K", isNullable: false)
                .WithColumn<string>("StrCol", isNullable: false)
                .WithColumn<int>("IntCol", isNullable: false)
                .WithColumn<long>("LongCol", isNullable: false)
                .WithColumn<double>("RealCol", isNullable: false)
                .WithColumn<decimal>("DecCol", isNullable: false)
                .WithColumn<bool>("BoolCol", isNullable: false)
                .WithColumn<DateTime>("DtCol", isNullable: false)
                .WithColumn<TimeSpan>("TsCol", isNullable: false)
                .WithColumn<Guid>("GuidCol", isNullable: false)
                .WithColumn<object>("DynCol", isNullable: true)
                .WithRow("matched", "hello", 7, 700L, 3.25, 19.95m, true, dt, ts, g, bagDoc.RootElement.Clone())
                .Build());

        var rows = ExecuteJoin(db, "L | join kind=leftouter R on K");
        Assert.That(rows, Has.Count.EqualTo(2));

        var matched = rows.Single(r => (string)r["K"]! == "matched");
        Assert.That(matched["K1"], Is.EqualTo("matched"));
        Assert.That(matched["StrCol"], Is.EqualTo("hello"));
        Assert.That(ReadCell<int>(matched, "IntCol"), Is.EqualTo(7));
        Assert.That(ReadCell<long>(matched, "LongCol"), Is.EqualTo(700L));
        Assert.That(ReadCell<double>(matched, "RealCol"), Is.EqualTo(3.25));
        Assert.That(ReadCell<decimal>(matched, "DecCol"), Is.EqualTo(19.95m));
        Assert.That(matched["BoolCol"], Is.EqualTo(true));
        Assert.That(matched["DtCol"], Is.EqualTo(dt));
        Assert.That(matched["TsCol"], Is.EqualTo(ts));
        Assert.That(matched["GuidCol"], Is.EqualTo(g));
        var dynBag = ExtractDynamicBag(matched["DynCol"]);
        Assert.That(dynBag["env"], Is.EqualTo("prod"));
        Assert.That(ExtractDynamicArray(dynBag["tags"]), Is.EqualTo(new object?[] { "a", "b" }));

        var missing = rows.Single(r => (string)r["K"]! == "missing");
        Assert.That(missing["K1"], Is.EqualTo(""));
        Assert.That(missing["StrCol"], Is.EqualTo(""));
        Assert.That(missing["IntCol"], Is.Null);
        Assert.That(missing["LongCol"], Is.Null);
        Assert.That(missing["RealCol"], Is.Null);
        Assert.That(missing["DecCol"], Is.Null);
        Assert.That(missing["BoolCol"], Is.Null);
        Assert.That(missing["DtCol"], Is.Null);
        Assert.That(missing["TsCol"], Is.Null);
        Assert.That(missing["GuidCol"], Is.Null);
        Assert.That(missing["DynCol"], Is.Null);
    }

    [Test]
    [Description("Unmatched rightouter row pads missing left columns with empty string for strings and null for value/dynamic types.")]
    public void UnmatchedRightOuterRow_PadsMissingLeftColumnsCorrectly()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<int>("Id", isNullable: false)
                .WithColumn<string>("LName", isNullable: false)
                .WithColumn<bool>("LFlag", isNullable: false)
                .WithRow(1, "left-one", true)
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<int>("Id", isNullable: false)
                .WithColumn<string>("RName", isNullable: false)
                .WithRow(1, "right-one")
                .WithRow(2, "right-orphan")
                .Build());

        var rows = ExecuteJoin(db, "L | join kind=rightouter R on Id");
        var orphan = rows.Single(r => ReadCell<int>(r, "Id1") == 2);

        Assert.That(orphan["Id"], Is.Null);
        Assert.That(orphan["LName"], Is.EqualTo(""));
        Assert.That(orphan["LFlag"], Is.Null);
        Assert.That(orphan["RName"], Is.EqualTo("right-orphan"));
    }

    [Test]
    [Description("Leftouter join distinguishes a matched right row whose payload is null/empty from an unmatched row via the right key column.")]
    public void LeftOuterJoin_DistinguishesMatchedNullPayloadFromUnmatchedRow()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("Events")
                .WithColumn<long>("DeviceId", isNullable: false)
                .WithColumn<string>("EventId", isNullable: false)
                .WithRow(1L, "e-matched-null-payload")
                .WithRow(2L, "e-unmatched")
                .Build(),
            new NullableTableBuilder("Devices")
                .WithColumn<long>("DeviceId", isNullable: false)
                .WithColumn<int>("Score", isNullable: true)
                .WithColumn<string>("Notes", isNullable: true)
                .WithRow(1L, null, "")
                .Build());

        string query = """
            Events
            | join kind=leftouter Devices on DeviceId
            | extend HasDeviceRow = isnotnull(DeviceId1)
            | project EventId, HasDeviceRow, Score, Notes
            """;

        var actual = ExecuteAndGetNullable4Columns<string, bool, int?, string>(
            "EventId", "HasDeviceRow", "Score", "Notes", db, query);

        Assert.That(actual, Is.EquivalentTo(new (string, bool, int?, string)[]
        {
            ("e-matched-null-payload", true,  null, ""),
            ("e-unmatched",            false, null, "")
        }));
    }

    #endregion
}
