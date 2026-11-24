using NullableTable = KustoPlayground.Core.Tests.SummarizeOperatorOpusTableBuilder;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

public partial class NullableAstraTests
{
    [TestCase("bool(null)")]
    [TestCase("int(null)")]
    [TestCase("long(null)")]
    [TestCase("real(null)")]
    [TestCase("decimal(null)")]
    [TestCase("datetime(null)")]
    [TestCase("timespan(null)")]
    [TestCase("guid(null)")]
    [TestCase("dynamic(null)")]
    public void PrintTypedNullReturnsOneNamedCellNotAnAbsentColumn(string literal)
    {
        var row = ExecuteAndGetSingleRow(new KustoDatabase(), $"print Missing = {literal}");
        AssertColumnsInOrder(row, "Missing");
        AssertIsNull(row, "Missing");
    }

    [TestCase("+", 10.0)]
    [TestCase("-", 2.0)]
    [TestCase("*", 24.0)]
    [TestCase("/", 1.5)]
    [TestCase("%", 2.0)]
    public void RealArithmeticPropagatesNullFromEitherSide(string op, double control)
    {
        var db = CreateDatabase(new NullableTable("T")
            .WithColumn<int>("Id").WithColumn<double>("A").WithColumn<double>("B")
            .WithRow(1, null, null).WithRow(2, null, 4.0).WithRow(3, 6.0, null)
            .WithRow(4, 6.0, 4.0).Build());
        AssertProjection(db, $"A {op} B", null, null, null, control);
    }

    [Test]
    public void CoalesceKeepsZeroAndFallsThroughOnlyForMissingNumbers()
    {
        AssertProjection(NumberPairs(), "coalesce(A, B, -9)", -9L, 0L, 0L, 0L, -1L, 2L);
    }

    [Test]
    public void IffUsesTheElseBranchForAnUnknownPredicate()
    {
        AssertProjection(Samples<bool?>(null, false, true), "iff(V, long(null), 0)", 0L, 0L, null);
    }

    // A missing source string is the Kusto empty string; neither whitespace nor the text "null" is missing.
    private static KustoDatabase TextSamples() => Samples<string?>(null, "", " ", "AbC", "null");

    private static IEnumerable<TestCaseData> StringPredicates()
    {
        static TestCaseData Matches(string predicate, params int[] ids) => new(predicate, ids);
        return [
            Matches("V == ''", 1, 2),             Matches("V != ''", 3, 4, 5),
            Matches("V =~ 'abc'", 4),             Matches("V !~ 'abc'", 1, 2, 3, 5),
            Matches("V contains 'B'", 4),         Matches("V !contains 'B'", 1, 2, 3, 5),
            Matches("V startswith 'a'", 4),       Matches("V !startswith 'a'", 1, 2, 3, 5),
            Matches("V endswith 'c'", 4),         Matches("V !endswith 'c'", 1, 2, 3, 5),
            Matches("V matches regex '^$'", 1, 2),
            Matches("V in ('', 'abc')", 1, 2),    Matches("V !in ('', 'abc')", 3, 4, 5),
            Matches("V in~ ('', 'abc')", 1, 2, 4), Matches("V !in~ ('', 'abc')", 3, 5),
            Matches("isnull(V)"),                 Matches("isnotnull(V)", 1, 2, 3, 4, 5),
            Matches("isempty(V)", 1, 2),          Matches("isnotempty(V)", 3, 4, 5)
        ];
    }

    [TestCaseSource(nameof(StringPredicates))]
    public void MissingStringProjectsTheSameBooleanAsEmptyString(string predicate, int[] trueIds)
    {
        AssertProjection(TextSamples(), predicate, Enumerable.Range(1, 5)
            .Select(id => (object?)trueIds.Contains(id)).ToArray());
    }

    [TestCaseSource(nameof(StringPredicates))]
    public void WhereOnStringsEmitsBothMissingAndEmptyTextOrNeither(string predicate, int[] trueIds)
    {
        var ids = TestUtils.ExecuteAndGetDataForOneColumn<int>("Id", TextSamples(), $"T | where {predicate}");
        Assert.That(ids, Is.EquivalentTo(trueIds));
    }

    [Test]
    public void StringCoalesceSkipsMissingAndEmptyButNotWhitespace()
    {
        AssertProjection(TextSamples(), "coalesce(V, 'fallback')", "fallback", "fallback", " ", "AbC", "null");
    }

    [Test]
    public void DynamicNullIsNotAnEmptyArrayOrBagAndCastsToEmptyText()
    {
        var row = ExecuteAndGetSingleRow(new KustoDatabase(), """
            print Missing=isnull(dynamic(null)), EmptyArray=isnull(dynamic([])),
                  EmptyBag=isnull(dynamic({})), Text=tostring(dynamic(null))
            """);
        Assert.That(GetValue<bool>(row, "Missing"), Is.True);
        Assert.That(GetValue<bool>(row, "EmptyArray"), Is.False);
        Assert.That(GetValue<bool>(row, "EmptyBag"), Is.False);
        Assert.That(GetValue<string>(row, "Text"), Is.Empty);
    }

    [TestCase("toupper(V)",                 "Ab",   "AB")]
    [TestCase("tolower(V)",                 "Ab",   "ab")]
    [TestCase("base64_encode_tostring(V)",  "Hi",   "SGk=")]
    [TestCase("base64_decode_tostring(V)",  "SGk=", "Hi")]
    [TestCase("url_encode(V)",              "A$",   "A%24")]
    [TestCase("url_decode(V)",              "A%24", "A$")]
    public void StringFunctionsReturnEmptyStringForMissingText(string expression, string input, string result)
    {
        AssertProjection(Samples<string?>(null, "", input), expression, "", "", result);
    }

    [Test]
    public void StringLengthDistinguishesMissingFromWhitespaceAndTheWordNull()
    {
        AssertProjection(Samples<string?>(null, "", " ", "null"), "strlen(V)", 0, 0, 1, 4);
    }

    [Test]
    public void ToDateTimeKeepsRowsWithMissingEmptyOrMalformedText()
    {
        var stamp = new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc);
        AssertProjection(Samples<string?>(null, "", "not a date", "2024-02-29T12:00:00"),
            "todatetime(V)", null, null, null, stamp);
    }

    [Test]
    public void ToTimespanDistinguishesMissingOrMalformedTextFromZeroDuration()
    {
        AssertProjection(Samples<string?>(null, "", "not a duration", "00:00:00", "01:30:00"),
            "totimespan(V)", null, null, null, TimeSpan.Zero, TimeSpan.FromMinutes(90));
    }

    [Test]
    public void MakeTimespanDoesNotSubstituteZeroForAnExplicitlyMissingComponent()
    {
        var db = CreateDatabase(new NullableTable("T").WithColumn<int>("Id")
            .WithColumn<int>("D").WithColumn<int>("H").WithColumn<int>("M").WithColumn<int>("S")
            .WithRow(1, null, 2, 3, 4).WithRow(2, 1, null, 3, 4)
            .WithRow(3, 1, 2, null, 4).WithRow(4, 1, 2, 3, null)
            .WithRow(5, 0, 0, 0, 0).WithRow(6, 1, 2, 3, 4).Build());
        AssertProjection(db, "make_timespan(D, H, M, S)", null, null, null, null,
            TimeSpan.Zero, new TimeSpan(1, 2, 3, 4));
    }

    [TestCase("now() - ago(V)")]
    [TestCase("now(V) - now()")]
    public void ClockFunctionsDistinguishMissingFromZeroOffsetWithoutReadingTheWallClock(string expression)
    {
        AssertProjection(Samples<TimeSpan?>(null, TimeSpan.Zero, TimeSpan.FromHours(2)),
            expression, null, TimeSpan.Zero, TimeSpan.FromHours(2));
    }

    [Test]
    public void BinRequiresBothValueAndBinSize()
    {
        var db = CreateDatabase(new NullableTable("T")
            .WithColumn<int>("Id").WithColumn<long>("V").WithColumn<long>("Step")
            .WithRow(1, null, 5L).WithRow(2, 14L, null).WithRow(3, null, null)
            .WithRow(4, 14L, 5L).Build());
        AssertProjection(db, "bin(V, Step)", null, null, null, 10L);
    }

    [Test]
    public void BinAtKeepsNullSeparateFromTheBucketThatContainsZero()
    {
        AssertProjection(Samples<long?>(null, 0, 14), "bin_at(V, 5, 2)", null, -3L, 12L);
    }

    [Test]
    public void DateTimeAdditionDoesNotConflateMissingDurationWithZero()
    {
        var stamp = new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc);
        var db = CreateDatabase(new NullableTable("T")
            .WithColumn<int>("Id").WithColumn<DateTime>("Stamp").WithColumn<TimeSpan>("Delta")
            .WithRow(1, null, TimeSpan.Zero).WithRow(2, stamp, null)
            .WithRow(3, stamp, TimeSpan.Zero).WithRow(4, stamp, TimeSpan.FromDays(1)).Build());
        AssertProjection(db, "Stamp + Delta", null, null, stamp, stamp.AddDays(1));
    }
}
