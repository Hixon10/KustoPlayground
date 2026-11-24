using NullableTable = KustoPlayground.Core.Tests.SummarizeOperatorOpusTableBuilder;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

public partial class NullableAstraTests
{
    private static KustoDatabase Samples<T>(params T?[] values) =>
        CreateDatabase(SummarizeNeonTestUtils.GenerateNullableTableWith2Columns<int, T>(
            values.Select((value, i) => (i + 1, value)).ToArray(), "T", "Id", "V"));

    // The DSL shuffles rows. Check both row identity and the raw cell (including null), without
    // making scalar tests depend on KQL sort or on a conversion that could turn null into default(T).
    private static void AssertProjection(KustoDatabase db, string expression, params object?[] expected)
    {
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<int, object?>("Id", "Result", db,
            $"T | project Id, Result = {expression}").OrderBy(row => row.Item1).ToArray();
        Assert.That(rows.Select(row => row.Item1), Is.EqualTo(Enumerable.Range(1, expected.Length)), expression);
        Assert.That(rows.Select(row => row.Item2), Is.EqualTo(expected), expression);
    }

    private static KustoDatabase NumberPairs() => CreateDatabase(new NullableTable("T")
        .WithColumn<int>("Id").WithColumn<long>("A").WithColumn<long>("B")
        .WithRow(1, null, null).WithRow(2, null, 0L).WithRow(3, 0L, null)
        .WithRow(4, 0L, 0L).WithRow(5, -1L, 1L).WithRow(6, 2L, 1L).Build());

    // https://learn.microsoft.com/en-us/kusto/query/scalar-data-types/null-values
    [TestCase("==", null, false, false, true,  false, false)]
    [TestCase("!=", null, true,  true,  false, true,  true)]
    [TestCase("<",  null, null,  null,  false, true,  false)]
    [TestCase("<=", null, null,  null,  true,  true,  false)]
    [TestCase(">",  null, null,  null,  false, false, true)]
    [TestCase(">=", null, null,  null,  true,  false, true)]
    public void ComparisonProjectsKustoTruthValue(string op, bool? bothNull, bool? leftNull,
        bool? rightNull, bool equal, bool less, bool greater)
    {
        AssertProjection(NumberPairs(), $"A {op} B", bothNull, leftNull, rightNull, equal, less, greater);
    }

    private static IEnumerable<TestCaseData> NonNullControls()
    {
        foreach (object value in new object[] { 0, 0L, 0.0, 0m, false, TimeSpan.Zero, DateTime.UnixEpoch, Guid.Empty })
            yield return new TestCaseData(value).SetArgDisplayNames(value.GetType().Name);
    }

    [TestCaseSource(nameof(NonNullControls))]
    public void EqualityDoesNotCoerceMissingCellsToAZeroFalseOrEpoch<T>(T control) where T : struct
    {
        var db = CreateDatabase(new NullableTable("T").WithColumn<int>("Id").WithColumn<T>("A").WithColumn<T>("B")
            .WithRow(1, null, null).WithRow(2, null, control).WithRow(3, control, null)
            .WithRow(4, control, control).Build());
        AssertProjection(db, "A == B", null, false, false, true);
    }

    [TestCase("A == B", 4)]
    [TestCase("A != B", 2, 3, 5, 6)]
    [TestCase("A < B", 5)]
    [TestCase("A == long(null)")]
    [TestCase("long(null) != A", 3, 4, 5, 6)]
    [TestCase("isnull(A)", 1, 2)]
    [TestCase("isnotnull(A)", 3, 4, 5, 6)]
    [TestCase("A > B or isnull(A)", 1, 2, 6)]
    [TestCase("A < B and isnotnull(B)", 5)]
    [TestCase("bool(null)")]
    public void WhereEmitsOnlyTrueAndKeepsTheOriginalRow(string predicate, params int[] expectedIds)
    {
        var ids = TestUtils.ExecuteAndGetDataForOneColumn<int>("Id", NumberPairs(), $"T | where {predicate}");
        Assert.That(ids, Is.EquivalentTo(expectedIds));
    }

    [TestCase(null,  null,  null,  null)]
    [TestCase(null,  false, false, null)]
    [TestCase(null,  true,  null,  true)]
    [TestCase(false, null,  false, null)]
    [TestCase(true,  null,  null,  true)]
    [TestCase(false, false, false, false)]
    [TestCase(true,  true,  true,  true)]
    [TestCase(true,  false, false, true)]
    [TestCase(false, true,  false, true)]
    public void BooleanConnectivesHaveDecisiveOperands(bool? left, bool? right, bool? both, bool? either)
    {
        var db = CreateDatabase(new NullableTable("T").WithColumn<bool>("L").WithColumn<bool>("R")
            .WithRow(left, right).Build());
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<bool?, bool?>("Both", "Either", db,
            "T | project Both = L and R, Either = L or R");
        Assert.That(actual, Is.EqualTo(new[] { (both, either) }));
    }

    [Test]
    public void NotPreservesUnknownInsteadOfTurningItIntoTrue()
    {
        AssertProjection(Samples<bool?>(null, false, true), "not(V)", null, true, false);
    }

    [TestCase("V", 3)]
    [TestCase("not(V)", 2)]
    [TestCase("V or isnull(V)", 1, 3)]
    public void WhereAcceptsANullableBooleanColumn(string predicate, params int[] expectedIds)
    {
        var ids = TestUtils.ExecuteAndGetDataForOneColumn<int>("Id", Samples<bool?>(null, false, true),
            $"T | where {predicate}");
        Assert.That(ids, Is.EquivalentTo(expectedIds));
    }

    [TestCase("V in (0, 2)",               false, true,  false, true)]
    [TestCase("V !in (0, 2)",              true,  false, true,  false)]
    [TestCase("V in (long(null), 2)",      null,  false, false, true)]
    [TestCase("V !in (2, long(null))",     null,  true,  true,  false)]
    [TestCase("V in (long(null))",         null,  false, false, false)]
    [TestCase("V !in (long(null))",        null,  true,  true,  true)]
    public void MembershipComposesEqualityRatherThanTreatingNullAsASetKey(
        string expression, bool? missing, bool zero, bool one, bool two)
    {
        AssertProjection(Samples<long?>(null, 0, 1, 2), expression, missing, zero, one, two);
    }

    [TestCase("V in (0, 2)", 2, 4)]
    [TestCase("V !in (0, 2)", 1, 3)]
    [TestCase("V !in (0, long(null))", 3, 4)]
    [TestCase("V between (0 .. 1)", 2, 3)]
    [TestCase("V !between (0 .. 1)", 4)]
    public void WhereConsumesMembershipAndRangePredicatesWithoutLosingTheirNullSemantics(
        string predicate, params int[] expectedIds)
    {
        var ids = TestUtils.ExecuteAndGetDataForOneColumn<int>("Id", Samples<long?>(null, 0, 1, 2),
            $"T | where {predicate}");
        Assert.That(ids, Is.EquivalentTo(expectedIds));
    }

    [TestCase("between",  true,  false)]
    [TestCase("!between", false, true)]
    public void RangeWithAMissingBoundCanStillHaveADecisiveOtherBound(string op, bool inside, bool outside)
    {
        var db = CreateDatabase(new NullableTable("T")
            .WithColumn<int>("Id").WithColumn<long>("V").WithColumn<long>("Lo").WithColumn<long>("Hi")
            .WithRow(1, null, 0L, 2L)
            .WithRow(2, 1L, null, 2L).WithRow(3, 3L, null, 2L)
            .WithRow(4, 1L, 0L, null).WithRow(5, -1L, 0L, null)
            .WithRow(6, 0L, 0L, 2L).WithRow(7, 2L, 0L, 2L)
            .WithRow(8, 1L, null, null).Build());
        AssertProjection(db, $"V {op} (Lo .. Hi)", null, null, outside, null, outside, inside, inside, null);
    }

    [TestCase("between", true, false)]
    [TestCase("!between", false, true)]
    public void DateTimeRangeWithTimespanUpperBoundDoesNotTreatMissingTimestampAsAnEpoch(
        string op, bool inside, bool outside)
    {
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        AssertProjection(Samples<DateTime?>(null, start, start.AddDays(1), start.AddDays(1).AddTicks(1)),
            $"V {op} (datetime(2024-01-01) .. 1d)", null, inside, inside, outside);
    }
}
