using System.Text.Json;

namespace KustoPlayground.Core.Tests;

public class DynamicCollectionAggregateAstraTests
{
    private static KustoDatabase Readings(params (string Site, long Seq, string Json)[] readings)
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWith3Columns(readings, "Site", "Seq", "Json", "Readings"));
        return db;
    }

    [Test]
    public void SortedMakeListConcatenatesArrayInputsOneLevelAndPreservesDuplicates()
    {
        var db = Readings(("A", 4, "[[2,null]]"), ("B", 2, "[]"), ("A", 1, "[[2,null],5]"),
            ("A", 2, "null"), ("A", 3, """{"k":7}"""), ("B", 1, "false"), ("C", 1, "null"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, object>("Site", "Items", db,
            """
            Readings | extend V=parse_json(Json)
            | sort by Seq asc | summarize Items=make_list(V) by Site
            """);
        Assert.That(rows.Select(r => r.Item1), Is.EquivalentTo(new[] { "A", "B", "C" }));
        var lists = rows.ToDictionary(r => r.Item1, r => r.Item2);
        AstraTestUtils.AssertJsonValue(lists["A"], """[[2,null],5,{"k":7},[2,null]]""");
        AstraTestUtils.AssertJsonValue(lists["B"], "[false]");
        AstraTestUtils.AssertJsonValue(lists["C"], "[]");
    }

    [Test]
    public void MakeListMaxSizeAppliesPerGroupAndCountsDuplicatesButNotNulls()
    {
        var db = Readings(("A", 1, "null"), ("A", 2, "7"), ("A", 3, "7"), ("A", 4, "7"),
            ("B", 1, "null"), ("B", 2, "8"), ("B", 3, "9"), ("C", 1, "null"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, object>("Site", "Items", db,
            "Readings | sort by Seq asc | summarize Items=make_list(parse_json(Json), 2) by Site");
        Assert.That(rows.Select(r => r.Item1), Is.EquivalentTo(new[] { "A", "B", "C" }));
        var lists = rows.ToDictionary(r => r.Item1, r => r.Item2);
        AstraTestUtils.AssertJsonValue(lists["A"], "[7,7]");
        AstraTestUtils.AssertJsonValue(lists["B"], "[8,9]");
        AstraTestUtils.AssertJsonValue(lists["C"], "[]");
    }

    [Test]
    public void MakeSetUnionsArrayElementsAndLimitsDistinctValuesSeparatelyForEachGroup()
    {
        var db = Readings(("A", 1, """["red","red","blue"]"""), ("A", 2, """["blue","green"]"""),
            ("A", 3, "null"), ("B", 1, "[]"), ("B", 2, """["only","only"]"""), ("C", 1, "null"));
        var rows = OpusTestUtils.ExecuteAndGetRows(db,
            "Readings | summarize Items=make_set(parse_json(Json), 2) by Site");
        Assert.That(rows.Select(r => r["Site"]), Is.EquivalentTo(new[] { "A", "B", "C" }));
        var sets = rows.ToDictionary(r => (string)r["Site"]!, r => SummarizeOperatorOpusTestUtils.GetDynamicArray(r, "Items"));
        Assert.That(sets["A"], Has.Count.EqualTo(2));
        Assert.That(sets["A"], Is.Unique.And.SubsetOf(new[] { "red", "blue", "green" }));
        Assert.That(sets["B"], Is.EquivalentTo(new[] { "only" }));
        Assert.That(sets["C"], Is.Empty);
    }

    [Test]
    public void MakeBagSkipsNonBagsAndPicksOnlyAmongValuesFromTheSameGroup()
    {
        var db = Readings(("A", 1, """{"shared":2,"meta":{"tags":[2,1]}}"""),
            ("A", 2, """{"shared":7,"present":null}"""), ("A", 3, "[99]"), ("A", 4, "null"),
            ("B", 1, """{"shared":30,"onlyB":true}"""), ("B", 2, "false"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, object>("Site", "Bag", db,
            "Readings | summarize Bag=make_bag(parse_json(Json)) by Site");
        Assert.That(rows.Select(r => r.Item1), Is.EquivalentTo(new[] { "A", "B" }));
        var bags = rows.ToDictionary(r => r.Item1, r => JsonSerializer.SerializeToElement(r.Item2));
        var a = bags["A"];
        Assert.That(a.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "shared", "present", "meta" }));
        Assert.That(a.GetProperty("shared").GetInt64(), Is.AnyOf(2L, 7L));
        AstraTestUtils.AssertJsonValue(a.GetProperty("present"), "null");
        AstraTestUtils.AssertJsonValue(a.GetProperty("meta"), """{"tags":[2,1]}""");
        AstraTestUtils.AssertJsonValue(bags["B"], """{"onlyB":true,"shared":30}""");
    }

    [Test]
    public void MakeBagMaxSizeBoundsTheNumberOfSlotsNotTheirValues()
    {
        var db = Readings(("A", 1, """{"a":1}"""), ("A", 2, """{"b":2}"""), ("A", 3, """{"c":3}"""),
            ("B", 1, """{"x":9}"""));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, object>("Site", "Bag", db,
            "Readings | summarize Bag=make_bag(parse_json(Json), 2) by Site");
        Assert.That(rows.Select(r => r.Item1), Is.EquivalentTo(new[] { "A", "B" }));
        var bags = rows.ToDictionary(r => r.Item1, r => JsonSerializer.SerializeToElement(r.Item2));
        var entries = bags["A"].EnumerateObject().Select(p => (p.Name, p.Value.GetInt64())).ToArray();
        Assert.That(entries, Has.Length.EqualTo(2));
        Assert.That(entries.Select(p => p.Name), Is.Unique);
        Assert.That(entries, Is.SubsetOf(new[] { ("a", 1L), ("b", 2L), ("c", 3L) }));
        AstraTestUtils.AssertJsonValue(bags["B"], """{"x":9}""");
    }

    [Test]
    public void ParsedSamplesCanBeExpandedAggregatedBySensorAndUnpackedIntoTypedMetrics()
    {
        var db = Readings(
            ("east", 1, """{"samples":[{"metric":"temp","v":"18"},{"metric":"bad","v":"unknown"}]}"""),
            ("west", 1, """{"samples":[{"metric":"temp","v":0}]}"""),
            ("east", 2, """{"samples":[{"metric":"fan","v":2}]}"""), ("empty", 1, """{"samples":[]}"""));
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long?>("Site", "temp", "fan", db,
            """
            Readings | extend P=parse_json(Json)
            | mv-expand Sample=P.samples
            | extend Metric=tostring(Sample.metric), N=tolong(Sample.v)
            | where isnotnull(N)
            | summarize Metrics=make_bag(bag_pack(Metric, N)) by Site
            | evaluate bag_unpack(Metrics) : (*, temp:long, fan:long)
            """);
        Assert.That(rows, Is.EquivalentTo(new (string, long, long?)[] { ("east", 18, 2), ("west", 0, null) }));
    }
}
