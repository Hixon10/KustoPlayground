using NullableTable = KustoPlayground.Core.Tests.SummarizeOperatorOpusTableBuilder;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

// Forward-looking TDD cases: join is not yet a README-supported operator.
public class NullableAstraJoinTests
{
    private static KustoDatabase JoinInputs() => CreateDatabase(
        new NullableTable("L").WithColumn<int>("LId").WithColumn<long>("K")
            .WithRow(101, null).WithRow(102, null).WithRow(103, 0L)
            .WithRow(104, 2L).WithRow(105, 2L).WithRow(106, 5L).Build(),
        new NullableTable("R").WithColumn<int>("RId").WithColumn<long>("K")
            .WithRow(201, null).WithRow(202, 0L).WithRow(203, 2L).WithRow(204, 2L).WithRow(205, 7L).Build());

    [TestCase("inner",      false, false)]
    [TestCase("leftouter",  true,  false)]
    [TestCase("rightouter", false, true)]
    [TestCase("fullouter",  true,  true)]
    public void JoinMatchesNullKeysAndOnlyPadsUnmatchedSidesRequestedByKind(
        string kind, bool preserveLeft, bool preserveRight)
    {
        var pairs = TestUtils.ExecuteAndGetDataFor2Columns<int?, int?>("LId", "RId", JoinInputs(),
            $"L | join kind={kind} (R) on K | project LId, RId");

        // ADX equijoins match the null key bucket even though scalar long(null) == long(null) is unknown.
        List<(int?, int?)> expected = [(101, 201), (102, 201), (103, 202), (104, 203), (104, 204), (105, 203), (105, 204)];
        if (preserveLeft)
            expected.Add((106, null));
        if (preserveRight)
            expected.Add((null, 205));
        Assert.That(pairs, Is.EquivalentTo(expected));
    }

    [TestCase("leftsemi",  "LId", 101, 102, 103, 104, 105)]
    [TestCase("leftanti",  "LId", 106)]
    [TestCase("rightsemi", "RId", 201, 202, 203, 204)]
    [TestCase("rightanti", "RId", 205)]
    public void ExistenceJoinsCountANullKeyOnTheOtherSideAsAMatch(
        string kind, string id, params int[] expectedIds)
    {
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<int>(id, JoinInputs(),
            $"L | join kind={kind} (R) on K | project {id}");
        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [Test]
    public void DefaultInnerUniqueDeduplicatesLeftNullKeysWithoutRequiringAParticularWinner()
    {
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<long?, int>("K", "RId", JoinInputs(),
            "L | join (R) on K | project K, RId");
        Assert.That(actual, Is.EquivalentTo(new (long?, int)[] { (null, 201), (0, 202), (2, 203), (2, 204) }));
    }

    [Test]
    public void NullKeyPresentOnOnlyOneSideRemainsAnUnmatchedOuterRow()
    {
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<int?, int?>("LId", "RId", JoinInputs(),
            "L | where isnull(K) | join kind=fullouter (R | where K == 0) on K | project LId, RId");
        Assert.That(actual, Is.EquivalentTo(new (int?, int?)[] { (101, null), (102, null), (null, 202) }));
    }

    [Test]
    public void CompositeJoinNeedsEveryKeyComponentToMatch()
    {
        var db = CreateDatabase(
            new NullableTable("L").WithColumn<int>("LId").WithColumn<long>("K").WithColumn<long>("Part")
                .WithRow(1, null, null).WithRow(2, null, 0L).WithRow(3, 0L, null).WithRow(4, 0L, 0L).Build(),
            new NullableTable("R").WithColumn<int>("RId").WithColumn<long>("K").WithColumn<long>("Part")
                .WithRow(11, null, null).WithRow(12, null, 0L).WithRow(13, 0L, null).WithRow(14, 0L, 0L).Build());
        var pairs = TestUtils.ExecuteAndGetDataFor2Columns<int, int>("LId", "RId", db,
            "L | join kind=inner (R) on K, Part | project LId, RId");
        Assert.That(pairs, Is.EquivalentTo(new[] { (1, 11), (2, 12), (3, 13), (4, 14) }));
    }

    [Test]
    public void LeftOuterJoinMatchesEmptyStringAndDistinguishesMatchedNullPayloadFromNoRightRow()
    {
        var db = CreateDatabase(
            new NullableTable("L").WithColumn<int>("LId").WithColumn<string>("K")
                .WithRow(1, "").WithRow(2, "absent").Build(),
            new NullableTable("R").WithColumn<int>("RId").WithColumn<string>("K")
                .WithColumn<long>("Payload").WithColumn<string>("Note").WithRow(10, "", null, "found").Build());
        var actual = TestUtils.ExecuteAndGetDataFor4Columns<int, int?, long?, string>("LId", "RId", "Payload", "Note",
            db, "L | join kind=leftouter (R) on K | project LId, RId, Payload, Note");
        Assert.That(actual, Is.EquivalentTo(new (int, int?, long?, string)[]
            { (1, 10, null, "found"), (2, null, null, "") }));
    }
}
