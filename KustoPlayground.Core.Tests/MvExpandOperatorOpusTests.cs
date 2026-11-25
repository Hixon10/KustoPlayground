using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// mv-expand - https://learn.microsoft.com/en-us/kusto/query/mv-expand-operator.
/// <list type="bullet">
/// <item>every element of a dynamic array (or every property of a property bag) becomes a separate row and the other
/// columns of the input row are repeated; arrays keep the element order, the order of bag properties is not defined;</item>
/// <item>an empty array or bag produces no rows; null and scalar values produce one row with the value itself
/// (a string is never parsed as JSON);</item>
/// <item>the expanded column keeps its name and is dynamic; <c>x = expr</c> adds a new column and keeps the source column;
/// a path like <c>d.items</c> gets the name d_items and other unnamed expressions get Column1, Column2 ...;</item>
/// <item>with_itemindex=i adds a 0-based long index of the element (0 for null and scalars);</item>
/// <item>limit N expands at most N elements of every input row (limit 0 produces no rows);</item>
/// <item><c>to typeof(T)</c> only casts the elements and never parses strings: 1.5 to long is 1, '7' to long is null,
/// a long to bool is true, a real to bool is null and a long to timespan is that many ticks;</item>
/// <item>kind=bag (the default, the legacy syntax is bagexpansion=bag) expands a property bag into single-property bags,
/// kind=array expands it into [key, value] arrays;</item>
/// <item>several expressions are expanded in parallel: the elements are aligned by index and the shorter ones are padded
/// with null (null counts as one element and [] as none), a row is dropped only when all expressions are empty;</item>
/// <item>only dynamic expressions can be expanded.</item>
/// </list>
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class MvExpandOperatorOpusTests
{
    // Expanding arrays

    [Test]
    [Description("mv-expand - every element of an array becomes a row, the other columns are repeated")]
    public void MvExpand_ArrayColumn_ExpandsEveryElementIntoRowAndRepeatsOtherColumns()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Items | mv-expand arr");

        Assert.That(rows, Has.Count.EqualTo(11));
        Assert.That(rows[0].Keys, Is.EquivalentTo(new[] { "id", "arr", "s" }));

        var elements = GroupById(rows, "arr");
        Assert.That(elements.Keys, Is.EquivalentTo(new[] { 1L, 3L, 4L, 5L, 7L }),
            "an empty array (id 2) and an empty property bag (id 6) produce no rows");
        AssertDynamicJson(elements[1], "[10,20,30]", "id 1: the elements keep their order");
        AssertDynamicJson(elements[3], "[null]", "id 3: null produces one row with null");
        AssertDynamicJson(elements[4], """["x",1.5,null,{"k":"v"},[1,2]]""", "id 4: elements of mixed types");
        AssertDynamicJson(elements[5], "[7]", "id 5: a scalar produces one row with the scalar");
        AssertDynamicJson(elements[7], """["str"]""", "id 7: a string is a scalar, it is not parsed");

        var texts = GroupById(rows, "s");
        Assert.That(texts[1], Is.EqualTo(new object?[] { "one", "one", "one" }));
        Assert.That(texts[3], Is.EqualTo(new object?[] { "three" }));
        Assert.That(texts[4], Is.EqualTo(new object?[] { "four", "four", "four", "four", "four" }));
        Assert.That(texts[5], Is.EqualTo(new object?[] { "five" }));
        Assert.That(texts[7], Is.EqualTo(new object?[] { "seven" }));
    }

    [Test]
    [Description("mv-expand - the expanded elements keep their types (long, real, string, null, property bag, array)")]
    public void MvExpand_ElementTypes_KeepsTheTypeOfEveryElement()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Items | mv-expand arr | extend t = gettype(arr)");

        var types = GroupById(rows, "t");
        Assert.That(types.Keys, Is.EquivalentTo(new[] { 1L, 3L, 4L, 5L, 7L }));
        Assert.That(types[1], Is.EqualTo(new object?[] { "long", "long", "long" }));
        Assert.That(types[3], Is.EqualTo(new object?[] { "null" }));
        Assert.That(types[4], Is.EqualTo(new object?[] { "string", "double", "null", "dictionary", "array" }));
        Assert.That(types[5], Is.EqualTo(new object?[] { "long" }));
        Assert.That(types[7], Is.EqualTo(new object?[] { "string" }));
    }

    [TestCase(1L, 3, Description = "an array with 3 elements")]
    [TestCase(2L, 0, Description = "an empty array produces no rows")]
    [TestCase(3L, 1, Description = "null produces one row")]
    [TestCase(4L, 5, Description = "nested arrays and bags are single elements")]
    [TestCase(5L, 1, Description = "a scalar produces one row")]
    [TestCase(6L, 0, Description = "an empty property bag produces no rows")]
    [TestCase(7L, 1, Description = "a string produces one row")]
    [TestCase(100L, 0, Description = "an empty input produces no rows")]
    [Description("mv-expand - the number of rows produced by one input row")]
    public void MvExpand_SingleInputRow_ReturnsExpectedNumberOfRows(long id, int expectedRows)
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, $"Items | where id == {id} | mv-expand arr");

        Assert.That(rows, Has.Count.EqualTo(expectedRows));
    }

    [Test]
    [Description("mv-expand with_itemindex - adds the 0-based long index of the element, null and scalars get 0")]
    public void MvExpand_WithItemIndex_AddsZeroBasedIndexOfTheElement()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Items | mv-expand with_itemindex=i arr");

        var indexes = GroupById(rows, "i");
        Assert.That(indexes.Keys, Is.EquivalentTo(new[] { 1L, 3L, 4L, 5L, 7L }));
        AssertTypedValues<long>(indexes[1], 0, 1, 2);
        AssertTypedValues<long>(indexes[3], 0);
        AssertTypedValues<long>(indexes[4], 0, 1, 2, 3, 4);
        AssertTypedValues<long>(indexes[5], 0);
        AssertTypedValues<long>(indexes[7], 0);

        var bagRow = rows.Single(row => Equals(Col(row, "id"), 4L) && Equals(Col(row, "i"), 3L));
        AssertDynamicJson(Col(bagRow, "arr"), """{"k":"v"}""", "the element with index 3 of id 4");
    }

    [Test]
    [Description("mv-expand - with_itemindex, a named expression and limit can be combined")]
    public void MvExpand_NamedExpressionWithLimit_ExpandsAtMostLimitElementsPerRow()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Items | mv-expand with_itemindex=i x = arr limit 2");

        var values = GroupById(rows, "x");
        Assert.That(values.Keys, Is.EquivalentTo(new[] { 1L, 3L, 4L, 5L, 7L }));
        AssertDynamicJson(values[1], "[10,20]", "id 1");
        AssertDynamicJson(values[3], "[null]", "id 3");
        AssertDynamicJson(values[4], """["x",1.5]""", "id 4");
        AssertDynamicJson(values[5], "[7]", "id 5");
        AssertDynamicJson(values[7], """["str"]""", "id 7");

        var indexes = GroupById(rows, "i");
        AssertTypedValues<long>(indexes[1], 0, 1);
        AssertTypedValues<long>(indexes[4], 0, 1);
        AssertDynamicJson(GroupById(rows, "arr")[1], "[[10,20,30],[10,20,30]]", "the source column is kept in every row");
    }

    [TestCase(1, new[] { 1L, 3L, 4L, 5L, 7L })]
    [TestCase(2, new[] { 1L, 1L, 3L, 4L, 4L, 5L, 7L })]
    [TestCase(3, new[] { 1L, 1L, 1L, 3L, 4L, 4L, 4L, 5L, 7L })]
    [TestCase(10, new[] { 1L, 1L, 1L, 3L, 4L, 4L, 4L, 4L, 4L, 5L, 7L },
        Description = "a limit larger than the arrays returns all elements")]
    [Description("mv-expand limit - the limit is applied to every input row, not to the whole result")]
    public void MvExpand_Limit_LimitsTheNumberOfRowsPerInputRow(int limit, long[] expectedIds)
    {
        var kustoDatabase = CreateItemsDatabase();

        var ids = ExecuteAndGetIds(kustoDatabase, $"Items | mv-expand arr limit {limit}");

        Assert.That(ids, Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("mv-expand limit 1 - returns the first element of every input row")]
    public void MvExpand_LimitOne_ReturnsTheFirstElementOfEveryRow()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Items | mv-expand arr limit 1");

        AssertDynamicJsonById(SingleById(rows, "arr"),
            (1, "10"), (3, "null"), (4, "\"x\""), (5, "7"), (7, "\"str\""));
    }

    [Test]
    [Description("mv-expand limit 0 - produces no rows")]
    public void MvExpand_LimitZero_ProducesNoRows()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Items | mv-expand arr limit 0");

        Assert.That(rows, Is.Empty);
    }

    [Test]
    [Description("mv-expand x = arr - adds the expanded column x and keeps the source column arr")]
    public void MvExpand_NamedExpression_AddsNewColumnAndKeepsTheSourceColumn()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Items | where id == 1 | mv-expand x = arr");

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0].Keys, Is.EquivalentTo(new[] { "id", "arr", "s", "x" }));
        AssertDynamicJson(rows.Select(row => Col(row, "x")).ToList(), "[10,20,30]", "x");
        foreach (var row in rows)
        {
            AssertDynamicJson(Col(row, "arr"), "[10,20,30]", "arr");
            Assert.That(Col(row, "s"), Is.EqualTo("one"));
        }
    }

    // Schema

    [TestCase("Items | mv-expand arr", new[] { "id:long", "arr:dynamic", "s:string" },
        Description = "the expanded column keeps its name and position")]
    [TestCase("Items | mv-expand with_itemindex=i arr", new[] { "id:long", "arr:dynamic", "s:string", "i:long" },
        Description = "the index column is added at the end")]
    [TestCase("Items | mv-expand x = arr", new[] { "id:long", "arr:dynamic", "s:string", "x:dynamic" },
        Description = "a named expression adds a column at the end")]
    [TestCase("Items | mv-expand arr to typeof(long)", new[] { "id:long", "arr:long", "s:string" })]
    [TestCase("Items | mv-expand arr to typeof(string)", new[] { "id:long", "arr:string", "s:string" })]
    [TestCase("Items | mv-expand kind=array arr to typeof(real)", new[] { "id:long", "arr:real", "s:string" })]
    [TestCase("Items | mv-expand with_itemindex=i x = arr to typeof(long)",
        new[] { "id:long", "arr:dynamic", "s:string", "x:long", "i:long" })]
    [Description("mv-expand - the schema of the result")]
    public void MvExpand_Schema_ReturnsExpectedColumns(string query, string[] expected)
    {
        var kustoDatabase = CreateItemsDatabase();

        var schema = ExecuteAndGetSchema(kustoDatabase, query + " | getschema");

        Assert.That(schema, Is.EqualTo(expected));
    }

    [Test]
    [Description("mv-expand to typeof(T) - the expanded column gets the type T for every scalar type")]
    public void MvExpand_ToTypeofEveryType_ReturnsColumnsOfTheGivenType()
    {
        var kustoDatabase = new KustoDatabase();

        var schema = ExecuteAndGetSchema(kustoDatabase,
            "print v = dynamic([1]) | mv-expand a1 = v to typeof(int), a2 = v to typeof(bool), a3 = v to typeof(datetime), "
            + "a4 = v to typeof(timespan), a5 = v to typeof(guid), a6 = v to typeof(decimal), a7 = v to typeof(dynamic), "
            + "a8 = v to typeof(double), a9 = v to typeof(long) | getschema");

        Assert.That(schema, Is.EqualTo(new[]
        {
            "v:dynamic", "a1:int", "a2:bool", "a3:datetime", "a4:timespan", "a5:guid", "a6:decimal", "a7:dynamic",
            "a8:real", "a9:long"
        }));
    }

    // Casting the expanded values with to typeof(...)

    [Test]
    [Description("mv-expand to typeof(long) - numbers are converted (1.5 is truncated to 1), other values become null")]
    public void MvExpand_ToTypeofLong_ConvertsNumbersAndReturnsNullForOtherValues()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Items | mv-expand arr to typeof(long)");

        var values = GroupById(rows, "arr");
        Assert.That(values.Keys, Is.EquivalentTo(new[] { 1L, 3L, 4L, 5L, 7L }));
        AssertTypedValues<long>(values[1], 10, 20, 30);
        AssertTypedValues<long>(values[3], (long?)null);
        AssertTypedValues<long>(values[4], null, 1, null, null, null);
        AssertTypedValues<long>(values[5], 7);
        AssertTypedValues<long>(values[7], (long?)null);
    }

    [TestCase(0, 10L, 10.0, "10", true, null, Description = "a long: a non-zero long is true")]
    [TestCase(1, null, null, "x", null, null, Description = "a string is not parsed")]
    [TestCase(2, 2L, 2.5, "2.5", null, null, Description = "a real: it is truncated to long, it is not a bool")]
    [TestCase(3, null, null, "true", true, null, Description = "a bool is not a number")]
    [TestCase(4, null, null, "{\"a\":1}", null, null, Description = "a property bag: only tostring works")]
    [TestCase(5, null, null, null, null, null, Description = "null: every cast is null (an empty string for string)")]
    [TestCase(6, null, null, "2020-01-01T00:00:00.0000000Z", null, "2020-01-01", Description = "a datetime")]
    [TestCase(7, null, null, "7", null, null, Description = "a string with a number is not parsed")]
    [TestCase(8, 2L, 2.9, "2.9", null, null, Description = "a real is truncated, not rounded")]
    [TestCase(9, -3L, -3.0, "-3", true, null, Description = "a negative long is true")]
    [TestCase(10, null, null, "[1]", null, null, Description = "an array: only tostring works")]
    [TestCase(11, null, null, "true", null, null, Description = "the string 'true' is not parsed")]
    [TestCase(12, null, null, "false", false, null, Description = "a bool")]
    [Description("mv-expand to typeof(T) - casts every element to T without parsing strings")]
    public void MvExpand_ToTypeof_CastsEveryElementWithoutParsingStrings(
        int index, long? expectedLong, double? expectedReal, string? expectedString, bool? expectedBool,
        string? expectedDateTime)
    {
        var kustoDatabase = CreateCastsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Casts | mv-expand with_itemindex=i l = v to typeof(long), r = v to typeof(real), str = v to typeof(string), "
            + "b = v to typeof(bool), dt = v to typeof(datetime)");

        Assert.That(rows, Has.Count.EqualTo(13));
        var row = rows.Single(candidate => Equals(Col(candidate, "i"), (long)index));
        AssertTypedOrNull(Col(row, "l"), expectedLong);
        AssertTypedOrNull(Col(row, "r"), expectedReal);
        AssertTypedOrNull(Col(row, "b"), expectedBool);
        if (expectedString is null)
        {
            Assert.That(Col(row, "str"), Is.Null.Or.Empty);
        }
        else
        {
            Assert.That(Col(row, "str"), Is.EqualTo(expectedString));
        }

        if (expectedDateTime is null)
        {
            Assert.That(Col(row, "dt"), Is.Null);
        }
        else
        {
            OpusTestUtils.AssertTypedValue(Col(row, "dt"), OpusTestUtils.ParseDateTime(expectedDateTime));
        }
    }

    [TestCase("Typed | mv-expand with_itemindex=i e = v | extend t = gettype(e)")]
    [TestCase("Typed | mv-expand with_itemindex=i e = v to typeof(dynamic) | extend t = gettype(e)",
        Description = "to typeof(dynamic) doesn't change the values")]
    [Description("mv-expand - datetime, timespan and guid elements keep their Kusto types")]
    public void MvExpand_TypedElements_KeepTheirKustoTypes(string query)
    {
        var kustoDatabase = CreateTypedDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, query);

        Assert.That(rows, Has.Count.EqualTo(11));
        AssertDynamic(GroupById(rows, "e")[1],
            Arr(Utc(2020, 1, 1), TimeSpan.FromHours(1), SampleGuid, 1.5, 2L, "x", true, "01:00:00", GuidText, null,
                Bag(("a", 1L))));
        Assert.That(GroupById(rows, "t")[1], Is.EqualTo(new object?[]
        {
            "datetime", "timespan", "guid", "double", "long", "string", "bool", "string", "string", "null", "dictionary"
        }));
        AssertTypedValues<long>(GroupById(rows, "i")[1], 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
    }

    [Test]
    [Description("mv-expand to typeof(timespan / guid / int) - only values of a compatible type are converted "
                 + "(a long becomes a timespan of that many ticks), the strings '01:00:00' and a guid text are not parsed")]
    public void MvExpand_ToTypeofTimespanGuidAndInt_ConvertsOnlyCompatibleValues()
    {
        var kustoDatabase = CreateTypedDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Typed | mv-expand with_itemindex=i ts = v to typeof(timespan), g = v to typeof(guid), n = v to typeof(int)");

        Assert.That(rows, Has.Count.EqualTo(11));
        AssertTypedValues<long>(GroupById(rows, "i")[1], 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        AssertTypedValues<TimeSpan>(GroupById(rows, "ts")[1],
            null, TimeSpan.FromHours(1), null, null, TimeSpan.FromTicks(2), null, null, null, null, null, null);
        AssertTypedValues<Guid>(GroupById(rows, "g")[1],
            null, null, SampleGuid, null, null, null, null, null, null, null, null);
        AssertTypedValues<int>(GroupById(rows, "n")[1],
            null, null, null, 1, 2, null, null, null, null, null, null);
    }

    [Test]
    [Description("mv-expand - tostring() of expanded datetime, timespan and guid elements returns their Kusto text")]
    public void MvExpand_ToStringOfTypedElements_ReturnsKustoText()
    {
        var kustoDatabase = CreateTypedDatabase();

        var values = OpusTestUtils.ExecuteAndGetValues("str", kustoDatabase,
            "Typed | mv-expand with_itemindex=i e = v | where i <= 2 | sort by i asc | project str = tostring(e)");

        Assert.That(values, Is.EqualTo(new object?[] { "2020-01-01T00:00:00.0000000Z", "01:00:00", GuidText }));
    }

    [Test]
    [Description("mv-expand to typeof(long) | summarize - aggregates the elements of every input row")]
    public void MvExpand_ToTypeofLongThenSummarize_AggregatesElementsPerInputRow()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Items | mv-expand arr to typeof(long) "
            + "| summarize total = sum(arr), nonNull = countif(isnotnull(arr)), c = count() by id");

        AssertTypedById<long>(SingleById(rows, "total"), (1, 60L), (3, 0L), (4, 1L), (5, 7L), (7, 0L));
        AssertTypedById<long>(SingleById(rows, "nonNull"), (1, 3L), (3, 0L), (4, 1L), (5, 1L), (7, 0L));
        AssertTypedById<long>(SingleById(rows, "c"), (1, 3L), (3, 1L), (4, 5L), (5, 1L), (7, 1L));
    }

    [Test]
    [Description("mv-expand - with_itemindex, a named expression, to typeof(long) and limit together")]
    public void MvExpand_ItemIndexCastAndLimit_CanBeCombined()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Items | mv-expand with_itemindex=i x = arr to typeof(long) limit 2");

        var values = GroupById(rows, "x");
        Assert.That(values.Keys, Is.EquivalentTo(new[] { 1L, 3L, 4L, 5L, 7L }));
        AssertTypedValues<long>(values[1], 10, 20);
        AssertTypedValues<long>(values[3], (long?)null);
        AssertTypedValues<long>(values[4], null, 1);
        AssertTypedValues<long>(values[5], 7);
        AssertTypedValues<long>(values[7], (long?)null);

        var indexes = GroupById(rows, "i");
        AssertTypedValues<long>(indexes[1], 0, 1);
        AssertTypedValues<long>(indexes[3], 0);
        AssertTypedValues<long>(indexes[4], 0, 1);
        AssertTypedValues<long>(indexes[5], 0);
        AssertTypedValues<long>(indexes[7], 0);
    }

    // Expanding property bags: kind=bag and kind=array

    [TestCase("Bags | mv-expand d", Description = "kind=bag is the default")]
    [TestCase("Bags | mv-expand kind=bag d")]
    [TestCase("Bags | mv-expand bagexpansion=bag d", Description = "the legacy syntax")]
    [Description("mv-expand kind=bag - every property of a bag becomes a bag with one property, arrays are expanded "
                 + "by elements")]
    public void MvExpand_BagKindBag_ExpandsPropertiesIntoSinglePropertyBags(string query)
    {
        var kustoDatabase = CreateBagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, query);

        var values = GroupById(rows, "d");
        Assert.That(values.Keys, Is.EquivalentTo(new[] { 1L, 3L, 4L, 5L, 6L }), "an empty bag (id 2) produces no rows");
        AssertDynamicJsonUnordered(values[1], """[{"a":1},{"b":"x"}]""", "id 1: the property order is not defined");
        AssertDynamicJson(values[3], """[{"n":null}]""", "id 3: a property with a null value is expanded");
        AssertDynamicJson(values[4], "[[1,2],[3]]", "id 4: an array is expanded by elements");
        AssertDynamicJson(values[5], """[{"nested":{"k":1}}]""", "id 5: a nested bag is not expanded");
        AssertDynamicJson(values[6], "[null]", "id 6: null produces one row");
    }

    [TestCase("Bags | mv-expand kind=array d")]
    [TestCase("Bags | mv-expand bagexpansion=array d", Description = "the legacy syntax")]
    [Description("mv-expand kind=array - every property of a bag becomes a [key, value] array, arrays are expanded "
                 + "by elements")]
    public void MvExpand_BagKindArray_ExpandsPropertiesIntoKeyValueArrays(string query)
    {
        var kustoDatabase = CreateBagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, query);

        var values = GroupById(rows, "d");
        Assert.That(values.Keys, Is.EquivalentTo(new[] { 1L, 3L, 4L, 5L, 6L }), "an empty bag (id 2) produces no rows");
        AssertDynamicJsonUnordered(values[1], """[["a",1],["b","x"]]""", "id 1: the property order is not defined");
        AssertDynamicJson(values[3], """[["n",null]]""", "id 3: a property with a null value is expanded");
        AssertDynamicJson(values[4], "[[1,2],[3]]", "id 4: an array is expanded by elements, they are not changed");
        AssertDynamicJson(values[5], """[["nested",{"k":1}]]""", "id 5: a nested bag is not expanded");
        AssertDynamicJson(values[6], "[null]", "id 6: null produces one row");
    }

    [Test]
    [Description("mv-expand with_itemindex - numbers the expanded properties of a bag from 0")]
    public void MvExpand_BagWithItemIndex_NumbersThePropertiesFromZero()
    {
        var kustoDatabase = CreateBagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Bags | where id == 1 | mv-expand with_itemindex=i d");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertTypedValues<long>(rows.Select(row => Col(row, "i")).ToList(), 0, 1);
    }

    [Test]
    [Description("mv-expand limit 1 - returns one property of a bag")]
    public void MvExpand_BagWithLimit_ReturnsOneProperty()
    {
        var kustoDatabase = CreateBagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Bags | where id == 1 | mv-expand d limit 1");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJsonOneOf(Col(rows[0], "d"), """{"a":1}""", """{"b":"x"}""");
    }

    [Test]
    [Description("mv-expand kind=array - the property name and the value can be read with [0] and [1]")]
    public void MvExpand_BagKindArray_KeyAndValueCanBeExtracted()
    {
        var kustoDatabase = CreateBagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Bags | mv-expand kind=array kv = d "
            + "| project id, propName = tostring(kv[0]), propValue = kv[1], t = gettype(kv[1])");

        var byKey = rows.ToDictionary(row => $"{Col(row, "id")}:{Col(row, "propName")}");
        Assert.That(byKey.Keys, Is.EquivalentTo(new[] { "1:a", "1:b", "3:n", "4:1", "4:3", "5:nested", "6:" }));
        AssertDynamicJson(Col(byKey["1:a"], "propValue"), "1", "1:a");
        AssertDynamicJson(Col(byKey["1:b"], "propValue"), "\"x\"", "1:b");
        AssertDynamicJson(Col(byKey["3:n"], "propValue"), "null", "3:n");
        AssertDynamicJson(Col(byKey["4:1"], "propValue"), "2", "4:1: the second element of the array [1,2]");
        AssertDynamicJson(Col(byKey["4:3"], "propValue"), "null", "4:3: [3] has no second element");
        AssertDynamicJson(Col(byKey["5:nested"], "propValue"), """{"k":1}""", "5:nested");
        AssertDynamicJson(Col(byKey["6:"], "propValue"), "null", "6: null");

        Assert.That(Col(byKey["1:a"], "t"), Is.EqualTo("long"));
        Assert.That(Col(byKey["1:b"], "t"), Is.EqualTo("string"));
        Assert.That(Col(byKey["3:n"], "t"), Is.EqualTo("null"));
        Assert.That(Col(byKey["4:1"], "t"), Is.EqualTo("long"));
        Assert.That(Col(byKey["4:3"], "t"), Is.EqualTo("null"));
        Assert.That(Col(byKey["5:nested"], "t"), Is.EqualTo("dictionary"));
        Assert.That(Col(byKey["6:"], "t"), Is.EqualTo("null"));
    }

    [Test]
    [Description("mv-expand kind=array | summarize make_bag(bag_pack(...)) - rebuilds the expanded bag")]
    public void MvExpand_BagKindArrayThenMakeBag_RebuildsTheBag()
    {
        var kustoDatabase = CreateBagsDatabase();

        var value = OpusTestUtils.ExecuteAndGetSingleValue("b", kustoDatabase,
            "Bags | where id == 1 | mv-expand kind=array kv = d | summarize b = make_bag(bag_pack(tostring(kv[0]), kv[1]))");

        AssertDynamicJson(value, """{"a":1,"b":"x"}""");
    }

    // Expanding several expressions in parallel

    [Test]
    [Description("mv-expand a, b - the elements are aligned by index, the shorter array is padded with null, "
                 + "null counts as one element and a row is dropped only when all arrays are empty")]
    public void MvExpand_TwoArrays_AlignsElementsByIndexAndPadsWithNull()
    {
        var kustoDatabase = CreatePairsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Pairs | mv-expand with_itemindex=i a, b");

        var aValues = GroupById(rows, "a");
        var bValues = GroupById(rows, "b");
        var indexes = GroupById(rows, "i");
        Assert.That(aValues.Keys, Is.EquivalentTo(new[] { 1L, 2L, 3L, 5L, 6L, 7L }), "two empty arrays (id 4) produce no rows");

        AssertDynamicJson(aValues[1], "[1,2,3]", "a of id 1");
        AssertDynamicJson(bValues[1], """["x","y",null]""", "b of id 1: the shorter array is padded with null");
        AssertTypedValues<long>(indexes[1], 0, 1, 2);

        AssertDynamicJson(aValues[2], "[null]", "a of id 2: an empty array is padded with null");
        AssertDynamicJson(bValues[2], """["z"]""", "b of id 2");
        AssertTypedValues<long>(indexes[2], 0);

        AssertDynamicJson(aValues[3], "[null]", "a of id 3: null");
        AssertDynamicJson(bValues[3], "[true]", "b of id 3");

        AssertDynamicJson(aValues[5], "[null]", "a of id 5: two nulls produce one row");
        AssertDynamicJson(bValues[5], "[null]", "b of id 5");

        AssertDynamicJson(aValues[6], "[5]", "a of id 6");
        AssertDynamicJson(bValues[6], "[null]", "b of id 6: null");

        AssertDynamicJson(aValues[7], "[8,9]", "a of id 7");
        AssertDynamicJsonUnordered(bValues[7], """[{"k":"v"},{"w":1}]""", "b of id 7: a bag is expanded by properties");
        AssertTypedValues<long>(indexes[7], 0, 1);
    }

    [Test]
    [Description("mv-expand a, b limit 1 - returns the first aligned elements of every input row")]
    public void MvExpand_TwoArraysWithLimit_ReturnsTheFirstAlignedRow()
    {
        var kustoDatabase = CreatePairsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Pairs | mv-expand a, b limit 1");

        AssertDynamicJsonById(SingleById(rows, "a"), (1, "1"), (2, "null"), (3, "null"), (5, "null"), (6, "5"), (7, "8"));
        var bValues = SingleById(rows, "b");
        AssertDynamicJson(bValues[1], "\"x\"", "id 1");
        AssertDynamicJson(bValues[2], "\"z\"", "id 2");
        AssertDynamicJson(bValues[3], "true", "id 3");
        AssertDynamicJson(bValues[5], "null", "id 5");
        AssertDynamicJson(bValues[6], "null", "id 6");
        AssertDynamicJsonOneOf(bValues[7], """{"k":"v"}""", """{"w":1}""");
    }

    [Test]
    [Description("mv-expand kind=array a, b - an array and a bag expanded in parallel, the bag gives [key, value] arrays")]
    public void MvExpand_ArrayAndBagWithKindArray_AlignsKeyValueArrays()
    {
        var kustoDatabase = CreatePairsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Pairs | where id == 7 | mv-expand kind=array a, b");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertDynamicJson(rows.Select(row => Col(row, "a")).ToList(), "[8,9]", "a");
        AssertDynamicJsonUnordered(rows.Select(row => Col(row, "b")).ToList(), """[["k","v"],["w",1]]""", "b");
    }

    [Test]
    [Description("mv-expand a - only the given column is expanded, the other dynamic column is repeated")]
    public void MvExpand_OneOfTwoArrays_KeepsTheOtherArray()
    {
        var kustoDatabase = CreatePairsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Pairs | mv-expand a");

        var bValues = GroupById(rows, "b");
        Assert.That(bValues.ToDictionary(group => group.Key, group => group.Value.Count),
            Is.EquivalentTo(new Dictionary<long, int> { [1] = 3, [3] = 1, [5] = 1, [6] = 1, [7] = 2 }),
            "the number of rows per id");
        AssertDynamicJson(bValues[1], """[["x","y"],["x","y"],["x","y"]]""", "b of id 1");
        AssertDynamicJson(bValues[3], "[[true]]", "b of id 3");
        AssertDynamicJson(bValues[5], "[null]", "b of id 5");
        AssertDynamicJson(bValues[7], """[{"k":"v","w":1},{"k":"v","w":1}]""", "b of id 7");
    }

    [TestCase("Pairs | mv-expand with_itemindex=i a, b", new[] { "id:long", "a:dynamic", "b:dynamic", "i:long" })]
    [TestCase("Pairs | mv-expand with_itemindex=i a to typeof(long), b to typeof(string)",
        new[] { "id:long", "a:long", "b:string", "i:long" })]
    [Description("mv-expand a, b - the schema of the result")]
    public void MvExpand_TwoArraysSchema_ReturnsExpectedColumns(string query, string[] expected)
    {
        var kustoDatabase = CreatePairsDatabase();

        var schema = ExecuteAndGetSchema(kustoDatabase, query + " | getschema");

        Assert.That(schema, Is.EqualTo(expected));
    }

    [Test]
    [Description("mv-expand a to typeof(long), b to typeof(string) - every expression is cast separately")]
    public void MvExpand_TwoArraysWithDifferentCasts_CastsEveryColumnSeparately()
    {
        var kustoDatabase = CreateMixedDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Mixed | mv-expand with_itemindex=i a to typeof(long), b to typeof(string)");

        AssertTypedValues<long>(GroupById(rows, "a")[1], 1, null);
        Assert.That(GroupById(rows, "b")[1], Is.EqualTo(new object?[] { "3", "x" }));
        AssertTypedValues<long>(GroupById(rows, "i")[1], 0, 1);
    }

    [TestCase("Mixed | mv-expand a | mv-expand b", new[] { "1-3", "1-x", "2-3", "2-x" },
        Description = "two mv-expand operators produce every combination")]
    [TestCase("Mixed | mv-expand a, b", new[] { "1-3", "2-x" },
        Description = "one mv-expand aligns the elements by index")]
    [Description("mv-expand - chained and parallel expansion of two arrays")]
    public void MvExpand_ChainedAndParallel_ProduceDifferentCombinations(string query, string[] expected)
    {
        var kustoDatabase = CreateMixedDatabase();

        var values = OpusTestUtils.ExecuteAndGetValues("combo", kustoDatabase,
            query + " | project combo = strcat(tostring(a), '-', tostring(b))");

        Assert.That(values.Select(value => value as string), Is.EquivalentTo(expected));
    }

    // Column names, nested documents and parsed JSON

    [Test]
    [Description("mv-expand d.items, pack_array(...) - a path gets the column name d_items, "
                 + "another unnamed expression gets Column1")]
    public void MvExpand_PathAndUnnamedExpression_GetGeneratedColumnNames()
    {
        var kustoDatabase = CreateNestedDatabase();

        var schema = ExecuteAndGetSchema(kustoDatabase, "Nested | mv-expand d.items, pack_array(id, 5) | getschema");
        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Nested | mv-expand d.items, pack_array(id, 5)");

        Assert.That(schema, Is.EqualTo(new[] { "id:long", "d:dynamic", "d_items:dynamic", "Column1:dynamic" }));
        Assert.That(rows, Has.Count.EqualTo(2));
        AssertDynamicJson(rows.Select(row => Col(row, "d_items")).ToList(), "[1,2]", "d_items");
        AssertDynamicJson(rows.Select(row => Col(row, "Column1")).ToList(), "[1,5]", "Column1");
        foreach (var row in rows)
        {
            AssertDynamicJson(Col(row, "d"), """{"items":[1,2]}""", "the source column is kept");
        }
    }

    [Test]
    [Description("mv-expand u = d.users - expands an array of objects, the properties of every object can be read; "
                 + "an empty array produces no rows and a missing property produces one row with null")]
    public void MvExpand_NestedArrayOfObjects_ReadsPropertiesOfEveryObject()
    {
        var kustoDatabase = CreateDocsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Docs | mv-expand u = d.users "
            + "| project id, userName = tostring(u.name), userAge = tolong(u.age), uNull = isnull(u)");

        var names = GroupById(rows, "userName");
        Assert.That(names.Keys, Is.EquivalentTo(new[] { 1L, 3L }), "the empty array of id 2 produces no rows");
        Assert.That(names[1], Is.EqualTo(new object?[] { "a", "b" }));
        Assert.That(names[3], Is.EqualTo(new object?[] { "" }));

        var ages = GroupById(rows, "userAge");
        AssertTypedValues<long>(ages[1], 30, 40);
        AssertTypedValues<long>(ages[3], (long?)null);

        var nulls = GroupById(rows, "uNull");
        AssertTypedValues<bool>(nulls[1], false, false);
        AssertTypedValues<bool>(nulls[3], true);
    }

    [Test]
    [Description("mv-expand x = parse_json(raw) - expands the parsed JSON of a string column; invalid JSON is a string, "
                 + "an empty string and JSON null are null, [] and {} produce no rows")]
    public void MvExpand_ParseJsonOfStringColumn_ExpandsParsedValues()
    {
        var kustoDatabase = CreateRawJsonDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "RawJson | mv-expand with_itemindex=i x = parse_json(raw) | extend t = gettype(x)");

        var values = GroupById(rows, "x");
        var types = GroupById(rows, "t");
        Assert.That(values.Keys, Is.EquivalentTo(new[] { 1L, 2L, 3L, 5L, 6L, 7L, 9L }));

        AssertDynamicJson(values[1], """[1,{"k":2}]""", "id 1");
        Assert.That(types[1], Is.EqualTo(new object?[] { "long", "dictionary" }));

        AssertDynamicJson(values[2], """["oops"]""", "id 2: invalid JSON is the original string");
        Assert.That(types[2], Is.EqualTo(new object?[] { "string" }));

        AssertDynamicJson(values[3], "[null]", "id 3: an empty string is null");
        Assert.That(types[3], Is.EqualTo(new object?[] { "null" }));

        AssertDynamicJsonUnordered(values[5], """[{"a":1},{"b":[2,3]}]""", "id 5: an object is expanded by properties");
        Assert.That(types[5], Is.EqualTo(new object?[] { "dictionary", "dictionary" }));

        AssertDynamicJson(values[6], """["text"]""", "id 6: a JSON string");
        Assert.That(types[6], Is.EqualTo(new object?[] { "string" }));

        AssertDynamicJson(values[7], "[42]", "id 7: a JSON number");
        Assert.That(types[7], Is.EqualTo(new object?[] { "long" }));

        AssertDynamicJson(values[9], "[null]", "id 9: JSON null");
        Assert.That(types[9], Is.EqualTo(new object?[] { "null" }));

        AssertTypedValues<long>(GroupById(rows, "i")[1], 0, 1);
    }

    [Test]
    [Description("mv-expand - only the top level is expanded: nested arrays, empty arrays and bags are single elements")]
    public void MvExpand_NestedAndNullElements_AreExpandedOneLevelOnly()
    {
        var kustoDatabase = CreateEdgeDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Edge | mv-expand with_itemindex=i arr | extend t = gettype(arr)");

        var values = GroupById(rows, "arr");
        var types = GroupById(rows, "t");
        Assert.That(values.Keys, Is.EquivalentTo(new[] { 1L, 2L, 3L, 4L, 5L, 6L }));

        AssertDynamicJson(values[1], "[null,null]", "id 1: every null element produces a row");
        Assert.That(types[1], Is.EqualTo(new object?[] { "null", "null" }));
        AssertTypedValues<long>(GroupById(rows, "i")[1], 0, 1);

        AssertDynamicJson(values[2], "[[]]", "id 2: an empty array element is kept");
        Assert.That(types[2], Is.EqualTo(new object?[] { "array" }));

        AssertDynamicJson(values[3], "[{}]", "id 3: an empty bag element is kept");
        Assert.That(types[3], Is.EqualTo(new object?[] { "dictionary" }));

        AssertDynamicJson(values[4], "[[1,[2]]]", "id 4: a nested array is not flattened");
        Assert.That(types[4], Is.EqualTo(new object?[] { "array" }));

        AssertDynamicJson(values[5], "[1,[2,[3]]]", "id 5");
        Assert.That(types[5], Is.EqualTo(new object?[] { "long", "array" }));

        AssertDynamicJson(values[6], """["[1,2]"]""", "id 6: a string with JSON text is not parsed");
        Assert.That(types[6], Is.EqualTo(new object?[] { "string" }));
    }

    // Errors

    [TestCase("Items | mv-expand s", Description = "a string column can't be expanded")]
    [TestCase("Items | mv-expand id", Description = "a long column can't be expanded")]
    [TestCase("Items | mv-expand arr | summarize c = count() by arr",
        Description = "the expanded column is dynamic, it can't be a group key without a cast")]
    [Description("mv-expand - invalid queries fail")]
    public void MvExpand_InvalidQuery_Fails(string query)
    {
        var kustoDatabase = CreateItemsDatabase();

        OpusTestUtils.AssertQueryFails(kustoDatabase, query);
    }

    // Combining mv-expand with other operators

    [Test]
    [Description("mv-expand | summarize by tostring(...) - groups the expanded elements by their text, "
                 + "null elements are grouped under an empty string")]
    public void MvExpand_ThenSummarizeByToString_GroupsElementsByText()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Items | mv-expand arr | summarize c = count() by v = tostring(arr)");

        var counts = SingleByKey(rows, "v", "c");
        Assert.That(counts.Keys, Is.EquivalentTo(new[]
        {
            "", "1.5", "10", "20", "30", "7", "[1,2]", "str", "x", """{"k":"v"}"""
        }));
        OpusTestUtils.AssertTypedValue(counts[""], 2L);
        foreach (var (key, count) in counts.Where(item => item.Key != ""))
        {
            Assert.That(count, Is.TypeOf<long>().And.EqualTo(1L), $"key '{key}'");
        }
    }

    [TestCase("Items | mv-expand arr | where arr == 20", new[] { 1L },
        Description = "a dynamic element is compared with a number, strings and bags don't match")]
    [TestCase("Items | mv-expand arr | where tolong(arr) > 15", new[] { 1L, 1L })]
    [TestCase("Items | mv-expand arr to typeof(long) | where arr > 15", new[] { 1L, 1L })]
    [TestCase("Items | mv-expand arr | where gettype(arr) == 'string'", new[] { 4L, 7L })]
    [TestCase("Items | mv-expand arr | where isnull(arr)", new[] { 3L, 4L },
        Description = "null input rows and null elements")]
    [Description("mv-expand | where - filters the expanded elements")]
    public void MvExpand_ThenWhere_FiltersExpandedElements(string query, long[] expectedIds)
    {
        var kustoDatabase = CreateItemsDatabase();

        var ids = ExecuteAndGetIds(kustoDatabase, query);

        Assert.That(ids, Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("mv-expand y = dynamic([...]) - a constant array is expanded for every input row")]
    public void MvExpand_ConstantArray_IsExpandedForEveryInputRow()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Items | where id <= 2 | mv-expand y = dynamic(['p','q'])");

        var values = GroupById(rows, "y");
        Assert.That(values.Keys, Is.EquivalentTo(new[] { 1L, 2L }));
        AssertDynamicJson(values[1], """["p","q"]""", "id 1");
        AssertDynamicJson(values[2], """["p","q"]""", "id 2: the empty arr column is not expanded");
    }

    [Test]
    [Description("print | mv-expand - expands an array after print")]
    public void MvExpand_AfterPrint_ExpandsTheArray()
    {
        var kustoDatabase = new KustoDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "print x = 1 | mv-expand y = dynamic(['p','q'])");

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertDynamicJson(rows.Select(row => Col(row, "y")).ToList(), """["p","q"]""", "y");
        AssertTypedValues<long>(rows.Select(row => Col(row, "x")).ToList(), 1, 1);
    }

    [Test]
    [Description("mv-expand e = pack_array(id, s) - expands the values of several columns, they keep their types")]
    public void MvExpand_PackArrayOfColumns_ExpandsColumnValues()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Items | where id == 1 | mv-expand e = pack_array(id, s) | extend t = gettype(e)");

        AssertDynamicJson(rows.Select(row => Col(row, "e")).ToList(), """[1,"one"]""", "e");
        Assert.That(rows.Select(row => Col(row, "t")).ToList(), Is.EqualTo(new object?[] { "long", "string" }));
    }

    [Test]
    [Description("mv-expand | summarize by gettype(...) - groups the expanded elements by their type")]
    public void MvExpand_ThenSummarizeByType_GroupsElementsByType()
    {
        var kustoDatabase = CreateItemsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Items | mv-expand arr | summarize ids = make_set(id) by t = gettype(arr)");

        AssertDynamicJsonUnorderedByKey(SingleByKey(rows, "t", "ids"),
            ("array", "[4]"),
            ("dictionary", "[4]"),
            ("double", "[4]"),
            ("long", "[1,5]"),
            ("null", "[3,4]"),
            ("string", "[4,7]"));
    }

    // Typical scenarios: tags

    [Test]
    [Description("mv-expand tag = tags to typeof(string) | summarize count() by tag - counts every tag")]
    public void MvExpand_TagsThenSummarizeCount_CountsEveryTag()
    {
        var kustoDatabase = CreateTagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Tags | mv-expand tag = tags to typeof(string) | where tag != '' | summarize c = count() by tag");

        var counts = SingleByKey(rows, "tag", "c");
        Assert.That(counts.Keys, Is.EquivalentTo(new[] { "kusto", "db", "cloud" }));
        OpusTestUtils.AssertTypedValue(counts["kusto"], 2L);
        OpusTestUtils.AssertTypedValue(counts["db"], 3L);
        OpusTestUtils.AssertTypedValue(counts["cloud"], 1L);
    }

    [Test]
    [Description("mv-expand with_itemindex | sort by index | summarize make_list() by id - rebuilds the arrays "
                 + "in the original order")]
    public void MvExpand_TagsWithItemIndexThenMakeList_RebuildsTheArrays()
    {
        var kustoDatabase = CreateTagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Tags | mv-expand with_itemindex=i tag = tags | where isnotnull(tag) | sort by i asc "
            + "| summarize l = make_list(tag) by id");

        AssertDynamicJsonById(SingleById(rows, "l"),
            (1, """["kusto","db"]"""),
            (2, """["db"]"""),
            (5, """["db","kusto","cloud"]"""));
    }

    [Test]
    [Description("mv-expand | summarize count() by tag | summarize make_bag(bag_pack(tag, c)) - builds a bag of tag counts")]
    public void MvExpand_TagsCountThenMakeBag_BuildsTagCounts()
    {
        var kustoDatabase = CreateTagsDatabase();

        var value = OpusTestUtils.ExecuteAndGetSingleValue("b", kustoDatabase,
            "Tags | mv-expand tag = tags to typeof(string) | where tag != '' | summarize c = count() by tag "
            + "| summarize b = make_bag(bag_pack(tag, c))");

        AssertDynamicJson(value, """{"kusto":2,"db":3,"cloud":1}""");
    }

    [Test]
    [Description("mv-expand | summarize count() by id - an empty array has no rows, null has one row")]
    public void MvExpand_TagsThenCountById_CountsRowsPerInputRow()
    {
        var kustoDatabase = CreateTagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Tags | mv-expand tag = tags | summarize n = count() by id");

        AssertTypedById<long>(SingleById(rows, "n"), (1, 2L), (2, 1L), (4, 1L), (5, 3L));
    }

    [Test]
    [Description("mv-expand | where tostring(tag) == ... - finds the input rows with the tag")]
    public void MvExpand_TagsThenWhereToString_FindsInputRowsWithTag()
    {
        var kustoDatabase = CreateTagsDatabase();

        var ids = ExecuteAndGetIds(kustoDatabase, "Tags | mv-expand tag = tags | where tostring(tag) == 'db'");

        Assert.That(ids, Is.EqualTo(new[] { 1L, 2L, 5L }));
    }

    [Test]
    [Description("mv-expand with_itemindex | where i == 1 - returns the second element of every array")]
    public void MvExpand_TagsWithItemIndexOne_ReturnsTheSecondTag()
    {
        var kustoDatabase = CreateTagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Tags | mv-expand with_itemindex=i tag = tags | where i == 1");

        AssertDynamicJsonById(SingleById(rows, "tag"), (1, "\"db\""), (5, "\"kusto\""));
    }

    [Test]
    [Description("mv-expand | summarize make_list(id) by tag | mv-expand ... to typeof(long) | summarize sum() - "
                 + "builds an inverted index and expands it again")]
    public void MvExpand_TagsInvertedIndex_SumsIdsPerTag()
    {
        var kustoDatabase = CreateTagsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Tags | mv-expand tag = tags to typeof(string) | where tag != '' | summarize ids = make_list(id) by tag "
            + "| mv-expand id2 = ids to typeof(long) | summarize total = sum(id2) by tag");

        var totals = SingleByKey(rows, "tag", "total");
        Assert.That(totals.Keys, Is.EquivalentTo(new[] { "kusto", "db", "cloud" }));
        OpusTestUtils.AssertTypedValue(totals["kusto"], 6L);
        OpusTestUtils.AssertTypedValue(totals["db"], 8L);
        OpusTestUtils.AssertTypedValue(totals["cloud"], 5L);
    }

    [Test]
    [Description("pack_all() | mv-expand kind=array - turns the columns of a row into [name, value] rows")]
    public void MvExpand_PackAllWithKindArray_TurnsColumnsIntoRows()
    {
        var kustoDatabase = CreatePeopleDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "People | extend p = pack_all() | mv-expand kind=array kv = p "
            + "| project id, propName = tostring(kv[0]), propValue = kv[1], t = gettype(kv[1])");

        AssertDynamicJsonByKey(SingleByKey(rows, "propName", "propValue"), ("id", "1"), ("name", "\"ann\""));
        var types = SingleByKey(rows, "propName", "t");
        Assert.That(types["id"], Is.EqualTo("long"));
        Assert.That(types["name"], Is.EqualTo("string"));
    }

    private static KustoDatabase CreateItemsDatabase()
    {
        var table = OpusTestUtils.NewTable("Items")
            .WithColumn<long>("id")
            .WithColumn<object>("arr")
            .WithColumn<string>("s")
            .WithRow(1L, Arr(10L, 20L, 30L), "one")
            .WithRow(2L, Arr(), "two")
            .WithRow(3L, null, "three")
            .WithRow(4L, Json("""["x",1.5,null,{"k":"v"},[1,2]]"""), "four")
            .WithRow(5L, 7L, "five")
            .WithRow(6L, Bag(), "six")
            .WithRow(7L, "str", "seven")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateCastsDatabase()
    {
        var table = OpusTestUtils.NewTable("Casts")
            .WithColumn<long>("id")
            .WithColumn<object>("v")
            .WithRow(1L, Arr(10L, "x", 2.5, true, Bag(("a", 1L)), null, Utc(2020, 1, 1), "7", 2.9, -3L, Arr(1L), "true", false))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateTypedDatabase()
    {
        var table = OpusTestUtils.NewTable("Typed")
            .WithColumn<long>("id")
            .WithColumn<object>("v")
            .WithRow(1L, Arr(Utc(2020, 1, 1), TimeSpan.FromHours(1), SampleGuid, 1.5, 2L, "x", true, "01:00:00", GuidText, null,
                Bag(("a", 1L))))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateBagsDatabase()
    {
        var table = OpusTestUtils.NewTable("Bags")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Json("""{"a":1,"b":"x"}"""))
            .WithRow(2L, Bag())
            .WithRow(3L, Json("""{"n":null}"""))
            .WithRow(4L, Json("[[1,2],[3]]"))
            .WithRow(5L, Json("""{"nested":{"k":1}}"""))
            .WithRow(6L, null)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreatePairsDatabase()
    {
        var table = OpusTestUtils.NewTable("Pairs")
            .WithColumn<long>("id")
            .WithColumn<object>("a")
            .WithColumn<object>("b")
            .WithRow(1L, Arr(1L, 2L, 3L), Arr("x", "y"))
            .WithRow(2L, Arr(), Arr("z"))
            .WithRow(3L, null, Arr(true))
            .WithRow(4L, Arr(), Arr())
            .WithRow(5L, null, null)
            .WithRow(6L, Arr(5L), null)
            .WithRow(7L, Arr(8L, 9L), Json("""{"k":"v","w":1}"""))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateMixedDatabase()
    {
        var table = OpusTestUtils.NewTable("Mixed")
            .WithColumn<long>("id")
            .WithColumn<object>("a")
            .WithColumn<object>("b")
            .WithRow(1L, Arr(1L, "2"), Arr(3L, "x"))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateDocsDatabase()
    {
        var table = OpusTestUtils.NewTable("Docs")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Json("""{"users":[{"name":"a","age":30},{"name":"b","age":40}]}"""))
            .WithRow(2L, Json("""{"users":[]}"""))
            .WithRow(3L, Json("""{"other":1}"""))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateNestedDatabase()
    {
        var table = OpusTestUtils.NewTable("Nested")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Json("""{"items":[1,2]}"""))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateRawJsonDatabase()
    {
        var table = OpusTestUtils.NewTable("RawJson")
            .WithColumn<long>("id")
            .WithColumn<string>("raw")
            .WithRow(1L, """[1,{"k":2}]""")
            .WithRow(2L, "oops")
            .WithRow(3L, "")
            .WithRow(4L, "[]")
            .WithRow(5L, """{"a":1,"b":[2,3]}""")
            .WithRow(6L, "\"text\"")
            .WithRow(7L, "42")
            .WithRow(8L, "{}")
            .WithRow(9L, "null")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateEdgeDatabase()
    {
        var table = OpusTestUtils.NewTable("Edge")
            .WithColumn<long>("id")
            .WithColumn<object>("arr")
            .WithRow(1L, Json("[null,null]"))
            .WithRow(2L, Json("[[]]"))
            .WithRow(3L, Json("[{}]"))
            .WithRow(4L, Json("[[1,[2]]]"))
            .WithRow(5L, Json("[1,[2,[3]]]"))
            .WithRow(6L, "[1,2]")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateTagsDatabase()
    {
        var table = OpusTestUtils.NewTable("Tags")
            .WithColumn<long>("id")
            .WithColumn<object>("tags")
            .WithRow(1L, Arr("kusto", "db"))
            .WithRow(2L, Arr("db"))
            .WithRow(3L, Arr())
            .WithRow(4L, null)
            .WithRow(5L, Arr("db", "kusto", "cloud"))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreatePeopleDatabase()
    {
        var table = OpusTestUtils.NewTable("People")
            .WithColumn<long>("id")
            .WithColumn<string>("name")
            .WithRow(1L, "ann")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }
}
