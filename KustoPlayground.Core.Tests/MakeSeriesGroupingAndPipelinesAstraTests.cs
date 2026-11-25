using static KustoPlayground.Core.Tests.AstraTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

public partial class MakeSeriesAstraTests
{
    [TestCase("")]
    [TestCase("hint.shufflekey=Site")]
    public void CompositeKeysKeepOnlyObservedCombinationsWithOrWithoutAShuffleHint(string hint)
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith3Columns<string, string, long>(
            [("a|b", "c", 1), ("a", "b|c", 3), ("a|b", "c", 1), ("a", "b|c", 4),
                ("a", "c", 6), ("A", "c", 5), ("", "c", 2)],
            "Site", "Device", "X", "T"));
        var rows = ExecuteAndGetRows(db, $"T | make-series {hint} N=count() on X from 0 to 8 step 2 by Site, Device");
        var groups = rows.ToDictionary(r => (GetValue<string>(r, "Site"), GetValue<string>(r, "Device")));

        Assert.That(groups.Keys, Is.EquivalentTo(new[] { ("a|b", "c"), ("a", "b|c"), ("a", "c"), ("A", "c"), ("", "c") }));
        const string axis = "[0,2,4,6]";
        AssertCounts(groups[("a|b", "c")], axis, "[2,0,0,0]");
        AssertCounts(groups[("a", "b|c")], axis, "[0,1,1,0]");
        AssertCounts(groups[("a", "c")], axis, "[0,0,0,1]");
        AssertCounts(groups[("A", "c")], axis, "[0,0,1,0]");
        AssertCounts(groups[("", "c")], axis, "[0,1,0,0]");
    }

    [Test]
    public void NullNumericGroupKeyHasItsOwnSeriesDistinctFromZero()
    {
        var db = CreateDatabase(OpusTestUtils.NewTable("T").WithColumn<long>("Key").WithColumn<long>("X")
            .WithRow(null, 1L).WithRow(0L, 1L).WithRow(0L, 1L).WithRow(null, 3L).Build());
        var rows = ExecuteAndGetRows(db, "T | make-series N=count() on X from 0 to 4 step 1 by Key");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertCounts(GetSingleRowWhere(rows, "Key", null), "[0,1,2,3]", "[0,1,0,1]");
        AssertCounts(GetSingleRowWhere(rows, "Key", 0L), "[0,1,2,3]", "[0,2,0,0]");
    }

    [Test]
    public void OmittedDatetimeBoundsYieldWholeUtcHourBinsLocalToEachSensor()
    {
        var day = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var db = CreateDatabase(TestUtils.GenerateTableWith2Columns<string, DateTime>(
            [("a", day.AddMinutes(115)), ("a", day.AddMinutes(185)),
                ("b", day.AddMinutes(125)), ("b", day.AddMinutes(160))], "Sensor", "Stamp", "T"));
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, DateTime, long>("Sensor", "Stamp", "N", db, """
            T | make-series N=count() on Stamp step 1h by Sensor
              | mv-expand Stamp to typeof(datetime), N to typeof(long)
            """);
        Assert.That(rows, Is.EquivalentTo(new[]
        {
            ("a", day.AddHours(1), 1L), ("a", day.AddHours(2), 0L), ("a", day.AddHours(3), 1L),
            ("b", day.AddHours(2), 2L)
        }));
    }

    [Test]
    public void LetBoundsAndComputedGroupKeyUseTheFilteredAndProjectedAxis()
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith2Columns<long, string>(
            [(1, "Rack-A"), (2, "rack-a"), (5, "RACK-A"), (3, "skip"), (7, "Rack-A")], "X", "Device", "T"));
        var row = ExecuteAndGetSingleRow(db, """
            let lo = 0;
            let width = 2;
            let hi = lo + 3 * width;
            T | where Device != "skip"
              | extend ['offset slot']=X-1
              | make-series ['hit count']=count() on ['offset slot'] from lo to hi step width
                by Family=tolower(Device)
            """);
        AssertColumnsInOrder(row, "Family", "hit count", "offset slot");
        Assert.That(row["Family"], Is.EqualTo("rack-a"));
        AssertJsonValue(row["offset slot"], "[0,2,4]");
        AssertJsonValue(row["hit count"], "[2,0,1]");
    }

    [Test]
    public void InferredNamesAreUniqueAndTheAxisFollowsTheAggregates()
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith3Columns<string, long, long>(
            [("a", 0, 3), ("a", 0, 5), ("a", 2, 3)], "Site", "X", "V", "T"));
        var row = ExecuteAndGetSingleRow(db, """
            T | make-series count(), sum(V), sum(V) default=-1 on X from 0 to 3 step 1 by Key=Site
            """);
        AssertColumnsInOrder(row, "Key", "count_", "sum_V", "sum_V1", "X");
        Assert.That(row["Key"], Is.EqualTo("a"));
        AssertJsonValue(row["count_"], "[2,0,1]");
        AssertJsonValue(row["sum_V"], "[8,0,3]");
        AssertJsonValue(row["sum_V1"], "[8,-1,3]");
        AssertJsonValue(row["X"], "[0,1,2]");
    }

    [Test]
    public void EmptySeriesStillDeclaresScalarKeysAndDynamicArraysInOrdinalOrder()
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith4Columns<string, long, long, long>(
            [], "Site", "Sensor", "X", "V", "T"));
        var schema = OpusTestUtils.ExecuteGetSchema(db, """
            T | make-series count(), Mean=avg(V) on X from 1 to 5 step 2 by Id=Sensor, Site
              | getschema
            """);
        Assert.That(schema.Select(c => (c.ColumnName, c.ColumnOrdinal, c.ColumnType)), Is.EquivalentTo(new[]
        {
            ("Id", 0, "long"), ("Site", 1, "string"), ("count_", 2, "dynamic"),
            ("Mean", 3, "dynamic"), ("X", 4, "dynamic")
        }));
    }

    [Test]
    public void ParallelMvExpandPairsEachBinWithItsCountsTotalsAndIndexWithinTheGroup()
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith3Columns<string, long, long>(
            [("a", 0, 4), ("a", 1, 3), ("a", 4, 5), ("b", 2, 1), ("b", 3, 6), ("skip", 1, -3)],
            "Site", "X", "V", "T"));
        var rows = ExecuteAndGetRows(db, """
            T | where V >= 0
              | make-series V=sum(V) default=-1, N=count() on X from 0 to 6 step 2 by Site
              | mv-expand with_itemindex=I X to typeof(long), V to typeof(long), N to typeof(long)
            """);
        var actual = rows.Select(r => (GetValue<string>(r, "Site"), GetValue<long>(r, "I"),
            GetValue<long>(r, "X"), GetValue<long>(r, "V"), GetValue<long>(r, "N")));
        Assert.That(actual, Is.EquivalentTo(new[]
        {
            ("a", 0L, 0L, 7L, 2L), ("a", 1L, 2L, -1L, 0L), ("a", 2L, 4L, 5L, 1L),
            ("b", 0L, 0L, -1L, 0L), ("b", 1L, 2L, 7L, 2L), ("b", 2L, 4L, -1L, 0L)
        }));
    }

    [Test]
    public void TabularLetExposesIndexableSeriesToDownstreamSummarize()
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith2Columns<string, long>(
            [("a", 0), ("a", 0), ("a", 2), ("b", 1), ("b", 1)], "Site", "X", "T"));
        var rows = TestUtils.ExecuteAndGetDataFor4Columns<long, long, long, long>("Sites", "Slots", "HeadEvents", "TailEvents", db, """
            let S = T | make-series N=count() on X from 0 to 3 step 1 by Site;
            S | summarize Sites=count(), Slots=sum(array_length(X)), HeadEvents=sum(tolong(N[0])), TailEvents=sum(tolong(N[-1]))
            """);
        Assert.That(rows, Is.EqualTo(new[] { (2L, 6L, 2L, 1L) }));
    }

    [Test]
    public void LinearInterpolationFillsAnInteriorNullWithoutReplacingAnObservedZeroOrTheEdges()
    {
        var row = ExecuteAndGetSingleRow(Measurements((1, 0), (3, 6)), """
            T | make-series S=avg(V) default=double(null) on X from 0 to 5 step 1
              | project Raw=S, Filled=series_fill_linear(S, double(null), false)
            """);
        AssertJsonValue(row["Raw"], "[null,0,null,6,null]");
        AssertJsonValue(row["Filled"], "[null,0,3,6,null]");
    }
}
