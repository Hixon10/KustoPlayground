using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Collection aggregates which build dynamic arrays and property bags under summarize:
/// make_list - https://learn.microsoft.com/en-us/kusto/query/make-list-aggregate-function,
/// make_list_if - https://learn.microsoft.com/en-us/kusto/query/make-list-if-aggregate-function,
/// make_list_with_nulls - https://learn.microsoft.com/en-us/kusto/query/make-list-with-nulls-aggregate-function,
/// make_set - https://learn.microsoft.com/en-us/kusto/query/make-set-aggregate-function,
/// make_set_if - https://learn.microsoft.com/en-us/kusto/query/make-set-if-aggregate-function,
/// make_bag - https://learn.microsoft.com/en-us/kusto/query/make-bag-aggregate-function
/// and make_bag_if - https://learn.microsoft.com/en-us/kusto/query/make-bag-if-aggregate-function.
/// <para>
/// make_list keeps duplicates and the order of a sorted input (the order of an unsorted input is undefined),
/// make_set returns distinct values (compared by type and value, also nested arrays and bags) without a guaranteed
/// order. Both skip nulls (make_list_with_nulls keeps them) and both add the elements of a dynamic array instead of
/// the array itself: only one level is flattened (nested arrays and property bags stay elements) and null elements of
/// the array are skipped as well. The optional maxSize (1..1048576, make_list_with_nulls has none) limits the number
/// of elements after the flattening.
/// make_bag merges the property bags of a group: other values are ignored, properties with null values are kept,
/// nested bags are not merged, a key found in several rows gets one of its values (which one is undefined) and the
/// optional maxSize limits the number of keys.
/// Elements keep their Kusto types (datetime, timespan, guid, decimal ...; an int value becomes a long).
/// Without input rows summarize returns one row with [] / {} (and no rows with by). Grouping by a dynamic value is an
/// error, group by tostring(...) or another conversion instead.
/// </para>
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class DynamicOpusCollectionAggregatesTests
{
    // make_list / make_set / make_list_with_nulls - flattening, nulls, order and distinct values

    [Test]
    [Description("make_list - arrays are flattened one level (their null elements are skipped), empty strings, bags and nested arrays are kept, nulls and empty arrays add nothing")]
    public void MakeList_MixedDynamicValues_FlattensArraysAndSkipsNulls()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("l", CreateMixedDatabase(),
            "Mixed | sort by id asc | summarize l = make_list(d)");

        AssertDynamicJson(value, """["",{},[1,2],[3],"x",1,{"k":[1]}]""");
    }

    [Test]
    [Description("make_list_with_nulls - like make_list, but null values and null elements of flattened arrays are kept")]
    public void MakeListWithNulls_MixedDynamicValues_KeepsNulls()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("l", CreateMixedDatabase(),
            "Mixed | sort by id asc | summarize l = make_list_with_nulls(d)");

        AssertDynamicJson(value, """["",{},null,[1,2],[3],"x",null,1,{"k":[1]}]""");
    }

    [Test]
    [Description("make_set - the distinct flattened values, nulls are skipped")]
    public void MakeSet_MixedDynamicValues_ReturnsDistinctFlattenedValues()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("s", CreateMixedDatabase(),
            "Mixed | summarize s = make_set(d)");

        AssertDynamicJsonUnordered(value, """["",{},[1,2],[3],"x",1,{"k":[1]}]""");
    }

    [TestCase("asc", """[1,2,2,3,3,"a","A","a"]""")]
    [TestCase("desc", """["a","A","a",3,2,3,1,2]""", Description = "the elements of every array keep their order")]
    [Description("make_list - duplicates are kept and a sorted input keeps its order")]
    public void MakeList_SortedInput_KeepsInputOrderAndDuplicates(string direction, string expectedJson)
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("l", CreateSetsDatabase(),
            $"Sets | sort by id {direction} | summarize l = make_list(d)");

        AssertDynamicJson(value, expectedJson);
    }

    [Test]
    [Description("make_list - the order of an unsorted input is undefined, but every value is returned")]
    public void MakeList_UnsortedInput_ReturnsAllValues()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("l", CreateSetsDatabase(),
            "Sets | summarize l = make_list(d)");

        AssertDynamicJsonUnordered(value, """[1,2,2,3,3,"a","A","a"]""");
    }

    [Test]
    [Description("make_set - duplicates across rows and inside arrays are removed, strings are case-sensitive, null elements are skipped")]
    public void MakeSet_ArraysWithDuplicates_ReturnsDistinctElements()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("s", CreateSetsDatabase(),
            "Sets | summarize s = make_set(d)");

        AssertDynamicJsonUnordered(value, """[1,2,3,"a","A"]""");
    }

    [Test]
    [Description("make_set - nested arrays and property bags are compared by value, [1,2] and [2,1] are different elements")]
    public void MakeSet_NestedArraysAndBags_AreComparedByValue()
    {
        var kustoDatabase = CreateDynamicDatabase("Nested",
            (1, Arr((object)Arr(1L, 2L))),
            (2, Arr(Arr(1L, 2L), Arr(2L, 1L))),
            (3, Arr(Bag(("a", 1L)), Bag(("a", 1L)))));

        var value = OpusTestUtils.ExecuteAndGetSingleValue("s", kustoDatabase, "Nested | summarize s = make_set(d)");

        AssertDynamicJsonUnordered(value, """[[1,2],[2,1],{"a":1}]""");
    }

    [Test]
    [Description("make_set - values are compared by type: 1 (long), '1' (string) and 1.0 (real) are different values")]
    public void MakeSet_SameNumberWithDifferentTypes_KeepsEveryType()
    {
        var kustoDatabase = CreateDynamicDatabase("Numbers",
            (1, 1L),
            (2, "1"),
            (3, 1.0),
            (4, 1L),
            (5, Bag(("a", 1L))),
            (6, Bag(("a", 1L))));

        var value = OpusTestUtils.ExecuteAndGetSingleValue("s", kustoDatabase, "Numbers | summarize s = make_set(d)");

        AssertDynamicJsonUnordered(value, """[1,"1",1.0,{"a":1}]""");
    }

    [Test]
    [Description("make_set - datetime values are deduplicated and keep their type, nulls are skipped")]
    public void MakeSet_DatetimeColumn_ReturnsDistinctDatetimes()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("s", CreateLoginsDatabase(),
            "Logins | summarize s = make_set(t)");

        AssertDynamicUnordered(value, Utc(2020, 1, 1), Utc(2021, 6, 1));
    }

    [Test]
    [Description("make_list - the datetime duplicates are kept in the sorted input order")]
    public void MakeList_DatetimeColumn_KeepsDuplicates()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("l", CreateLoginsDatabase(),
            "Logins | sort by id asc | summarize l = make_list(t)");

        AssertDynamic(value, Arr(Utc(2020, 1, 1), Utc(2020, 1, 1), Utc(2021, 6, 1)));
    }

    // typed values

    [Test]
    [Description("make_list - the elements keep the Kusto type of the column, nulls are skipped, empty strings are kept")]
    public void MakeList_TypedColumns_KeepsElementTypes()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateTypedDatabase(),
            "Typed | sort by id asc | summarize ld = make_list(dt), lt = make_list(ts), lg = make_list(g), lr = make_list(r), "
            + "lb = make_list(b), ls = make_list(s), lm = make_list(m), li = make_list(i)");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamic(Col(rows[0], "ld"), Arr(Utc(2020, 1, 1)), "ld");
        AssertDynamic(Col(rows[0], "lt"), Arr(TimeSpan.FromHours(1)), "lt");
        AssertDynamic(Col(rows[0], "lg"), Arr(SampleGuid), "lg");
        AssertDynamic(Col(rows[0], "lr"), Arr(1.5), "lr");
        AssertDynamic(Col(rows[0], "lb"), Arr(true), "lb");
        AssertDynamic(Col(rows[0], "ls"), Arr("x", ""), "ls");
        AssertDynamic(Col(rows[0], "lm"), Arr(1.25m), "lm");
        AssertDynamic(Col(rows[0], "li"), Arr(7L), "li");
    }

    [TestCase("dt", "datetime")]
    [TestCase("ts", "timespan")]
    [TestCase("g", "guid")]
    [TestCase("r", "double")]
    [TestCase("b", "bool")]
    [TestCase("s", "string")]
    [TestCase("m", "decimal")]
    [TestCase("i", "long", Description = "an int value becomes a long in a dynamic value")]
    [Description("make_list - gettype of a collected element is the type of the column")]
    public void MakeList_TypedColumn_ElementHasColumnType(string column, string expectedType)
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("t", CreateTypedDatabase(),
            $"Typed | summarize l = make_list({column}) | project t = gettype(l[0])");

        OpusTestUtils.AssertTypedValue(value, expectedType);
    }

    [Test]
    [Description("make_list_with_nulls - null values of typed columns are kept, a string column has empty strings instead of nulls")]
    public void MakeListWithNulls_TypedColumns_KeepsNulls()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateTypedDatabase(),
            "Typed | sort by id asc | summarize nr = make_list_with_nulls(r), ns = make_list_with_nulls(s), nd = make_list_with_nulls(dt)");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamic(Col(rows[0], "nr"), Arr(1.5, null), "nr");
        AssertDynamic(Col(rows[0], "ns"), Arr("x", ""), "ns");
        AssertDynamic(Col(rows[0], "nd"), Arr(Utc(2020, 1, 1), null), "nd");
    }

    // JSON text, parse_json and arrays built per row

    [Test]
    [Description("make_list - strings are never parsed (JSON text stays a string), parse_json results are flattened like other arrays and invalid JSON stays a string")]
    public void MakeList_JsonText_StringsStayStringsAndParsedArraysAreFlattened()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateTextsDatabase(),
            "Texts | sort by id asc | summarize raw = make_list(s), parsed = make_list(parse_json(s))");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "raw"), """["{\"a\":1}","not json","[1,2]","{\"b\":{\"c\":true}}"]""", "raw");
        AssertDynamicJson(Col(rows[0], "parsed"), """[{"a":1},"not json",1,2,{"b":{"c":true}}]""", "parsed");
    }

    [Test]
    [Description("make_bag - merges the bags parsed from JSON text, invalid JSON (a string) and arrays are ignored")]
    public void MakeBag_ParsedJsonText_MergesParsedBags()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("b", CreateTextsDatabase(),
            "Texts | summarize b = make_bag(parse_json(s))");

        AssertDynamicJson(value, """{"a":1,"b":{"c":true}}""");
    }

    [Test]
    [Description("make_list - an array built per row is flattened, wrap it in another array or use a bag to collect one item per row")]
    public void MakeList_ArrayBuiltPerRow_IsFlattened()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateLettersDatabase(),
            "Letters | sort by id asc | summarize flat = make_list(pack_array(id, e)), "
            + "pairs = make_list(pack_array(pack_array(id, e))), bags = make_list(bag_pack('id', id, 'e', e))");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "flat"), """[1,"a",2,"b"]""", "flat");
        AssertDynamicJson(Col(rows[0], "pairs"), """[[1,"a"],[2,"b"]]""", "pairs");
        AssertDynamicJson(Col(rows[0], "bags"), """[{"id":1,"e":"a"},{"id":2,"e":"b"}]""", "bags");
    }

    [Test]
    [Description("make_list - pack_all() collects whole rows as property bags")]
    public void MakeList_PackAll_CollectsRowsAsBags()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("packedRows", CreateLettersDatabase(),
            "Letters | sort by id asc | summarize packedRows = make_list(pack_all())");

        AssertDynamicJson(value, """[{"id":1,"e":"a"},{"id":2,"e":"b"}]""");
    }

    [Test]
    [Description("make_list - collecting the lists of groups flattens them, pack_array keeps one list per group")]
    public void MakeList_ListsOfGroups_AreFlattenedUnlessPacked()
    {
        var table = OpusTestUtils.NewTable("Points")
            .WithColumn<long>("x")
            .WithColumn<string>("g")
            .WithRow(1L, "a")
            .WithRow(2L, "a")
            .WithRow(3L, "b")
            .Build();
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Points | sort by x asc | summarize l = make_list(x) by g | sort by g asc "
            + "| summarize flat = make_list(l), nested = make_list(pack_array(l))");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "flat"), "[1,2,3]", "flat");
        AssertDynamicJson(Col(rows[0], "nested"), "[[1,2],[3]]", "nested");
    }

    // maxSize

    [TestCase("make_list(d, 1)", "[1]")]
    [TestCase("make_list(d, 2)", "[1,2]")]
    [TestCase("make_list(d, 3)", "[1,2,3]")]
    [TestCase("make_list(d, 4)", "[1,2,3,4]", Description = "the limit can stop in the middle of a flattened array")]
    [TestCase("make_list(d, 6)", "[1,2,3,4,5,6]")]
    [TestCase("make_list(d, 1000)", "[1,2,3,4,5,6]")]
    [TestCase("make_list(d)", "[1,2,3,4,5,6]")]
    [Description("make_list - maxSize limits the number of elements after flattening, the first elements of the sorted input are kept")]
    public void MakeList_MaxSize_LimitsFlattenedElements(string aggregate, string expectedJson)
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("l", CreateBatchesDatabase(),
            $"Batches | sort by id asc | summarize l = {aggregate}");

        AssertDynamicJson(value, expectedJson);
    }

    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(4, 4)]
    [TestCase(10, 6)]
    [Description("make_set - maxSize limits the number of distinct elements, which elements are returned is undefined")]
    public void MakeSet_MaxSize_LimitsDistinctElements(int maxSize, int expectedCount)
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("s", CreateBatchesDatabase(),
            $"Batches | summarize s = make_set(d, {maxSize})");

        AssertDynamicArraySubsetOf(value, expectedCount, "[1,2,3,4,5,6]");
    }

    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 3)]
    [TestCase(10, 3)]
    [Description("make_bag - maxSize limits the number of keys, which keys are returned is undefined")]
    public void MakeBag_MaxSize_LimitsNumberOfKeys(int maxSize, int expectedCount)
    {
        var kustoDatabase = CreateDynamicDatabase("BagLimits",
            (1, Bag(("a", 1L), ("b", 2L))),
            (2, Bag(("c", 3L))));

        var value = OpusTestUtils.ExecuteAndGetSingleValue("b", kustoDatabase,
            $"BagLimits | summarize b = make_bag(d, {maxSize})");

        AssertDynamicBagSubsetOf(value, expectedCount, """{"a":1,"b":2,"c":3}""");
    }

    // make_bag

    [Test]
    [Description("make_bag - merges the property bags, keeps properties with null values, ignores arrays, strings, numbers, nulls and empty bags")]
    public void MakeBag_MixedValues_MergesOnlyPropertyBags()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateBagsDatabase(), "Bags | summarize b = make_bag(d), c = count()");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "b"), """{"a":null,"b":1,"c":{"x":1}}""");
        OpusTestUtils.AssertTypedValue(Col(rows[0], "c"), 8L);
    }

    [Test]
    [Description("make_bag - returns an empty property bag when no value is a property bag")]
    public void MakeBag_NoPropertyBags_ReturnsEmptyBag()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("b", CreateBagsDatabase(),
            "Bags | where id in (2, 3, 4, 5, 7, 8) | summarize b = make_bag(d)");

        AssertDynamicJson(value, "{}");
    }

    [Test]
    [Description("make_bag - collects bags built with bag_pack per row, a null value is kept, a repeated key with the same value appears once")]
    public void MakeBag_BagPackPerRow_CollectsKeyValuePairs()
    {
        var table = OpusTestUtils.NewTable("Pairs")
            .WithColumn<long>("id")
            .WithColumn<string>("k")
            .WithColumn<long>("v")
            .WithRow(1L, "a", 1L)
            .WithRow(2L, "b", null)
            .WithRow(3L, "a", 1L)
            .Build();
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var value = OpusTestUtils.ExecuteAndGetSingleValue("b", kustoDatabase, "Pairs | summarize b = make_bag(bag_pack(k, v))");

        AssertDynamicJson(value, """{"a":1,"b":null}""");
    }

    [Test]
    [Description("make_bag - a key found in several rows gets one of its values: nested bags are not merged and a null value doesn't remove the key")]
    public void MakeBag_DuplicateKeys_OneValueWins()
    {
        var kustoDatabase = CreateDynamicDatabase("Dupes",
            (1, Bag(("n", Bag(("1", 1L))), ("a", 1L))),
            (2, Bag(("n", Bag(("2", 2L))), ("a", 2L))),
            (3, Bag(("z", null))),
            (4, Bag(("z", 1L))));

        var value = OpusTestUtils.ExecuteAndGetSingleValue("b", kustoDatabase, "Dupes | summarize b = make_bag(d)");

        var properties = BagProperties(value);
        Assert.That(properties.Keys, Is.EquivalentTo(new[] { "a", "n", "z" }));
        AssertDynamicJsonOneOf(properties["a"], "1", "2");
        AssertDynamicJsonOneOf(properties["n"], """{"1":1}""", """{"2":2}""");
        AssertDynamicJsonOneOf(properties["z"], "null", "1");
    }

    [Test]
    [Description("make_bag - datetime and guid values keep their types")]
    public void MakeBag_TypedValues_KeepsTypes()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateTypedDatabase(),
            "Typed | where id == 1 | summarize b = make_bag(bag_pack('t', dt, 'g', g)) "
            + "| extend tType = gettype(b.t), gType = gettype(b.g)");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamic(Col(rows[0], "b"), Bag(("t", Utc(2020, 1, 1)), ("g", SampleGuid)));
        OpusTestUtils.AssertTypedValue(Col(rows[0], "tType"), "datetime");
        OpusTestUtils.AssertTypedValue(Col(rows[0], "gType"), "guid");
    }

    [Test]
    [Description("make_bag by - merges the bags per group, a group without property bags gets an empty bag")]
    public void MakeBag_ByGroup_MergesBagsPerGroup()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreatePropsDatabase(), "Props | summarize props = make_bag(d) by host");

        AssertDynamicJsonByKey(SingleByKey(rows, "host", "props"),
            ("h1", """{"cpu":1,"mem":2}"""),
            ("h2", """{"cpu":3}"""),
            ("h3", "{}"));
    }

    [Test]
    [Description("make_list - property bags (also empty ones) are elements, they are not flattened")]
    public void MakeList_BagsOfGroups_KeepsBagsAsElements()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreatePropsDatabase(),
            "Props | summarize props = make_bag(d) by host | sort by host asc "
            + "| summarize perHost = make_list(props), hosts = make_list(host)");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "perHost"), """[{"cpu":1,"mem":2},{"cpu":3},{}]""", "perHost");
        AssertDynamicJson(Col(rows[0], "hosts"), """["h1","h2","h3"]""", "hosts");
    }

    [Test]
    [Description("make_bag by - pivots key/value rows into one bag per group whose properties can be read back")]
    public void MakeBag_KeyValueRows_PivotsIntoBagPerGroup()
    {
        var table = OpusTestUtils.NewTable("Metrics")
            .WithColumn<long>("id")
            .WithColumn<string>("metric")
            .WithColumn<long>("amount")
            .WithRow(1L, "cpu", 10L)
            .WithRow(1L, "mem", 20L)
            .WithRow(2L, "cpu", 30L)
            .Build();
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Metrics | summarize b = make_bag(bag_pack(metric, amount)) by id "
            + "| extend cpu = tolong(b.cpu), mem = tolong(b['mem'])");

        AssertDynamicJsonById(SingleById(rows, "b"), (1, """{"cpu":10,"mem":20}"""), (2, """{"cpu":30}"""));
        AssertTypedById<long>(SingleById(rows, "cpu"), (1, 10L), (2, 30L));
        AssertTypedById<long>(SingleById(rows, "mem"), (1, 20L), (2, null));
    }

    // summarize by

    [Test]
    [Description("summarize by - make_list keeps the sorted order inside every group, make_set is distinct, make_list_with_nulls keeps missing values, groups without values get [] and {}")]
    public void CollectionAggregates_ByGroupDescending_KeepOrderPerGroup()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateEventsDatabase(),
            "Events | sort by seq desc | summarize events = make_list(d.e), uniq = make_set(d.e), bags = make_bag(d.x), "
            + "withNulls = make_list_with_nulls(d.e), tagList = make_list(d.tags), firstTwo = make_list(d.tags, 2), c = count() "
            + "by user = tostring(d.u)");

        AssertDynamicJsonByKey(SingleByKey(rows, "user", "events"),
            ("a", """["d","c","a"]"""), ("b", """["b"]"""), ("c", "[]"));
        AssertDynamicJsonUnorderedByKey(SingleByKey(rows, "user", "uniq"),
            ("a", """["a","c","d"]"""), ("b", """["b"]"""), ("c", "[]"));
        AssertDynamicJsonByKey(SingleByKey(rows, "user", "bags"),
            ("a", "{}"), ("b", "{}"), ("c", "{}"));
        AssertDynamicJsonByKey(SingleByKey(rows, "user", "withNulls"),
            ("a", """["d","c","a"]"""), ("b", """["b"]"""), ("c", "[null,null]"));
        AssertDynamicJsonByKey(SingleByKey(rows, "user", "tagList"),
            ("a", """["t3","t1","t1","t2","t2"]"""), ("b", "[]"), ("c", "[]"));
        AssertDynamicJsonByKey(SingleByKey(rows, "user", "firstTwo"),
            ("a", """["t3","t1"]"""), ("b", "[]"), ("c", "[]"));

        var counts = SingleByKey(rows, "user", "c");
        OpusTestUtils.AssertTypedValue(counts["a"], 3L);
        OpusTestUtils.AssertTypedValue(counts["b"], 1L);
        OpusTestUtils.AssertTypedValue(counts["c"], 2L);
    }

    [Test]
    [Description("summarize by - with an ascending sort the lists of every group are ascending")]
    public void CollectionAggregates_ByGroupAscending_KeepOrderPerGroup()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateEventsDatabase(),
            "Events | sort by seq asc | summarize events = make_list(d.e), tagList = make_list(d.tags), "
            + "firstTwo = make_list(d.tags, 2), tags = make_set(d.tags) by user = tostring(d.u)");

        AssertDynamicJsonByKey(SingleByKey(rows, "user", "events"),
            ("a", """["a","c","d"]"""), ("b", """["b"]"""), ("c", "[]"));
        AssertDynamicJsonByKey(SingleByKey(rows, "user", "tagList"),
            ("a", """["t2","t1","t2","t3","t1"]"""), ("b", "[]"), ("c", "[]"));
        AssertDynamicJsonByKey(SingleByKey(rows, "user", "firstTwo"),
            ("a", """["t2","t1"]"""), ("b", "[]"), ("c", "[]"));
        AssertDynamicJsonUnorderedByKey(SingleByKey(rows, "user", "tags"),
            ("a", """["t1","t2","t3"]"""), ("b", "[]"), ("c", "[]"));
    }

    [Test]
    [Description("summarize by - group by the type and a property of dynamic values (converted with gettype / tostring) and collect the ids")]
    public void MakeList_GroupedByTypeAndProperty_CollectsIdsPerGroup()
    {
        var kustoDatabase = CreateDynamicDatabase("Kinds",
            (1, 1L),
            (2, "a"),
            (4, Arr(1L)),
            (5, Bag(("grp", "k1"))),
            (6, null),
            (7, true),
            (8, 7L),
            (9, Bag(("grp", "k1"))),
            (10, Bag(("other", 1L))));

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Kinds | summarize c = count(), ids = make_list(id) by t = gettype(d), category = tostring(d.grp)");

        (string Group, long Count, string Ids)[] expected =
        [
            ("array|", 1, "[4]"),
            ("bool|", 1, "[7]"),
            ("dictionary|", 1, "[10]"),
            ("dictionary|k1", 2, "[5,9]"),
            ("long|", 2, "[1,8]"),
            ("null|", 1, "[6]"),
            ("string|", 1, "[2]")
        ];
        var groups = rows.Select(row => $"{Col(row, "t")}|{Col(row, "category")}").ToList();
        Assert.That(groups, Is.EquivalentTo(expected.Select(item => item.Group)));

        foreach (var (group, count, ids) in expected)
        {
            var row = rows[groups.IndexOf(group)];
            OpusTestUtils.AssertTypedValue(Col(row, "c"), count);
            AssertDynamicJsonUnordered(Col(row, "ids"), ids, group);
        }
    }

    [Test]
    [Description("make_set results can be used by array functions: array_length and set_has_element per group")]
    public void MakeSet_ResultUsedByArrayFunctions_PerGroup()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateEventsDatabase(),
            "Events | summarize uniq = make_set(d.e) by user = tostring(d.u) "
            + "| extend n = array_length(uniq), hasA = set_has_element(uniq, 'a')");

        var lengths = SingleByKey(rows, "user", "n");
        OpusTestUtils.AssertTypedValue(lengths["a"], 3L);
        OpusTestUtils.AssertTypedValue(lengths["b"], 1L);
        OpusTestUtils.AssertTypedValue(lengths["c"], 0L);

        var hasA = SingleByKey(rows, "user", "hasA");
        OpusTestUtils.AssertTypedValue(hasA["a"], true);
        OpusTestUtils.AssertTypedValue(hasA["b"], false);
        OpusTestUtils.AssertTypedValue(hasA["c"], false);
    }

    [Test]
    [Description("make_list results can be indexed: the first and the last (negative index) element per group, an empty list has none")]
    public void MakeList_ResultIndexed_PerGroup()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateEventsDatabase(),
            "Events | sort by seq asc | summarize l = make_list(d.e) by user = tostring(d.u) "
            + "| extend firstEvent = tostring(l[0]), lastEvent = tostring(l[-1])");

        var firstEvents = SingleByKey(rows, "user", "firstEvent");
        var lastEvents = SingleByKey(rows, "user", "lastEvent");
        Assert.That(firstEvents["a"], Is.EqualTo("a"));
        Assert.That(lastEvents["a"], Is.EqualTo("d"));
        Assert.That(firstEvents["b"], Is.EqualTo("b"));
        Assert.That(lastEvents["b"], Is.EqualTo("b"));
        Assert.That(firstEvents["c"], Is.EqualTo(""));
        Assert.That(lastEvents["c"], Is.EqualTo(""));
    }

    // make_list_if / make_set_if / make_bag_if

    [TestCase("make_list_if(d.tag, tolong(d.n) > 1)", """["y","z","y"]""")]
    [TestCase("make_list_if(d.tag, d.n > 1)", """["y","z","y"]""", Description = "the dynamic string '7' is compared as a number")]
    [TestCase("make_list_if(id, d.n > 4)", "[2,3,5]")]
    [TestCase("make_list_if(id, d.n == 7)", "[3]", Description = "the dynamic string '7' equals the number 7")]
    [TestCase("make_list_if(id, d.tag == 'y')", "[2,5]")]
    [TestCase("make_list_if(id, isnull(d.n))", "[4]", Description = "a missing property is null")]
    [TestCase("make_list_if(id, false)", "[]")]
    [TestCase("make_list_if(d.tag, tolong(d.n) > 1, 2)", """["y","z"]""", Description = "maxSize")]
    [Description("make_list_if - collects the values of the rows where the predicate is true, in the sorted input order")]
    public void MakeListIf_DynamicPredicate_CollectsMatchingValues(string aggregate, string expectedJson)
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("l", CreateReadingsDatabase(),
            $"Readings | sort by id asc | summarize l = {aggregate}");

        AssertDynamicJson(value, expectedJson);
    }

    [Test]
    [Description("make_set_if - the distinct values of the rows where the predicate is true")]
    public void MakeSetIf_DynamicPredicate_ReturnsDistinctMatchingValues()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("s", CreateReadingsDatabase(),
            "Readings | summarize s = make_set_if(tostring(d.tag), tolong(d.n) > 1)");

        AssertDynamicJsonUnordered(value, """["y","z"]""");
    }

    [Test]
    [Description("make_set_if - maxSize limits the number of distinct values")]
    public void MakeSetIf_MaxSize_LimitsDistinctValues()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("s", CreateReadingsDatabase(),
            "Readings | summarize s = make_set_if(d.tag, tolong(d.n) > 1, 1)");

        AssertDynamicArraySubsetOf(value, 1, """["y","z"]""");
    }

    [Test]
    [Description("make_bag_if - merges the bags of the rows where the predicate is true, the values keep their dynamic type")]
    public void MakeBagIf_DynamicPredicate_MergesMatchingBags()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateReadingsDatabase(),
            "Readings | summarize b = make_bag_if(bag_pack(tostring(d.tag), d.n), tolong(d.n) < 9) | extend zType = gettype(b.z)");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "b"), """{"x":1,"y":5,"z":"7"}""");
        OpusTestUtils.AssertTypedValue(Col(rows[0], "zType"), "string");
    }

    [Test]
    [Description("make_bag_if - a repeated key (y) gets one of its values")]
    public void MakeBagIf_RepeatedKey_OneValueWins()
    {
        var value = OpusTestUtils.ExecuteAndGetSingleValue("b", CreateReadingsDatabase(),
            "Readings | summarize b = make_bag_if(bag_pack(tostring(d.tag), d.n), isnotnull(d.n))");

        var properties = BagProperties(value);
        Assert.That(properties.Keys, Is.EquivalentTo(new[] { "x", "y", "z" }));
        AssertDynamicJson(properties["x"], "1", "x");
        AssertDynamicJson(properties["z"], "\"7\"", "z");
        AssertDynamicJsonOneOf(properties["y"], "5", "9");
    }

    // no input rows

    [Test]
    [Description("summarize without by - without input rows returns one row with empty arrays and an empty bag")]
    public void CollectionAggregates_NoRows_ReturnEmptyCollections()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateDocsDatabase(),
            "Docs | where id < 0 | summarize l = make_list(d), s = make_set(d), b = make_bag(d), n = make_list_with_nulls(d)");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "l"), "[]", "l");
        AssertDynamicJson(Col(rows[0], "s"), "[]", "s");
        AssertDynamicJson(Col(rows[0], "b"), "{}", "b");
        AssertDynamicJson(Col(rows[0], "n"), "[]", "n");
    }

    [Test]
    [Description("summarize by - without input rows there are no groups")]
    public void CollectionAggregates_NoRowsWithBy_ReturnNoRows()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateDocsDatabase(),
            "Docs | where id < 0 | summarize l = make_list(d), b = make_bag(d) by g = tostring(d.u)");

        Assert.That(rows, Is.Empty);
    }

    // errors

    [TestCase("Docs | summarize make_list(d, 0)", Description = "maxSize must be in 1..1048576")]
    [TestCase("Docs | summarize make_list(d, -1)")]
    [TestCase("Docs | summarize make_set(d, 0)")]
    [TestCase("Docs | summarize make_bag(d, 0)")]
    [TestCase("Docs | summarize make_list_with_nulls(d, 2)", Description = "make_list_with_nulls has no maxSize")]
    [TestCase("Docs | summarize make_bag(s)", Description = "make_bag expects a dynamic value")]
    [TestCase("Docs | summarize count() by d", Description = "grouping by a dynamic value is not supported")]
    [TestCase("Docs | summarize count() by d.u")]
    [Description("collection aggregates - invalid arguments and grouping by dynamic values are errors")]
    public void CollectionAggregates_InvalidQuery_Fails(string query)
    {
        OpusTestUtils.AssertQueryFails(CreateDocsDatabase(), query);
    }

    // result column names

    [TestCase("make_list(d)", "list_d:dynamic")]
    [TestCase("make_set(d)", "set_d:dynamic")]
    [TestCase("make_bag(d)", "bag_d:dynamic")]
    [TestCase("make_list_with_nulls(d)", "list_d:dynamic")]
    [TestCase("make_list(d.a)", "list_d_a:dynamic")]
    [TestCase("make_set(d.a, 5)", "set_d_a:dynamic")]
    [TestCase("make_set(tostring(d.k))", "set_d_k:dynamic")]
    [TestCase("make_bag(bag_pack('z', id))", "bag_:dynamic")]
    [TestCase("make_list_if(d.a, id > 1)", "list_d_a:dynamic")]
    [TestCase("make_set_if(d, id > 1)", "set_d:dynamic")]
    [TestCase("make_bag_if(d, id > 1)", "bag_d:dynamic")]
    [Description("collection aggregates - default names of the result columns, the result type is always dynamic")]
    public void CollectionAggregates_WithoutName_UseDefaultColumnName(string aggregate, string expectedColumn)
    {
        var schema = ExecuteAndGetSchema(CreateDocsDatabase(), $"Docs | summarize {aggregate} | getschema");

        Assert.That(schema, Is.EqualTo(new[] { expectedColumn }));
    }

    [Test]
    [Description("collection aggregates - repeated default names get a numeric suffix")]
    public void CollectionAggregates_RepeatedDefaultNames_GetSuffix()
    {
        var schema = ExecuteAndGetSchema(
            CreateDocsDatabase(),
            "Docs | summarize make_list(d), make_list_with_nulls(d), make_bag(d), make_bag_if(d, id > 1), make_list(d.a), "
            + "make_list_if(d.a, id > 1), make_set(d), make_set_if(d, id > 1) | getschema");

        Assert.That(schema, Is.EqualTo(new[]
        {
            "list_d:dynamic", "list_d1:dynamic", "bag_d:dynamic", "bag_d1:dynamic",
            "list_d_a:dynamic", "list_d_a1:dynamic", "set_d:dynamic", "set_d1:dynamic"
        }));
    }

    [Test]
    [Description("summarize by - the default name of a tostring(d.u) group column is d_u")]
    public void MakeList_GroupedByConvertedProperty_UsesDefaultGroupColumnName()
    {
        var schema = ExecuteAndGetSchema(CreateDocsDatabase(),
            "Docs | summarize ids = make_list(id) by tostring(d.u) | getschema");

        Assert.That(schema, Is.EqualTo(new[] { "d_u:string", "ids:dynamic" }));
    }

    private static KustoDatabase CreateDynamicDatabase(string tableName, params (long Id, object? Value)[] rows)
    {
        var builder = OpusTestUtils.NewTable(tableName)
            .WithColumn<long>("id")
            .WithColumn<object>("d");
        foreach (var (id, value) in rows)
        {
            builder.WithRow(id, value);
        }

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(builder.Build());
        return kustoDatabase;
    }

    private static KustoDatabase CreateMixedDatabase() => CreateDynamicDatabase("Mixed",
        (1, ""),
        (2, Arr()),
        (3, Bag()),
        (4, null),
        (5, Arr(Arr(1L, 2L), Arr(3L))),
        (6, "x"),
        (7, Arr(null, 1L)),
        (8, Bag(("k", Arr(1L)))));

    private static KustoDatabase CreateSetsDatabase() => CreateDynamicDatabase("Sets",
        (1, Arr(1L, 2L)),
        (2, Arr(2L, 3L)),
        (3, Arr(3L, null)),
        (4, Arr("a", "A", "a")));

    private static KustoDatabase CreateBatchesDatabase() => CreateDynamicDatabase("Batches",
        (1, Arr(1L, 2L, 3L)),
        (2, Arr(4L, 5L)),
        (3, 6L));

    private static KustoDatabase CreateBagsDatabase() => CreateDynamicDatabase("Bags",
        (1, Bag(("a", null), ("b", 1L))),
        (2, Arr(1L, 2L)),
        (3, "str"),
        (4, 5L),
        (5, null),
        (6, Bag(("c", Bag(("x", 1L))))),
        (7, Bag()),
        (8, Arr(Bag(("z", 1L)))));

    private static KustoDatabase CreateReadingsDatabase() => CreateDynamicDatabase("Readings",
        (1, Bag(("n", 1L), ("tag", "x"))),
        (2, Bag(("n", 5L), ("tag", "y"))),
        (3, Bag(("n", "7"), ("tag", "z"))),
        (4, Bag(("tag", "w"))),
        (5, Bag(("n", 9L), ("tag", "y"))));

    private static KustoDatabase CreateEventsDatabase()
    {
        var table = OpusTestUtils.NewTable("Events")
            .WithColumn<long>("seq")
            .WithColumn<object>("d")
            .WithRow(3L, Bag(("u", "a"), ("e", "c"), ("tags", Arr("t1", "t2"))))
            .WithRow(1L, Bag(("u", "a"), ("e", "a"), ("tags", Arr("t2"))))
            .WithRow(2L, Bag(("u", "b"), ("e", "b"), ("tags", Arr())))
            .WithRow(4L, Bag(("u", "a"), ("e", "d"), ("tags", Arr("t3", "t1"))))
            .WithRow(5L, Bag(("u", "c")))
            .WithRow(6L, Bag(("u", "c")))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreatePropsDatabase()
    {
        var table = OpusTestUtils.NewTable("Props")
            .WithColumn<long>("id")
            .WithColumn<string>("host")
            .WithColumn<object>("d")
            .WithRow(1L, "h1", Bag(("cpu", 1L)))
            .WithRow(2L, "h1", Bag(("mem", 2L)))
            .WithRow(3L, "h2", Bag(("cpu", 3L)))
            .WithRow(4L, "h2", null)
            .WithRow(5L, "h3", Arr(1L))
            .WithRow(6L, "h3", """{"z":1}""")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateTypedDatabase()
    {
        var table = OpusTestUtils.NewTable("Typed")
            .WithColumn<long>("id")
            .WithColumn<DateTime>("dt")
            .WithColumn<TimeSpan>("ts")
            .WithColumn<Guid>("g")
            .WithColumn<double>("r")
            .WithColumn<bool>("b")
            .WithColumn<string>("s")
            .WithColumn<decimal>("m")
            .WithColumn<int>("i")
            .WithRow(1L, Utc(2020, 1, 1), TimeSpan.FromHours(1), SampleGuid, 1.5, true, "x", 1.25m, 7)
            .WithRow(2L, null, null, null, null, null, "", null, null)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateLoginsDatabase()
    {
        var table = OpusTestUtils.NewTable("Logins")
            .WithColumn<long>("id")
            .WithColumn<DateTime>("t")
            .WithRow(1L, Utc(2020, 1, 1))
            .WithRow(2L, Utc(2020, 1, 1))
            .WithRow(3L, Utc(2021, 6, 1))
            .WithRow(4L, null)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateTextsDatabase()
    {
        var table = OpusTestUtils.NewTable("Texts")
            .WithColumn<long>("id")
            .WithColumn<string>("s")
            .WithRow(1L, """{"a":1}""")
            .WithRow(2L, "not json")
            .WithRow(3L, "[1,2]")
            .WithRow(4L, """{"b":{"c":true}}""")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateLettersDatabase()
    {
        var table = OpusTestUtils.NewTable("Letters")
            .WithColumn<long>("id")
            .WithColumn<string>("e")
            .WithRow(1L, "a")
            .WithRow(2L, "b")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateDocsDatabase()
    {
        var table = OpusTestUtils.NewTable("Docs")
            .WithColumn<long>("id")
            .WithColumn<string>("s")
            .WithColumn<object>("d")
            .WithRow(1L, "a", Bag(("u", "x"), ("a", 1L), ("k", "v")))
            .WithRow(2L, "b", Arr(1L))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }
}
