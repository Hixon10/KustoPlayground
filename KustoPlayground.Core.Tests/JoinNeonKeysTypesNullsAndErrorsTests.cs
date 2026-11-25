using System.Text.Json;
using static KustoPlayground.Core.Tests.JoinNeonTestUtils;
using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

public class JoinNeonKeysTypesNullsAndErrorsTests
{
    #region Same-name, explicit, parenthesized, and bracketed join keys

    [Test]
    [Description("Explicit equality ($left.DeviceId == $right.Id) joins columns with different names without renaming.")]
    public void ExplicitJoinKey_DifferentColumnNames_MatchesWithoutRenaming()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("Events")
                .WithColumn<string>("EventId", false)
                .WithColumn<string>("DeviceId", false)
                .WithRow("e1", "d-100")
                .WithRow("e2", "d-200")
                .WithRow("e3", "d-999")
                .Build(),
            new NullableTableBuilder("Inventory")
                .WithColumn<string>("Id", false)
                .WithColumn<string>("Hostname", false)
                .WithRow("d-100", "web-01")
                .WithRow("d-200", "api-02")
                .WithRow("d-300", "db-03")
                .Build());

        var rows = ExecuteJoin(db, "Events | join kind=leftouter Inventory on $left.DeviceId == $right.Id");
        AssertExactColumnOrder(rows, "EventId", "DeviceId", "Id", "Hostname");

        var actual = rows.Select(r => (
            (string)r["EventId"]!,
            (string)r["DeviceId"]!,
            (string)r["Id"]!,
            (string)r["Hostname"]!)).ToList();

        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("e1", "d-100", "d-100", "web-01"),
            ("e2", "d-200", "d-200", "api-02"),
            ("e3", "d-999", "", "")
        }));
    }

    [TestCase("L | join kind=inner R on DeviceId")]
    [TestCase("L | join kind=inner R on $left.DeviceId == $right.DeviceId")]
    [TestCase("L | join kind=inner R on ($left.DeviceId == $right.DeviceId)")]
    [Description("Same-name shorthand, explicit equality on identical column names, and parenthesized explicit equality produce identical results.")]
    public void SameNameExplicitAndParenthesizedJoinConditions_ProduceIdenticalResults(string query)
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<string>("DeviceId", false).WithColumn<int>("LVal", false)
                .WithRow("d1", 1).WithRow("d2", 2).Build(),
            new NullableTableBuilder("R").WithColumn<string>("DeviceId", false).WithColumn<int>("RVal", false)
                .WithRow("d1", 10).WithRow("d3", 30).Build());

        var actual = ExecuteAndGetNullable4Columns<string, int, string, int>(
            "DeviceId", "LVal", "DeviceId1", "RVal", db, query);
        Assert.That(actual, Is.EquivalentTo(new[] { ("d1", 1, "d1", 10) }));
    }

    [TestCase("L | join kind=inner R on ['Device Id'] | project LVal, RVal")]
    [TestCase("L | join kind=inner R on $left.['Device Id'] == $right.['Target Id'] | project LVal, RVal")]
    [TestCase("L | join kind=inner R on $left.[\"Device Id\"] == $right.[\"Target Id\"] | project LVal, RVal")]
    [Description("Bracketed column identifiers with spaces work in both same-name shorthand and explicit $left/$right join conditions.")]
    public void BracketedColumnNamesWithSpaces_WorkInShorthandAndExplicitJoinKeys(string query)
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<string>("Device Id", false)
                .WithColumn<int>("LVal", false)
                .WithRow("dev-1", 100)
                .WithRow("dev-2", 200)
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<string>("Device Id", false)
                .WithColumn<string>("Target Id", false)
                .WithColumn<int>("RVal", false)
                .WithRow("dev-1", "dev-1", 10)
                .WithRow("dev-3", "dev-2", 20)
                .Build());

        var actual = ExecuteAndGetNullable2Columns<int, int>("LVal", "RVal", db, query);
        if (query.Contains("Target Id"))
        {
            Assert.That(actual, Is.EquivalentTo(new[] { (100, 10), (200, 20) }));
        }
        else
        {
            Assert.That(actual, Is.EquivalentTo(new[] { (100, 10) }));
        }
    }

    #endregion

    #region Multiple join keys (composite keys)

    private static KustoDatabase CreateMultiTenantDatabase()
    {
        // Both tenant-A and tenant-B have a device named "dev-1" — multi-key join must keep them separate.
        Table events = new NullableTableBuilder("Events")
            .WithColumn<string>("EventId", false)
            .WithColumn<string>("TenantId", false)
            .WithColumn<string>("DeviceId", false)
            .WithRow("e1", "tenant-A", "dev-1")
            .WithRow("e2", "tenant-B", "dev-1")
            .WithRow("e3", "tenant-A", "dev-2")
            .WithRow("e4", "tenant-A", "dev-missing")
            .WithRow("e5", "tenant-C", "dev-1")
            .Build();

        Table devices = new NullableTableBuilder("Devices")
            .WithColumn<string>("TenantId", false)
            .WithColumn<string>("DeviceId", false)
            .WithColumn<string>("OrgId", false)
            .WithColumn<string>("MachineId", false)
            .WithColumn<string>("Owner", false)
            .WithRow("tenant-A", "dev-1", "tenant-A", "dev-1", "Alice-A")
            .WithRow("tenant-B", "dev-1", "tenant-B", "dev-1", "Bob-B")
            .WithRow("tenant-A", "dev-2", "tenant-A", "dev-2", "Carol-A")
            .WithRow("tenant-B", "dev-2", "tenant-B", "dev-2", "Dave-B")
            .Build();

        return CreateDatabase(events, devices);
    }

    [TestCase("Events | join kind=inner Devices on TenantId, DeviceId")]
    [TestCase("Events | join kind=inner Devices on TenantId and DeviceId")]
    [TestCase("Events | join kind=inner Devices on $left.TenantId == $right.OrgId and $left.DeviceId == $right.MachineId")]
    [TestCase("Events | join kind=inner Devices on $left.TenantId == $right.OrgId, $left.DeviceId == $right.MachineId")]
    [TestCase("Events | join kind=inner Devices on ($left.TenantId == $right.OrgId) and ($left.DeviceId == $right.MachineId)")]
    [TestCase("Events | join kind=inner Devices on TenantId, $left.DeviceId == $right.MachineId")]
    [TestCase("Events | join kind=inner Devices on $left.TenantId == $right.OrgId, DeviceId")]
    [Description("Multiple join keys (comma, and, explicit equalities, parenthesized, and mixed forms) require ALL keys to match so identical DeviceIds in different tenants never cross-match.")]
    public void MultiKeyJoin_AllSyntaxes_KeepSameDeviceIdInDifferentTenantsSeparate(string query)
    {
        KustoDatabase db = CreateMultiTenantDatabase();
        var actual = ExecuteAndGetNullable2Columns<string, string>("EventId", "Owner", db, query);

        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("e1", "Alice-A"),
            ("e2", "Bob-B"),
            ("e3", "Carol-A")
        }));
    }

    [Test]
    [Description("Composite join keys are positional: (A='x', B='y') does not match (A='y', B='x').")]
    public void MultiKeyJoin_SwappedValuesAcrossColumns_DoNotMatch()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<string>("A", false).WithColumn<string>("B", false).WithColumn<int>("LId", false)
                .WithRow("x", "y", 1).WithRow("a", "b", 2).Build(),
            new NullableTableBuilder("R").WithColumn<string>("A", false).WithColumn<string>("B", false).WithColumn<int>("RId", false)
                .WithRow("y", "x", 10).WithRow("a", "b", 20).Build());

        var actual = ExecuteAndGetNullable2Columns<int, int>("LId", "RId", db, "L | join kind=inner R on A, B");
        Assert.That(actual, Is.EquivalentTo(new[] { (2, 20) }));
    }

    [Test]
    [Description("Three-key composite join across mixed scalar types (string, int, bool) matches only when all three columns match.")]
    public void ThreeKeyCompositeJoin_MixedScalarTypes_MatchesOnlyExactTriples()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<string>("Tenant", false)
                .WithColumn<int>("Shard", false)
                .WithColumn<bool>("IsProd", false)
                .WithColumn<string>("LTag", false)
                .WithRow("t1", 1, true, "L1")
                .WithRow("t1", 1, false, "L2")
                .WithRow("t1", 2, true, "L3")
                .WithRow("t2", 1, true, "L4")
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<string>("Tenant", false)
                .WithColumn<int>("Shard", false)
                .WithColumn<bool>("IsProd", false)
                .WithColumn<string>("RTag", false)
                .WithRow("t1", 1, true, "R1")
                .WithRow("t1", 2, false, "R2")
                .WithRow("t2", 1, true, "R3")
                .Build());

        var actual = ExecuteAndGetNullable2Columns<string, string>(
            "LTag", "RTag", db, "L | join kind=inner R on Tenant, Shard, IsProd");
        Assert.That(actual, Is.EquivalentTo(new[] { ("L1", "R1"), ("L4", "R3") }));
    }

    [Test]
    [Description("Multi-key innerunique deduplicates by the composite tuple (TenantId, DeviceId), not by TenantId alone.")]
    public void MultiKeyInnerUnique_DeduplicatesByFullCompositeKey()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<string>("TenantId", false)
                .WithColumn<string>("DeviceId", false)
                .WithColumn<int>("Seq", false)
                .WithRow("t1", "d1", 1)
                .WithRow("t1", "d1", 2)
                .WithRow("t1", "d2", 3)
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<string>("TenantId", false)
                .WithColumn<string>("DeviceId", false)
                .WithColumn<string>("Note", false)
                .WithRow("t1", "d1", "n1")
                .WithRow("t1", "d1", "n2")
                .WithRow("t1", "d2", "n3")
                .Build());

        var rows = ExecuteJoin(db, "L | join R on TenantId, DeviceId");
        // (t1, d1): 1 surviving left x 2 right = 2 rows
        // (t1, d2): 1 surviving left x 1 right = 1 row
        Assert.That(rows, Has.Count.EqualTo(3));

        var d1Rows = rows.Where(r => (string)r["DeviceId"]! == "d1").ToList();
        Assert.That(d1Rows, Has.Count.EqualTo(2));
        Assert.That(d1Rows.Select(r => ReadCell<int>(r, "Seq")).Distinct().ToList(), Has.Count.EqualTo(1));
        Assert.That(d1Rows.Select(r => (string)r["Note"]!), Is.EquivalentTo(new[] { "n1", "n2" }));

        var d2Rows = rows.Where(r => (string)r["DeviceId"]! == "d2").ToList();
        Assert.That(d2Rows, Has.Count.EqualTo(1));
        Assert.That((ReadCell<int>(d2Rows[0], "Seq"), (string)d2Rows[0]["Note"]!), Is.EqualTo((3, "n3")));
    }

    [Test]
    [Description("Multi-key leftanti and leftouter treat partial key matches (same TenantId, different DeviceId) as unmatched.")]
    public void MultiKeyLeftAntiAndLeftOuter_PartialKeyMatchesAreUnmatched()
    {
        KustoDatabase db = CreateMultiTenantDatabase();

        var antiEvents = ExecuteAndGetNullableColumn<string>(
            "EventId", db, "Events | join kind=leftanti Devices on TenantId, DeviceId");
        Assert.That(antiEvents, Is.EquivalentTo(new[] { "e4", "e5" }));

        var leftOuter = ExecuteAndGetNullable2Columns<string, string>(
            "EventId", "Owner", db, "Events | join kind=leftouter Devices on TenantId, DeviceId");
        Assert.That(leftOuter, Is.EquivalentTo(new[]
        {
            ("e1", "Alice-A"),
            ("e2", "Bob-B"),
            ("e3", "Carol-A"),
            ("e4", ""),
            ("e5", "")
        }));
    }

    [Test]
    [Description("Redundant duplicate key in on clause (on K, K) and comparing one left key to two right columns ($left.A == $right.X and $left.A == $right.Y) both work.")]
    public void DuplicateAndSharedColumnInOnClause_MatchesCorrectly()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<int>("A", false).WithColumn<string>("Tag", false)
                .WithRow(5, "five").WithRow(7, "seven").Build(),
            new NullableTableBuilder("R")
                .WithColumn<int>("A", false)
                .WithColumn<int>("X", false)
                .WithColumn<int>("Y", false)
                .WithColumn<string>("RTag", false)
                .WithRow(5, 5, 5, "both-five")
                .WithRow(5, 5, 9, "only-x-five")
                .WithRow(7, 7, 7, "both-seven")
                .Build());

        var redundant = ExecuteAndGetNullable2Columns<string, string>(
            "Tag", "RTag", db, "L | join kind=inner R on A, A");
        Assert.That(redundant, Is.EquivalentTo(new[]
        {
            ("five", "both-five"),
            ("five", "only-x-five"),
            ("seven", "both-seven")
        }));

        var sharedLeft = ExecuteAndGetNullable2Columns<string, string>(
            "Tag", "RTag", db, "L | join kind=inner R on $left.A == $right.X and $left.A == $right.Y");
        Assert.That(sharedLeft, Is.EquivalentTo(new[]
        {
            ("five", "both-five"),
            ("seven", "both-seven")
        }));
    }

    #endregion

    #region Scalar key types, case sensitivity, numeric edge cases, and int/long compatibility

    [Test]
    [Description("String join keys are strictly case-sensitive, whitespace-sensitive, and Unicode-exact.")]
    public void StringJoinKeys_AreCaseSensitiveWhitespaceSensitiveAndUnicodeExact()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<string>("K", false).WithColumn<int>("LId", false)
                .WithRow("dev-1", 1)
                .WithRow("DEV-1", 2)
                .WithRow("Dev-1", 3)
                .WithRow(" dev-1", 4)
                .WithRow("dev-1 ", 5)
                .WithRow("caf\u00e9", 6)
                .WithRow("cafe", 7)
                .WithRow("", 8)
                .Build(),
            new NullableTableBuilder("R").WithColumn<string>("K", false).WithColumn<int>("RId", false)
                .WithRow("dev-1", 10)
                .WithRow("DEV-1", 20)
                .WithRow("caf\u00e9", 60)
                .WithRow("", 80)
                .Build());

        var actual = ExecuteAndGetNullable2Columns<int, int>("LId", "RId", db, "L | join kind=inner R on K");
        Assert.That(actual, Is.EquivalentTo(new[] { (1, 10), (2, 20), (6, 60), (8, 80) }));
    }

    [Test]
    [Description("Users needing case-insensitive string joins can normalize keys explicitly with extend + tolower() prior to joining.")]
    public void CaseInsensitiveJoinIdiom_ViaToLowerExtend_MatchesMixedCaseKeys()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("Events").WithColumn<string>("DeviceId", false).WithColumn<string>("EventId", false)
                .WithRow("Dev-Alpha", "e1").WithRow("DEV-BETA", "e2").Build(),
            new NullableTableBuilder("Devices").WithColumn<string>("DeviceId", false).WithColumn<string>("Owner", false)
                .WithRow("dev-alpha", "Alice").WithRow("dev-beta", "Bob").Build());

        string query = """
            Events
            | extend NormalizedKey = tolower(DeviceId)
            | join kind=inner (Devices | extend NormalizedKey = tolower(DeviceId)) on NormalizedKey
            | project EventId, Owner
            """;

        var actual = ExecuteAndGetNullable2Columns<string, string>("EventId", "Owner", db, query);
        Assert.That(actual, Is.EquivalentTo(new[] { ("e1", "Alice"), ("e2", "Bob") }));
    }

    [Test]
    [Description("Explicit equality allows joining int against long (in either direction), preserves boundary values, and does not wrap long values outside int range.")]
    public void ExplicitIntToLongJoinKey_MatchesNumericEqualityWithoutOverflow()
    {
        long overflowedLong = (long)int.MaxValue + 1L;
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("LeftInt")
                .WithColumn<int>("IntKey", true)
                .WithColumn<string>("LTag", false)
                .WithRow(0, "zero")
                .WithRow(-42, "neg")
                .WithRow(int.MaxValue, "max")
                .WithRow(int.MinValue, "min")
                .WithRow(null, "null-key")
                .Build(),
            new NullableTableBuilder("RightLong")
                .WithColumn<long>("LongKey", true)
                .WithColumn<string>("RTag", false)
                .WithRow(0L, "r-zero")
                .WithRow(-42L, "r-neg")
                .WithRow((long)int.MaxValue, "r-max")
                .WithRow((long)int.MinValue, "r-min")
                .WithRow(overflowedLong, "r-overflow")
                .WithRow(null, "r-null")
                .Build());

        var intToLong = ExecuteAndGetNullable2Columns<string, string>(
            "LTag", "RTag", db, "LeftInt | join kind=inner RightLong on $left.IntKey == $right.LongKey");
        Assert.That(intToLong, Is.EquivalentTo(new[]
        {
            ("zero", "r-zero"),
            ("neg", "r-neg"),
            ("max", "r-max"),
            ("min", "r-min"),
            ("null-key", "r-null")
        }));

        var longToInt = ExecuteAndGetNullable2Columns<string, string>(
            "RTag", "LTag", db, "RightLong | join kind=inner LeftInt on $left.LongKey == $right.IntKey");
        Assert.That(longToInt, Is.EquivalentTo(new[]
        {
            ("r-zero", "zero"),
            ("r-neg", "neg"),
            ("r-max", "max"),
            ("r-min", "min"),
            ("r-null", "null-key")
        }));
    }

    [Test]
    [Description("Join works across all supported non-string scalar key types: int, long, real, decimal, bool, datetime, timespan, and guid.")]
    public void AllScalarKeyTypes_MatchCorrectly()
    {
        DateTime dt1 = new(2025, 3, 15, 10, 30, 0, DateTimeKind.Utc);
        DateTime dt2 = new(2025, 3, 16, 12, 0, 0, DateTimeKind.Utc);
        TimeSpan ts1 = TimeSpan.FromMinutes(15);
        TimeSpan ts2 = TimeSpan.FromHours(2);
        Guid g1 = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Guid g2 = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<int>("RowId", false)
                .WithColumn<int>("IntK", false)
                .WithColumn<long>("LongK", false)
                .WithColumn<double>("RealK", false)
                .WithColumn<decimal>("DecK", false)
                .WithColumn<bool>("BoolK", false)
                .WithColumn<DateTime>("DtK", false)
                .WithColumn<TimeSpan>("TsK", false)
                .WithColumn<Guid>("GuidK", false)
                .WithRow(1, 7, 7000000000L, 1.5, 12.30m, true, dt1, ts1, g1)
                .WithRow(2, 8, 8000000000L, 2.5, 99.99m, false, dt2, ts2, g2)
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<int>("MatchId", false)
                .WithColumn<int>("IntK", false)
                .WithColumn<long>("LongK", false)
                .WithColumn<double>("RealK", false)
                .WithColumn<decimal>("DecK", false)
                .WithColumn<bool>("BoolK", false)
                .WithColumn<DateTime>("DtK", false)
                .WithColumn<TimeSpan>("TsK", false)
                .WithColumn<Guid>("GuidK", false)
                .WithRow(10, 7, 7000000000L, 1.5, 12.3m, true, dt1, ts1, g1)
                .Build());

        foreach (string keyCol in new[] { "IntK", "LongK", "RealK", "DecK", "BoolK", "DtK", "TsK", "GuidK" })
        {
            var pairs = ExecuteAndGetNullable2Columns<int, int>(
                "RowId", "MatchId", db, $"L | join kind=inner R on {keyCol}");
            Assert.That(pairs, Is.EquivalentTo(new[] { (1, 10) }), $"Failed join on scalar key column '{keyCol}'.");
        }
    }

    [Test]
    [Description("Real key edge cases: -0.0 does not match +0.0 (even though -0.0 == 0.0 is true), NaN matches NaN (not null), +Infinity matches +Infinity (not -Infinity); decimal 1.0m matches 1.00m.")]
    public void RealAndDecimalKeyEdgeCases_SignedZeroNaNInfinityAndTrailingScale()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("RealL").WithColumn<int>("LId", false).WithColumn<double>("K", true)
                .WithRow(1, -0.0)
                .WithRow(2, double.NaN)
                .WithRow(3, double.PositiveInfinity)
                .WithRow(4, double.NegativeInfinity)
                .WithRow(5, null)
                .Build(),
            new NullableTableBuilder("RealR").WithColumn<int>("RId", false).WithColumn<double>("K", true)
                .WithRow(10, +0.0)
                .WithRow(20, double.NaN)
                .WithRow(30, double.PositiveInfinity)
                .WithRow(40, double.NegativeInfinity)
                .WithRow(50, null)
                .Build(),
            new NullableTableBuilder("DecL").WithColumn<int>("LId", false).WithColumn<decimal>("K", true)
                .WithRow(1, 1.0m)
                .WithRow(2, 0.00m)
                .WithRow(3, null)
                .Build(),
            new NullableTableBuilder("DecR").WithColumn<int>("RId", false).WithColumn<decimal>("K", true)
                .WithRow(10, 1.000m)
                .WithRow(20, 0m)
                .WithRow(30, null)
                .Build());

        var realPairs = ExecuteAndGetNullable2Columns<int, int>("LId", "RId", db, "RealL | join kind=inner RealR on K");
        Assert.That(realPairs, Is.EquivalentTo(new[] { (2, 20), (3, 30), (4, 40), (5, 50) }));

        var decPairs = ExecuteAndGetNullable2Columns<int, int>("LId", "RId", db, "DecL | join kind=inner DecR on K");
        Assert.That(decPairs, Is.EquivalentTo(new[] { (1, 10), (2, 20), (3, 30) }));
    }

    #endregion

    #region Duplicate null-key semantics

    [Test]
    [Description("Duplicate null keys on both sides (2 left x 3 right) follow exact multiplicity rules across every join kind.")]
    public void DuplicateNullKeysOnBothSides_FollowJoinFlavorMultiplicityRules()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<int>("LId", false).WithColumn<long>("K", true)
                .WithRow(1, null).WithRow(2, null).Build(),
            new NullableTableBuilder("R").WithColumn<int>("RId", false).WithColumn<long>("K", true)
                .WithRow(10, null).WithRow(20, null).WithRow(30, null).Build());

        var inner = ExecuteAndGetNullable2Columns<int, int>("LId", "RId", db, "L | join kind=inner R on K");
        Assert.That(inner, Is.EquivalentTo(new[]
        {
            (1, 10), (1, 20), (1, 30),
            (2, 10), (2, 20), (2, 30)
        }));

        var innerUnique = ExecuteAndGetNullable2Columns<int, int>("LId", "RId", db, "L | join R on K");
        Assert.That(innerUnique, Has.Count.EqualTo(3));
        Assert.That(innerUnique.Select(r => r.Item1).Distinct().ToList(), Has.Count.EqualTo(1));
        Assert.That(innerUnique.Select(r => r.Item2), Is.EquivalentTo(new[] { 10, 20, 30 }));

        Assert.That(ExecuteAndGetNullableColumn<int>("LId", db, "L | join kind=leftsemi R on K"), Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(ExecuteAndGetNullableColumn<int>("RId", db, "L | join kind=rightsemi R on K"), Is.EquivalentTo(new[] { 10, 20, 30 }));
        Assert.That(ExecuteJoin(db, "L | join kind=leftanti R on K"), Is.Empty);
        Assert.That(ExecuteJoin(db, "L | join kind=rightanti R on K"), Is.Empty);
    }

    [Test]
    [Description("Null key never matches default values (0, 0.0, false, DateTime.MinValue, TimeSpan.Zero, Guid.Empty), whereas nullable string null normalizes to empty string in KQL.")]
    public void NullKey_NeverMatchesDefaultValuesExceptStringNormalization()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("NumL").WithColumn<int>("LId", false).WithColumn<long>("K", true)
                .WithRow(1, null).Build(),
            new NullableTableBuilder("NumR").WithColumn<int>("RId", false).WithColumn<long>("K", true)
                .WithRow(10, 0L).Build(),
            new NullableTableBuilder("BoolL").WithColumn<int>("LId", false).WithColumn<bool>("K", true)
                .WithRow(1, null).Build(),
            new NullableTableBuilder("BoolR").WithColumn<int>("RId", false).WithColumn<bool>("K", true)
                .WithRow(10, false).Build(),
            new NullableTableBuilder("StrL").WithColumn<int>("LId", false).WithColumn<string>("K", true)
                .WithRow(1, null).Build(),
            new NullableTableBuilder("StrR").WithColumn<int>("RId", false).WithColumn<string>("K", false)
                .WithRow(10, "").Build());

        Assert.That(ExecuteJoin(db, "NumL | join kind=inner NumR on K"), Is.Empty);
        Assert.That(ExecuteJoin(db, "BoolL | join kind=inner BoolR on K"), Is.Empty);

        // KQL strings have no null value: a stored null string is treated as "" and matches "".
        var strMatch = ExecuteAndGetNullable2Columns<int, int>("LId", "RId", db, "StrL | join kind=inner StrR on K");
        Assert.That(strMatch, Is.EquivalentTo(new[] { (1, 10) }));
    }

    [Test]
    [Description("Composite keys with partial or full nulls match only when both key positions and nullness match identically.")]
    public void PartialNullCompositeKeys_MatchExactNullPositionsOnly()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<int>("LId", false)
                .WithColumn<int>("K1", true)
                .WithColumn<int>("K2", true)
                .WithRow(1, null, 10)
                .WithRow(2, 10, null)
                .WithRow(3, null, null)
                .WithRow(4, 10, 20)
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<int>("RId", false)
                .WithColumn<int>("K1", true)
                .WithColumn<int>("K2", true)
                .WithRow(100, null, 10)
                .WithRow(200, null, null)
                .WithRow(300, 10, 20)
                .Build());

        var inner = ExecuteAndGetNullable2Columns<int, int>("LId", "RId", db, "L | join kind=inner R on K1, K2");
        Assert.That(inner, Is.EquivalentTo(new[] { (1, 100), (3, 200), (4, 300) }));

        var leftAnti = ExecuteAndGetNullableColumn<int>("LId", db, "L | join kind=leftanti R on K1, K2");
        Assert.That(leftAnti, Is.EquivalentTo(new[] { 2 }));
    }

    [Test]
    [Description("A null key present on only one side is unmatched and behaves consistently across outer, semi, and anti joins.")]
    public void OneSidedNullKey_IsUnmatchedAcrossOuterSemiAndAntiJoins()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<int>("LId", false)
                .WithColumn<int>("K", true)
                .WithRow(1, 10)
                .WithRow(2, null)
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<int>("RId", false)
                .WithColumn<int>("K", true)
                .WithRow(100, 10)
                .WithRow(200, 20)
                .Build());

        Assert.That(ExecuteAndGetNullable2Columns<int, int>("LId", "RId", db, "L | join kind=inner R on K"),
            Is.EquivalentTo(new[] { (1, 100) }));
        Assert.That(ExecuteAndGetNullable2Columns<int, int?>("LId", "RId", db, "L | join kind=leftouter R on K"),
            Is.EquivalentTo(new (int, int?)[] { (1, 100), (2, null) }));
        Assert.That(ExecuteAndGetNullableColumn<int>("LId", db, "L | join kind=leftsemi R on K"),
            Is.EquivalentTo(new[] { 1 }));
        Assert.That(ExecuteAndGetNullableColumn<int>("LId", db, "L | join kind=leftanti R on K"),
            Is.EquivalentTo(new[] { 2 }));

        // Swap so null exists only on the right side.
        Assert.That(ExecuteAndGetNullable2Columns<int?, int>("RId", "LId", db, "R | join kind=rightouter L on K"),
            Is.EquivalentTo(new (int?, int)[] { (100, 1), (null, 2) }));
        Assert.That(ExecuteAndGetNullableColumn<int>("LId", db, "R | join kind=rightsemi L on K"),
            Is.EquivalentTo(new[] { 1 }));
        Assert.That(ExecuteAndGetNullableColumn<int>("LId", db, "R | join kind=rightanti L on K"),
            Is.EquivalentTo(new[] { 2 }));
    }

    [Test]
    [Description("Explicit int == long equality works consistently across outer, semi, and anti joins (not only inner).")]
    public void ExplicitIntToLongJoinKey_WorksAcrossOuterSemiAndAntiFlavors()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<int>("IntKey", false)
                .WithColumn<string>("LName", false)
                .WithRow(1, "l1")
                .WithRow(2, "l2")
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<long>("LongKey", false)
                .WithColumn<string>("RName", false)
                .WithRow(1L, "r1")
                .WithRow(3L, "r3")
                .Build());

        Assert.That(
            ExecuteAndGetNullable2Columns<string, string>(
                "LName", "RName", db, "L | join kind=leftouter R on $left.IntKey == $right.LongKey"),
            Is.EquivalentTo(new[] { ("l1", "r1"), ("l2", "") }));

        Assert.That(
            ExecuteAndGetNullable2Columns<string, string>(
                "LName", "RName", db, "L | join kind=fullouter R on $left.IntKey == $right.LongKey"),
            Is.EquivalentTo(new[] { ("l1", "r1"), ("l2", ""), ("", "r3") }));

        Assert.That(
            ExecuteAndGetNullableColumn<string>("LName", db, "L | join kind=leftsemi R on $left.IntKey == $right.LongKey"),
            Is.EquivalentTo(new[] { "l1" }));

        Assert.That(
            ExecuteAndGetNullableColumn<string>("LName", db, "L | join kind=leftanti R on $left.IntKey == $right.LongKey"),
            Is.EquivalentTo(new[] { "l2" }));

        Assert.That(
            ExecuteAndGetNullableColumn<string>("RName", db, "L | join kind=rightanti R on $left.IntKey == $right.LongKey"),
            Is.EquivalentTo(new[] { "r3" }));
    }

    [Test]
    [Description("Bracket-quoted KQL keyword column names (e.g., ['kind'], ['on']) work in both shorthand and explicit join keys.")]
    public void KeywordColumnNames_WorkInShorthandAndExplicitJoinKeys()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<string>("kind", false)
                .WithColumn<int>("on", false)
                .WithColumn<string>("LTag", false)
                .WithRow("alpha", 1, "L1")
                .WithRow("beta", 2, "L2")
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<string>("kind", false)
                .WithColumn<int>("on", false)
                .WithColumn<string>("RTag", false)
                .WithRow("alpha", 1, "R1")
                .WithRow("alpha", 9, "R2")
                .Build());

        var shorthand = ExecuteAndGetNullable2Columns<string, string>(
            "LTag", "RTag", db, "L | join kind=inner R on ['kind'], ['on']");
        Assert.That(shorthand, Is.EquivalentTo(new[] { ("L1", "R1") }));

        var explicitJoin = ExecuteAndGetNullable2Columns<string, string>(
            "LTag", "RTag", db, "L | join kind=inner R on $left.['kind'] == $right.['kind'] and $left.['on'] == $right.['on']");
        Assert.That(explicitJoin, Is.EquivalentTo(new[] { ("L1", "R1") }));
    }

    [Test]
    [Description("Column names are case-sensitive: DeviceId vs deviceid fails shorthand on DeviceId, succeeds with explicit equality, and produces no collision suffix.")]
    public void ColumnNameCaseSensitivity_RequiresExplicitEqualityAndAvoidsCollisionSuffix()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<string>("DeviceId", false)
                .WithColumn<string>("LVal", false)
                .WithRow("dev-1", "left-1")
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<string>("deviceid", false)
                .WithColumn<string>("RVal", false)
                .WithRow("dev-1", "right-1")
                .Build());

        AssertJoinError(db, "L | join kind=inner R on DeviceId");

        var rows = ExecuteJoin(db, "L | join kind=inner R on $left.DeviceId == $right.deviceid");
        AssertExactColumnOrder(rows, "DeviceId", "LVal", "deviceid", "RVal");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["LVal"], Is.EqualTo("left-1"));
        Assert.That(rows[0]["RVal"], Is.EqualTo("right-1"));
    }

    #endregion

    #region Incompatible key types and dynamic key rules

    [TestCase("L | join kind=inner R on $left.StrK == $right.LongK")]
    [TestCase("L | join kind=inner R on $left.LongK == $right.StrK")]
    [TestCase("L | join kind=inner R on $left.StrK == $right.IntK")]
    [TestCase("L | join kind=inner R on $left.StrK == $right.RealK")]
    [TestCase("L | join kind=inner R on $left.StrK == $right.BoolK")]
    [TestCase("L | join kind=inner R on $left.StrK == $right.DtK")]
    [TestCase("L | join kind=inner R on $left.StrK == $right.GuidK")]
    [TestCase("L | join kind=inner R on $left.IntK == $right.RealK")]
    [TestCase("L | join kind=inner R on $left.LongK == $right.RealK")]
    [TestCase("L | join kind=inner R on $left.IntK == $right.DecK")]
    [TestCase("L | join kind=inner R on $left.LongK == $right.DecK")]
    [TestCase("L | join kind=inner R on $left.RealK == $right.DecK")]
    [TestCase("L | join kind=inner R on $left.BoolK == $right.IntK")]
    [TestCase("L | join kind=inner R on $left.TsK == $right.LongK")]
    [TestCase("L | join kind=inner R on MismatchedStrLong")]
    [Description("Joining on incompatible key types (e.g., string vs long, int vs real, or mismatched same-name shorthand) is rejected.")]
    public void IncompatibleJoinKeyTypes_AreRejectedWithError(string query)
    {
        DateTime dt = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Guid g = Guid.Parse("11111111-1111-1111-1111-111111111111");

        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<string>("StrK", false)
                .WithColumn<int>("IntK", false)
                .WithColumn<long>("LongK", false)
                .WithColumn<double>("RealK", false)
                .WithColumn<decimal>("DecK", false)
                .WithColumn<bool>("BoolK", false)
                .WithColumn<DateTime>("DtK", false)
                .WithColumn<TimeSpan>("TsK", false)
                .WithColumn<Guid>("GuidK", false)
                .WithColumn<string>("MismatchedStrLong", false)
                .WithColumn<int>("MismatchedIntLong", false)
                .WithRow("1", 1, 1L, 1.0, 1m, true, dt, TimeSpan.FromSeconds(1), g, "1", 1)
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<string>("StrK", false)
                .WithColumn<int>("IntK", false)
                .WithColumn<long>("LongK", false)
                .WithColumn<double>("RealK", false)
                .WithColumn<decimal>("DecK", false)
                .WithColumn<bool>("BoolK", false)
                .WithColumn<DateTime>("DtK", false)
                .WithColumn<TimeSpan>("TsK", false)
                .WithColumn<Guid>("GuidK", false)
                .WithColumn<long>("MismatchedStrLong", false)
                .WithColumn<long>("MismatchedIntLong", false)
                .WithRow("1", 1, 1L, 1.0, 1m, true, dt, TimeSpan.FromSeconds(1), g, 1L, 1L)
                .Build());

        AssertJoinError(db, query);
    }

    [Test]
    public void SameNameIntAndLongKeysMatchWithoutChangingTheirOutputTypes()
    {
        var db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<int>("MismatchedIntLong").WithRow(1).Build(),
            new NullableTableBuilder("R").WithColumn<long>("MismatchedIntLong").WithRow(1L).Build());
        var row = ExecuteJoin(db, "L | join kind=inner R on MismatchedIntLong").Single();
        Assert.That(row["MismatchedIntLong"], Is.TypeOf<int>().And.EqualTo(1));
        Assert.That(row["MismatchedIntLong1"], Is.TypeOf<long>().And.EqualTo(1L));
    }

    [TestCase("L | join kind=inner R on DynKey")]
    [TestCase("L | join kind=inner R on $left.DynKey == $right.DynKey")]
    [TestCase("L | join kind=inner R on $left.DynKey == $right.StrKey")]
    [TestCase("L | join kind=inner R on $left.StrKey == $right.DynKey")]
    [TestCase("L | join kind=inner R on $left.DynKey == $right.LongKey")]
    [TestCase("L | join kind=leftouter R on DynKey")]
    [TestCase("L | join kind=leftsemi R on DynKey")]
    [TestCase("L | join kind=leftanti R on DynKey")]
    [TestCase("L | join kind=inner R on StrKey, DynKey")]
    [TestCase("L | join kind=inner R on $left.StrKey == $right.StrKey and $left.DynKey == $right.DynKey")]
    [TestCase("L | extend ParsedKey = parse_json(RawJson).id | join kind=inner (R | extend ParsedKey = parse_json(RawJson).id) on ParsedKey")]
    [TestCase("L | extend ParsedKey = parse_json(RawJson).id | join kind=inner R on $left.ParsedKey == $right.StrKey")]
    [Description("Dynamic join keys are rejected (both shorthand and explicit) rather than silently stringified.")]
    public void DynamicJoinKeys_AreRejectedWithoutSilentStringification(string query)
    {
        using JsonDocument leftDoc = JsonDocument.Parse("\"dev-1\"");
        using JsonDocument rightDoc = JsonDocument.Parse("\"dev-1\"");

        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<object>("DynKey", true)
                .WithColumn<string>("StrKey", false)
                .WithColumn<long>("LongKey", false)
                .WithColumn<string>("RawJson", false)
                .WithRow(leftDoc.RootElement.Clone(), "dev-1", 1L, """{"id":"dev-1"}""")
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<object>("DynKey", true)
                .WithColumn<string>("StrKey", false)
                .WithColumn<long>("LongKey", false)
                .WithColumn<string>("RawJson", false)
                .WithRow(rightDoc.RootElement.Clone(), "dev-1", 1L, """{"id":"dev-1"}""")
                .Build());

        AssertJoinError(db, query);
    }

    [Test]
    [Description("Casting dynamic keys explicitly with tostring() or tolong() before joining succeeds and matches exact typed values.")]
    public void DynamicKeys_ExplicitlyCastBeforeJoin_Succeed()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("RawEvents")
                .WithColumn<string>("EventId", false)
                .WithColumn<string>("Payload", false)
                .WithRow("e1", """{"deviceId":"dev-1","tenantNum":100}""")
                .WithRow("e2", """{"deviceId":"dev-2","tenantNum":200}""")
                .Build(),
            new NullableTableBuilder("Devices")
                .WithColumn<string>("DeviceId", false)
                .WithColumn<long>("TenantNum", false)
                .WithColumn<string>("Owner", false)
                .WithRow("dev-1", 100L, "Alice")
                .WithRow("dev-2", 999L, "Bob")
                .Build());

        string stringCastQuery = """
            RawEvents
            | extend Parsed = parse_json(Payload)
            | extend DeviceId = tostring(Parsed.deviceId)
            | join kind=inner Devices on DeviceId
            | project EventId, Owner
            """;

        var byString = ExecuteAndGetNullable2Columns<string, string>("EventId", "Owner", db, stringCastQuery);
        Assert.That(byString, Is.EquivalentTo(new[] { ("e1", "Alice"), ("e2", "Bob") }));

        string compositeCastQuery = """
            RawEvents
            | extend Parsed = parse_json(Payload)
            | extend DeviceId = tostring(Parsed.deviceId), TenantNum = tolong(Parsed.tenantNum)
            | join kind=inner Devices on DeviceId, TenantNum
            | project EventId, Owner
            """;

        var byComposite = ExecuteAndGetNullable2Columns<string, string>("EventId", "Owner", db, compositeCastQuery);
        Assert.That(byComposite, Is.EquivalentTo(new[] { ("e1", "Alice") }));
    }

    #endregion

    #region Syntax and semantic error cases

    [TestCase("L | join R")]
    [TestCase("L | join kind=outer R on Id")]
    [TestCase("L | join kind=cross R on Id")]
    [TestCase("L | join kind=unknown R on Id")]
    [TestCase("L | join kind=INNER R on Id")]
    [TestCase("L | join kind=inner MissingTable on Id")]
    [TestCase("L | join kind=inner R on NoSuchColumn")]
    [TestCase("L | join kind=inner R on OnlyOnLeft")]
    [TestCase("L | join kind=inner R on OnlyOnRight")]
    [TestCase("L | join kind=inner R on $left.NoSuchColumn == $right.Id")]
    [TestCase("L | join kind=inner R on $left.Id == $right.NoSuchColumn")]
    [TestCase("L | join kind=inner R on $left.Id == $left.Id")]
    [TestCase("L | join kind=inner R on $right.Id == $right.Id")]
    [TestCase("L | join kind=inner R on $left.Id > $right.Id")]
    [TestCase("L | join kind=inner R on $left.Id < $right.Id")]
    [TestCase("L | join kind=inner R on $left.Id != $right.Id")]
    [TestCase("L | join kind=inner R on $left.Key =~ $right.Key")]
    [TestCase("L | join kind=inner R on $left.Id == $right.Id or $left.Key == $right.Key")]
    [TestCase("L | join kind=inner R on tolower($left.Key) == tolower($right.Key)")]
    [TestCase("L | join kind=inner R on $left.Id + 1 == $right.Id")]
    [TestCase("L | join kind=inner R on $left['Key'] == $right['Key']")]
    [Description("Malformed join syntax, unsupported kinds, missing or invalid columns, non-equality predicates, and expressions in on return execution errors.")]
    public void InvalidJoinQueries_ReturnExecutionError(string query)
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<int>("Id", false)
                .WithColumn<string>("Key", false)
                .WithColumn<string>("OnlyOnLeft", false)
                .WithRow(1, "a", "x")
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<int>("Id", false)
                .WithColumn<string>("Key", false)
                .WithColumn<string>("OnlyOnRight", false)
                .WithRow(1, "a", "y")
                .Build());

        AssertJoinError(db, query);
    }

    [Test]
    [Description("A repeated kind= parameter is accepted and the first one is used: kind=inner kind=leftouter is an inner join that drops the unmatched left row.")]
    public void RepeatedKindParameter_IsAcceptedAndTheFirstKindIsUsed()
    {
        KustoDatabase db = CreateDatabase(
            new NullableTableBuilder("L")
                .WithColumn<int>("Id", false)
                .WithColumn<string>("Key", false)
                .WithColumn<string>("OnlyOnLeft", false)
                .WithRow(1, "a", "x")
                .WithRow(2, "b", "z")
                .Build(),
            new NullableTableBuilder("R")
                .WithColumn<int>("Id", false)
                .WithColumn<string>("Key", false)
                .WithColumn<string>("OnlyOnRight", false)
                .WithRow(1, "a", "y")
                .Build());

        var rows = ExecuteJoin(db, "L | join kind=inner kind=leftouter R on Id");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertExactColumnOrder(rows, "Id", "Key", "OnlyOnLeft", "Id1", "Key1", "OnlyOnRight");
        Assert.That(ReadCell<int>(rows[0], "Id"), Is.EqualTo(1));
        Assert.That(ReadCell<string>(rows[0], "Key"), Is.EqualTo("a"));
        Assert.That(ReadCell<string>(rows[0], "OnlyOnLeft"), Is.EqualTo("x"));
        Assert.That(ReadCell<int>(rows[0], "Id1"), Is.EqualTo(1));
        Assert.That(ReadCell<string>(rows[0], "Key1"), Is.EqualTo("a"));
        Assert.That(ReadCell<string>(rows[0], "OnlyOnRight"), Is.EqualTo("y"));
    }

    [Test]
    public void RightOperandFirstMatchesTheSameKeyPair()
    {
        var db = CreateDatabase(
            new NullableTableBuilder("L").WithColumn<int>("Id").WithRow(1).WithRow(2).Build(),
            new NullableTableBuilder("R").WithColumn<int>("Id").WithRow(1).Build());
        var row = ExecuteJoin(db, "L | join kind=inner R on $right.Id == $left.Id").Single();
        Assert.That(row["Id"], Is.EqualTo(1));
        Assert.That(row["Id1"], Is.EqualTo(1));
    }

    #endregion
}
