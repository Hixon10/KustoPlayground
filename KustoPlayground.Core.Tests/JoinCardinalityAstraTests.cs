using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;
using static KustoPlayground.Core.Tests.TestUtils;

namespace KustoPlayground.Core.Tests;

public class JoinCardinalityAstraTests
{
    private static KustoDatabase RequestsAndTraces() => CreateDatabase(
        GenerateTableWith2Columns<string, int>(
            [("hot", 1), ("hot", 2), ("retry", 3), ("retry", 3), ("request-only", 4), ("request-only", 4)],
            "OperationId", "RequestId", "Requests"),
        GenerateTableWith2Columns<string, int>(
            [("hot", 10), ("hot", 11), ("hot", 12), ("retry", 20), ("retry", 20), ("trace-only", 30), ("trace-only", 30)],
            "OperationId", "TraceId", "Traces"));

    [TestCase("join Traces")]
    [TestCase("join kind=innerunique (Traces)")]
    public void InnerUniqueUsesOneLeftWinnerForAllMatchingRightRows(string join)
    {
        var pairs = ExecuteAndGetDataFor2Columns<int, int>("RequestId", "TraceId", RequestsAndTraces(),
            $"Requests | {join} on OperationId");

        var winners = pairs.Where(p => p.Item2 == 10).Select(p => p.Item1).ToList();
        Assert.That(winners, Has.Count.EqualTo(1));
        int winner = winners.Single();
        Assert.That(winner, Is.AnyOf(1, 2));
        Assert.That(pairs, Is.EquivalentTo(new[] { (winner, 10), (winner, 11), (winner, 12), (3, 20), (3, 20) }));
    }

    [TestCase("inner",      false, false)]
    [TestCase("leftouter",  true,  false)]
    [TestCase("rightouter", false, true)]
    [TestCase("fullouter",  true,  true)]
    public void PairJoinsPreserveEveryMatchingPairAndEachRequestedUnmatchedRow(
        string kind, bool keepUnmatchedRequests, bool keepUnmatchedTraces)
    {
        var pairs = ExecuteAndGetDataFor2Columns<int?, int?>("RequestId", "TraceId", RequestsAndTraces(),
            $"Requests | join kind={kind} Traces on OperationId");

        List<(int?, int?)> expected =
        [
            (1, 10), (1, 11), (1, 12), (2, 10), (2, 11), (2, 12),
            (3, 20), (3, 20), (3, 20), (3, 20)
        ];
        if (keepUnmatchedRequests)
            expected.AddRange([(4, null), (4, null)]);
        if (keepUnmatchedTraces)
            expected.AddRange([(null, 30), (null, 30)]);

        Assert.That(pairs, Is.EquivalentTo(expected));
    }

    [TestCase("leftsemi",     "RequestId", new[] { 1, 2, 3, 3 })]
    [TestCase("leftanti",     "RequestId", new[] { 4, 4 })]
    [TestCase("anti",         "RequestId", new[] { 4, 4 })]
    [TestCase("leftantisemi", "RequestId", new[] { 4, 4 })]
    [TestCase("rightsemi",     "TraceId", new[] { 10, 11, 12, 20, 20 })]
    [TestCase("rightanti",     "TraceId", new[] { 30, 30 })]
    [TestCase("rightantisemi", "TraceId", new[] { 30, 30 })]
    public void ExistenceJoinsReturnOnlyTheSelectedSideWithItsOriginalMultiplicity(
        string kind, string idColumn, int[] expectedIds)
    {
        var rows = ExecuteAndGetRows(RequestsAndTraces(), $"Requests | join kind={kind} Traces on OperationId");

        Assert.That(GetValues<int>(rows, idColumn), Is.EquivalentTo(expectedIds));
        foreach (var row in rows)
            AssertColumnsInOrder(row, "OperationId", idColumn);
    }

    [TestCase("innerunique", false, false)]
    [TestCase("inner",       false, false)]
    [TestCase("leftouter",   true,  false)]
    [TestCase("rightouter",  false, true)]
    [TestCase("fullouter",   true,  true)]
    [TestCase("leftsemi",    false, false)]
    [TestCase("rightsemi",   false, false)]
    [TestCase("leftanti",    true,  false)]
    [TestCase("rightanti",   false, true)]
    public void EmptyOppositeInputNeverProvidesAMatch(string kind, bool keepPopulatedLeft, bool keepPopulatedRight)
    {
        var db = CreateDatabase(
            GenerateTableWithColumn<long>([], "DeviceId", "EmptyKeys"),
            GenerateTableWith2Columns<long, int>([(7, 51), (7, 52)], "DeviceId", "EventId", "Events"));

        var emptyRight = ExecuteAndGetRows(db, $"Events | join kind={kind} EmptyKeys on DeviceId");
        var emptyLeft = ExecuteAndGetRows(db, $"EmptyKeys | join kind={kind} Events on DeviceId");

        int[] eventIds = [51, 52];
        Assert.That(GetValues<int>(emptyRight, "EventId"), Is.EquivalentTo(keepPopulatedLeft ? eventIds : Array.Empty<int>()),
            "empty right input");
        Assert.That(GetValues<int>(emptyLeft, "EventId"), Is.EquivalentTo(keepPopulatedRight ? eventIds : Array.Empty<int>()),
            "empty left input");
    }
}
