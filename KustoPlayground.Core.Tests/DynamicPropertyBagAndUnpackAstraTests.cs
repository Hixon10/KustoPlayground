using System.Text.Json;

namespace KustoPlayground.Core.Tests;

public class DynamicPropertyBagAndUnpackAstraTests
{
    [Test]
    public void BagPackUsesEachRowsKeyAndKeepsNullAndStructuredValues()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWith2Columns<string, long>([("a.b", 4), ("A", 0)], "Key", "N", "T"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, object>("Key", "P", db,
            """T | project Key, P=bag_pack(Key, N + 1, "raw", pack_array(N, N), "unset", dynamic(null))""");
        Assert.That(rows.Select(r => r.Item1), Is.EquivalentTo(new[] { "a.b", "A" }));
        AstraTestUtils.AssertJsonValue(rows.Single(r => r.Item1 == "a.b").Item2, """{"raw":[4,4],"unset":null,"a.b":5}""");
        AstraTestUtils.AssertJsonValue(rows.Single(r => r.Item1 == "A").Item2, """{"A":1,"unset":null,"raw":[0,0]}""");
    }

    [TestCase("""bag_pack("key", 2, "unpaired")""")]
    [TestCase("""bag_pack(7, "value")""")]
    public void BagPackRequiresCompletePairsWithStringKeys(string expression)
    {
        AstraTestUtils.AssertRejected(new KustoDatabase(), $"print P={expression}");
    }

    [Test]
    public void PackAllKeepsNonJsonScalarTypesUnderTheCurrentColumnNames()
    {
        var rows = TestUtils.ExecuteAndGetDataFor4Columns<string, string, string, long>("WhenType", "SpanType", "IdType", "N",
            new KustoDatabase(), """
            print Stamp=datetime(2024-02-29), Span=2h, Token=guid(73c22437-33ba-4f69-8c68-4f3e8b53878b), Wide=9007199254740993
            | project WhenSeen=Stamp, Span, Token, Wide
            | extend P=pack_all()
            | project WhenType=gettype(P.WhenSeen), SpanType=gettype(P.Span), IdType=gettype(P.Token), N=tolong(P.Wide)
            """);
        Assert.That(rows, Is.EqualTo(new[] { ("datetime", "timespan", "guid", 9007199254740993L) }));
    }

    [TestCase("""dynamic({"tag":null,"Tag":1,"a.b":2,"child":{"inner":3}})""", new[] { "Tag", "a.b", "child", "tag" })]
    [TestCase("dynamic({})", new string[] { })]
    public void BagKeysEnumeratesAllRootKeysOnlyInUnspecifiedOrder(string bag, string[] expected)
    {
        var keys = JsonSerializer.SerializeToElement(AstraTestUtils.Print<object?>($"bag_keys({bag})"));
        Assert.That(keys.EnumerateArray().Select(key => key.GetString()), Is.EquivalentTo(expected));
    }

    [TestCase("present", true)]
    [TestCase("absent", false)]
    [TestCase("Present", false)]
    [TestCase("child", true)]
    [TestCase("leaf", false)]
    [TestCase("$.child.leaf", true)]
    [TestCase("$.child.other", false)]
    public void BagHasKeyTestsPresenceEvenWhenTheSlotIsNull(string key, bool expected)
    {
        Assert.That(AstraTestUtils.Print<bool>($$$"""bag_has_key(dynamic({"present":null,"child":{"leaf":null}}), '{{{key}}}')"""),
            Is.EqualTo(expected));
    }

    [Test]
    public void PresentNullAndMissingSlotReadTheSameButHaveDifferentMembership()
    {
        var rows = TestUtils.ExecuteAndGetDataFor4Columns<bool, bool, bool, bool>("NullA", "NullB", "HasA", "HasB",
            new KustoDatabase(), """
            print P=dynamic({"a":null})
            | project NullA=isnull(P.a), NullB=isnull(P.b), HasA=bag_has_key(P,'a'), HasB=bag_has_key(P,'b')
            """);
        Assert.That(rows, Is.EqualTo(new[] { (true, true, true, false) }));
    }

    [TestCase("""bag_merge(dynamic({"x":null,"obj":{"a":1}}), dynamic({"x":2,"obj":{"b":2},"y":3}), dynamic({"y":4,"z":5}))""",
        """{"z":5,"obj":{"a":1},"x":null,"y":3}""")]
    [TestCase("""bag_merge(dynamic({}), dynamic({"x":1}), dynamic({"X":2,"x":3}))""", """{"x":1,"X":2}""")]
    [TestCase("bag_merge(dynamic({}), dynamic({}))", "{}")]
    public void BagMergeTakesTheWholeLeftmostValueForEachKey(string expression, string expected)
    {
        AstraTestUtils.AssertJsonValue(AstraTestUtils.Print<object?>(expression), expected);
    }

    [TestCase("""bag_set_key(dynamic({}), "new", dynamic([1,null]))""", """{"new":[1,null]}""")]
    [TestCase("""bag_set_key(dynamic({"x":1,"y":2}), "x", dynamic(null))""", """{"x":null,"y":2}""")]
    [TestCase("""bag_set_key(dynamic({"id":1}), "$.child.flag", true)""", """{"id":1,"child":{"flag":true}}""")]
    [TestCase("""bag_set_key(parse_json('[1]'), "x", 2)""", "null")]
    [TestCase("""bag_set_key(parse_json('null'), "x", 2)""", "null")]
    public void BagSetKeyAddsOrUpdatesSlotsOnlyOnBags(string expression, string expected)
    {
        AstraTestUtils.AssertJsonValue(AstraTestUtils.Print<object?>(expression), expected);
    }

    [Test]
    public void UpdatingANestedSlotDoesNotMutateTheSourceOrDiscardSiblings()
    {
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<object, object>("Original", "Changed", new KustoDatabase(),
            """
            print Original=dynamic({"child":{"flag":false,"id":2},"keep":3})
            | extend Changed=bag_set_key(Original, "$.child.flag", true)
            """);
        Assert.That(rows, Has.Count.EqualTo(1));
        AstraTestUtils.AssertJsonValue(rows[0].Item1, """{"child":{"flag":false,"id":2},"keep":3}""");
        AstraTestUtils.AssertJsonValue(rows[0].Item2, """{"child":{"flag":true,"id":2},"keep":3}""");
    }

    private const string Bags = "Events | project Id, P=parse_json(Json)";

    private static KustoDatabase Events()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWith2Columns<long, string>(
            [(1, """{"N":3000000001,"Mixed":7,"Child":{"leaf":1},"AllNull":null}"""),
             (2, """{"N":3000000002,"Mixed":"text","Flag":false}"""),
             (3, """{"N":null,"AllNull":null}"""), (4, "{}"), (5, "null")], "Id", "Json", "Events"));
        return db;
    }

    [Test]
    public void UnpackWithWildcardKeepsSourceColumnsAndOnlyDeclaredSlotsForEveryRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(Events(), Bags + " | evaluate bag_unpack(P) : (*, N:long, Flag:bool)");
        foreach (var row in rows)
            Assert.That(row.Keys, Is.EquivalentTo(new[] { "Id", "N", "Flag" }));
        Assert.That(rows.Select(r => ((long)r["Id"]!, (long?)r["N"], (bool?)r["Flag"])),
            Is.EquivalentTo(new (long, long?, bool?)[]
                { (1, 3000000001, null), (2, 3000000002, false), (3, null, null), (4, null, null), (5, null, null) }));
    }

    [Test]
    public void UnpackWithoutWildcardReturnsOnlyTheNamedSchemaColumns()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(Events(), Bags + " | evaluate bag_unpack(P) : (N:long)");
        foreach (var row in rows)
            Assert.That(row.Keys, Is.EquivalentTo(new[] { "N" }));
        Assert.That(rows.Select(r => (long?)r["N"]), Is.EquivalentTo(new long?[] { null, 3000000002, null, 3000000001, null }));
    }

    [Test]
    public void ExplicitUnpackSchemaExistsEvenWhenNoRowsOrSlotsWereObserved()
    {
        var columns = TestUtils.ExecuteAndGetDataFor2Columns<string, string>("ColumnName", "ColumnType", Events(),
            Bags + " | where false | evaluate bag_unpack(P) : (*, NeverSeen:datetime, N:long) | getschema");
        Assert.That(columns, Is.EquivalentTo(new[] { ("Id", "long"), ("NeverSeen", "datetime"), ("N", "long") }));
    }

    [Test]
    public void DiscoveredColumnsUseAllRowsIgnoreNullsAndStopAtTopLevel()
    {
        var columns = TestUtils.ExecuteAndGetDataFor2Columns<string, string>("ColumnName", "ColumnType", Events(),
            Bags + " | evaluate bag_unpack(P) | getschema");
        Assert.That(columns, Is.EquivalentTo(new[]
            { ("Id", "long"), ("N", "long"), ("Mixed", "dynamic"), ("Child", "dynamic"), ("Flag", "bool") }));
    }

    [Test]
    public void DiscoveredMixedSlotsKeepTheirOriginalValuesAndMissingSlotsAreNull()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(Events(), Bags + " | evaluate bag_unpack(P)");
        Assert.That(rows.Select(r => (long)r["Id"]!), Is.EquivalentTo(new long[] { 1, 2, 3, 4, 5 }));
        foreach (var row in rows)
            Assert.That(row.Keys, Is.EquivalentTo(new[] { "Id", "N", "Mixed", "Child", "Flag" }));
        var byId = rows.ToDictionary(r => (long)r["Id"]!);
        AstraTestUtils.AssertJsonValue(byId[1]["Mixed"], "7");
        AstraTestUtils.AssertJsonValue(byId[2]["Mixed"], "\"text\"");
        AstraTestUtils.AssertJsonValue(byId[1]["Child"], """{"leaf":1}""");
        Assert.That(byId[2]["Child"], Is.Null);
        Assert.That(byId[1]["Flag"], Is.Null);
        Assert.That(byId[2]["Flag"], Is.EqualTo(false));
        foreach (long id in new long[] { 3, 4, 5 })
            Assert.That(byId[id].Where(pair => pair.Key != "Id").Select(pair => pair.Value), Is.All.Null);
    }

    [TestCase("")]
    [TestCase(" : (*, slot_Id:string, slot_Late:long)")]
    public void PrefixSeparatesAnUnpackedSlotFromAnExistingColumn(string schema)
    {
        var row = SummarizeOperatorOpusTestUtils.ExecuteAndGetSingleRow(new KustoDatabase(),
            """print Id=3, P=dynamic({"Id":"inner","Late":3000000001}) | evaluate bag_unpack(P, 'slot_')""" + schema);
        Assert.That(row.Keys, Is.EquivalentTo(new[] { "Id", "slot_Id", "slot_Late" }));
        Assert.That((row["Id"], row["slot_Id"], row["slot_Late"]), Is.EqualTo((3L, "inner", 3000000001L)));
    }
}
