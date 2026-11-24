using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Construction and manipulation of dynamic property bags and arrays:
/// bag_pack / pack - https://learn.microsoft.com/en-us/kusto/query/pack-function,
/// pack_array - https://learn.microsoft.com/en-us/kusto/query/pack-array-function,
/// bag_keys - https://learn.microsoft.com/en-us/kusto/query/bag-keys-function,
/// bag_has_key - https://learn.microsoft.com/en-us/kusto/query/bag-has-key-function,
/// bag_merge - https://learn.microsoft.com/en-us/kusto/query/bag-merge-function,
/// bag_set_key - https://learn.microsoft.com/en-us/kusto/query/bag-set-key-function
/// and pack_all - https://learn.microsoft.com/en-us/kusto/query/pack-all-function combined with these functions.
/// <para>
/// Values keep their Kusto types inside the constructed bag / array (datetime, timespan, guid, decimal ...),
/// int values become long and real values are reported as "double" by gettype.
/// Property names are case-sensitive. A property holding null is different from a missing property:
/// d.key returns null in both cases, only bag_has_key / bag_keys / bag_merge see the difference.
/// The functions never modify their input values, every call returns a new value.
/// bag_keys and bag_set_key return null for values that are not property bags (null, arrays, scalars),
/// bag_merge ignores such arguments.
/// </para>
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class DynamicOpusPackAndBagFunctionsTests
{
    // bag_pack

    [TestCase("bag_pack('a', 1)", """{"a":1}""")]
    [TestCase("bag_pack('a', 1, 'b', 'x', 'c', true)", """{"a":1,"b":"x","c":true}""")]
    [TestCase("pack('a', 1, 'b', 'x')", """{"a":1,"b":"x"}""", Description = "pack is the deprecated alias of bag_pack")]
    [TestCase("bag_pack('x', 1 + 2, 'y', strcat('a', 'b'), 'z', 1.5 * 2)", """{"x":3,"y":"ab","z":3.0}""", Description = "values can be expressions")]
    [TestCase("bag_pack('a', 1, 'A', 2)", """{"a":1,"A":2}""", Description = "keys are case-sensitive")]
    [TestCase("bag_pack('a.b', 1)", """{"a.b":1}""", Description = "a key with a dot is a single key, not a path")]
    [TestCase("bag_pack('k', dynamic(null))", """{"k":null}""", Description = "a null value is kept")]
    [TestCase("bag_pack('a', dynamic({'b': {'c': 1}}))", """{"a":{"b":{"c":1}}}""")]
    [TestCase("bag_pack('arr', pack_array(1, 2), 'bag', bag_pack('x', 1))", """{"arr":[1,2],"bag":{"x":1}}""", Description = "constructors can be nested")]
    [TestCase("bag_pack('', 1)", "{}", Description = "a pair with an empty key is skipped")]
    [TestCase("bag_pack('', 1, 'b', 2)", """{"b":2}""")]
    [Description("bag_pack - creates a property bag from key / value pairs")]
    public void BagPack_KeyValuePairs_ReturnsBag(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    [Test]
    [Description("bag_pack - values keep their Kusto types (datetime, timespan, real, bool, long), int becomes long")]
    public void BagPack_TypedValues_KeepKustoTypes()
    {
        var actual = OpusTestUtils.EvaluateScalar(
            "bag_pack('t', datetime(2020-01-02 03:04:05), 's', 90m, 'r', 0.25, 'b', false, 'l', 9223372036854775807, 'i', toint(7))");

        AssertDynamic(actual, Bag(
            ("t", Utc(2020, 1, 2, 3, 4, 5)),
            ("s", TimeSpan.FromMinutes(90)),
            ("r", 0.25),
            ("b", false),
            ("l", long.MaxValue),
            ("i", 7L)));
    }

    [Test]
    [Description("bag_pack - tostring writes the properties in the order of the arguments, datetime and timespan as Kusto strings")]
    public void BagPack_TypedValues_ToStringWritesKustoJson()
    {
        var actual = OpusTestUtils.EvaluateScalar(
            "tostring(bag_pack('t', datetime(2020-01-02 03:04:05), 's', 90m, 'r', 0.25, 'b', false, 'l', 9223372036854775807, 'i', toint(7)))");

        OpusTestUtils.AssertTypedValue(actual,
            """{"t":"2020-01-02T03:04:05.0000000Z","s":"01:30:00","r":0.25,"b":false,"l":9223372036854775807,"i":7}""");
    }

    [TestCase("gettype(bag_pack('a', 1))", "dictionary")]
    [TestCase("gettype(bag_pack('v', datetime(2020-01-01)).v)", "datetime")]
    [TestCase("gettype(bag_pack('v', 1h).v)", "timespan", Description = "unlike dynamic(1h), which stores the ticks as long")]
    [TestCase("gettype(bag_pack('v', 1.5).v)", "double")]
    [TestCase("gettype(bag_pack('v', toint(1)).v)", "long")]
    [TestCase("gettype(bag_pack('v', true).v)", "bool")]
    [TestCase("gettype(bag_pack('v', '2020-01-01').v)", "string", Description = "a string value isn't converted into a datetime")]
    [TestCase("gettype(bag_pack('v', guid(6f9619ff-8b86-d011-b42d-00c04fc964ff)).v)", "guid")]
    [TestCase("gettype(bag_pack('v', decimal(1.5)).v)", "decimal")]
    [Description("gettype - bag_pack returns a dictionary, the values keep their types")]
    public void BagPack_GetType_ReturnsTypeOfValue(string expression, string expected)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [Test]
    [Description("bag_pack - keys and values can come from columns, the pair of a row with an empty key is skipped")]
    public void BagPack_KeysFromColumns_ReturnsBagPerRow()
    {
        var actual = SingleById(
            OpusTestUtils.ExecuteAndGetRows(CreateKeyValueDatabase(), "Kv | project id, b = bag_pack(k, v, 'id', id)"),
            "b");

        AssertDynamicJsonById(actual,
            (1, """{"a":10,"id":1}"""),
            (2, """{"id":2}"""),
            (3, """{"b":30,"id":3}"""));
    }

    [Test]
    [Description("bag_pack - the key can be an expression")]
    public void BagPack_KeyExpression_ReturnsBagPerRow()
    {
        var actual = SingleById(
            OpusTestUtils.ExecuteAndGetRows(CreateKeyValueDatabase(), "Kv | project id, b = bag_pack(strcat('key_', id), v)"),
            "b");

        AssertDynamicJsonById(actual,
            (1, """{"key_1":10}"""),
            (2, """{"key_2":20}"""),
            (3, """{"key_3":30}"""));
    }

    [Test]
    [Description("bag_pack - the properties of a constructed bag can be accessed and filtered later in the same query")]
    public void BagPack_AccessAndFilter_ReturnsMatchingRows()
    {
        var kustoDatabase = CreateKeyValueDatabase();

        var ids = ExecuteAndGetIds(
            kustoDatabase,
            "Kv | extend kv = bag_pack('key', k, 'value', v) | where kv.value > 15 | project id");
        Assert.That(ids, Is.EqualTo(new[] { 2L, 3L }));

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Kv | extend b = bag_pack(k, v), kv = bag_pack('key', k, 'value', v) | project id, va = b.a, keyText = tostring(kv.key)");
        AssertDynamicJsonById(SingleById(rows, "va"), (1, "10"), (2, "null"), (3, "null"));
        Assert.That(SingleById(rows, "keyText"), Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = "a",
            [2] = "",
            [3] = "b"
        }));
    }

    [Test]
    [Description("bag_pack - the value of a dynamic column is nested as it is (bag, array, scalar or null)")]
    public void BagPack_DynamicColumnValue_IsNested()
    {
        var actual = SingleById(
            OpusTestUtils.ExecuteAndGetRows(CreateBagsDatabase(), "B | project id, p = bag_pack('id', id, 'payload', d)"),
            "p");

        AssertDynamicJsonById(actual,
            (1, """{"id":1,"payload":{"a":1,"b":"x"}}"""),
            (2, """{"id":2,"payload":{"a":null}}"""),
            (3, """{"id":3,"payload":{}}"""),
            (4, """{"id":4,"payload":null}"""),
            (5, """{"id":5,"payload":[1,2]}"""),
            (6, """{"id":6,"payload":"str"}"""),
            (7, """{"id":7,"payload":{"nested":{"a":1}}}"""));
    }

    [Test]
    [Description("getschema - the constructors and bag functions return dynamic values, bag_has_key returns bool")]
    public void PackAndBagFunctions_GetSchema_ReturnsDynamicAndBool()
    {
        var actual = ExecuteAndGetSchema(
            new KustoDatabase(),
            "print b = bag_pack('a', 1), p = pack_array(1, 'x'), k = bag_keys(dynamic({'a': 1})), h = bag_has_key(dynamic({'a': 1}), 'a'), "
            + "m = bag_merge(dynamic({'a': 1}), dynamic({'b': 2})), s = bag_set_key(dynamic({}), 'a', 1) | getschema");

        Assert.That(actual, Is.EqualTo(new[] { "b:dynamic", "p:dynamic", "k:dynamic", "h:bool", "m:dynamic", "s:dynamic" }));
    }

    [TestCase("bag_pack()", Description = "at least one key / value pair is required")]
    [TestCase("bag_pack('a')")]
    [TestCase("bag_pack('a', 1, 'b')", Description = "the number of arguments must be even")]
    [TestCase("bag_pack(1, 2)", Description = "keys must be strings")]
    [TestCase("bag_pack(dynamic({'k': 'a'}).k, 2)", Description = "a dynamic key must be converted with tostring first")]
    [TestCase("pack_array()", Description = "at least one argument is required")]
    [TestCase("bag_merge(dynamic({'a': 1}))", Description = "at least two arguments are required")]
    [Description("bag_pack / pack_array / bag_merge - invalid arguments fail")]
    public void PackAndBagFunctions_InvalidArguments_Fails(string expression)
    {
        OpusTestUtils.AssertQueryFails(new KustoDatabase(), $"print r = {expression}");
    }

    // pack_array

    [TestCase("pack_array(1, 'x', 2.5, true)", """[1,"x",2.5,true]""", Description = "elements of different types")]
    [TestCase("pack_array(1, dynamic(null), 'x')", """[1,null,"x"]""", Description = "null elements are kept")]
    [TestCase("pack_array(long(null), real(null), '')", """[null,null,""]""")]
    [TestCase("pack_array(dynamic(null))", "[null]")]
    [TestCase("pack_array(dynamic([1, 2]))", "[[1,2]]", Description = "an array argument is nested, not flattened")]
    [TestCase("pack_array(dynamic([]))", "[[]]")]
    [TestCase("pack_array(bag_pack('a', 1), pack_array(2, 3))", """[{"a":1},[2,3]]""")]
    [Description("pack_array - creates an array from the arguments, in the order of the arguments")]
    public void PackArray_Values_ReturnsArray(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    [Test]
    [Description("pack_array - elements keep their Kusto types, int becomes long")]
    public void PackArray_TypedValues_KeepKustoTypes()
    {
        var actual = OpusTestUtils.EvaluateScalar(
            "pack_array(datetime(2020-01-02 03:04:05), 90m, guid(6f9619ff-8b86-d011-b42d-00c04fc964ff), decimal(1.5), toint(5))");

        AssertDynamic(actual, Arr(Utc(2020, 1, 2, 3, 4, 5), TimeSpan.FromMinutes(90), SampleGuid, 1.5m, 5L));
    }

    [Test]
    [Description("pack_array - tostring writes datetime, timespan, guid and decimal elements as JSON strings")]
    public void PackArray_TypedValues_ToStringWritesKustoJson()
    {
        var actual = OpusTestUtils.EvaluateScalar(
            "tostring(pack_array(datetime(2020-01-02 03:04:05), 90m, guid(6f9619ff-8b86-d011-b42d-00c04fc964ff), decimal(1.5), toint(5)))");

        OpusTestUtils.AssertTypedValue(actual,
            """["2020-01-02T03:04:05.0000000Z","01:30:00","6f9619ff-8b86-d011-b42d-00c04fc964ff","1.5",5]""");
    }

    [TestCase("gettype(pack_array(1))", "array")]
    [TestCase("gettype(pack_array(toint(5))[0])", "long")]
    [TestCase("gettype(pack_array(1.5)[0])", "double")]
    [TestCase("gettype(pack_array(decimal(1.5))[0])", "decimal")]
    [TestCase("gettype(pack_array(datetime(2020-01-01))[0])", "datetime")]
    [TestCase("gettype(pack_array(90m)[0])", "timespan")]
    [TestCase("gettype(pack_array(guid(6f9619ff-8b86-d011-b42d-00c04fc964ff))[0])", "guid")]
    [Description("gettype - pack_array returns an array, the elements keep their types")]
    public void PackArray_GetType_ReturnsTypeOfElement(string expression, string expected)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [TestCase("array_length(pack_array(1, dynamic(null), 'x'))", 3L, Description = "null elements are counted")]
    [TestCase("array_length(pack_array(dynamic([1, 2, 3])))", 1L, Description = "a nested array is one element")]
    [Description("pack_array + array_length - every argument is one element")]
    public void PackArray_ArrayLength_CountsArguments(string expression, long expected)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [Test]
    [Description("pack_array - elements can come from the columns of the row")]
    public void PackArray_ColumnValues_ReturnsArrayPerRow()
    {
        var actual = SingleById(
            OpusTestUtils.ExecuteAndGetRows(CreateKeyValueDatabase(), "Kv | project id, a = pack_array(id, k, v)"),
            "a");

        AssertDynamicJsonById(actual,
            (1, """[1,"a",10]"""),
            (2, """[2,"",20]"""),
            (3, """[3,"b",30]"""));
    }

    [Test]
    [Description("pack_array - the value of a dynamic column is one element (bag, array, scalar or null)")]
    public void PackArray_DynamicColumnValue_IsOneElement()
    {
        var actual = SingleById(
            OpusTestUtils.ExecuteAndGetRows(CreateBagsDatabase(), "B | project id, a = pack_array(id, d)"),
            "a");

        AssertDynamicJsonById(actual,
            (1, """[1,{"a":1,"b":"x"}]"""),
            (2, """[2,{"a":null}]"""),
            (3, "[3,{}]"),
            (4, "[4,null]"),
            (5, "[5,[1,2]]"),
            (6, """[6,"str"]"""),
            (7, """[7,{"nested":{"a":1}}]"""));
    }

    // bag_keys

    [TestCase("bag_keys(bag_pack('b', 1, 'a', 2))", new[] { "b", "a" })]
    [TestCase("bag_keys(parse_json('{\"z\": 1, \"y\": {\"x\": 2}}'))", new[] { "z", "y" }, Description = "only the keys of the top level")]
    [TestCase("bag_keys(dynamic({'a.b': 1, 'A': 2, 'a': 3}))", new[] { "a.b", "A", "a" })]
    [TestCase("bag_keys(dynamic({'n': null}))", new[] { "n" }, Description = "a key with a null value is returned")]
    [TestCase("bag_keys(dynamic({'user': {'id': 1, 'name': 'x'}}).user)", new[] { "id", "name" })]
    [Description("bag_keys - returns an array with the keys of a property bag (in any order)")]
    public void BagKeys_Bag_ReturnsKeys(string expression, string[] expectedKeys)
    {
        AssertDynamicUnordered(OpusTestUtils.EvaluateScalar(expression), expectedKeys);
    }

    [Test]
    [Description("bag_keys - an empty bag has no keys")]
    public void BagKeys_EmptyBag_ReturnsEmptyArray()
    {
        AssertDynamic(OpusTestUtils.EvaluateScalar("bag_keys(dynamic({}))"), Arr());
    }

    [TestCase("bag_keys(dynamic(null))")]
    [TestCase("bag_keys(dynamic([1, 2]))")]
    [TestCase("bag_keys(dynamic('str'))")]
    [TestCase("bag_keys(dynamic(5))")]
    [TestCase("bag_keys(dynamic('{\"a\":1}'))", Description = "a string with JSON text isn't parsed")]
    [Description("bag_keys - null, arrays and scalars are not property bags, the result is null")]
    public void BagKeys_NotABag_ReturnsNull(string expression)
    {
        AssertDynamic(OpusTestUtils.EvaluateScalar(expression), null);
    }

    [Test]
    [Description("bag_keys - is evaluated per row, array_length of the keys counts the properties")]
    public void BagKeys_DynamicColumn_ReturnsKeysPerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateBagsDatabase(),
            "B | project id, bagKeys = bag_keys(d), keyCount = array_length(bag_keys(d)), nestedKeys = bag_keys(d.nested)");

        var keys = SingleById(rows, "bagKeys");
        AssertDynamicUnordered(keys[1], "a", "b");
        AssertDynamicUnordered(keys[2], "a");
        AssertDynamicUnordered(keys[3]);
        AssertDynamic(keys[4], null);
        AssertDynamic(keys[5], null);
        AssertDynamic(keys[6], null);
        AssertDynamicUnordered(keys[7], "nested");

        Assert.That(SingleById(rows, "keyCount"), Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = 2L,
            [2] = 1L,
            [3] = 0L,
            [4] = null,
            [5] = null,
            [6] = null,
            [7] = 1L
        }));

        var nestedKeys = SingleById(rows, "nestedKeys");
        AssertDynamicUnordered(nestedKeys[7], "a");
        foreach (var id in new[] { 1L, 2L, 3L, 4L, 5L, 6L })
        {
            AssertDynamic(nestedKeys[id], null, $"id {id}");
        }
    }

    // bag_has_key

    [TestCase("bag_has_key(dynamic({'a': 1}), 'a')", true)]
    [TestCase("bag_has_key(dynamic({'a': 1}), 'b')", false)]
    [TestCase("bag_has_key(dynamic({'a': 1}), 'A')", false, Description = "keys are case-sensitive")]
    [TestCase("bag_has_key(dynamic({'a': null}), 'a')", true, Description = "a key with a null value exists")]
    [TestCase("bag_has_key(bag_set_key(dynamic({}), 'n', dynamic(null)), 'n')", true)]
    [TestCase("bag_has_key(dynamic({'a': 1}), '')", false)]
    [TestCase("bag_has_key(dynamic({'a': {'b': 1}}), 'b')", false, Description = "a plain key is searched only in the top level")]
    [TestCase("bag_has_key(dynamic({'a': 1}), '$.a')", true, Description = "the key can be a JSONPath")]
    [TestCase("bag_has_key(dynamic({'a': {'b': null}}), '$.a.b')", true, Description = "a nested key with a null value exists")]
    [TestCase("bag_has_key(dynamic({'a.b': 1}), 'a.b')", true, Description = "a plain key with a dot is a single key")]
    [TestCase("bag_has_key(dynamic({'a.b': 1}), '$.a.b')", false, Description = "a JSONPath with a dot looks for a nested key")]
    [TestCase("bag_has_key(dynamic({'a.b': 1}), \"$['a.b']\")", true, Description = "a JSONPath with brackets can address a key with a dot")]
    [TestCase("bag_has_key(dynamic({'a': [{'b': 1}]}), '$.a[0].b')", false, Description = "array indexes aren't supported in the JSONPath")]
    [TestCase("bag_has_key(dynamic(null), 'a')", false)]
    [TestCase("bag_has_key(dynamic([1, 2]), 'a')", false)]
    [TestCase("bag_has_key(dynamic('str'), 'a')", false)]
    [Description("bag_has_key - checks whether a key exists in a property bag, also when its value is null")]
    public void BagHasKey_Key_ReturnsWhetherKeyExists(string expression, bool expected)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [Test]
    [Description("bag_has_key vs isnull - a property with null and a missing property are both null, only bag_has_key tells them apart")]
    public void BagHasKey_NullValueVersusMissingKey_DistinguishesRows()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateBagsDatabase(),
            "B | project id, hasA = bag_has_key(d, 'a'), nullA = isnull(d.a)");

        Assert.That(SingleById(rows, "hasA"), Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = true,
            [2] = true,
            [3] = false,
            [4] = false,
            [5] = false,
            [6] = false,
            [7] = false
        }));
        Assert.That(SingleById(rows, "nullA"), Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = false,
            [2] = true,
            [3] = true,
            [4] = true,
            [5] = true,
            [6] = true,
            [7] = true
        }));
    }

    [TestCase("B | where bag_has_key(d, 'a') | project id", new[] { 1L, 2L })]
    [TestCase("B | where bag_has_key(d, 'a') and isnull(d.a) | project id", new[] { 2L }, Description = "the key exists with a null value")]
    [TestCase("B | where isnull(d.a) | project id", new[] { 2L, 3L, 4L, 5L, 6L, 7L })]
    [TestCase("B | where not(bag_has_key(d, 'a')) | project id", new[] { 3L, 4L, 5L, 6L, 7L })]
    [TestCase("B | where array_length(bag_keys(d)) > 0 | project id", new[] { 1L, 2L, 7L })]
    [Description("where - bag_has_key and bag_keys filter rows by the existence of keys")]
    public void BagFunctions_InWhere_FiltersRows(string query, long[] expectedIds)
    {
        Assert.That(ExecuteAndGetIds(CreateBagsDatabase(), query), Is.EqualTo(expectedIds));
    }

    // bag_merge

    [TestCase("bag_merge(dynamic({'a': 1}), dynamic({'b': 2}))", """{"a":1,"b":2}""")]
    [TestCase("bag_merge(dynamic({'a': 1}), dynamic({'a': 2, 'b': 2}), dynamic({'a': 3, 'b': 3, 'c': 5}))", """{"a":1,"b":2,"c":5}""", Description = "the value of the leftmost bag wins")]
    [TestCase("bag_merge(dynamic({'a': null}), dynamic({'a': 1}))", """{"a":null}""", Description = "an existing key with a null value also wins")]
    [TestCase("bag_merge(dynamic({'b': 1}), dynamic({'a': null}))", """{"b":1,"a":null}""", Description = "null values are kept")]
    [TestCase("bag_merge(dynamic({'a': {'x': 1}}), dynamic({'a': {'y': 2}}))", """{"a":{"x":1}}""", Description = "nested bags are not merged")]
    [TestCase("bag_merge(dynamic({'a': 1}), dynamic({'A': 2}))", """{"a":1,"A":2}""", Description = "keys are case-sensitive")]
    [TestCase("bag_merge(dynamic({}), dynamic({'a': 1}))", """{"a":1}""")]
    [TestCase("bag_merge(dynamic({'a': 1}), dynamic('str'))", """{"a":1}""", Description = "arguments that are not property bags are ignored")]
    [TestCase("bag_merge(dynamic(null), dynamic({'a': 1}))", """{"a":1}""")]
    [TestCase("bag_merge(dynamic([1]), dynamic({'a': 1}))", """{"a":1}""")]
    [TestCase("bag_merge(dynamic(null), dynamic(null))", "null", Description = "without any property bag the result is null")]
    [TestCase("bag_merge(dynamic([1]), dynamic(5))", "null")]
    [Description("bag_merge - merges property bags, on duplicate keys the value of the leftmost bag wins")]
    public void BagMerge_Bags_ReturnsMergedBag(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    [Test]
    [Description("bag_merge - with the row first and defaults last, the values of the row win (also a present key with null)")]
    public void BagMerge_RowBeforeDefaults_FillsOnlyMissingKeys()
    {
        var actual = SingleById(
            OpusTestUtils.ExecuteAndGetRows(CreateBagsDatabase(), "B | project id, merged = bag_merge(d, bag_pack('a', 'default', 'id', id))"),
            "merged");

        AssertDynamicJsonById(actual,
            (1, """{"a":1,"b":"x","id":1}"""),
            (2, """{"a":null,"id":2}"""),
            (3, """{"a":"default","id":3}"""),
            (4, """{"a":"default","id":4}"""),
            (5, """{"a":"default","id":5}"""),
            (6, """{"a":"default","id":6}"""),
            (7, """{"nested":{"a":1},"a":"default","id":7}"""));
    }

    [Test]
    [Description("bag_merge - with overrides first and the row last, the overrides win")]
    public void BagMerge_OverridesBeforeRow_ReplacesKeys()
    {
        var actual = SingleById(
            OpusTestUtils.ExecuteAndGetRows(CreateBagsDatabase(), "B | project id, merged = bag_merge(bag_pack('a', 'default'), d)"),
            "merged");

        AssertDynamicJsonById(actual,
            (1, """{"a":"default","b":"x"}"""),
            (2, """{"a":"default"}"""),
            (3, """{"a":"default"}"""),
            (4, """{"a":"default"}"""),
            (5, """{"a":"default"}"""),
            (6, """{"a":"default"}"""),
            (7, """{"a":"default","nested":{"a":1}}"""));
    }

    // bag_set_key

    [TestCase("bag_set_key(dynamic({'a': 1}), 'b', 'x')", """{"a":1,"b":"x"}""", Description = "adds a new key")]
    [TestCase("bag_set_key(dynamic({'a': 1}), 'a', 2)", """{"a":2}""", Description = "replaces the value of an existing key")]
    [TestCase("bag_set_key(dynamic({'a': 1}), 'a', dynamic(null))", """{"a":null}""", Description = "the key is kept with a null value")]
    [TestCase("bag_set_key(dynamic({}), 'n', dynamic(null))", """{"n":null}""")]
    [TestCase("bag_set_key(dynamic({'a': 1}), 'arr', dynamic([1, 2]))", """{"a":1,"arr":[1,2]}""")]
    [TestCase("bag_set_key(dynamic({'a': 1}), 'A', 2)", """{"a":1,"A":2}""", Description = "keys are case-sensitive")]
    [TestCase("bag_set_key(dynamic({'a': 1}), 'a.b', 2)", """{"a":1,"a.b":2}""", Description = "a plain key with a dot is a single key")]
    [TestCase("bag_set_key(dynamic({'base': true}), '', 1)", """{"base":true,"":1}""", Description = "unlike bag_pack, the empty key is added")]
    [TestCase("bag_set_key(dynamic({}), '$.a', 5)", """{"a":5}""", Description = "the key can be a JSONPath")]
    [TestCase("bag_set_key(dynamic({}), '$.a.b.c', 5)", """{"a":{"b":{"c":5}}}""", Description = "missing parent bags are created")]
    [TestCase("bag_set_key(dynamic({'a': {'x': 1, 'y': 2}}), '$.a.x', 5)", """{"a":{"x":5,"y":2}}""", Description = "the other keys of the nested bag are kept")]
    [TestCase("bag_set_key(dynamic({'a': 5}), '$.a.b', 1)", """{"a":{"b":1}}""", Description = "a scalar on the path is replaced by a bag")]
    [TestCase("bag_set_key(dynamic({'a': [1]}), '$.a.b', 1)", """{"a":{"b":1}}""", Description = "an array on the path is replaced by a bag")]
    [TestCase("bag_set_key(dynamic(null), 'a', 1)", "null")]
    [TestCase("bag_set_key(dynamic([1, 2]), 'a', 1)", "null")]
    [TestCase("bag_set_key(dynamic('str'), 'a', 1)", "null")]
    [TestCase("bag_set_key(dynamic(5), 'a', 1)", "null")]
    [Description("bag_set_key - returns the bag with the key added or replaced, values that are not property bags return null")]
    public void BagSetKey_Key_ReturnsUpdatedBag(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    [Test]
    [Description("bag_set_key - the value keeps its Kusto type")]
    public void BagSetKey_TypedValue_KeepsKustoType()
    {
        var actual = OpusTestUtils.EvaluateScalar("bag_set_key(dynamic({'a': 1}), 't', datetime(2020-01-02 03:04:05))");

        AssertDynamic(actual, Bag(("a", 1L), ("t", Utc(2020, 1, 2, 3, 4, 5))));
        OpusTestUtils.AssertTypedValue(
            OpusTestUtils.EvaluateScalar("gettype(bag_set_key(dynamic({}), 't', datetime(2020-01-01)).t)"),
            "datetime");
    }

    [Test]
    [Description("bag_set_key - is evaluated per row, rows that are not property bags return null")]
    public void BagSetKey_DynamicColumn_ReturnsUpdatedBagPerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateBagsDatabase(),
            "B | project id, withId = bag_set_key(d, 'id', id), deep = bag_set_key(d, '$.nested.b', id)");

        AssertDynamicJsonById(SingleById(rows, "withId"),
            (1, """{"a":1,"b":"x","id":1}"""),
            (2, """{"a":null,"id":2}"""),
            (3, """{"id":3}"""),
            (4, "null"),
            (5, "null"),
            (6, "null"),
            (7, """{"nested":{"a":1},"id":7}"""));
        AssertDynamicJsonById(SingleById(rows, "deep"),
            (1, """{"a":1,"b":"x","nested":{"b":1}}"""),
            (2, """{"a":null,"nested":{"b":2}}"""),
            (3, """{"nested":{"b":3}}"""),
            (4, "null"),
            (5, "null"),
            (6, "null"),
            (7, """{"nested":{"a":1,"b":7}}"""));
    }

    [TestCase("bag_set_key(bag_pack('id', 12, 'tags', dynamic(null)), 'tagCount', array_length(dynamic(null)))", """{"id":12,"tags":null,"tagCount":null}""")]
    [TestCase("bag_merge(bag_set_key(bag_pack('a', 1), 'b', pack_array(1, 2)), dynamic({'a': 0, 'c': 3}))", """{"a":1,"b":[1,2],"c":3}""")]
    [Description("combinations - the constructors and bag functions can be nested")]
    public void PackAndBagFunctions_Nested_ReturnsCombinedValue(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    // pack_all combined with the bag functions

    [Test]
    [Description("pack_all + bag_keys - every column is a key, also when its value is null or empty")]
    public void PackAll_BagKeys_ReturnsAllColumnNames()
    {
        var keys = SingleById(
            OpusTestUtils.ExecuteAndGetRows(CreatePackedDatabase(), "Packed | project id, bagKeys = bag_keys(pack_all())"),
            "bagKeys");

        AssertDynamicUnordered(keys[11], "id", "name", "tags");
        AssertDynamicUnordered(keys[12], "id", "name", "tags");
    }

    [Test]
    [Description("pack_all + bag_has_key - pack_all(true) drops the keys of null and empty columns")]
    public void PackAll_BagHasKey_IgnoreNullEmptyDropsKeys()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreatePackedDatabase(),
            "Packed | project id, hasTags = bag_has_key(pack_all(), 'tags'), hasTagsNonEmpty = bag_has_key(pack_all(true), 'tags'), "
            + "keyCount = array_length(bag_keys(pack_all(true)))");

        Assert.That(SingleById(rows, "hasTags"), Is.EquivalentTo(new Dictionary<long, object?> { [11] = true, [12] = true }));
        Assert.That(SingleById(rows, "hasTagsNonEmpty"), Is.EquivalentTo(new Dictionary<long, object?> { [11] = true, [12] = false }));
        Assert.That(SingleById(rows, "keyCount"), Is.EquivalentTo(new Dictionary<long, object?> { [11] = 3L, [12] = 1L }));
    }

    [Test]
    [Description("pack_all + bag_merge - explicit values on the left win over the columns of the row")]
    public void PackAll_BagMerge_LeftValuesWin()
    {
        var actual = SingleById(
            OpusTestUtils.ExecuteAndGetRows(
                CreatePackedDatabase(),
                "Packed | project id, merged = bag_merge(bag_pack('id', -1, 'extra', true), pack_all())"),
            "merged");

        AssertDynamicJsonById(actual,
            (11, """{"id":-1,"extra":true,"name":"ann","tags":["x"]}"""),
            (12, """{"id":-1,"extra":true,"name":"","tags":null}"""));
    }

    [Test]
    [Description("pack_all(true) - drops a dynamic null and an empty string column, but keeps an empty array, an empty bag "
                 + "and an empty string inside a dynamic column")]
    public void PackAll_IgnoreNullEmpty_KeepsEmptyDynamicValues()
    {
        var table = OpusTestUtils.NewTable("Empties")
            .WithColumn<long>("i")
            .WithColumn<object>("a")
            .WithColumn<object>("b")
            .WithColumn<object>("n")
            .WithColumn<object>("e")
            .WithColumn<string>("s")
            .WithRow(1L, Arr(), Bag(), null, "", "")
            .Build();
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Empties | project packed = pack_all(), nonEmpty = pack_all(true), packedText = tostring(pack_all()), nonEmptyText = tostring(pack_all(true))");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "packed"), """{"i":1,"a":[],"b":{},"n":null,"e":"","s":""}""");
        AssertDynamicJson(Col(rows[0], "nonEmpty"), """{"i":1,"a":[],"b":{},"e":""}""");
        OpusTestUtils.AssertTypedValue(Col(rows[0], "packedText"), """{"i":1,"a":[],"b":{},"n":null,"e":"","s":""}""");
        OpusTestUtils.AssertTypedValue(Col(rows[0], "nonEmptyText"), """{"i":1,"a":[],"b":{},"e":""}""");
    }

    // immutability

    [Test]
    [Description("immutability - bag_set_key and bag_merge return new bags, the bag of a let statement isn't changed")]
    public void BagFunctions_LetBag_IsNotModified()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            new KustoDatabase(),
            """
            let b = dynamic({'a': 1});
            print setResult = bag_set_key(b, 'c', 3), mergeResult = bag_merge(b, dynamic({'z': 1})), original = b
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "setResult"), """{"a":1,"c":3}""");
        AssertDynamicJson(Col(rows[0], "mergeResult"), """{"a":1,"z":1}""");
        AssertDynamicJson(Col(rows[0], "original"), """{"a":1}""");
    }

    [Test]
    [Description("immutability - a bag shared by all rows (let) is updated separately for every row")]
    public void BagFunctions_SharedLetBagPerRow_IsNotModified()
    {
        var table = OpusTestUtils.NewTable("Names")
            .WithColumn<long>("id")
            .WithColumn<string>("k")
            .WithRow(1L, "x")
            .WithRow(2L, "y")
            .WithRow(3L, "z")
            .Build();
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            """
            let seed = dynamic({'a': 1});
            Names | extend b = bag_set_key(seed, 'k', k), m = bag_merge(seed, bag_pack('k', k)), c = seed
            """);

        AssertDynamicJsonById(SingleById(rows, "b"),
            (1, """{"a":1,"k":"x"}"""),
            (2, """{"a":1,"k":"y"}"""),
            (3, """{"a":1,"k":"z"}"""));
        AssertDynamicJsonById(SingleById(rows, "m"),
            (1, """{"a":1,"k":"x"}"""),
            (2, """{"a":1,"k":"y"}"""),
            (3, """{"a":1,"k":"z"}"""));
        AssertDynamicJsonById(SingleById(rows, "c"),
            (1, """{"a":1}"""),
            (2, """{"a":1}"""),
            (3, """{"a":1}"""));
    }

    [Test]
    [Description("immutability - functions applied to a dynamic column don't change the column, neither in the query nor in the table")]
    public void BagFunctions_DynamicColumn_IsNotModified()
    {
        var kustoDatabase = CreateBagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "B | extend withId = bag_set_key(d, 'id', id), deep = bag_set_key(d, '$.nested.b', id), merged = bag_merge(d, bag_pack('extra', 1)) "
            + "| project id, d, withId, deep, merged");
        AssertOriginalBags(SingleById(rows, "d"));
        AssertDynamicJson(SingleById(rows, "merged")[1], """{"a":1,"b":"x","extra":1}""");

        var again = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "B | project id, d");
        AssertOriginalBags(SingleById(again, "d"));
    }

    private static void AssertOriginalBags(IReadOnlyDictionary<long, object?> actual)
    {
        AssertDynamicJsonById(actual,
            (1, """{"a":1,"b":"x"}"""),
            (2, """{"a":null}"""),
            (3, "{}"),
            (4, "null"),
            (5, "[1,2]"),
            (6, "\"str\""),
            (7, """{"nested":{"a":1}}"""));
    }

    private static KustoDatabase CreateBagsDatabase()
    {
        var table = OpusTestUtils.NewTable("B")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Bag(("a", 1L), ("b", "x")))
            .WithRow(2L, Bag(("a", null)))
            .WithRow(3L, Bag())
            .WithRow(4L, null)
            .WithRow(5L, Arr(1L, 2L))
            .WithRow(6L, "str")
            .WithRow(7L, Bag(("nested", Bag(("a", 1L)))))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateKeyValueDatabase()
    {
        var table = OpusTestUtils.NewTable("Kv")
            .WithColumn<long>("id")
            .WithColumn<string>("k")
            .WithColumn<long>("v")
            .WithRow(1L, "a", 10L)
            .WithRow(2L, "", 20L)
            .WithRow(3L, "b", 30L)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreatePackedDatabase()
    {
        var table = OpusTestUtils.NewTable("Packed")
            .WithColumn<long>("id")
            .WithColumn<string>("name")
            .WithColumn<object>("tags")
            .WithRow(11L, "ann", Arr("x"))
            .WithRow(12L, "", null)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }
}
