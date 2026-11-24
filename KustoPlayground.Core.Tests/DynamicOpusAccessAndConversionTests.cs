using System.Globalization;
using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Dynamic property and array access - https://learn.microsoft.com/en-us/kusto/query/scalar-data-types/dynamic#dynamic-object-accessors
/// and conversion functions of dynamic values: gettype, isnull, tostring, toint, tolong, toreal / todouble, tobool,
/// todatetime, totimespan, toguid (https://learn.microsoft.com/en-us/kusto/query/gettype-function ...).
/// Properties are accessed with d.name, d['name'] (needed for keys like 'a.b') or d[expression]; property names
/// are case-sensitive. Array elements are accessed with d[index]; negative indexes count from the end.
/// Accessors return dynamic values. A missing property, an out-of-range index, or an accessor applied to a value that
/// is not a bag / array returns null.
/// Conversion functions return null when the value can't be converted (they don't fail).
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class DynamicOpusAccessAndConversionTests
{
    private const string SampleLet =
        "let d = dynamic({'user': {'id': 7, 'name': 'ann'}, 'a.b': 'dotted', 'items': [10, 'x', {'k': 'v'}, null], '0': 'zero', 'n': null});";

    // property and array access

    [TestCase("d.user.id", "7")]
    [TestCase("d['user']['name']", "\"ann\"")]
    [TestCase("d.user['id']", "7", Description = "dot and bracket notation can be mixed")]
    [TestCase("d.user", """{"id":7,"name":"ann"}""")]
    [TestCase("d.items", """[10,"x",{"k":"v"},null]""")]
    [TestCase("d['a.b']", "\"dotted\"", Description = "a key containing a dot needs the bracket notation")]
    [TestCase("d.a.b", "null", Description = "d.a.b looks for the nested property b of the property a")]
    [TestCase("d.User", "null", Description = "property names are case-sensitive")]
    [TestCase("d.absent", "null")]
    [TestCase("d.absent.deeper", "null", Description = "accessing a property of a missing property doesn't fail")]
    [TestCase("d['0']", "\"zero\"")]
    [TestCase("d[0]", "null", Description = "an index can't be used to access a property of a bag")]
    [TestCase("d.items[0]", "10")]
    [TestCase("d.items[1]", "\"x\"")]
    [TestCase("d.items[2].k", "\"v\"")]
    [TestCase("d['items'][2]['k']", "\"v\"")]
    [TestCase("d.items[4]", "null", Description = "an index after the last element returns null")]
    [TestCase("d.items[-1]", "null", Description = "the last element is null")]
    [TestCase("d.items[-2].k", "\"v\"", Description = "negative indexes count from the end")]
    [TestCase("d.items[-4]", "10")]
    [TestCase("d.items[-5]", "null", Description = "a negative index before the first element returns null")]
    [TestCase("d.items['0']", "null", Description = "a string key can't be used to access an array element")]
    [TestCase("d.items[toint(1)]", "\"x\"")]
    [TestCase("d.items[1 + 1].k", "\"v\"", Description = "the index can be an expression")]
    [TestCase("d.items[array_length(d.items) - 3]", "\"x\"")]
    [TestCase("d[strcat('us', 'er')].name", "\"ann\"", Description = "the key can be an expression")]
    [Description("dynamic accessors - nested properties and array elements are returned, missing properties and out-of-range indexes return null")]
    public void Access_PropertyOrElement_ReturnsValueOrNull(string expression, string expectedJson)
    {
        var actual = EvaluateWithLets(SampleLet, expression);

        AssertDynamicJson(actual, expectedJson);
    }

    [TestCase("dynamic('str').a")]
    [TestCase("dynamic('str')[0]")]
    [TestCase("dynamic(5)[0]")]
    [TestCase("dynamic(5).a")]
    [TestCase("dynamic(null).a")]
    [TestCase("dynamic(null)[0]")]
    [TestCase("dynamic(null).a.b[0]")]
    [TestCase("dynamic([1, 2]).a")]
    [TestCase("dynamic([])[0]")]
    [TestCase("dynamic({})['']")]
    [Description("dynamic accessors - applied to a scalar, null or the wrong kind of collection they return null instead of failing")]
    public void Access_OnValueWithoutSuchMember_ReturnsNull(string expression)
    {
        Assert.That(OpusTestUtils.EvaluateScalar(expression), Is.Null);
    }

    [TestCase("gettype(d.user)", "dictionary")]
    [TestCase("gettype(d.items)", "array")]
    [TestCase("gettype(d.items[1])", "string")]
    [TestCase("gettype(d.user.id)", "long")]
    [TestCase("gettype(d.n)", "null")]
    [TestCase("gettype(d.absent)", "null")]
    [TestCase("gettype(d.items[100])", "null")]
    [Description("gettype - returns the type of an accessed property or element")]
    public void Access_GetType_ReturnsTypeOfAccessedValue(string expression, string expected)
    {
        OpusTestUtils.AssertTypedValue(EvaluateWithLets(SampleLet, expression), expected);
    }

    [TestCase("isnull(d.n)", true, Description = "a property with the null value")]
    [TestCase("isnull(d.absent)", true, Description = "a missing property")]
    [TestCase("isnull(d.items[-1])", true)]
    [TestCase("isnull(d.items[10])", true)]
    [TestCase("isnull(d.user)", false)]
    [TestCase("isnotnull(d.user.id)", true)]
    [TestCase("isempty(d.n)", true)]
    [TestCase("isempty(d.absent)", true)]
    [TestCase("isempty(d.items)", false)]
    [TestCase("isnotempty(d['a.b'])", true)]
    [TestCase("bag_has_key(d, 'n')", true, Description = "unlike isnull, bag_has_key distinguishes a property with null from a missing property")]
    [TestCase("bag_has_key(d, 'absent')", false)]
    [Description("isnull / isempty - null properties and missing properties are both null, only bag_has_key can distinguish them")]
    public void Access_NullAndMissingProperties_AreNull(string expression, bool expected)
    {
        OpusTestUtils.AssertTypedValue(EvaluateWithLets(SampleLet, expression), expected);
    }

    [Test]
    [Description("getschema - accessors return dynamic values, conversion functions return typed values")]
    public void Access_GetSchema_AccessorsReturnDynamic()
    {
        var actual = ExecuteAndGetSchema(
            new KustoDatabase(),
            $"""
             {SampleLet}
             print id = d.user.id, name = d['user'].name, item = d.items[0], typedId = tolong(d.user.id), typedName = tostring(d.user.name)
             | getschema
             """);

        Assert.That(actual, Is.EqualTo(new[] { "id:dynamic", "name:dynamic", "item:dynamic", "typedId:long", "typedName:string" }));
    }

    [Test]
    [Description("d[keyColumn] - the property name can come from a string column, a missing key returns null")]
    public void Access_KeyFromColumn_ReturnsPropertyPerRow()
    {
        var table = OpusTestUtils.NewTable("Keys")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithColumn<string>("key")
            .WithRow(1L, Bag(("user", Bag(("name", "ann"))), ("n", 1L)), "user")
            .WithRow(2L, Bag(("user", Bag(("name", "bob"))), ("n", 2L)), "absent")
            .WithRow(3L, Bag(("user", Bag(("name", "cid"))), ("n", 3L)), "n")
            .Build();
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Keys | project id, v = d[key], name = tostring(d[key].name), t = gettype(d[key])");

        var values = SingleById(rows, "v");
        AssertDynamic(values[1], Bag(("name", "ann")));
        AssertDynamic(values[2], null);
        AssertDynamic(values[3], 3);
        Assert.That(SingleById(rows, "name"), Is.EquivalentTo(new Dictionary<long, object?> { [1] = "ann", [2] = "", [3] = "" }));
        Assert.That(SingleById(rows, "t"), Is.EquivalentTo(new Dictionary<long, object?> { [1] = "dictionary", [2] = "null", [3] = "long" }));
    }

    [Test]
    [Description("d[indexColumn] - the array index can come from a long column, negative indexes count from the end, out-of-range indexes return null")]
    public void Access_IndexFromColumn_ReturnsElementPerRow()
    {
        var table = OpusTestUtils.NewTable("Indexes")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithColumn<long>("idx")
            .WithRow(1L, Bag(("items", Arr(10L, "x", Bag(("k", "v"))))), 1L)
            .WithRow(2L, Bag(("items", Arr(20L))), -1L)
            .WithRow(3L, Bag(("items", Arr(1L, 2L))), 5L)
            .WithRow(4L, Bag(("items", Arr(1L, 2L))), null)
            .Build();
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Indexes | project id, v = d.items[idx], t = gettype(d.items[idx])");

        var values = SingleById(rows, "v");
        AssertDynamic(values[1], "x");
        AssertDynamic(values[2], 20);
        AssertDynamic(values[3], null);
        AssertDynamic(values[4], null);
        Assert.That(SingleById(rows, "t"), Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = "string",
            [2] = "long",
            [3] = "null",
            [4] = "null"
        }));
    }

    [Test]
    [Description("d[expression] - the key can be computed per row with extend")]
    public void Access_KeyComputedWithExtend_ReturnsPropertyPerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateFlexibleDatabase(),
            "F | extend k = strcat('na', 'me') | project id, v = tostring(d[k])");

        Assert.That(SingleById(rows, "v"), Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = "a",
            [2] = "b",
            [3] = "",
            [4] = "c",
            [5] = "",
            [6] = ""
        }));
    }

    [Test]
    [Description("dynamic column with rows of different shapes - the same accessor returns values of different types or null per row")]
    public void Access_RowsWithDifferentShapes_ReturnValuePerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateFlexibleDatabase(),
            "F | project id, age = d.age, t = gettype(d.age), name = tostring(d.name), firstItem = d[0]");

        var ages = SingleById(rows, "age");
        AssertDynamic(ages[1], 30);
        AssertDynamic(ages[2], "45");
        AssertDynamic(ages[3], 25.5);
        AssertDynamic(ages[4], null);
        AssertDynamic(ages[5], null);
        AssertDynamic(ages[6], null);
        Assert.That(SingleById(rows, "t"), Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = "long",
            [2] = "string",
            [3] = "double",
            [4] = "null",
            [5] = "null",
            [6] = "null"
        }));
        Assert.That(SingleById(rows, "name"), Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = "a",
            [2] = "b",
            [3] = "",
            [4] = "c",
            [5] = "",
            [6] = ""
        }));
        var firstItems = SingleById(rows, "firstItem");
        AssertDynamic(firstItems[6], 1);
        Assert.That(firstItems.Where(item => item.Key != 6).Select(item => item.Value), Is.All.Null);
    }

    [TestCase("F | where d.age > 28 | project id", new[] { 1L, 2L }, Description = "the string '45' is compared as a number")]
    [TestCase("F | where tolong(d.age) > 28 | project id", new[] { 1L, 2L })]
    [TestCase("F | where toreal(d.age) < 28 | project id", new[] { 3L })]
    [TestCase("F | where d.name == 'a' | project id", new[] { 1L })]
    [TestCase("F | where tostring(d.name) in ('a', 'c') | project id", new[] { 1L, 4L })]
    [TestCase("F | where isnull(d.age) | project id", new[] { 4L, 5L, 6L })]
    [TestCase("F | where isnotempty(d.name) | project id", new[] { 1L, 2L, 4L })]
    [TestCase("F | where gettype(d) == 'array' | project id", new[] { 6L })]
    [TestCase("F | where gettype(d.age) == 'string' | project id", new[] { 2L })]
    [Description("where - rows are filtered by properties of a dynamic column, rows without the property don't match comparisons")]
    public void Access_WhereOnProperty_FiltersRows(string query, long[] expectedIds)
    {
        Assert.That(ExecuteAndGetIds(CreateFlexibleDatabase(), query), Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("summarize - extracted and converted properties can be aggregated and used as group by keys")]
    public void Access_SummarizeOnProperties_AggregatesExtractedValues()
    {
        var kustoDatabase = CreateFlexibleDatabase();

        var totals = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "F | summarize total = sum(toreal(d.age)), average = avg(toreal(d.age)), withName = countif(isnotnull(d.name))").Single();
        OpusTestUtils.AssertNumericValue(Col(totals, "total"), 100.5);
        OpusTestUtils.AssertNumericValue(Col(totals, "average"), 33.5);
        OpusTestUtils.AssertNumericValue(Col(totals, "withName"), 3);

        var byType = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "F | summarize c = count() by t = gettype(d.age)")
            .ToDictionary(row => (string)Col(row, "t")!, row => Convert.ToInt64(Col(row, "c"), CultureInfo.InvariantCulture));
        Assert.That(byType, Is.EquivalentTo(new Dictionary<string, long> { ["long"] = 1, ["string"] = 1, ["double"] = 1, ["null"] = 3 }));

        var byName = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "F | summarize c = count() by name = tostring(d.name)")
            .ToDictionary(row => (string)Col(row, "name")!, row => Convert.ToInt64(Col(row, "c"), CultureInfo.InvariantCulture));
        Assert.That(byName, Is.EquivalentTo(new Dictionary<string, long> { ["a"] = 1, ["b"] = 1, ["c"] = 1, [""] = 3 }));
    }

    [Test]
    [Description("sort - rows are sorted by a property converted with extend")]
    public void Access_SortByConvertedProperty_SortsRows()
    {
        var actual = OpusTestUtils.ExecuteAndGetValues(
            "id",
            CreateFlexibleDatabase(),
            "F | where isnotnull(d.age) | extend age = toreal(d.age) | sort by age desc | project id");

        Assert.That(actual, Is.EqualTo(new object[] { 2L, 1L, 3L }));
    }

    [Test]
    [Description("extend / project - typed columns created from properties of a dynamic column have scalar types")]
    public void Access_ExtendTypedColumns_HaveScalarTypes()
    {
        var kustoDatabase = CreateFlexibleDatabase();

        var schema = ExecuteAndGetSchema(
            kustoDatabase,
            "F | project id, age = toreal(d.age), name = tostring(d.name), raw = d.age | getschema");
        Assert.That(schema, Is.EqualTo(new[] { "id:long", "age:real", "name:string", "raw:dynamic" }));

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "F | project id, age = toreal(d.age)");
        var ages = SingleById(rows, "age");
        AssertTypedOrNull<double>(ages[1], 30.0);
        AssertTypedOrNull<double>(ages[2], 45.0);
        AssertTypedOrNull<double>(ages[3], 25.5);
        AssertTypedOrNull<double>(ages[4], null);
        AssertTypedOrNull<double>(ages[5], null);
        AssertTypedOrNull<double>(ages[6], null);
    }

    // conversion functions

    [TestCase("tolong(dynamic(12))", 12L)]
    [TestCase("tolong(dynamic('12'))", 12L)]
    [TestCase("tolong(dynamic(12.7))", 12L, Description = "the fractional part is truncated")]
    [TestCase("tolong(dynamic(true))", 1L)]
    [TestCase("tolong(dynamic('12.7'))", null, Description = "a string with a fractional part is not a long")]
    [TestCase("tolong(dynamic('abc'))", null)]
    [TestCase("tolong(dynamic([1]))", null)]
    [TestCase("tolong(dynamic({'a': 1}))", null)]
    [TestCase("tolong(dynamic(null))", null)]
    [TestCase("tolong(parse_json('{\"v\": 9223372036854775807}').v)", long.MaxValue)]
    [Description("tolong - converts dynamic numbers, numeric strings and bools, other values become null")]
    public void ToLong_DynamicValue_ReturnsLongOrNull(string expression, long? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [TestCase("toint(dynamic('12'))", 12)]
    [TestCase("toint(dynamic(12.7))", 12)]
    [TestCase("toint(dynamic(3000000000))", -1294967296, Description = "a long outside of the int range wraps around")]
    [TestCase("toint(dynamic('abc'))", null)]
    [TestCase("toint(dynamic(null))", null)]
    [Description("toint - converts dynamic values to int, other values become null")]
    public void ToInt_DynamicValue_ReturnsIntOrNull(string expression, int? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [TestCase("toreal(dynamic('1.5'))", 1.5)]
    [TestCase("toreal(dynamic(1))", 1.0)]
    [TestCase("toreal(dynamic(true))", 1.0)]
    [TestCase("todouble(dynamic('1e3'))", 1000.0)]
    [TestCase("todouble(dynamic(-0.25))", -0.25)]
    [TestCase("toreal(dynamic('abc'))", null)]
    [TestCase("todouble(dynamic([1.5]))", null)]
    [TestCase("toreal(dynamic(null))", null)]
    [Description("toreal / todouble - convert dynamic numbers, numeric strings and bools to real, other values become null")]
    public void ToReal_DynamicValue_ReturnsRealOrNull(string expression, double? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [TestCase("tobool(dynamic('true'))", true)]
    [TestCase("tobool(dynamic('TRUE'))", true)]
    [TestCase("tobool(dynamic(true))", true)]
    [TestCase("tobool(dynamic(1))", true)]
    [TestCase("tobool(dynamic(2))", true, Description = "every number except 0 is true")]
    [TestCase("tobool(dynamic(0))", false)]
    [TestCase("tobool(dynamic('yes'))", null)]
    [TestCase("tobool(dynamic([true]))", null)]
    [TestCase("tobool(dynamic(null))", null)]
    [Description("tobool - converts dynamic bools, 'true' / 'false' strings and numbers, other values become null")]
    public void ToBool_DynamicValue_ReturnsBoolOrNull(string expression, bool? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [TestCase("todatetime(dynamic('2020-01-01'))", "2020-01-01 00:00:00")]
    [TestCase("todatetime(dynamic(datetime(2020-01-02 03:04:05)))", "2020-01-02 03:04:05")]
    [TestCase("todatetime(parse_json('{\"t\": \"2020-01-02T03:04:05.123Z\"}').t)", "2020-01-02 03:04:05.123")]
    [TestCase("todatetime(dynamic('garbage'))", null)]
    [TestCase("todatetime(dynamic(null))", null)]
    [Description("todatetime - converts dynamic datetimes and datetime strings, other values become null")]
    public void ToDateTime_DynamicValue_ReturnsDateTimeOrNull(string expression, string? expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        AssertTypedOrNull<DateTime>(actual, expected is null ? null : OpusTestUtils.ParseDateTime(expected));
    }

    [Test]
    [Description("todatetime - a dynamic long is interpreted as ticks")]
    public void ToDateTime_DynamicLong_IsInterpretedAsTicks()
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("todatetime(dynamic(1))"), new DateTime(1, DateTimeKind.Utc));
    }

    [TestCase("totimespan(dynamic('01:00:00'))", "01:00:00")]
    [TestCase("totimespan(dynamic(36000000000))", "01:00:00", Description = "a long is interpreted as ticks")]
    [TestCase("totimespan(dynamic(1h))", "01:00:00", Description = "dynamic(1h) is stored as ticks")]
    [TestCase("totimespan(pack_array(90m)[0])", "01:30:00")]
    [TestCase("totimespan(dynamic('garbage'))", null)]
    [TestCase("totimespan(dynamic(null))", null)]
    [Description("totimespan - converts dynamic timespans, timespan strings and ticks, other values become null")]
    public void ToTimeSpan_DynamicValue_ReturnsTimeSpanOrNull(string expression, string? expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        AssertTypedOrNull<TimeSpan>(actual, expected is null ? null : OpusTestUtils.ParseTimeSpan(expected));
    }

    [TestCase("toguid(dynamic('6f9619ff-8b86-d011-b42d-00c04fc964ff'))", GuidText)]
    [TestCase("toguid(dynamic(guid(6f9619ff-8b86-d011-b42d-00c04fc964ff)))", GuidText)]
    [TestCase("toguid(parse_json('{\"g\": \"6f9619ff-8b86-d011-b42d-00c04fc964ff\"}').g)", GuidText)]
    [TestCase("toguid(dynamic('x'))", null)]
    [TestCase("toguid(dynamic(null))", null)]
    [Description("toguid - converts dynamic guids and guid strings, other values become null")]
    public void ToGuid_DynamicValue_ReturnsGuidOrNull(string expression, string? expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        AssertTypedOrNull<Guid>(actual, expected is null ? null : Guid.Parse(expected));
    }

    [TestCase("gettype(tolong(dynamic('12')))", "long")]
    [TestCase("gettype(toint(dynamic('12')))", "int")]
    [TestCase("gettype(toreal(dynamic(1)))", "real")]
    [TestCase("gettype(todouble(dynamic(1)))", "real")]
    [TestCase("gettype(tobool(dynamic(1)))", "bool")]
    [TestCase("gettype(todatetime(dynamic('2020-01-01')))", "datetime")]
    [TestCase("gettype(totimespan(dynamic('01:00:00')))", "timespan")]
    [TestCase("gettype(toguid(dynamic('6f9619ff-8b86-d011-b42d-00c04fc964ff')))", "guid")]
    [TestCase("gettype(tostring(dynamic(1)))", "string")]
    [Description("gettype - conversion functions return scalar (not dynamic) values")]
    public void Conversion_ResultType_IsScalarType(string expression, string expected)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [TestCase("dynamic(1) == 1")]
    [TestCase("dynamic('a') == 'a'")]
    [TestCase("dynamic(2) > 1")]
    [TestCase("dynamic(1.5) < 2")]
    [TestCase("dynamic({'a': 5}).a == 5")]
    [TestCase("dynamic({'a': 'x'}).a != 'y'")]
    [Description("comparisons - a dynamic scalar can be compared with a scalar of the same kind without conversion")]
    public void Comparison_DynamicScalarWithScalar_ReturnsTrue(string expression)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar(expression), true);
    }

    [TestCase("dynamic(2) * 3", 6L)]
    [TestCase("dynamic({'a': 2}).a + 1", 3L)]
    [TestCase("dynamic(2.5) + 1", 3L, Description = "the dynamic operand is converted to long (truncated) before the operation")]
    [TestCase("dynamic(25.5) * 2", 50L, Description = "25.5 is truncated to 25, not rounded to 26")]
    [TestCase("dynamic('4') * 2", 8L, Description = "a numeric string is converted")]
    [TestCase("dynamic(true) + 1", 2L)]
    [TestCase("dynamic(7) / 2", 3L, Description = "long division")]
    [TestCase("dynamic('abc') + 1", null)]
    [TestCase("dynamic(null) + 1", null)]
    [TestCase("dynamic([1]) + 1", null)]
    [TestCase("dynamic({'a': 2}).absent + 1", null)]
    [Description("arithmetic - a dynamic operand combined with a long is converted to long and the result is a long")]
    public void Arithmetic_DynamicWithLong_ReturnsLong(string expression, long? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [TestCase("dynamic(2.5) + 1.0", 3.5)]
    [TestCase("dynamic(7) / 2.0", 3.5)]
    [TestCase("dynamic({'a': 2}).a * 1.5", 3.0)]
    [TestCase("dynamic('abc') * 1.5", null)]
    [Description("arithmetic - a dynamic operand combined with a real is converted to real and the result is a real")]
    public void Arithmetic_DynamicWithReal_ReturnsReal(string expression, double? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [Test]
    [Description("arithmetic - two dynamic operands can't be combined, the values must be converted first")]
    public void Arithmetic_DynamicWithDynamic_Fails()
    {
        OpusTestUtils.AssertQueryFails(new KustoDatabase(), "print r = dynamic(30) - dynamic(5)");
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("tolong(dynamic(30)) - tolong(dynamic(5))"), 25L);
    }

    [Test]
    [Description("extend - arithmetic on a property of a dynamic column follows the type of the other operand per row")]
    public void Arithmetic_OnPropertyOfColumn_ReturnsTypedValuePerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateFlexibleDatabase(),
            "F | project id, doubled = d.age * 2, half = d.age / 2.0");

        var doubled = SingleById(rows, "doubled");
        AssertTypedOrNull<long>(doubled[1], 60L);
        AssertTypedOrNull<long>(doubled[2], 90L);
        AssertTypedOrNull<long>(doubled[3], 50L);
        AssertTypedOrNull<long>(doubled[4], null);
        AssertTypedOrNull<long>(doubled[5], null);
        AssertTypedOrNull<long>(doubled[6], null);
        var half = SingleById(rows, "half");
        AssertTypedOrNull<double>(half[1], 15.0);
        AssertTypedOrNull<double>(half[2], 22.5);
        AssertTypedOrNull<double>(half[3], 12.75);
        AssertTypedOrNull<double>(half[4], null);
    }

    [Test]
    [Description("tostring / strcat - property values are converted into text, strings without quotes and nested values as JSON")]
    public void Conversion_StrcatWithAccessedValues_ReturnsText()
    {
        var actual = EvaluateWithLets(SampleLet, "strcat(d.user.name, '#', d.user.id, '#', d.items[2], '#', d.absent, '#', d['a.b'])");

        OpusTestUtils.AssertTypedValue(actual, """ann#7#{"k":"v"}##dotted""");
    }

    private static KustoDatabase CreateFlexibleDatabase()
    {
        var table = OpusTestUtils.NewTable("F")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Bag(("age", 30L), ("name", "a")))
            .WithRow(2L, Bag(("age", "45"), ("name", "b")))
            .WithRow(3L, Bag(("age", 25.5)))
            .WithRow(4L, Bag(("name", "c")))
            .WithRow(5L, null)
            .WithRow(6L, Arr(1L, 2L))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }
}
