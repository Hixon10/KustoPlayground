using System.Text.Json;

namespace KustoPlayground.Core.Tests;

public class DynamicMvExpandAndApplyAstraTests
{
    private static KustoDatabase Batches(params (string Parent, string Json)[] batches)
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWith2Columns(batches, "Parent", "Json", "Batches"));
        return db;
    }

    [Test]
    public void ExpandingANestedArrayPreservesMixedElementsInOrderAndRepeatsTheParent()
    {
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, long, object?>("Parent", "Pos", "Item", new KustoDatabase(),
            """
            print Parent="A", P=dynamic({"items":[{"x":1},["second",2],null,{"x":1}]})
            | mv-expand with_itemindex=Pos Item=P.items
            """);
        Assert.That(rows.Select(r => (r.Item1, r.Item2)), Is.EqualTo(new[] { ("A", 0L), ("A", 1L), ("A", 2L), ("A", 3L) }));
        string[] expected = ["""{"x":1}""", """["second",2]""", "null", """{"x":1}"""];
        for (int i = 0; i < expected.Length; i++)
            AstraTestUtils.AssertJsonValue(rows[i].Item3, expected[i]);
    }

    [Test]
    public void NullExpandsOnceButEmptyBagsAndArraysDoNotEmitRows()
    {
        var db = Batches(("null", "null"), ("array", "[]"), ("bag", "{}"), ("elements", "[null,null]"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, object?>("Parent", "P", db,
            "Batches | extend P=parse_json(Json) | mv-expand P");
        Assert.That(rows, Is.EquivalentTo(new (string, object?)[] { ("null", null), ("elements", null), ("elements", null) }));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ExpansionLimitAndItemIndexRestartAtEachInputRow(int limit)
    {
        var db = Batches(("A", "[10,11,12]"), ("B", "[20,21,22]"), ("C", "[30]"));
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>("Parent", "Pos", "N", db,
            $"Batches | extend N=parse_json(Json) | mv-expand with_itemindex=Pos N to typeof(long) limit {limit}");
        (string, long, long)[] expected = limit switch
        {
            0 => [],
            1 => [("A", 0, 10), ("B", 0, 20), ("C", 0, 30)],
            _ => [("A", 0, 10), ("A", 1, 11), ("B", 0, 20), ("B", 1, 21), ("C", 0, 30)]
        };
        Assert.That(rows, Is.EquivalentTo(expected));
    }

    [Test]
    public void TypeofIsACastNotAParserAndDoesNotDiscardMismatches()
    {
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<long, long?>("Pos", "V", new KustoDatabase(),
            """
            print V=dynamic([2,"3",false,null,{"n":1},4])
            | mv-expand with_itemindex=Pos V to typeof(long)
            """);
        Assert.That(rows, Is.EqualTo(new (long, long?)[] { (0, 2), (1, null), (2, null), (3, null), (4, null), (5, 4) }));
    }

    [Test]
    public void UntypedExpansionStaysDynamicWhileTypedExpansionReplacesTheColumnType()
    {
        var schema = TestUtils.ExecuteAndGetDataFor2Columns<string, string>("ColumnName", "ColumnType", new KustoDatabase(),
            """
            print Parent="A", Raw=dynamic([1]), N=dynamic([2])
            | mv-expand with_itemindex=Pos Raw, N to typeof(long)
            | getschema
            """);
        Assert.That(schema, Is.EquivalentTo(new[] { ("Parent", "string"), ("Raw", "dynamic"), ("N", "long"), ("Pos", "long") }));
    }

    [Test]
    public void ParallelArraysZipToTheLongestEvenWhenTheFirstArrayIsEmpty()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWith3Columns<string, string, string>(
            [("left", "[4,5,6]", "[10]"), ("right", "[2]", "[30,40]"), ("empty", "[]", "[1,2]"),
             ("null", "null", "[51,52]"), ("none", "[]", "[]")], "Parent", "LeftJson", "RightJson", "Pairs"));
        var rows = TestUtils.ExecuteAndGetDataFor4Columns<string, long, long?, long?>("Parent", "Pos", "A", "B", db,
            """
            Pairs | project Parent, A=parse_json(LeftJson), B=parse_json(RightJson)
            | mv-expand with_itemindex=Pos A to typeof(long), B to typeof(long)
            """);
        Assert.That(rows, Is.EquivalentTo(new (string, long, long?, long?)[]
        {
            ("left", 0, 4, 10), ("left", 1, 5, null), ("left", 2, 6, null),
            ("right", 0, 2, 30), ("right", 1, null, 40), ("empty", 0, null, 1), ("empty", 1, null, 2),
            ("null", 0, null, 51), ("null", 1, null, 52)
        }));
    }

    [TestCase("")]
    [TestCase("kind=bag")]
    public void DefaultAndBagModeProduceSingleEntryBagsWithoutOrderingTheKeys(string mode)
    {
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<long, object>("Pos", "P", new KustoDatabase(),
            $$$"""print P=dynamic({"A":null,"B":{"deep":[2,1]}}) | mv-expand {{{mode}}} with_itemindex=Pos P""");
        Assert.That(rows.Select(r => r.Item1), Is.EquivalentTo(new long[] { 0, 1 }));
        var entries = rows.Select(r => JsonSerializer.SerializeToElement(r.Item2).EnumerateObject().Single())
            .ToDictionary(p => p.Name, p => p.Value);
        Assert.That(entries.Keys, Is.EquivalentTo(new[] { "A", "B" }));
        AstraTestUtils.AssertJsonValue(entries["A"], "null");
        AstraTestUtils.AssertJsonValue(entries["B"], """{"deep":[2,1]}""");
    }

    [Test]
    public void ArrayModeProducesExactlyOneKeyValuePairPerBagEntry()
    {
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, object>("Parent", "P", new KustoDatabase(),
            """print Parent="sensor", P=dynamic({"a.b":null,"child":{"n":3}}) | mv-expand kind=array P""");
        Assert.That(rows.Select(r => r.Item1), Is.EqualTo(new[] { "sensor", "sensor" }));
        var pairs = rows.Select(r => JsonSerializer.SerializeToElement(r.Item2)).ToArray();
        Assert.That(pairs.Select(p => p.GetArrayLength()), Is.All.EqualTo(2));
        var byKey = pairs.ToDictionary(p => p[0].GetString()!, p => p[1]);
        Assert.That(byKey.Keys, Is.EquivalentTo(new[] { "a.b", "child" }));
        AstraTestUtils.AssertJsonValue(byKey["a.b"], "null");
        AstraTestUtils.AssertJsonValue(byKey["child"], """{"n":3}""");
    }

    [Test]
    public void BagAndTypedArrayCanExpandInParallelWithNullPadding()
    {
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<long, object?, long>("Pos", "P", "N", new KustoDatabase(),
            """
            print P=dynamic({"only":3}), N=dynamic([10,20,30])
            | mv-expand kind=array with_itemindex=Pos P, N to typeof(long) limit 2
            """);
        Assert.That(rows.Select(r => (r.Item1, r.Item3)), Is.EqualTo(new[] { (0L, 10L), (1L, 20L) }));
        AstraTestUtils.AssertJsonValue(rows[0].Item2, """["only",3]""");
        Assert.That(rows[1].Item2, Is.Null);
    }

    [Test]
    public void TwoBagsZipAndPadWithoutPrescribingAnEnumerationOrder()
    {
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<long, object, object?>("Pos", "A", "B", new KustoDatabase(),
            """
            print A=dynamic({"red":2,"blue":4}), B=dynamic({"only":9})
            | mv-expand kind=array with_itemindex=Pos A, B
            """);
        Assert.That(rows.Select(r => r.Item1), Is.EqualTo(new long[] { 0, 1 }));
        var pairs = rows.Select(r => JsonSerializer.SerializeToElement(r.Item2));
        Assert.That(pairs.Select(p => (p[0].GetString(), p[1].GetInt64())),
            Is.EquivalentTo(new[] { ("red", 2L), ("blue", 4L) }));
        AstraTestUtils.AssertJsonValue(rows[0].Item3, """["only",9]""");
        Assert.That(rows[1].Item3, Is.Null);
    }

    [Test]
    public void BagCastDoesNotTurnAKeyValuePairIntoItsNumericPayload()
    {
        var db = Batches(("A", """{"x":1,"y":2}"""), ("B", """{"z":3}"""));
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long?>("Parent", "Pos", "P", db,
            """
            Batches | extend P=parse_json(Json)
            | mv-expand kind=array with_itemindex=Pos P to typeof(long) limit 1
            """);
        Assert.That(rows, Is.EquivalentTo(new (string, long, long?)[] { ("A", 0, null), ("B", 0, null) }));
    }

    [Test]
    public void ApplyTopTwoRunsPerSourceRowNotPerTableOrDistinctParentLabel()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWith3Columns<string, long, string>(
            [("same", 11, "[3,9,7]"), ("same", 12, "[12,4,12]"), ("other", 13, "[5]"), ("empty", 14, "[]")],
            "Parent", "Batch", "Json", "Batches"));
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>("Parent", "Batch", "Score", db,
            """
            Batches | extend Scores=parse_json(Json)
            | mv-apply Score=Scores to typeof(long) on (top 2 by Score desc)
            | project Parent, Batch, Score
            """);
        Assert.That(rows, Is.EquivalentTo(new[]
            { ("same", 11L, 9L), ("same", 11L, 7L), ("same", 12L, 12L), ("same", 12L, 12L), ("other", 13L, 5L) }));
    }

    [Test]
    public void ApplyFiltersAgainstTheParentsThresholdThenAggregatesOnlyThatSubtable()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWith3Columns<string, long, string>(
            [("low", 3, "[1,4,6]"), ("high", 5, "[1,4,6]"), ("none", 7, "[1,4,6]")], "Parent", "Threshold", "Json", "Batches"));
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>("Parent", "Kept", "Total", db,
            """
            Batches | extend Scores=parse_json(Json)
            | mv-apply Score=Scores to typeof(long) on (
                where Score >= Threshold | summarize Kept=count(), Total=sum(Score)
              )
            | project Parent, Kept, Total
            """);
        Assert.That(rows, Is.EquivalentTo(new[] { ("low", 2L, 10L), ("high", 1L, 6L), ("none", 0L, 0L) }));
    }

    [Test]
    public void ApplyLimitsBeforeRunningTheFilterAndKeepsTheOriginalIndex()
    {
        var db = Batches(("A", "[0,5,99]"), ("B", "[1,6,100]"), ("C", "[0,0,9]"));
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, long, long>("Parent", "Pos", "Score", db,
            """
            Batches | extend Score=parse_json(Json)
            | mv-apply with_itemindex=Pos Score to typeof(long) limit 2 on (where Score >= 5)
            """);
        Assert.That(rows, Is.EquivalentTo(new[] { ("A", 1L, 5L), ("B", 1L, 6L) }));
    }

    [Test]
    public void SortingInsideApplyDeterminesTheOrderOfEachParentsCollectedList()
    {
        var db = Batches(("A", "[3,10,1]"), ("B", "[8,2,11]"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, object>("Parent", "Ranking", db,
            """
            Batches | extend Score=parse_json(Json)
            | mv-apply Score to typeof(long) on (
                sort by Score desc | take 2 | summarize Ranking=make_list(Score)
              )
            """);
        Assert.That(rows.Select(r => r.Item1), Is.EquivalentTo(new[] { "A", "B" }));
        var rankings = rows.ToDictionary(r => r.Item1, r => r.Item2);
        AstraTestUtils.AssertJsonValue(rankings["A"], "[10,3]");
        AstraTestUtils.AssertJsonValue(rankings["B"], "[11,8]");
    }

    [Test]
    public void ApplyAlignedArraysCanFilterOnPaddingAndOnCastFailures()
    {
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<long, long?, long?>("Pos", "A", "B", new KustoDatabase(),
            """
            print A=dynamic([7,"8",9]), B=dynamic([10,20])
            | mv-apply with_itemindex=Pos A to typeof(long), B to typeof(long) on (where isnull(A) or isnull(B))
            """);
        Assert.That(rows, Is.EqualTo(new (long, long?, long?)[] { (1, null, 20), (2, 9, null) }));
    }
}
