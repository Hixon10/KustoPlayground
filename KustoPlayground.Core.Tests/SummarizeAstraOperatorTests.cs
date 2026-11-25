namespace KustoPlayground.Core.Tests;

public partial class SummarizeAstraTests
{
    private static KustoDatabase WithTable(Table table)
    {
        var db = new KustoDatabase();
        db.AddTable(table);
        return db;
    }

    private static KustoDatabase Numbers(params long[] values) =>
        WithTable(TestUtils.GenerateTableWithColumn(values, "Value", "T"));

    private static KustoDatabase GroupedNumbers(params (string Key, long Value)[] rows) =>
        WithTable(TestUtils.GenerateTableWith2Columns(rows, "Key", "Value", "T"));

    // The existing GenerateTableWith* helpers declare every column non-nullable.
    private static KustoDatabase NullableSamples(params (long? Key, long? Value, bool? Keep)[] rows)
    {
        var table = new Table("T", [new Column<long>("Key"), new Column<long>("Value"), new Column<bool>("Keep")]);
        foreach (var (key, value, keep) in rows)
        {
            table.AddRow(new Dictionary<string, object?> { ["Key"] = key, ["Value"] = value, ["Keep"] = keep });
        }
        return WithTable(table);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ResultRows(KustoDatabase db, string query)
    {
        var result = db.ExecuteQuery(query);
        Assert.That(result.ExecutionErrors, Is.Null, $"{query}: {result.ExecutionErrors?.FirstOrDefault()?.Description}");
        Assert.That(result.ResultRows, Is.Not.Null);
        return result.ResultRows!;
    }

    [Test]
    public void CountWithoutByCountsEveryInputRow()
    {
        var actual = TestUtils.ExecuteAndGetDataForOneColumn<long>("count_", Numbers(8, 8, 2), "T | summarize count()");
        Assert.That(actual, Is.EqualTo(new[] { 3L }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NoAggregatesAndNoKeysIsRejected(bool empty)
    {
        var result = (empty ? Numbers() : Numbers(1, 1, 2)).ExecuteQuery("T | summarize");
        Assert.That(result.ResultRows, Is.Null);
        Assert.That(result.ExecutionErrors, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void GroupingAloneProjectsOnlyDistinctCombinationsOfKeys()
    {
        var db = WithTable(TestUtils.GenerateTableWith3Columns<string, string, long>(
            [("a|b", "c", 1), ("a", "b|c", 2), ("a|b", "c", 3), ("a", "c", 1)],
            "Region", "Kind", "Unused", "T"));

        var rows = ResultRows(db, "T | summarize by Region, Kind");
        foreach (var row in rows)
            Assert.That(row.Keys, Is.EquivalentTo(new[] { "Region", "Kind" }));
        Assert.That(rows.Select(r => ((string)r["Region"]!, (string)r["Kind"]!)),
            Is.EquivalentTo(new[] { ("a|b", "c"), ("a", "b|c"), ("a", "c") }));
    }

    [Test]
    public void StringKeysAreCaseSensitiveAndTheEmptyStringIsAKey()
    {
        var db = GroupedNumbers(("east", 2), ("East", 3), ("east", 2), ("", 5), ("", 8));
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>("Key", "N", "Total", db,
            "T | summarize N=count(), Total=sum(Value) by Key");
        Assert.That(actual, Is.EquivalentTo(new[] { ("east", 2L, 4L), ("East", 1L, 3L), ("", 2L, 13L) }));
    }

    [Test]
    public void NullComponentsGroupTogetherButDoNotCollideWithZeroOrChangePosition()
    {
        var db = NullableSamples((null, 0, true), (null, 0, false), (0, null, false),
            (null, null, true), (0, 0, null));
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<long?, long?, long>("Key", "Value", "N", db,
            "T | summarize N=count() by Key, Value");
        Assert.That(actual, Is.EquivalentTo(new (long?, long?, long)[]
            { (null, 0, 2), (0, null, 1), (null, null, 1), (0, 0, 1) }));
    }

    [Test]
    public void AdjacentLongKeysAboveDoublePrecisionStayDistinct()
    {
        const long first = 9_007_199_254_740_992L;
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<long, long>("Value", "N",
            Numbers(first, first + 1, first), "T | summarize N=count() by Value");
        Assert.That(actual, Is.EquivalentTo(new[] { (first, 2L), (first + 1, 1L) }));
    }

    [TestCase("T | summarize by Value")]
    [TestCase("T | summarize N=count() by Value")]
    [TestCase("T | summarize Total=sum(Value) by Bucket=1")]
    public void GroupedEmptyTableDoesNotInventAGroup(string query)
    {
        Assert.That(ResultRows(Numbers(), query), Is.Empty);
    }

    [Test]
    public void ConstantKeyOnNonemptyInputProducesOneGroupWithThatKey()
    {
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<long, long>("Bucket", "N", Numbers(3, 5, 3),
            "T | summarize N=count() by Bucket=1");
        Assert.That(actual, Is.EqualTo(new[] { (1L, 3L) }));
    }

    [Test]
    public void FilterThatRemovesEveryRowHasDifferentGroupedAndUngroupedCardinality()
    {
        var db = Numbers(1, 2, 3);
        const string input = "T | where Value < 0";
        var global = TestUtils.ExecuteAndGetDataForOneColumn<long>("N", db, input + " | summarize N=count()");
        Assert.That(global, Is.EqualTo(new[] { 0L }));
        Assert.That(ResultRows(db, input + " | summarize N=count() by Value"), Is.Empty);
    }

    [Test]
    public void GroupKeysPrecedeAggregatesAndRepeatedDefaultNamesAreMadeUnique()
    {
        var db = GroupedNumbers(("x", 4), ("x", 4));
        var rows = ResultRows(db,
            "T | summarize count(), sum(Value), sum(Value), Smallest=min(Value) by V=Value, K=Key");
        Assert.That(rows, Has.Count.EqualTo(1));
        var row = rows[0];
        Assert.That(row.Keys, Is.EqualTo(new[] { "V", "K", "count_", "sum_Value", "sum_Value1", "Smallest" }));
        Assert.That(row.Values, Is.EqualTo(new object[] { 4L, "x", 2L, 8L, 8L, 4L }));
    }

    [Test]
    public void AGroupAliasDoesNotShadowTheInputOfAnAggregate()
    {
        var db = GroupedNumbers(("x", 6), ("x", 2), ("y", 1));
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>("Value", "Total", db,
            "T | summarize Total=sum(Value) by Value=Key");
        Assert.That(actual, Is.EquivalentTo(new[] { ("x", 8L), ("y", 1L) }));
    }

    [Test]
    public void QuotedAliasesAndComputedKeysCanBeReferencedDownstream()
    {
        var db = GroupedNumbers(("East", 4), ("EAST", 5), ("west", 6), ("east", -1));
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>("Region", "Total", db, """
            T
            | summarize ['total cost']=sum(Value) by ['region name']=tolower(Key), Positive=Value > 0
            | where Positive
            | project Region=['region name'], Total=['total cost']
            """);
        Assert.That(actual, Is.EquivalentTo(new[] { ("east", 9L), ("west", 6L) }));
    }

    [Test]
    public void IntegerBinsFloorNegativesAndDoNotFillGaps()
    {
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<long, long>("Value", "N",
            Numbers(-6, -5, -1, 0, 4, 5, 20), "T | summarize N=count() by bin(Value, 5)");
        Assert.That(actual, Is.EquivalentTo(new[] { (-10L, 1L), (-5L, 2L), (0L, 2L), (5L, 1L), (20L, 1L) }));
    }

    [Test]
    public void NegativeBinSizeRoundsKeysUpInsteadOfProducingNull()
    {
        // Kusto computes bin(v, -5) as floor(v / -5) * -5, which rounds each value up to a multiple of 5.
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<long?, long>("Bucket", "N", Numbers(0, 3, 8),
            "T | summarize N=count() by Bucket=bin(Value, -5)");
        Assert.That(actual, Is.EquivalentTo(new (long?, long)[] { (0, 1), (5, 1), (10, 1) }));
    }

    [Test]
    public void RealBinsKeepFractionalBoundaries()
    {
        var db = WithTable(TestUtils.GenerateTableWithColumn<double>([-0.25, 0, 0.25, 0.5], "Reading", "T"));
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<double, long>("Bucket", "N", db,
            "T | summarize N=count() by Bucket=bin(Reading, 0.5)");
        Assert.That(actual, Is.EquivalentTo(new[] { (-0.5, 1L), (0.0, 2L), (0.5, 1L) }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DatetimesUseExactKeysUnlessExplicitlyBinned(bool binByHour)
    {
        var hour = new DateTime(2025, 1, 1, 23, 0, 0, DateTimeKind.Utc);
        var db = WithTable(TestUtils.GenerateTableWithColumn<DateTime>(
            [hour, hour.AddMinutes(59), hour.AddMinutes(59), hour.AddHours(1)], "Stamp", "T"));
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, long>("Stamp", "N", db,
            binByHour ? "T | summarize N=count() by bin(Stamp, 1h)" : "T | summarize N=count() by Stamp");
        var expected = binByHour
            ? new[] { (hour, 3L), (hour.AddHours(1), 1L) }
            : new[] { (hour, 1L), (hour.AddMinutes(59), 2L), (hour.AddHours(1), 1L) };
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    public void TimespanHistogramUsesBothCategoryAndDayBucket()
    {
        var day = TimeSpan.FromDays(1);
        var db = WithTable(TestUtils.GenerateTableWith2Columns<string, TimeSpan>(
            [("a", day), ("a", day + TimeSpan.FromHours(2)), ("b", day), ("a", day * 2)], "Kind", "Elapsed", "T"));
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<string, TimeSpan, long>("Kind", "Length", "N", db,
            "T | summarize N=count() by Kind, Length=bin(Elapsed, 1d)");
        Assert.That(actual, Is.EquivalentTo(new[] { ("a", day, 2L), ("b", day, 1L), ("a", day * 2, 1L) }));
    }

    [Test]
    public void SummarizeConsumesFilteredProjectedRowsAndFeedsFilterProjectAndSort()
    {
        var db = GroupedNumbers(("a", 4), ("a", 5), ("a", -10), ("b", 6), ("c", 1));
        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, long>("Region", "Total", db, """
            T | where Value > 0 | project Key, Price=Value
            | summarize Total=sum(Price) by Key
            | where Total >= 5 | project Region=Key, Total | sort by Total desc
            """);
        Assert.That(actual, Is.EqualTo(new[] { ("a", 9L), ("b", 6L) }));
    }

    [Test]
    public void SecondSummarizeAggregatesGroupsRatherThanOriginalRows()
    {
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<long, long, long>("Groups", "Rows", "Largest",
            Numbers(1, 1, 1, 2, 2, 3), """
            T | summarize N=count() by Value
              | summarize Groups=count(), Rows=sum(N), Largest=max(N)
            """);
        Assert.That(actual, Is.EqualTo(new[] { (3L, 6L, 3L) }));
    }

    [TestCase("hint.strategy=shuffle")]
    [TestCase("hint.shufflekey=Key")]
    [TestCase("hint.num_partitions=4")]
    [TestCase("hint.strategy=shuffle hint.num_partitions=2")]
    [TestCase("hint.shufflekey=Key hint.shufflekey=Value")]
    public void ExecutionHintsDoNotChangeLogicalGroups(string hints)
    {
        var db = GroupedNumbers(("a", 1), ("a", 2), ("a", 1), ("b", 1));
        var actual = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>("Key", "Value", "N", db,
            $"T | summarize {hints} N=count() by Key, Value");
        Assert.That(actual, Is.EquivalentTo(new[] { ("a", 1L, 2L), ("a", 2L, 1L), ("b", 1L, 1L) }));
    }

    [TestCase("sum(Value) by Key", "sum(Missing) by Key")]
    [TestCase("count() by Key", "count() by Missing")]
    [TestCase("max(Value) by Key", "Value by Key")]
    [TestCase("sum(Value)", "sum(Key)")]
    [TestCase("countif(Value > 0)", "countif(Value)")]
    [TestCase("max(Value)", "max(sum(Value))")]
    [TestCase("count() by Value", "count() by K=sum(Value)")]
    [TestCase("count() by K=tostring(pack_array(Value))", "count() by K=pack_array(Value)")]
    [TestCase("count() by Key", "count() by")]
    [TestCase("N=count()", "N=")]
    [TestCase("Total=sum(Value) | extend Twice=2*Total", "Total=sum(Value), Twice=2*Total")]
    [TestCase("count() by Key | project Key", "count() by Key | project Value")]
    public void InvalidExpressionsAreRejectedWhileTheirValidCounterpartsSucceed(string valid, string invalid)
    {
        var db = GroupedNumbers(("a", 2), ("a", 5));
        Assert.That(ResultRows(db, $"T | summarize {valid}"), Is.Not.Empty);
        var result = db.ExecuteQuery($"T | summarize {invalid}");
        Assert.That(result.ResultRows, Is.Null);
        Assert.That(result.ExecutionErrors, Is.Not.Null.And.Not.Empty);
    }
}
