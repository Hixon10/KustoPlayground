using System.Text.Json;

namespace KustoPlayground.Core.Tests;

public partial class SummarizeAstraTests
{
    private static KustoDatabase Readings() =>
        WithTable(TestUtils.GenerateTableWith4Columns<string, long, string, long>(
            [("a", 3, "old", 90), ("a", 8, "new", 12), ("b", 5, "only", 7)], "Key", "Score", "Label", "Cost", "T"));

    [TestCase("arg_min", 3L, "old", 90L)]
    [TestCase("arg_max", 8L, "new", 12L)]
    public void ArgExtremumStarReturnsAllColumnsFromTheWinningRowWithoutRepeatingKeys(
        string aggregate, long score, string label, long cost)
    {
        var rows = ResultRows(Readings(), $"T | summarize {aggregate}(Score, *) by Key");
        foreach (var row in rows)
            Assert.That(row.Keys, Is.EquivalentTo(new[] { "Key", "Score", "Label", "Cost" }));
        var actual = rows.Select(r => ((string)r["Key"]!, (long)r["Score"]!, (string)r["Label"]!, (long)r["Cost"]!));
        Assert.That(actual, Is.EquivalentTo(new[] { ("a", score, label, cost), ("b", 5L, "only", 7L) }));
    }

    [Test]
    public void TupleAliasRenamesOnlyTheColumnsReturnedByArgMin()
    {
        var rows = ResultRows(Readings(), "T | summarize (Low, AtLow)=arg_min(Score, Label) by Key");
        foreach (var row in rows)
            Assert.That(row.Keys, Is.EquivalentTo(new[] { "Key", "Low", "AtLow" }));
        var actual = rows.Select(r => ((string)r["Key"]!, (long)r["Low"]!, (string)r["AtLow"]!));
        Assert.That(actual, Is.EquivalentTo(new[] { ("a", 3L, "old"), ("b", 5L, "only") }));
    }

    [TestCase("arg_min", 20L)]
    [TestCase("arg_max", 0L)]
    public void TiedExtremaMayChooseEitherWinnerButCannotMixTheirPayloads(string aggregate, long losingScore)
    {
        var db = WithTable(TestUtils.GenerateTableWith3Columns<long, string, long>(
            [(10, "x", 17), (10, "y", 71), (losingScore, "z", 0)], "Score", "Label", "Cost", "T"));
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<long, string, long>("Score", "Label", "Cost", db,
            $"T | summarize {aggregate}(Score, Label, Cost)");
        Assert.That(actual, Has.Count.EqualTo(1));
        Assert.That(new[] { (10L, "x", 17L), (10L, "y", 71L) }, Does.Contain(actual[0]));
    }

    [TestCase("arg_min")]
    [TestCase("arg_max")]
    public void ArgExtremaIgnoreNullUnlessEveryCandidateInTheGroupIsNull(string aggregate)
    {
        var db = NullableSamples((1, null, false), (1, 5, true), (2, null, true), (2, null, true));
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<long, long?, bool>("Key", "Value", "Keep", db,
            $"T | summarize {aggregate}(Value, Keep) by Key");
        Assert.That(actual, Is.EquivalentTo(new (long, long?, bool)[] { (1, 5, true), (2, null, true) }));
    }

    [TestCase("arg_min")]
    [TestCase("arg_max")]
    public void EmptyGlobalArgExtremumRetainsNullablePayloadColumns(string aggregate)
    {
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<long?, long?, bool?>("Key", "Value", "Keep", NullableSamples(),
            $"T | summarize {aggregate}(Value, *)");
        Assert.That(actual, Is.EqualTo(new (long?, long?, bool?)[] { (null, null, null) }));
    }

    [Test]
    public void TakeAnyStarReturnsEveryNonKeyColumnFromOneRecordPerGroup()
    {
        var rows = ResultRows(Readings(), "T | summarize take_any(*) by Key");
        foreach (var row in rows)
            Assert.That(row.Keys, Is.EquivalentTo(new[] { "Key", "Score", "Label", "Cost" }));
        var groups = rows.ToDictionary(r => (string)r["Key"]!, r => ((long)r["Score"]!, (string)r["Label"]!, (long)r["Cost"]!));
        Assert.That(groups.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        Assert.That(new[] { (3L, "old", 90L), (8L, "new", 12L) }, Does.Contain(groups["a"]));
        Assert.That(groups["b"], Is.EqualTo((5L, "only", 7L)));
    }

    [Test]
    public void ExplicitTakeAnyReturnsOnlyTheNamedColumnsFromTheSameRecord()
    {
        var rows = ResultRows(Readings(), "T | summarize take_any(Label, Cost)");
        Assert.That(rows, Has.Count.EqualTo(1));
        var row = rows[0];
        Assert.That(row.Keys, Is.EquivalentTo(new[] { "Label", "Cost" }));
        var sample = ((string)row["Label"]!, (long)row["Cost"]!);
        Assert.That(new[] { ("old", 90L), ("new", 12L), ("only", 7L) }, Does.Contain(sample));
    }

    [Test]
    public void SingleColumnTakeAnyPrefersAPresentValue()
    {
        var db = NullableSamples((1, null, true), (1, 9, false), (1, null, null));
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<long?>("Value", db, "T | summarize take_any(Value)");
        Assert.That(actual, Is.EqualTo(new long?[] { 9 }));
    }

    [Test]
    public void TakeAnyIfReturnsNullWhenNoRowQualifies()
    {
        var db = NullableSamples((1, 8, true), (1, 9, false), (2, 4, false), (2, 6, null));
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<long, long?>("Key", "Sample", db,
            "T | summarize Sample=take_anyif(Value, Keep) by Key");
        Assert.That(actual, Is.EquivalentTo(new (long, long?)[] { (1, 8), (2, null) }));
    }

    // Compare dynamic results by JSON contents rather than requiring a particular CLR collection type.
    private static JsonElement JsonAggregate(KustoDatabase db, string query, string column = "Items")
    {
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<object>(column, db, query);
        Assert.That(actual, Has.Count.EqualTo(1));
        return JsonSerializer.SerializeToElement(actual[0]);
    }

    private static long?[] LongItems(JsonElement json)
    {
        Assert.That(json.ValueKind, Is.EqualTo(JsonValueKind.Array));
        return json.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? (long?)null : e.GetInt64()).ToArray();
    }

    [TestCase("make_list(Value)")]
    [TestCase("make_set(Value)")]
    [TestCase("make_list_if(Value, Keep)")]
    [TestCase("make_set_if(Value, Keep)")]
    public void EmptyInputReturnsAnEmptyDynamicArrayNotNull(string aggregate)
    {
        var json = JsonAggregate(NullableSamples(), $"T | summarize Items={aggregate}");
        Assert.That(LongItems(json), Is.Empty);
    }

    [TestCase("make_list(Value)", "list_Value", true)]
    [TestCase("make_set(Value)", "set_Value", false)]
    public void CollectionsIgnoreNullsButOnlySetsRemoveDuplicates(string aggregate, string column, bool keepsDuplicates)
    {
        var db = NullableSamples((1, 7, true), (1, -3, false), (1, 7, false), (1, null, true));
        var actual = LongItems(JsonAggregate(db, $"T | summarize {aggregate}", column));
        Assert.That(actual, Is.EquivalentTo(keepsDuplicates ? new long?[] { 7, -3, 7 } : new long?[] { 7, -3 }));
    }

    [Test]
    public void MakeListFollowsExplicitInputSortOrderWithinEachGroup()
    {
        var db = WithTable(TestUtils.GenerateTableWith3Columns<string, long, long>(
            [("a", 30, 5), ("a", 10, 3), ("a", 20, 1), ("b", 60, 2), ("b", 40, 4)], "Key", "Value", "Seq", "T"));
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, object>("Key", "Items", db,
            "T | sort by Seq asc | summarize Items=make_list(Value) by Key");
        Assert.That(actual.Select(r => r.Item1), Is.EquivalentTo(new[] { "a", "b" }));
        var groups = actual.ToDictionary(r => r.Item1, r => LongItems(JsonSerializer.SerializeToElement(r.Item2)));
        Assert.That(groups["a"], Is.EqualTo(new long?[] { 20, 10, 30 }));
        Assert.That(groups["b"], Is.EqualTo(new long?[] { 60, 40 }));
    }

    [TestCase("make_list_if(Value, Keep)", true)]
    [TestCase("make_set_if(Value, Keep)", false)]
    public void ConditionalCollectionsKeepGroupsEvenWhenNoValuesQualify(string aggregate, bool keepsDuplicates)
    {
        var db = NullableSamples((1, 7, true), (1, 7, true), (1, null, true), (1, 4, false), (1, 9, null),
            (2, 6, false), (2, null, true));
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<long, object>("Key", "Items", db,
            $"T | summarize Items={aggregate} by Key");
        Assert.That(actual.Select(r => r.Item1), Is.EquivalentTo(new[] { 1L, 2L }));
        var groups = actual.ToDictionary(r => r.Item1, r => LongItems(JsonSerializer.SerializeToElement(r.Item2)));
        Assert.That(groups[1], Is.EquivalentTo(keepsDuplicates ? new long?[] { 7, 7 } : new long?[] { 7 }));
        Assert.That(groups[2], Is.Empty);
    }

    [TestCase("make_list(Value, 2)")]
    [TestCase("make_set(Value, 2)")]
    public void CollectionMaxSizeBoundsTheNumberOfInputValuesReturned(string aggregate)
    {
        var items = LongItems(JsonAggregate(Numbers(2, 4, 6, 8, 10), $"T | summarize Items={aggregate}"));
        Assert.That(items, Has.Length.EqualTo(2));
        Assert.That(items, Is.Unique);
        Assert.That(items, Is.SubsetOf(new long?[] { 2, 4, 6, 8, 10 }));
    }

    [Test]
    public void MakeListWithNullsPreservesNullMultiplicity()
    {
        var db = NullableSamples((1, null, true), (1, 5, false), (1, null, false));
        var items = LongItems(JsonAggregate(db, "T | summarize Items=make_list_with_nulls(Value)"));
        Assert.That(items, Is.EquivalentTo(new long?[] { null, 5, null }));
    }

    [Test]
    public void MakeSetOverDynamicArraysUnionsTheirElements()
    {
        var items = LongItems(JsonAggregate(Numbers(1, 2, 2), "T | summarize Items=make_set(pack_array(Value, Value+1))"));
        Assert.That(items, Is.EquivalentTo(new long?[] { 1, 2, 3 }));
    }

    [Test]
    public void MakeBagSkipsNonBagsAndChoosesAnInputValueForSharedKeys()
    {
        var db = WithTable(TestUtils.GenerateTableWithColumn<string>(
            ["""{"units":2}""", """{"units":7,"tax":3}""", "[5]", "null", "42"], "Payload", "T"));
        var bag = JsonAggregate(db, "T | summarize Items=make_bag(parse_json(Payload))");
        Assert.That(bag.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(bag.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "units", "tax" }));
        Assert.That(new[] { 2L, 7L }, Does.Contain(bag.GetProperty("units").GetInt64()));
        Assert.That(bag.GetProperty("tax").GetInt64(), Is.EqualTo(3L));
    }

    [Test]
    public void MakeBagIfPacksOnlyMatchingProperties()
    {
        var db = GroupedNumbers(("units", 2), ("tax", 3), ("discard", 0));
        var bag = JsonAggregate(db, "T | summarize Items=make_bag_if(bag_pack(Key, Value), Value > 0)");
        Assert.That(bag.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(bag.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "units", "tax" }));
        Assert.That(bag.GetProperty("units").GetInt64(), Is.EqualTo(2L));
        Assert.That(bag.GetProperty("tax").GetInt64(), Is.EqualTo(3L));
    }
}
