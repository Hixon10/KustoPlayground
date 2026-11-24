using System.Globalization;
using System.Text.Json;
using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// The dynamic data type - https://learn.microsoft.com/en-us/kusto/query/scalar-data-types/dynamic
/// and parse_json(json) - https://learn.microsoft.com/en-us/kusto/query/parse-json-function
/// A dynamic value is null, a scalar (bool, long, real, string, datetime, timespan, guid ...), an ordered array
/// of dynamic values (elements can have different types) or a property bag with string keys (unordered) and dynamic values.
/// Arrays and bags can be nested. gettype() reports 'double' for a real stored in a dynamic value ('real' for a real scalar).
/// Strings in dynamic(...) literals and in parse_json() which look like a datetime or a guid become datetime / guid values,
/// a timespan in a dynamic(...) literal is stored as a long number of ticks.
/// parse_json() never fails on malformed JSON, it returns the original text as a dynamic string.
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class DynamicOpusColumnLiteralsAndParseJsonTests
{
    private static List<(long Id, object? Value)> StoredValues() =>
    [
        (1L, null),
        (2L, 5L),
        (3L, 1.5),
        (4L, "text"),
        (5L, true),
        (6L, Arr(1L, "a", 2.5, false, null, Arr(1L, 2L), Bag(("k", "v")))),
        (7L, Bag(("b", 1L), ("a", Arr(true, null)), ("n", null))),
        (8L, Utc(2020, 1, 2, 3, 4, 5)),
        (9L, TimeSpan.FromHours(1)),
        (10L, SampleGuid),
        (11L, Arr()),
        (12L, Bag())
    ];

    // dynamic column

    [Test]
    [Description("dynamic column - null, scalars, mixed / nested / empty arrays and nested / empty property bags are returned unchanged")]
    public void DynamicColumn_StoredValues_AreReturnedUnchanged()
    {
        var kustoDatabase = CreateStoredDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Stored | project id, d");
        var actual = SingleById(rows, "d");

        var expected = StoredValues();
        Assert.That(actual.Keys, Is.EquivalentTo(expected.Select(item => item.Id)));
        foreach (var (id, value) in expected)
        {
            AssertDynamic(actual[id], value, $"id {id}");
        }
    }

    [TestCase(1L, "null")]
    [TestCase(2L, "long")]
    [TestCase(3L, "double", Description = "a real number stored in a dynamic value is reported as 'double'")]
    [TestCase(4L, "string")]
    [TestCase(5L, "bool")]
    [TestCase(6L, "array")]
    [TestCase(7L, "dictionary")]
    [TestCase(8L, "datetime")]
    [TestCase(9L, "timespan", Description = "a timespan stored in a dynamic value keeps its type (like pack_array(1h)[0])")]
    [TestCase(10L, "guid")]
    [TestCase(11L, "array")]
    [TestCase(12L, "dictionary")]
    [Description("gettype - returns the type of the value stored in each row of a dynamic column")]
    public void DynamicColumn_GetType_ReturnsTypeOfStoredValue(long id, string expected)
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "t",
            CreateStoredDatabase(),
            $"Stored | where id == {id} | project t = gettype(d)");

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase(1L, "")]
    [TestCase(2L, "5")]
    [TestCase(3L, "1.5")]
    [TestCase(4L, "text")]
    [TestCase(5L, "true")]
    [TestCase(6L, """[1,"a",2.5,false,null,[1,2],{"k":"v"}]""")]
    [TestCase(7L, """{"b":1,"a":[true,null],"n":null}""")]
    [TestCase(8L, "2020-01-02T03:04:05.0000000Z")]
    [TestCase(9L, "01:00:00")]
    [TestCase(10L, GuidText)]
    [TestCase(11L, "[]")]
    [TestCase(12L, "{}")]
    [Description("tostring - a dynamic string is returned without quotes, null as an empty string, other values as JSON text which keeps the order of array elements and bag properties")]
    public void DynamicColumn_ToString_ReturnsJsonText(long id, string expected)
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "s",
            CreateStoredDatabase(),
            $"Stored | where id == {id} | project s = tostring(d)");

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [Test]
    [Description("getschema - a column with dynamic values has the type dynamic")]
    public void DynamicColumn_GetSchema_ReturnsDynamicType()
    {
        var actual = ExecuteAndGetSchema(CreateStoredDatabase(), "Stored | getschema");

        Assert.That(actual, Is.EqualTo(new[] { "id:long", "d:dynamic" }));
    }

    [Test]
    [Description("isnull / isempty - only the null value is null or empty, empty arrays and empty bags are neither null nor empty")]
    public void DynamicColumn_IsNullAndIsEmpty_OnlyMatchNull()
    {
        var kustoDatabase = CreateStoredDatabase();

        Assert.That(ExecuteAndGetIds(kustoDatabase, "Stored | where isnull(d) | project id"), Is.EqualTo(new[] { 1L }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Stored | where isempty(d) | project id"), Is.EqualTo(new[] { 1L }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Stored | where isnotnull(d) | project id"),
            Is.EqualTo(Enumerable.Range(2, 11).Select(id => (long)id)));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Stored | where isnotempty(d) | project id"), Has.Count.EqualTo(11));
    }

    [Test]
    [Description("extend - a dynamic column copied into a new column keeps the values and their types")]
    public void DynamicColumn_ExtendCopy_KeepsValuesAndTypes()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateStoredDatabase(),
            "Stored | extend c = d | project id, c, t = gettype(c)");

        var values = SingleById(rows, "c");
        foreach (var (id, value) in StoredValues())
        {
            AssertDynamic(values[id], value, $"id {id}");
        }

        var types = SingleById(rows, "t");
        Assert.That(types[8], Is.EqualTo("datetime"));
        Assert.That(types[9], Is.EqualTo("timespan"));
        Assert.That(types[10], Is.EqualTo("guid"));
    }

    [Test]
    [Description("summarize by gettype(d) - rows of a dynamic column can be grouped by the type of their values")]
    public void DynamicColumn_SummarizeByGetType_CountsValuesPerType()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateStoredDatabase(),
            "Stored | summarize c = count() by t = gettype(d)");

        var actual = rows.ToDictionary(
            row => (string)Col(row, "t")!,
            row => Convert.ToInt64(Col(row, "c"), CultureInfo.InvariantCulture));

        Assert.That(actual, Is.EquivalentTo(new Dictionary<string, long>
        {
            ["null"] = 1,
            ["long"] = 1,
            ["double"] = 1,
            ["string"] = 1,
            ["bool"] = 1,
            ["array"] = 2,
            ["dictionary"] = 2,
            ["datetime"] = 1,
            ["timespan"] = 1,
            ["guid"] = 1
        }));
    }

    [TestCase("Stored | summarize count() by d")]
    [TestCase("Stored | sort by d")]
    [TestCase("Stored | distinct d")]
    [Description("a dynamic column can't be a summarize by key, a sort key or a distinct column (convert it with tostring() first)")]
    public void DynamicColumn_AsGroupOrSortKey_Fails(string query)
    {
        OpusTestUtils.AssertQueryFails(CreateStoredDatabase(), query);
    }

    [Test]
    [Description("summarize by tostring(d) - equal arrays and bags have the same JSON text and are counted together, element order matters")]
    public void DynamicColumn_SummarizeByToString_GroupsEqualValues()
    {
        var table = OpusTestUtils.NewTable("Dup")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Bag(("a", 1L)))
            .WithRow(2L, Bag(("a", 1L)))
            .WithRow(3L, Arr(1L, 2L))
            .WithRow(4L, Arr(1L, 2L))
            .WithRow(5L, Arr(2L, 1L))
            .WithRow(6L, null)
            .WithRow(7L, "x")
            .Build();
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Dup | summarize c = count() by s = tostring(d)");

        var actual = rows.ToDictionary(
            row => (string)Col(row, "s")!,
            row => Convert.ToInt64(Col(row, "c"), CultureInfo.InvariantCulture));
        Assert.That(actual, Is.EquivalentTo(new Dictionary<string, long>
        {
            ["""{"a":1}"""] = 2,
            ["[1,2]"] = 2,
            ["[2,1]"] = 1,
            [""] = 1,
            ["x"] = 1
        }));
    }

    [Test]
    [Description("dynamic column - deeply nested arrays and bags are stored and can be navigated with property and index access")]
    public void DynamicColumn_DeeplyNestedValue_CanBeNavigated()
    {
        var table = OpusTestUtils.NewTable("Nested")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Json("""{"level1":{"level2":[{"level3":[0,[7,8]]}],"flag":true}}"""))
            .Build();
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var row = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            """
            Nested
            | project v = d.level1.level2[0].level3[1][0],
                      t = gettype(d.level1.level2[0].level3),
                      s = tostring(d.level1.level2),
                      f = d.level1.flag,
                      absent = d.level1.level2[0].level4
            """).Single();

        AssertDynamic(Col(row, "v"), 7);
        Assert.That(Col(row, "t"), Is.EqualTo("array"));
        Assert.That(Col(row, "s"), Is.EqualTo("""[{"level3":[0,[7,8]]}]"""));
        AssertDynamic(Col(row, "f"), true);
        Assert.That(Col(row, "absent"), Is.Null);
    }

    [Test]
    [Description("dynamic column - datetime, timespan, guid, long, real, bool and string values inside a bag keep their Kusto types")]
    public void DynamicColumn_TypedScalarsInsideBag_KeepKustoTypes()
    {
        var row = OpusTestUtils.ExecuteAndGetRows(
            CreateTypedBagDatabase(),
            """
            Typed
            | where id == 1
            | project start = gettype(d.start), took = gettype(d.took), uid = gettype(d.uid), total = gettype(d.total),
                      ratio = gettype(d.ratio), flag = gettype(d.flag), name = gettype(d.name)
            """).Single();

        Assert.That(Col(row, "start"), Is.EqualTo("datetime"));
        Assert.That(Col(row, "took"), Is.EqualTo("timespan"));
        Assert.That(Col(row, "uid"), Is.EqualTo("guid"));
        Assert.That(Col(row, "total"), Is.EqualTo("long"));
        Assert.That(Col(row, "ratio"), Is.EqualTo("double"));
        Assert.That(Col(row, "flag"), Is.EqualTo("bool"));
        Assert.That(Col(row, "name"), Is.EqualTo("string"));
    }

    [Test]
    [Description("conversion functions - typed values extracted from a bag are converted back to the scalar Kusto types without loss")]
    public void DynamicColumn_TypedScalarsInsideBag_ConvertBackToScalars()
    {
        var row = OpusTestUtils.ExecuteAndGetRows(
            CreateTypedBagDatabase(),
            """
            Typed
            | where id == 1
            | project start = todatetime(d.start), took = totimespan(d.took), uid = toguid(d.uid), total = tolong(d.total),
                      ratio = toreal(d.ratio), flag = tobool(d.flag), name = tostring(d.name)
            """).Single();

        OpusTestUtils.AssertTypedValue(Col(row, "start"), Utc(2020, 1, 2, 3, 4, 5));
        OpusTestUtils.AssertTypedValue(Col(row, "took"), TimeSpan.FromMinutes(90));
        OpusTestUtils.AssertTypedValue(Col(row, "uid"), SampleGuid);
        OpusTestUtils.AssertTypedValue(Col(row, "total"), long.MaxValue);
        OpusTestUtils.AssertTypedValue(Col(row, "ratio"), 0.25);
        OpusTestUtils.AssertTypedValue(Col(row, "flag"), false);
        OpusTestUtils.AssertTypedValue(Col(row, "name"), "n");
    }

    [Test]
    [Description("tostring - datetime, timespan and guid values inside a bag are written as JSON strings, long.MaxValue keeps all digits")]
    public void DynamicColumn_TypedScalarsInsideBag_ToStringWritesKustoJson()
    {
        var actual = OpusTestUtils.ExecuteAndGetSingleValue(
            "s",
            CreateTypedBagDatabase(),
            "Typed | where id == 1 | project s = tostring(d)");

        OpusTestUtils.AssertTypedValue(actual,
            """{"start":"2020-01-02T03:04:05.0000000Z","took":"01:30:00","uid":"6f9619ff-8b86-d011-b42d-00c04fc964ff","total":9223372036854775807,"ratio":0.25,"flag":false,"name":"n"}""");
    }

    [TestCase("Typed | where todatetime(d.start) < datetime(2021-01-01) | project id", new[] { 1L })]
    [TestCase("Typed | where totimespan(d.took) < 1h | project id", new[] { 2L })]
    [TestCase("Typed | where toguid(d.uid) == guid(6f9619ff-8b86-d011-b42d-00c04fc964ff) | project id", new[] { 1L })]
    [TestCase("Typed | where tobool(d.flag) | project id", new[] { 2L })]
    [TestCase("Typed | where isnull(d.start) | project id", new[] { 3L })]
    [TestCase("Typed | where tolong(d.total) > 5 | project id", new[] { 1L, 2L })]
    [Description("where - rows are filtered by typed values extracted from a dynamic column, missing properties are null")]
    public void DynamicColumn_TypedScalarsInsideBag_CanBeFiltered(string query, long[] expectedIds)
    {
        Assert.That(ExecuteAndGetIds(CreateTypedBagDatabase(), query), Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("summarize - typed values extracted from a dynamic column can be aggregated")]
    public void DynamicColumn_TypedScalarsInsideBag_CanBeAggregated()
    {
        var row = OpusTestUtils.ExecuteAndGetRows(
            CreateTypedBagDatabase(),
            """
            Typed
            | summarize latest = max(todatetime(d.start)), longest = max(totimespan(d.took)),
                        ratios = sum(toreal(d.ratio)), withoutStart = countif(isnull(d.start))
            """).Single();

        OpusTestUtils.AssertTypedValue(Col(row, "latest"), Utc(2021, 6, 1));
        OpusTestUtils.AssertTypedValue(Col(row, "longest"), TimeSpan.FromMinutes(90));
        OpusTestUtils.AssertNumericValue(Col(row, "ratios"), 1.75);
        OpusTestUtils.AssertNumericValue(Col(row, "withoutStart"), 1);
    }

    [Test]
    [Description("parse_json(tostring(d)) - the JSON text of null, scalars, arrays and bags is parsed back into the same dynamic value")]
    public void DynamicColumn_ToStringThenParseJson_RoundTrips()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateStoredDatabase(),
            "Stored | where id in (1, 2, 3, 4, 5, 6, 7, 11, 12) | project id, r = parse_json(tostring(d))");

        var actual = SingleById(rows, "r");
        Assert.That(actual.Keys, Is.EquivalentTo(new[] { 1L, 2L, 3L, 4L, 5L, 6L, 7L, 11L, 12L }));
        foreach (var (id, value) in StoredValues().Where(item => actual.ContainsKey(item.Id)))
        {
            AssertDynamic(actual[id], value, $"id {id}");
        }
    }

    [Test]
    [Description("JSON table definition - objects, arrays, null and scalars of a column with the type 'dynamic' are stored as dynamic values")]
    public void TableDefinition_DynamicColumn_StoresJsonValues()
    {
        const string json = """
                            {
                              "Name": "Events",
                              "Columns": [
                                { "Name": "id", "Type": "long", "Nullable": false },
                                { "Name": "d", "Type": "dynamic", "Nullable": true }
                              ],
                              "Rows": [
                                { "id": 1, "d": { "user": { "id": 7, "name": "ann" }, "tags": ["a", "b"] } },
                                { "id": 2, "d": [1, "x", 2.5, true, null] },
                                { "id": 3, "d": null },
                                { "id": 4, "d": 42 },
                                { "id": 5, "d": "text" }
                              ]
                            }
                            """;
        var tableDef = JsonSerializer.Deserialize<TableDef>(json);
        Assert.That(tableDef, Is.Not.Null);
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TableBuilderFromJson.Build(tableDef!));

        Assert.That(ExecuteAndGetSchema(kustoDatabase, "Events | getschema"), Is.EqualTo(new[] { "id:long", "d:dynamic" }));

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Events | project id, d, t = gettype(d)");
        var values = SingleById(rows, "d");
        var types = SingleById(rows, "t");
        AssertDynamic(values[1], Bag(("user", Bag(("id", 7), ("name", "ann"))), ("tags", Arr("a", "b"))));
        AssertDynamic(values[2], Arr(1, "x", 2.5, true, null));
        AssertDynamic(values[3], null);
        AssertDynamic(values[4], 42);
        AssertDynamic(values[5], "text");
        Assert.That(types, Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = "dictionary",
            [2] = "array",
            [3] = "null",
            [4] = "long",
            [5] = "string"
        }));

        var userId = OpusTestUtils.ExecuteAndGetSingleValue(
            "uid",
            kustoDatabase,
            "Events | where d.user.name == 'ann' | project uid = tolong(d.user.id)");
        OpusTestUtils.AssertTypedValue(userId, 7L);
    }

    // dynamic(...) literals

    [TestCase("dynamic(1)", "long")]
    [TestCase("dynamic(-7)", "long")]
    [TestCase("dynamic(1.5)", "double")]
    [TestCase("dynamic(true)", "bool")]
    [TestCase("dynamic('a')", "string")]
    [TestCase("dynamic(null)", "null")]
    [TestCase("dynamic([1, 'a'])", "array")]
    [TestCase("dynamic({})", "dictionary")]
    [TestCase("dynamic(datetime(2020-01-01))", "datetime")]
    [TestCase("dynamic(guid(6f9619ff-8b86-d011-b42d-00c04fc964ff))", "guid")]
    [TestCase("dynamic(1h)", "long", Description = "a timespan in a dynamic literal is stored as the long number of ticks")]
    [TestCase("dynamic([1h])[0]", "long", Description = "a timespan in a dynamic literal is stored as the long number of ticks")]
    [TestCase("dynamic('2020-01-01')", "datetime", Description = "a string which looks like a datetime becomes a datetime")]
    [TestCase("dynamic(['2020-01-01'])[0]", "datetime", Description = "a string which looks like a datetime becomes a datetime")]
    [TestCase("dynamic({'a':'2020-01-01T10:00:00Z'}).a", "datetime", Description = "a string which looks like a datetime becomes a datetime")]
    [TestCase("dynamic('6f9619ff-8b86-d011-b42d-00c04fc964ff')", "guid", Description = "a string which looks like a guid becomes a guid")]
    [TestCase("dynamic('12')", "string")]
    [TestCase("dynamic('1.5')", "string")]
    [TestCase("dynamic('true')", "string")]
    [TestCase("dynamic([1])[0]", "long")]
    [TestCase("dynamic([1.5])[0]", "double")]
    [TestCase("1.5", "real", Description = "a real scalar (not dynamic) is reported as 'real'")]
    [TestCase("toint(1)", "int")]
    [TestCase("decimal(1.5)", "decimal")]
    [TestCase("1h", "timespan")]
    [Description("gettype - returns the type of a dynamic literal (and of scalars for comparison)")]
    public void DynamicLiteral_GetType_ReturnsValueType(string expression, string expected)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar($"gettype({expression})"), expected);
    }

    [TestCase("dynamic(1)", "1")]
    [TestCase("dynamic(2.0)", "2.0", Description = "a real keeps the decimal point")]
    [TestCase("dynamic(true)", "true")]
    [TestCase("dynamic('a')", "a")]
    [TestCase("dynamic(['a', 1, 1.5])", """["a",1,1.5]""")]
    [TestCase("dynamic(1h)", "36000000000", Description = "a timespan in a dynamic literal is stored as the long number of ticks")]
    [TestCase("dynamic(['2020-01-01'])", """["2020-01-01T00:00:00.0000000Z"]""")]
    [TestCase("dynamic(datetime(2020-01-02 03:04:05))", "2020-01-02T03:04:05.0000000Z")]
    [TestCase("dynamic(guid(6f9619ff-8b86-d011-b42d-00c04fc964ff))", GuidText)]
    [TestCase("dynamic({'b': 1, 'a': [true, null]})", """{"b":1,"a":[true,null]}""", Description = "properties keep the order of the literal")]
    [TestCase("dynamic({'a': 1, 'A': 2})", """{"a":1,"A":2}""", Description = "property names are case-sensitive")]
    [TestCase("""dynamic({"k": 1})""", """{"k":1}""", Description = "property names can be in double quotes")]
    [TestCase("dynamic([[], {}, [[]]])", "[[],{},[[]]]")]
    [TestCase(
        "dynamic({'t': datetime(2020-01-02 03:04:05), 's': 1h, 'g': guid(6f9619ff-8b86-d011-b42d-00c04fc964ff), 'r': 2.0, 'b': true, 'n': null, 'l': 1})",
        """{"t":"2020-01-02T03:04:05.0000000Z","s":36000000000,"g":"6f9619ff-8b86-d011-b42d-00c04fc964ff","r":2.0,"b":true,"n":null,"l":1}""")]
    [Description("tostring - returns the JSON text of a dynamic literal")]
    public void DynamicLiteral_ToString_ReturnsJsonText(string expression, string expected)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar($"tostring({expression})"), expected);
    }

    [Test]
    [Description("dynamic(...) literal - arrays keep the order and the types of the elements, bags can be nested in arrays and vice versa")]
    public void DynamicLiteral_MixedArray_ReturnsDynamicArray()
    {
        var actual = OpusTestUtils.EvaluateScalar("dynamic([1, 'a', 2.5, true, null, [1, 2], {'k': 'v', 'e': []}])");

        AssertDynamic(actual, Arr(1, "a", 2.5, true, null, Arr(1, 2), Bag(("k", "v"), ("e", Arr()))));
    }

    [Test]
    [Description("dynamic(...) literal - datetime, guid and datetime-like / guid-like strings are stored as datetime and guid values")]
    public void DynamicLiteral_DateTimeAndGuidValues_KeepKustoTypes()
    {
        AssertDynamic(OpusTestUtils.EvaluateScalar("dynamic(datetime(2020-01-02 03:04:05))"), Utc(2020, 1, 2, 3, 4, 5));
        AssertDynamic(OpusTestUtils.EvaluateScalar("dynamic(guid(6f9619ff-8b86-d011-b42d-00c04fc964ff))"), SampleGuid);
        AssertDynamic(OpusTestUtils.EvaluateScalar("dynamic(['2020-01-01', '12', 'x'])"), Arr(Utc(2020, 1, 1), "12", "x"));
        AssertDynamic(OpusTestUtils.EvaluateScalar("dynamic({'g': '6f9619ff-8b86-d011-b42d-00c04fc964ff'})"), Bag(("g", SampleGuid)));
        AssertDynamic(OpusTestUtils.EvaluateScalar("dynamic(1h)"), TimeSpan.FromHours(1).Ticks);
    }

    [TestCase("dynamic({'a': 1, 'A': 2}).A", 2L)]
    [TestCase("dynamic({'a': 1, 'A': 2}).a", 1L)]
    [TestCase("""dynamic({"k": 1}).k""", 1L)]
    [TestCase("dynamic({'a': {'b': [5, 6]}}).a.b[1]", 6L)]
    [Description("dynamic(...) literal - properties and elements of a literal can be accessed directly, property names are case-sensitive")]
    public void DynamicLiteral_Access_ReturnsElement(string expression, long expected)
    {
        AssertDynamic(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [Test]
    [Description("let - a dynamic literal bound to a name can be used in expressions")]
    public void DynamicLiteral_InLetStatement_CanBeUsed()
    {
        var actual = EvaluateWithLets(
            "let config = dynamic({'limits': {'max': 10}, 'names': ['a', 'b']});",
            "strcat(tostring(config.names[1]), '-', tostring(config.limits.max), '-', tostring(array_length(config.names)))");

        OpusTestUtils.AssertTypedValue(actual, "b-10-2");
    }

    [Test]
    [Description("getschema - a dynamic literal and a property accessed from it are dynamic columns")]
    public void DynamicLiteral_GetSchema_ReturnsDynamicColumns()
    {
        var actual = ExecuteAndGetSchema(
            new KustoDatabase(),
            "print d = dynamic({'a': 1}), a = dynamic({'a': 1}).a, e = dynamic([1])[0], s = tostring(dynamic({'a': 1})) | getschema");

        Assert.That(actual, Is.EqualTo(new[] { "d:dynamic", "a:dynamic", "e:dynamic", "s:string" }));
    }

    // parse_json

    [TestCase("{\"a\":")]
    [TestCase("abc")]
    [TestCase("123abc")]
    [TestCase("[1,2")]
    [Description("parse_json - malformed JSON text doesn't fail the query, the result is a dynamic string with the original text")]
    public void ParseJson_MalformedText_ReturnsOriginalTextAsDynamicString(string text)
    {
        var table = OpusTestUtils.NewTable("Raw")
            .WithColumn<long>("id")
            .WithColumn<string>("raw")
            .WithRow(1L, text)
            .Build();
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var row = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Raw | extend d = parse_json(raw) | project d, t = gettype(d), s = tostring(d)").Single();

        AssertDynamic(Col(row, "d"), text);
        Assert.That(Col(row, "t"), Is.EqualTo("string"));
        Assert.That(Col(row, "s"), Is.EqualTo(text));
    }

    [TestCase("'1'", "long", "1")]
    [TestCase("'-5'", "long", "-5")]
    [TestCase("'1.5'", "double", "1.5")]
    [TestCase("'2.0'", "double", "2.0")]
    [TestCase("'1e3'", "double", "1000.0")]
    [TestCase("'9223372036854775808'", "double", "9223372036854776000.0", Description = "an integer which doesn't fit into a long becomes a real")]
    [TestCase("'true'", "bool", "true")]
    [TestCase("'\"s\"'", "string", "s")]
    [TestCase("'null'", "null", "")]
    [TestCase("''", "null", "", Description = "an empty string is parsed as null")]
    [TestCase("' [1, 2] '", "array", "[1,2]", Description = "whitespace around the JSON text is ignored")]
    [TestCase("'[1.50, -0, 1E2]'", "array", "[1.5,0,100.0]")]
    [TestCase("'{}'", "dictionary", "{}")]
    [TestCase("""'{"b":[{"c":null}],"a":{}}'""", "dictionary", """{"b":[{"c":null}],"a":{}}""")]
    [Description("parse_json - JSON scalars, arrays and objects become dynamic values of the matching type")]
    public void ParseJson_Text_ReturnsDynamicValueOfMatchingType(string kustoStringLiteral, string expectedType, string expectedText)
    {
        var row = OpusTestUtils.ExecuteAndGetRows(
            new KustoDatabase(),
            $"print d = parse_json({kustoStringLiteral}) | project t = gettype(d), s = tostring(d)").Single();

        Assert.That(Col(row, "t"), Is.EqualTo(expectedType));
        Assert.That(Col(row, "s"), Is.EqualTo(expectedText));
    }

    [Test]
    [Description("parse_json - escaped quotes, backslashes and unicode escapes in JSON strings are decoded")]
    public void ParseJson_EscapedCharacters_AreDecoded()
    {
        var actual = OpusTestUtils.EvaluateScalar("""tostring(parse_json(@'{"s":"a\"b\\c\u00e9"}').s)""");

        OpusTestUtils.AssertTypedValue(actual, "a\"b\\c\u00e9");
    }

    [TestCase("""'"2020-01-02T03:04:05Z"'""", "datetime")]
    [TestCase("""'"2020-01-02"'""", "datetime")]
    [TestCase("""'"2020-01-02 03:04:05"'""", "datetime")]
    [TestCase("""'"2020-01-02T03:04:05.1234567Z"'""", "datetime")]
    [TestCase("""'"6f9619ff-8b86-d011-b42d-00c04fc964ff"'""", "guid")]
    [TestCase("""'"01:00:00"'""", "string", Description = "timespan-like strings stay strings")]
    [TestCase("""'"42"'""", "string")]
    [TestCase("""'"hello"'""", "string")]
    [TestCase("""'"Thu, 02 Jan 2020 03:04:05 GMT"'""", "string", Description = "only ISO 8601 like datetime strings are detected")]
    [Description("parse_json - JSON strings which look like ISO 8601 datetimes or guids become datetime / guid values")]
    public void ParseJson_DateTimeAndGuidStrings_AreDetected(string kustoStringLiteral, string expectedType)
    {
        OpusTestUtils.AssertTypedValue(
            OpusTestUtils.EvaluateScalar($"gettype(parse_json({kustoStringLiteral}))"),
            expectedType);
    }

    [TestCase("""gettype(parse_json('["2020-01-02"]')[0])""", "datetime")]
    [TestCase("""gettype(parse_json('{"a":{"t":"2020-01-02T03:04:05Z"}}').a.t)""", "datetime")]
    [TestCase("""gettype(parse_json('{"g":"6f9619ff-8b86-d011-b42d-00c04fc964ff"}').g)""", "guid")]
    [TestCase("""tostring(parse_json('{"t":"2020-01-02T03:04:05Z","d":"2020-01-02"}'))""", """{"t":"2020-01-02T03:04:05.0000000Z","d":"2020-01-02T00:00:00.0000000Z"}""")]
    [TestCase("""tostring(tolong(parse_json('"2020-01-01T00:00:00Z"')))""", "637134336000000000", Description = "tolong of a datetime returns its ticks, so the string was detected as a datetime")]
    [TestCase("""tostring(parse_json('"2020-01-02T03:04:05Z"'))""", "2020-01-02T03:04:05.0000000Z")]
    [TestCase("""tostring(parse_json('["2020-01-02 03:04:05", "x"]'))""", """["2020-01-02T03:04:05.0000000Z","x"]""")]
    [Description("parse_json - datetime and guid detection also applies to nested strings, detected datetimes are written in the Kusto format")]
    public void ParseJson_NestedDateTimeAndGuidStrings_AreDetected(string expression, string expected)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [Test]
    [Description("parse_json - a nested JSON document becomes nested dynamic arrays and bags with typed values")]
    public void ParseJson_NestedDocument_ReturnsTypedDynamicValue()
    {
        var actual = OpusTestUtils.EvaluateScalar(
            """parse_json('{"when":"2020-01-02","n":[1,2.5,"x",true,null],"o":{},"deep":{"a":[{"b":[[]]}]}}')""");

        AssertDynamic(actual, Bag(
            ("when", Utc(2020, 1, 2)),
            ("n", Arr(1, 2.5, "x", true, null)),
            ("o", Bag()),
            ("deep", Bag(("a", Arr(Bag(("b", Arr((object)Arr())))))))));
    }

    [Test]
    [Description("parse_json on a string column - every row is parsed separately: objects, arrays, JSON strings, malformed text and empty values")]
    public void ParseJson_StringColumn_ReturnsDynamicValuePerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateLogsDatabase(),
            "Logs | extend d = parse_json(raw) | project id, t = gettype(d), s = tostring(d)");

        Assert.That(SingleById(rows, "t"), Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = "dictionary",
            [2] = "dictionary",
            [3] = "string",
            [4] = "null",
            [5] = "null",
            [6] = "array",
            [7] = "string"
        }));
        var text = SingleById(rows, "s");
        Assert.That(text[3], Is.EqualTo("not json"));
        Assert.That(text[4], Is.EqualTo(""));
        Assert.That(text[6], Is.EqualTo("[1,2,3]"));
        Assert.That(text[7], Is.EqualTo("quoted"));
    }

    [TestCase("Logs | extend d = parse_json(raw) | where d.level == 'error' | project id", new[] { 1L })]
    [TestCase("Logs | extend d = parse_json(raw) | where tostring(d.level) != 'error' | project id", new[] { 2L, 3L, 4L, 5L, 6L, 7L })]
    [TestCase("Logs | where parse_json(raw).ms > 100 | project id", new[] { 1L })]
    [TestCase("Logs | where array_length(parse_json(raw).tags) == 0 | project id", new[] { 2L })]
    [TestCase("Logs | where gettype(parse_json(raw)) == 'string' | project id", new[] { 3L, 7L })]
    [Description("where - rows of a string column are filtered by values parsed with parse_json")]
    public void ParseJson_StringColumn_CanBeFiltered(string query, long[] expectedIds)
    {
        Assert.That(ExecuteAndGetIds(CreateLogsDatabase(), query), Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("summarize - values parsed with parse_json are grouped and aggregated, rows with missing properties are in the empty group")]
    public void ParseJson_StringColumn_CanBeAggregated()
    {
        var kustoDatabase = CreateLogsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Logs | extend d = parse_json(raw) | summarize c = count(), ms = sum(toreal(d.ms)) by level = tostring(d.level)");

        var counts = rows.ToDictionary(
            row => (string)Col(row, "level")!,
            row => Convert.ToInt64(Col(row, "c"), CultureInfo.InvariantCulture));
        Assert.That(counts, Is.EquivalentTo(new Dictionary<string, long> { ["error"] = 1, ["info"] = 1, [""] = 5 }));

        var ms = rows.ToDictionary(row => (string)Col(row, "level")!, row => Col(row, "ms"));
        OpusTestUtils.AssertNumericValue(ms["error"], 120);
        OpusTestUtils.AssertNumericValue(ms["info"], 15.5);

        var tagCounts = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Logs | project id, n = array_length(parse_json(raw).tags)");
        Assert.That(SingleById(tagCounts, "n"), Is.EquivalentTo(new Dictionary<long, object?>
        {
            [1] = 2L,
            [2] = 0L,
            [3] = null,
            [4] = null,
            [5] = null,
            [6] = null,
            [7] = null
        }));
    }

    private static KustoDatabase CreateStoredDatabase()
    {
        var builder = OpusTestUtils.NewTable("Stored")
            .WithColumn<long>("id")
            .WithColumn<object>("d");

        foreach (var (id, value) in StoredValues())
        {
            builder.WithRow(id, value);
        }

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(builder.Build());
        return kustoDatabase;
    }

    private static KustoDatabase CreateTypedBagDatabase()
    {
        var table = OpusTestUtils.NewTable("Typed")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Bag(
                ("start", Utc(2020, 1, 2, 3, 4, 5)),
                ("took", TimeSpan.FromMinutes(90)),
                ("uid", SampleGuid),
                ("total", long.MaxValue),
                ("ratio", 0.25),
                ("flag", false),
                ("name", "n")))
            .WithRow(2L, Bag(
                ("start", Utc(2021, 6, 1)),
                ("took", TimeSpan.FromMinutes(10)),
                ("uid", Guid.Empty),
                ("total", 7L),
                ("ratio", 1.5),
                ("flag", true),
                ("name", "m")))
            .WithRow(3L, Bag(("total", 1L)))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateLogsDatabase()
    {
        var table = OpusTestUtils.NewTable("Logs")
            .WithColumn<long>("id")
            .WithColumn<string>("raw")
            .WithRow(1L, """{"level":"error","tags":["a","b"],"ms":120}""")
            .WithRow(2L, """{"level":"info","tags":[],"ms":15.5}""")
            .WithRow(3L, "not json")
            .WithRow(4L, "")
            .WithRow(5L, null)
            .WithRow(6L, "[1,2,3]")
            .WithRow(7L, "\"quoted\"")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }
}
