using System.Text.Json;

namespace KustoPlayground.Core.Tests;

public class DynamicArrayAndSetAstraTests
{
    [Test]
    public void PackArrayEvaluatesArgumentsPerRowAndKeepsNestedValuesAndDuplicates()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWith2Columns<long, string>([(2, "blue"), (5, "red")], "Id", "Label", "T"));
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<long, object>("Id", "Items", db,
            """T | project Id, Items=pack_array(Label, Id + 1, dynamic(null), dynamic({"v":[2,1]}), Label)""");
        Assert.That(rows.Select(r => r.Item1), Is.EquivalentTo(new[] { 2L, 5L }));
        AstraTestUtils.AssertJsonValue(rows.Single(r => r.Item1 == 2).Item2, """["blue",3,null,{"v":[2,1]},"blue"]""");
        AstraTestUtils.AssertJsonValue(rows.Single(r => r.Item1 == 5).Item2, """["red",6,null,{"v":[2,1]},"red"]""");
    }

    [Test]
    public void PackArrayStarUsesTheCurrentProjectedColumnOrder()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("Items", new KustoDatabase(),
            "print Id=7, Label='ship', Old=99 | project Label, Id | project Items=pack_array(*)");
        AstraTestUtils.AssertJsonValue(value, """["ship",7]""");
    }

    [TestCase("[]", 0)]
    [TestCase("[null,[1,2],{},null]", 4)]
    [TestCase("null", null)]
    [TestCase("{}", null)]
    [TestCase("\"[1,2]\"", null)]
    [TestCase("12", null)]
    public void ArrayLengthCountsTopLevelItemsOnlyAndRequiresAnArray(string json, int? length)
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWithColumn([json], "Json", "T"));
        var value = OpusTestUtils.ExecuteAndGetSingleValue("N", db, "T | project N=array_length(parse_json(Json))");
        Assert.That(value, Is.EqualTo(length));
    }

    [TestCase(1, 2, """["one",null]""")]
    [TestCase(2, 2, "[null]")]
    [TestCase(-2, -1, """[null,{"k":3}]""")]
    [TestCase(-20, 1, """[0,"one"]""")]
    [TestCase(3, 20, """[{"k":3}]""")]
    [TestCase(2, 1, "[]")]
    [TestCase(8, 12, "[]")]
    public void ArraySliceUsesInclusiveEndpointsRelativeToEitherEnd(int start, int end, string expected)
    {
        var actual = AstraTestUtils.Print<object?>($$"""array_slice(dynamic([0,"one",null,{"k":3}]), {{start}}, {{end}})""");
        AstraTestUtils.AssertJsonValue(actual, expected);
    }

    [TestCase("""array_concat(dynamic([3,null]), dynamic([]), dynamic([[1,2],3]))""", "[3,null,[1,2],3]")]
    [TestCase("array_concat(dynamic([]), dynamic([]))", "[]")]
    [TestCase("""array_reverse(dynamic([1,[2,3],null,{"v":4}]))""", """[{"v":4},null,[2,3],1]""")]
    [TestCase("array_reverse(dynamic([]))", "[]")]
    public void ConcatAndReverseOperateOnTheOuterArray(string expression, string expected)
    {
        AstraTestUtils.AssertJsonValue(AstraTestUtils.Print<object?>(expression), expected);
    }

    [TestCase("'a'", "", 0)]
    [TestCase("'a'", ", 1", 2)]
    [TestCase("'a'", ", -2", 4)]
    [TestCase("'a'", ", 1, 1", -1)]
    [TestCase("'a'", ", 1, -1, 2", 4)]
    [TestCase("'a'", ", 1, -1, 3", -1)]
    [TestCase("'A'", "", -1)]
    [TestCase("7", "", -1)]
    [TestCase("'7'", "", 3)]
    [TestCase("'a'", ", 0, -2", null)]
    [TestCase("'a'", ", 0, -1, -1", null)]
    public void ArrayIndexOfUsesExactValuesAndBoundsItsSearch(string needle, string options, int? expected)
    {
        var actual = AstraTestUtils.Print<object?>($"""array_index_of(dynamic(["a","b","a","7","a"]), {needle}{options})""");
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("array_sort_asc(dynamic([10,null,-1,2,2]))", "[-1,2,2,10,null]")]
    [TestCase("array_sort_desc(dynamic([10,null,-1,2,2]))", "[10,2,2,-1,null]")]
    [TestCase("array_sort_asc(dynamic([3,null,1]), false)", "[null,1,3]")]
    [TestCase("array_sort_desc(dynamic([3,null,1]), false)", "[null,3,1]")]
    [TestCase("""array_sort_asc(dynamic(["b",10,"a",2]))""", """[2,10,"a","b"]""")]
    public void ArraySortOrdersNumbersNumericallyAndHonorsNullPlacement(string expression, string expected)
    {
        AstraTestUtils.AssertJsonValue(AstraTestUtils.Print<object?>(expression), expected);
    }

    [TestCase("array_sort_asc", "[2,7,10]", """["two","seven","ten"]""")]
    [TestCase("array_sort_desc", "[10,7,2]", """["ten","seven","two"]""")]
    public void CompanionArraysFollowTheKeyPermutationUnlessTheirLengthDiffers(string function, string keys, string labels)
    {
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<object?, object?, object?>("Ordered", "Aligned", "Mismatch",
            new KustoDatabase(), $"""
            print K=dynamic([7,10,2]), Names=dynamic(["seven","ten","two"]), Short=dynamic([0])
            | extend (Ordered, Aligned, Mismatch)={function}(K, Names, Short)
            """);
        Assert.That(rows, Has.Count.EqualTo(1));
        AstraTestUtils.AssertJsonValue(rows[0].Item1, keys);
        AstraTestUtils.AssertJsonValue(rows[0].Item2, labels);
        Assert.That(rows[0].Item3, Is.Null);
    }

    [TestCase("dynamic([2,-3,1.25])", 0.25)]
    [TestCase("dynamic([7])", 7.0)]
    [TestCase("""dynamic([2,"3"])""", 2.0)]
    [TestCase("dynamic([1,[2]])", 1.0)]
    public void ArraySumIsRealAndDoesNotCoerceNonnumericElements(string array, double? expected)
    {
        Assert.That(AstraTestUtils.Print<double?>($"array_sum({array})"), Is.EqualTo(expected));
    }

    [TestCase("""array_iff(dynamic([true,false,-2,0,"true",null,true]), dynamic([10,11,12]), 99)""",
        "[10,99,12,99,null,null,null]")]
    [TestCase("""array_iff(dynamic([false,true,false]), "yes", dynamic([1]))""", """[1,"yes",null]""")]
    [TestCase("array_iff(dynamic([false]), dynamic([1,2]), dynamic([3,4]))", "[3]")]
    [TestCase("array_iff(dynamic([]), 1, 0)", "[]")]
    public void ArrayIffBroadcastsScalarsAndPadsOnlyTheSelectedBranch(string expression, string expected)
    {
        AstraTestUtils.AssertJsonValue(AstraTestUtils.Print<object?>(expression), expected);
    }

    [TestCase("set_union(dynamic([1,2,1]), dynamic([2,3]), dynamic([4,3]))", new long[] { 1, 2, 3, 4 })]
    [TestCase("set_union(dynamic([]), dynamic([2,2]))", new long[] { 2 })]
    [TestCase("set_intersect(dynamic([1,2,2,3]), dynamic([3,2,4]), dynamic([2,4]))", new long[] { 2 })]
    [TestCase("set_intersect(dynamic([1]), dynamic([]))", new long[] { })]
    [TestCase("set_difference(dynamic([1,2,1,3,4]), dynamic([2,9]), dynamic([4,8]))", new long[] { 1, 3 })]
    [TestCase("set_difference(dynamic([1,1]), dynamic([1,2]))", new long[] { })]
    public void SetOperationsReturnDistinctMembersWithoutAnOrderGuarantee(string expression, long[] expected)
    {
        var array = JsonSerializer.SerializeToElement(AstraTestUtils.Print<object?>(expression));
        Assert.That(array.EnumerateArray().Select(item => item.GetInt64()), Is.EquivalentTo(expected));
    }

    [Test]
    public void SetUnionDoesNotCollapseAStringAndANumberWithTheSameSpelling()
    {
        var row = SummarizeOperatorOpusTestUtils.ExecuteAndGetSingleRow(new KustoDatabase(),
            """print Items=set_union(dynamic([1,"1",1]), dynamic(["1",true]))""");
        Assert.That(SummarizeOperatorOpusTestUtils.GetDynamicArray(row, "Items"), Is.EquivalentTo(new object[] { 1L, "1", true }));
    }

    [TestCase("'alpha-beta'", true)]
    [TestCase("'ALPHA-BETA'", false)]
    [TestCase("'beta'", false)]
    [TestCase("7", true)]
    [TestCase("'7'", false)]
    [TestCase("'nested'", false)]
    public void SetHasElementTestsAWholeTopLevelElement(string value, bool expected)
    {
        Assert.That(AstraTestUtils.Print<bool>($"""set_has_element(dynamic(["alpha-beta",7,["nested"]]), {value})"""),
            Is.EqualTo(expected));
    }

    [TestCase("dynamic(['sea',['east',['west']]])", new[] { "east", "sea", "west" })]
    [TestCase("dynamic([])", new string[] { })]
    public void InFlattensDynamicArrayConstantsButStillComparesExactValues(string rhs, string[] expected)
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.GenerateTableWithColumn<string>(["sea", "SEA", "east", "eastern", "west", "sea-west"], "Name", "T"));
        var names = TestUtils.ExecuteAndGetDataForOneColumn<string>("Name", db, $"T | where Name in ({rhs})");
        Assert.That(names, Is.EquivalentTo(expected));
    }

    [TestCase("has 'routing'", true)]
    [TestCase("has 'nestedkey'", true)]
    [TestCase("has 'TIMEOUT'", true)]
    [TestCase("has 'time'", false)]
    [TestCase("has 'oute'", false)]
    [TestCase("has_any ('unknown', 'zone')", true)]
    [TestCase("has_any (dynamic(['absent','http']))", true)]
    [TestCase("has_any ('time', 'nested')", false)]
    public void DynamicTermSearchIncludesNestedKeysAndValuesButNotSubstrings(string predicate, bool expected)
    {
        var hit = AstraTestUtils.Print<bool>($$"""dynamic({"routing-zone":[{"NestedKey":"HTTP-timeout"}]}) {{predicate}}""");
        Assert.That(hit, Is.EqualTo(expected));
    }

    [Test]
    public void TermSearchAndExactArrayMembershipAnswerDifferentQuestionsForTheSameRow()
    {
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<bool, bool, bool>("Term", "Element", "Whole", new KustoDatabase(),
            """
            print Tags=dynamic(["cold-storage","archive"])
            | project Term=Tags has "storage", Element=set_has_element(Tags,"storage"), Whole=set_has_element(Tags,"cold-storage")
            """);
        Assert.That(rows, Is.EqualTo(new[] { (true, false, true) }));
    }
}
