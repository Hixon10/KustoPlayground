using static KustoPlayground.Core.Tests.AstraTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

public partial class MakeSeriesAstraTests
{
    [TestCase("")]
    [TestCase("by Site")]
    public void EmptyInputReturnsNoSeriesByDefaultEvenWithExplicitBounds(string grouping)
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith2Columns<string, long>([], "Site", "X", "T"));
        var rows = ExecuteAndGetRows(db, $"T | make-series N=count() default=8 on X from 0 to 4 step 1 {grouping}");
        Assert.That(rows, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void KindNonemptySynthesizesOneAxisAndEachAggregatesOwnDefault(bool filteredToEmpty)
    {
        var db = filteredToEmpty ? Measurements((1, 4), (3, 5)) : Measurements();
        string source = filteredToEmpty ? "T | where V < 0" : "T";
        var row = ExecuteAndGetSingleRow(db, $"""
            {source} | make-series kind=nonempty N=count(), Mean=avg(V) default=2.5, Low=min(V) default=long(null)
                       on X from -1 to 5 step 2
            """);
        AssertCounts(row, "[-1,1,3]", "[0,0,0]");
        AssertJsonValue(row["Mean"], "[2.5,2.5,2.5]");
        AssertJsonValue(row["Low"], "[null,null,null]");
    }

    [Test]
    public void KindNonemptyOnPopulatedInputDoesNotReplaceRealCountsOrAppendARow()
    {
        var row = ExecuteAndGetSingleRow(Points(1, 1, 3),
            "T | make-series kind=nonempty N=count() default=-1 on X from 0 to 4 step 1");
        AssertCounts(row, "[0,1,2,3]", "[-1,2,-1,1]");
    }

    [Test]
    public void KindNonemptyWithNoDataOrBoundsReturnsADefaultCountButNoAxisValues()
    {
        var row = ExecuteAndGetSingleRow(Points(), "T | make-series kind=nonempty N=count() on X step 1");
        AssertCounts(row, "[]", "[0]");
    }

    [Test]
    public void AGroupWithOnlyOutOfRangeRowsDoesNotCreateASeries()
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith2Columns<string, long>(
            [("before", -1), ("after", 4), ("after", 6), ("inside", 1)], "Site", "X", "T"));
        var row = ExecuteAndGetSingleRow(db, "T | make-series N=count() on X from 0 to 4 step 2 by Site");
        Assert.That(row["Site"], Is.EqualTo("inside"));
        AssertCounts(row, "[0,2]", "[1,0]");
    }

    [Test]
    public void OnlyKindNonemptyReturnsDefaultsWhenAllRowsMissTheInterval()
    {
        var db = Points(-2, 4, 8);
        const string series = "N=count() default=-1 on X from 0 to 4 step 2";
        Assert.That(ExecuteAndGetRows(db, $"T | make-series {series}"), Is.Empty);
        AssertCounts(ExecuteAndGetSingleRow(db, $"T | make-series kind=nonempty {series}"), "[0,2]", "[-1,-1]");
    }

    [TestCase("on X from 0 to 4 step 1")]
    [TestCase("N=count() from 0 to 4 step 1")]
    [TestCase("N=count() on X from 0 to 4")]
    [TestCase("N=count() on X in range(0, 4)")]
    [TestCase("kind=bogus N=count() on X step 1")]
    [TestCase("N=count() on Missing step 1")]
    [TestCase("S=sum(Missing) on X step 1")]
    [TestCase("N=count() on X step 1 by Missing")]
    [TestCase("N=count() on X step 1 by count()")]
    [TestCase("S=V on X step 1")]
    [TestCase("S=sum(max(V)) on X step 1")]
    [TestCase("S=sum(Site) on X step 1")]
    [TestCase("N=count() default=V on X step 1")]
    [TestCase("N=count() on Site step 1")]
    [TestCase("N=count() on Stamp step 1")]
    [TestCase("N=count() on X from datetime(2024-01-01) to 4 step 1")]
    [TestCase("N=count() on X step 1 | project V")]
    public void InvalidSyntaxTypesAndNameScopesAreRejectedEvenOnEmptyInput(string clause)
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith4Columns<long, long, string, DateTime>(
            [], "X", "V", "Site", "Stamp", "T"));
        AssertRejected(db, $"T | make-series {clause}");
    }

    [TestCase("N=count() on X from 0 to 2 step 0")]
    [TestCase("N=count() on X from 0 to 2 step -1")]
    [TestCase("N=count() on X from V to 2 step 1")]
    [TestCase("N=count() on X from 0 to V step 1")]
    [TestCase("N=count() on X from 0 to 2 step V")]
    [TestCase("N=count() default='absent' on X from 0 to 2 step 1")]
    [TestCase("kind=nonempty N=count() on X from 0 to 2 step 1 by Site")]
    [TestCase("Items=make_list(V) on X from 0 to 2 step 1")]
    [TestCase("arg_max(V, X, Site) on X from 0 to 2 step 1")]
    public void InvalidSeriesParametersAndAggregateResultsAreRejectedOnPopulatedInput(string clause)
    {
        var db = CreateDatabase(TestUtils.GenerateTableWith3Columns<long, long, string>(
            [(0, 3, "a")], "X", "V", "Site", "T"));
        // An unimplemented operator must not satisfy a domain-error test just by rejecting every query.
        AssertCounts(ExecuteAndGetSingleRow(db, "T | make-series N=count() on X from 0 to 2 step 1"),
            "[0,1]", "[1,0]");
        AssertRejected(db, $"T | make-series {clause}");
    }

    [Test]
    public void ExactlyTwoToTheTwentiethBinsFitButAnExtraBinMustFailOrTruncate()
    {
        const int limit = 1 << 20;
        var db = Points(0, 0, limit - 1);
        static string Query(int end) => $"""
            T | make-series N=count() on X from 0 to {end} step 1
              | project Bins=array_length(X), Counts=array_length(N), FirstX=tolong(X[0]), LastX=tolong(X[-1]),
                        FirstN=tolong(N[0]), LastN=tolong(N[-1])
            """;

        var full = ExecuteAndGetSingleRow(db, Query(limit));
        Assert.That(full["Bins"], Is.EqualTo(limit));
        Assert.That(full["Counts"], Is.EqualTo(limit));
        Assert.That((GetValue<long>(full, "FirstX"), GetValue<long>(full, "LastX"),
            GetValue<long>(full, "FirstN"), GetValue<long>(full, "LastN")), Is.EqualTo((0L, limit - 1L, 2L, 1L)));

        // Azure documents both rejection and truncation above the cap.
        var over = db.ExecuteQuery(Query(limit + 1));
        if (over.ExecutionErrors is { Count: > 0 })
        {
            Assert.That(over.ResultRows, Is.Null);
            return;
        }

        Assert.That(over.ExecutionErrors, Is.Null);
        Assert.That(over.ResultRows, Has.Count.EqualTo(1));
        var truncated = over.ResultRows![0];
        Assert.That(truncated["Bins"], Is.InRange(1, limit));
        Assert.That(truncated["Counts"], Is.EqualTo(truncated["Bins"]));
    }
}
