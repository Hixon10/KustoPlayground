using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Array and set functions on dynamic arrays and dynamic arrays on the right side of the in operator:
/// in / !in / in~ - https://learn.microsoft.com/en-us/kusto/query/in-operator,
/// array_length - https://learn.microsoft.com/en-us/kusto/query/array-length-function,
/// array_slice - https://learn.microsoft.com/en-us/kusto/query/array-slice-function,
/// array_concat - https://learn.microsoft.com/en-us/kusto/query/array-concat-function,
/// array_index_of - https://learn.microsoft.com/en-us/kusto/query/array-index-of-function,
/// array_reverse - https://learn.microsoft.com/en-us/kusto/query/array-reverse-function,
/// array_sort_asc - https://learn.microsoft.com/en-us/kusto/query/array-sort-asc-function,
/// array_sort_desc - https://learn.microsoft.com/en-us/kusto/query/array-sort-desc-function,
/// array_sum - https://learn.microsoft.com/en-us/kusto/query/array-sum-function,
/// array_iff - https://learn.microsoft.com/en-us/kusto/query/array-iff-function,
/// set_union - https://learn.microsoft.com/en-us/kusto/query/set-union-function,
/// set_intersect - https://learn.microsoft.com/en-us/kusto/query/set-intersect-function,
/// set_difference - https://learn.microsoft.com/en-us/kusto/query/set-difference-function
/// and set_has_element - https://learn.microsoft.com/en-us/kusto/query/set-has-element-function.
/// <para>
/// Values that are not arrays (null, property bags, scalars): array_length, array_slice, array_reverse, array_sort_*
/// and array_sum return null, array_index_of returns -1 and set_has_element returns false (both return null for null),
/// array_concat and set_union ignore such arguments, set_intersect / set_difference return [] for bags and scalars and
/// null when the (first) argument is null.
/// The set functions return distinct elements without a guaranteed order and skip null elements; elements are compared
/// by type and value: 1 (long), 1.0 (real) and '1' (string) are different elements, strings are case-sensitive,
/// nested arrays and property bags are compared by value.
/// The in operator flattens the nested arrays of its right side and converts the elements to the type of the left side
/// ('1' in (dynamic([1])) is true), set_has_element compares only the top-level elements and works with per-row arrays.
/// The functions never modify their input arrays.
/// </para>
/// Expected values were verified against a real Azure Data Explorer cluster (cases where the cluster returns different
/// results for constant and per-row evaluation, like set_difference on arrays mixing longs with reals or nulls, are not used).
/// </summary>
public class DynamicOpusArrayAndSetFunctionsTests
{
    // in / !in / in~ with dynamic arrays

    [TestCase("2 in (dynamic([1, 2, 3]))", true)]
    [TestCase("4 in (dynamic([1, 2, 3]))", false)]
    [TestCase("4 !in (dynamic([1, 2, 3]))", true)]
    [TestCase("'b' in (dynamic(['a', 'b']))", true)]
    [TestCase("'B' in (dynamic(['a', 'b']))", false, Description = "in is case-sensitive")]
    [TestCase("'B' in~ (dynamic(['a', 'b']))", true, Description = "in~ is case-insensitive")]
    [TestCase("'Hello' in~ (dynamic(['HELLO']))", true)]
    [TestCase("'b' !in~ (dynamic(['A', 'B']))", false)]
    [TestCase("'hello' in (dynamic(['hello world']))", false, Description = "elements are compared as whole values, not as terms")]
    [TestCase("'' in (dynamic(['', 'x']))", true)]
    [TestCase("'a' in (dynamic([]))", false)]
    [TestCase("5 in (dynamic(null))", false, Description = "a null array has no elements")]
    [TestCase("5 in (dynamic([null, 5]))", true)]
    [TestCase("long(null) in (dynamic([null, 5]))", null, Description = "a null left side returns null, a null element doesn't match it")]
    [TestCase("2 in (1, dynamic([2, 3]))", true, Description = "scalars and arrays can be mixed in the list")]
    [TestCase("'x' in (dynamic(['a']), 'x')", true)]
    [TestCase("2 in (dynamic([[1, 2], [3]]))", true, Description = "nested arrays are flattened")]
    [TestCase("3 in (dynamic([[1, [2, 3]]]))", true, Description = "nested arrays are flattened recursively")]
    [TestCase("1 in (dynamic(['1']))", true, Description = "the elements are converted to the type of the left side")]
    [TestCase("'1' in (dynamic([1]))", true)]
    [TestCase("2.0 in (dynamic([1, 2, 3]))", true)]
    [TestCase("2.5 in (dynamic([2, 3]))", false)]
    [TestCase("int(2) in (dynamic([1, 2, 3]))", true)]
    [TestCase("datetime(2020-01-01) in (dynamic([datetime(2020-01-01), datetime(2021-01-01)]))", true)]
    [TestCase("2 in (array_concat(dynamic([1]), dynamic([2])))", true, Description = "the array can be the result of a function")]
    [Description("in / !in / in~ - a dynamic array on the right side is a list of values")]
    public void In_DynamicArrayLiteral_ReturnsWhetherValueIsElement(string expression, bool? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [TestCase("2 in (arr)", true)]
    [TestCase("4 in (arr)", false)]
    [TestCase("2 !in (arr)", false)]
    [Description("in - the dynamic array can come from a let statement")]
    public void In_LetDynamicArray_ReturnsWhetherValueIsElement(string expression, bool expected)
    {
        OpusTestUtils.AssertTypedValue(EvaluateWithLets("let arr = dynamic([1, 2, 3]);", expression), expected);
    }

    [TestCase("Items | where d.tag in (dynamic(['a', 'b']))", new[] { 1L, 2L },
        Description = "a dynamic left side is compared with string elements; the array ['a'] of row 6 is not 'a'")]
    [TestCase("Items | where tolong(d.n) in (dynamic([1, 3]))", new[] { 1L, 3L, 4L },
        Description = "the string '1' of row 4 is converted by tolong, the null bag of row 5 doesn't match")]
    [Description("in - filters rows by a property of a dynamic column")]
    public void In_DynamicPropertyOnLeft_FiltersRows(string query, long[] expectedIds)
    {
        Assert.That(ExecuteAndGetIds(CreateItemsDatabase(), query), Is.EqualTo(expectedIds));
    }

    [TestCase("Labels | where tag in (dynamic(['a', 'b']))", new[] { 1L, 5L })]
    [TestCase("Labels | where tag in~ (dynamic(['a', 'b']))", new[] { 1L, 2L, 5L })]
    [TestCase("Labels | where tag !in (dynamic(['a', 'b']))", new[] { 2L, 3L, 4L, 6L }, Description = "the empty string is not in the array")]
    [TestCase("let tags = dynamic(['a', 'b']); Labels | where tag in (tags)", new[] { 1L, 5L })]
    [TestCase("let tags = dynamic(['a', 'b']); Labels | where tag in (tags, 'x')", new[] { 1L, 5L, 6L })]
    [Description("in / !in / in~ - filters a string column by a dynamic array")]
    public void In_StringColumn_FiltersRows(string query, long[] expectedIds)
    {
        Assert.That(ExecuteAndGetIds(CreateLabelsDatabase(), query), Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("in - the array can be calculated by toscalar(... | summarize make_list(...))")]
    public void In_ToScalarMakeList_FiltersRows()
    {
        var ids = ExecuteAndGetIds(
            CreateLabelsDatabase(),
            "let allowed = toscalar(Allowed | summarize make_list(id)); Labels | where id in (allowed)");

        Assert.That(ids, Is.EqualTo(new[] { 1L, 3L, 5L }));
    }

    [TestCase("print r = 'a' in (dynamic({'a': 1}))", Description = "a property bag is not a list of values")]
    [TestCase("Items | where 2 in (d)", Description = "the right side must be constant, use set_has_element for per-row arrays")]
    [TestCase("Items | where d.n in (dynamic([3]))", Description = "a dynamic left side can't be compared with numbers, use tolong(d.n)")]
    [Description("in - invalid right side or left side fails")]
    public void In_InvalidOperands_Fails(string query)
    {
        OpusTestUtils.AssertQueryFails(CreateItemsDatabase(), query);
    }

    [Test]
    [Description("in vs set_has_element - in flattens nested arrays, converts the elements and has a case-insensitive version, "
                 + "set_has_element compares the top-level elements by type and value")]
    public void In_ComparedWithSetHasElement_HasDifferentSemantics()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            new KustoDatabase(),
            "print inNested = 1 in (dynamic([[1], 2])), hasNested = set_has_element(dynamic([[1], 2]), 1), "
            + "inString = 1 in (dynamic(['1'])), hasString = set_has_element(dynamic(['1']), 1), "
            + "inCaseInsensitive = 'A' in~ (dynamic(['a'])), hasCaseSensitive = set_has_element(dynamic(['a']), 'A'), "
            + "inNull = 5 in (dynamic(null)), hasNull = set_has_element(dynamic(null), 2)");

        Assert.That(rows, Has.Count.EqualTo(1));
        OpusTestUtils.AssertTypedValue(Col(rows[0], "inNested"), true);
        OpusTestUtils.AssertTypedValue(Col(rows[0], "hasNested"), false);
        OpusTestUtils.AssertTypedValue(Col(rows[0], "inString"), true);
        OpusTestUtils.AssertTypedValue(Col(rows[0], "hasString"), false);
        OpusTestUtils.AssertTypedValue(Col(rows[0], "inCaseInsensitive"), true);
        OpusTestUtils.AssertTypedValue(Col(rows[0], "hasCaseSensitive"), false);
        OpusTestUtils.AssertTypedValue(Col(rows[0], "inNull"), false);
        Assert.That(Col(rows[0], "hasNull"), Is.Null);
    }

    // array_length

    [TestCase("array_length(dynamic([1, 2, 3]))", 3L)]
    [TestCase("array_length(dynamic([]))", 0L)]
    [TestCase("array_length(dynamic([[1, 2], [3]]))", 2L, Description = "a nested array is a single element")]
    [TestCase("array_length(pack_array(1, 'a', dynamic(null)))", 3L, Description = "null elements are counted")]
    [TestCase("array_length(parse_json('[1,2]'))", 2L)]
    [TestCase("array_length(dynamic({'a': 1}))", null, Description = "a property bag is not an array")]
    [TestCase("array_length(dynamic(null))", null)]
    [TestCase("array_length(dynamic('str'))", null)]
    [TestCase("array_length(dynamic(5))", null)]
    [TestCase("array_length(parse_json('not json'))", null, Description = "invalid JSON is parsed as a string")]
    [Description("array_length - returns the number of elements of an array, null for other values")]
    public void ArrayLength_ReturnsNumberOfElements(string expression, long? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [Test]
    [Description("array_length - per row of a dynamic column (empty array, null, bag, string and mixed arrays)")]
    public void ArrayLength_DynamicColumn_ReturnsLengthPerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(CreateArraysDatabase(), "Arrays | project id, len = array_length(arr)");

        AssertTypedById<long>(SingleById(rows, "len"), (1, 3), (2, 0), (3, null), (4, 2), (5, null), (6, null), (7, 3));
    }

    [TestCase("set_has_element(arr, 2)", new[] { 1L })]
    [TestCase("array_length(arr) > 1", new[] { 1L, 4L, 7L })]
    [TestCase("array_length(arr) == 0", new[] { 2L })]
    [TestCase("isnull(array_length(arr))", new[] { 3L, 5L, 6L }, Description = "null, a bag and a string are not arrays")]
    [TestCase("array_index_of(arr, 'a') >= 0", new[] { 4L })]
    [TestCase("array_sum(arr) > 5", new[] { 1L })]
    [TestCase("array_length(set_intersect(arr, dynamic([1, 'a']))) > 0", new[] { 1L, 4L, 7L })]
    [Description("array and set functions - filter rows with where")]
    public void ArrayFunctions_Where_FiltersRows(string predicate, long[] expectedIds)
    {
        var ids = ExecuteAndGetIds(CreateArraysDatabase(), $"Arrays | where {predicate} | project id");

        Assert.That(ids, Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("array_length - rows can be grouped by the length, values that are not arrays form the null group")]
    public void ArrayLength_SummarizeByLength_GroupsRows()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateArraysDatabase(),
            "Arrays | summarize c = count() by len = array_length(arr)");

        var actual = rows.Select(row => $"{Format(Col(row, "len"))}:{Format(Col(row, "c"))}").ToList();
        Assert.That(actual, Is.EquivalentTo(new[] { "null:3", "0:1", "2:1", "3:2" }));
    }

    // array_slice

    [TestCase("array_slice(dynamic([0, 1, 2, 3, 4]), 1, 3)", "[1,2,3]", Description = "the end index is inclusive")]
    [TestCase("array_slice(dynamic([0, 1, 2, 3, 4]), 0, 0)", "[0]")]
    [TestCase("array_slice(dynamic([0, 1, 2, 3, 4]), -2, -1)", "[3,4]", Description = "negative indexes count from the end")]
    [TestCase("array_slice(dynamic([0, 1, 2, 3, 4]), 2, -1)", "[2,3,4]")]
    [TestCase("array_slice(dynamic([0, 1, 2, 3, 4]), 3, 100)", "[3,4]", Description = "an end index after the last element is clamped")]
    [TestCase("array_slice(dynamic([0, 1, 2, 3, 4]), -100, 1)", "[0,1]", Description = "a start index before the first element is clamped")]
    [TestCase("array_slice(dynamic([0, 1, 2, 3, 4]), 3, 1)", "[]", Description = "start after end")]
    [TestCase("array_slice(dynamic([0, 1, 2, 3, 4]), 10, 12)", "[]", Description = "start after the last element")]
    [TestCase("array_slice(dynamic({'a': 1}), 0, 1)", "null")]
    [TestCase("array_slice(dynamic(null), 0, 1)", "null")]
    [Description("array_slice - returns the elements between the start and the end index")]
    public void ArraySlice_ReturnsElementsBetweenIndexes(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    // array_concat

    [TestCase("array_concat(dynamic([1, 2]), dynamic(['a']), dynamic([]))", """[1,2,"a"]""")]
    [TestCase("array_concat(dynamic([1]))", "[1]")]
    [TestCase("array_concat(dynamic([1]), dynamic([[2, 3]]))", "[1,[2,3]]", Description = "nested arrays are not flattened")]
    [TestCase("array_concat(dynamic([{'a': 1}]), dynamic([null]))", """[{"a":1},null]""", Description = "null elements are kept")]
    [TestCase("array_concat(dynamic([1]), dynamic(null))", "[1]", Description = "arguments that are not arrays are ignored")]
    [TestCase("array_concat(dynamic([1]), dynamic({'a': 1}))", "[1]")]
    [Description("array_concat - concatenates arrays in the order of the arguments")]
    public void ArrayConcat_ReturnsConcatenatedArray(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    [Test]
    [Description("array_concat / array_slice / array_reverse - elements keep their Kusto types (datetime, timespan, guid, decimal)")]
    public void ArrayFunctions_TypedElements_KeepKustoTypes()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            new KustoDatabase(),
            "print concatenated = array_concat(pack_array(datetime(2020-01-01)), pack_array(1h, guid(6f9619ff-8b86-d011-b42d-00c04fc964ff), decimal(1.5))), "
            + "sliced = array_slice(pack_array(datetime(2020-01-01), 1h), 1, 1), "
            + "reversed = array_reverse(pack_array(1h, datetime(2020-01-01)))");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamic(Col(rows[0], "concatenated"), Arr(Utc(2020, 1, 1), TimeSpan.FromHours(1), SampleGuid, 1.5m));
        AssertDynamic(Col(rows[0], "sliced"), Arr(TimeSpan.FromHours(1)));
        AssertDynamic(Col(rows[0], "reversed"), Arr(Utc(2020, 1, 1), TimeSpan.FromHours(1)));
    }

    // array_index_of

    [TestCase("array_index_of(dynamic(['a', 'b', 'c', 'b']), 'b')", 1L, Description = "the index of the first occurrence")]
    [TestCase("array_index_of(dynamic(['a', 'b', 'c', 'b']), 'z')", -1L)]
    [TestCase("array_index_of(dynamic(['a', 'b', 'c', 'b']), 'b', 2)", 3L, Description = "the search starts at the start index")]
    [TestCase("array_index_of(dynamic(['a', 'b', 'c', 'b']), 'b', -1)", 3L, Description = "a negative start index counts from the end")]
    [TestCase("array_index_of(dynamic(['a', 'b', 'c', 'b']), 'b', 0, 1)", -1L, Description = "only one element is searched")]
    [TestCase("array_index_of(dynamic(['a', 'b', 'c', 'b']), 'b', 0, -1, 2)", 3L, Description = "the second occurrence, length -1 searches until the end")]
    [TestCase("array_index_of(dynamic(['a', 'b', 'c', 'b']), 'b', 0, -1, 3)", -1L, Description = "there is no third occurrence")]
    [TestCase("array_index_of(dynamic([1, 2, 3]), 2)", 1L)]
    [TestCase("array_index_of(dynamic([1, '2', 3]), '2')", 1L)]
    [TestCase("array_index_of(dynamic([1, 2, 3]), '2')", -1L, Description = "the type must match, unlike the in operator")]
    [TestCase("array_index_of(dynamic([1.5, 2]), 1.5)", 0L)]
    [TestCase("array_index_of(dynamic(['A', 'a']), 'a')", 1L, Description = "the comparison is case-sensitive")]
    [TestCase("array_index_of(dynamic(null), 1)", null)]
    [Description("array_index_of - returns the zero-based index of a value, -1 if it isn't found")]
    public void ArrayIndexOf_ReturnsIndexOfValue(string expression, long? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    // array_reverse, array_sort_asc, array_sort_desc

    [TestCase("array_reverse(dynamic([1, 'a', [2], null]))", """[null,[2],"a",1]""")]
    [TestCase("array_reverse(dynamic([]))", "[]")]
    [TestCase("array_reverse(dynamic({'a': 1}))", "null")]
    [TestCase("array_reverse(dynamic(null))", "null")]
    [Description("array_reverse - returns the elements in reverse order, nested values are not reversed")]
    public void ArrayReverse_ReturnsReversedArray(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    [TestCase("array_sort_asc(dynamic([3, 1, 2]))", "[1,2,3]")]
    [TestCase("array_sort_desc(dynamic([3, 1, 2]))", "[3,2,1]")]
    [TestCase("array_sort_asc(dynamic(['b', 'a', 'C']))", """["C","a","b"]""", Description = "strings are compared ordinally")]
    [TestCase("array_sort_desc(dynamic(['b', 'a', 'C']))", """["b","a","C"]""")]
    [TestCase("array_sort_asc(dynamic(['10', '9', '100']))", """["10","100","9"]""", Description = "numeric strings are sorted as strings")]
    [TestCase("array_sort_asc(dynamic([2, null, 1]))", "[1,2,null]", Description = "nulls are last by default")]
    [TestCase("array_sort_asc(dynamic([2, null, 1]), false)", "[null,1,2]", Description = "nulls_last = false")]
    [TestCase("array_sort_desc(dynamic([2, null, 1]))", "[2,1,null]")]
    [TestCase("array_sort_desc(dynamic([2, null, 1]), false)", "[null,2,1]")]
    [TestCase("array_sort_asc(dynamic([2, 'a', 1.5, true]))", """[true,1.5,2,"a"]""", Description = "mixed element types")]
    [TestCase("array_sort_asc(dynamic([]))", "[]")]
    [TestCase("array_sort_asc(dynamic(null))", "null")]
    [TestCase("array_sort_asc(dynamic([3, 1, 2]), dynamic(['c', 'a', 'b']))", "[1,2,3]",
        Description = "several arrays return one sorted array per output column, a single output column gets the first sorted array")]
    [TestCase("array_sort_asc(dynamic([3, 1, 2]), dynamic(['x', 'y', 'z']))", "[1,2,3]",
        Description = "the other arrays are only reordered like the first one, they don't change the first sorted array")]
    [TestCase("array_sort_desc(dynamic([3, 1, 2]), dynamic(['x', 'y', 'z']))", "[3,2,1]")]
    [TestCase("array_sort_asc(dynamic([2, 1]), dynamic(['x']))", "[1,2]",
        Description = "another array of a different length doesn't change the first sorted array")]
    [Description("array_sort_asc / array_sort_desc - sort the elements of arrays")]
    public void ArraySort_ReturnsSortedArray(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    [Test]
    [Description("array_sort_asc - datetime elements are sorted chronologically and keep their type")]
    public void ArraySortAsc_DateTimes_KeepsType()
    {
        var actual = OpusTestUtils.EvaluateScalar("array_sort_asc(pack_array(datetime(2021-01-01), datetime(2020-01-01)))");

        AssertDynamic(actual, Arr(Utc(2020, 1, 1), Utc(2021, 1, 1)));
    }

    [Test]
    [Description("array_reverse / array_sort_asc / array_sort_desc - per row of a dynamic column")]
    public void ArrayReverseAndSort_DynamicColumn_ReturnsArrayPerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateArraysDatabase(),
            "Arrays | project id, rev = array_reverse(arr), sortedAsc = array_sort_asc(arr), sortedDesc = array_sort_desc(arr)");

        AssertDynamicJsonById(SingleById(rows, "rev"),
            (1, "[2,1,3]"), (2, "[]"), (3, "null"), (4, """["a","b"]"""), (5, "null"), (6, "null"), (7, "[2.5,null,1]"));
        AssertDynamicJsonById(SingleById(rows, "sortedAsc"),
            (1, "[1,2,3]"), (2, "[]"), (3, "null"), (4, """["a","b"]"""), (5, "null"), (6, "null"), (7, "[1,2.5,null]"));
        AssertDynamicJsonById(SingleById(rows, "sortedDesc"),
            (1, "[3,2,1]"), (2, "[]"), (3, "null"), (4, """["b","a"]"""), (5, "null"), (6, "null"), (7, "[2.5,1,null]"));
    }

    [Test]
    [Description("array_slice / array_concat - per row of a dynamic column, array_concat ignores values that are not arrays")]
    public void ArraySliceAndConcat_DynamicColumn_ReturnsArrayPerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateArraysDatabase(),
            "Arrays | project id, sliced = array_slice(arr, 1, -1), concatenated = array_concat(arr, dynamic([9]))");

        AssertDynamicJsonById(SingleById(rows, "sliced"),
            (1, "[1,2]"), (2, "[]"), (3, "null"), (4, """["a"]"""), (5, "null"), (6, "null"), (7, "[null,2.5]"));
        AssertDynamicJsonById(SingleById(rows, "concatenated"),
            (1, "[3,1,2,9]"), (2, "[9]"), (3, "[9]"), (4, """["b","a",9]"""), (5, "[9]"), (6, "[9]"), (7, "[1,null,2.5,9]"));
    }

    [Test]
    [Description("array_index_of / array_sum / set_has_element - typed results per row of a dynamic column")]
    public void ArrayIndexOfSumAndSetHasElement_DynamicColumn_ReturnsTypedValuePerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateArraysDatabase(),
            "Arrays | project id, idx = array_index_of(arr, 2), total = array_sum(arr), hasTwo = set_has_element(arr, 2)");

        AssertTypedById<long>(SingleById(rows, "idx"), (1, 2), (2, -1), (3, null), (4, -1), (5, -1), (6, -1), (7, -1));
        AssertTypedById<double>(SingleById(rows, "total"), (1, 6.0), (2, null), (3, null), (4, null), (5, null), (6, null), (7, 3.5));
        AssertTypedById<bool>(SingleById(rows, "hasTwo"), (1, true), (2, false), (3, null), (4, false), (5, false), (6, false), (7, false));
    }

    [Test]
    [Description("array_length - the last element by a calculated index is the same as the negative index -1")]
    public void ArrayLength_LastElementIndex_ReturnsLastElementPerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateArraysDatabase(),
            "Arrays | project id, lastItem = arr[array_length(arr) - 1], negItem = arr[-1]");

        foreach (var column in new[] { "lastItem", "negItem" })
        {
            AssertDynamicJsonById(SingleById(rows, column),
                (1, "2"), (2, "null"), (3, "null"), (4, "\"a\""), (5, "null"), (6, "null"), (7, "2.5"));
        }
    }

    // array_sum

    [TestCase("array_sum(dynamic([1, 2, 3]))", 6.0)]
    [TestCase("array_sum(dynamic([1.5, 2.5]))", 4.0)]
    [TestCase("array_sum(dynamic([1, 'a', 2]))", 3.0, Description = "elements that are not numbers are skipped")]
    [TestCase("array_sum(dynamic([1, null, 2]))", 3.0)]
    [TestCase("array_sum(dynamic(['1', '2']))", null, Description = "numeric strings are not converted")]
    [TestCase("array_sum(dynamic([]))", null)]
    [TestCase("array_sum(dynamic(null))", null)]
    [Description("array_sum - returns the sum of the numeric elements as real")]
    public void ArraySum_ReturnsRealSum(string expression, double? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    // array_iff / array_iif

    [TestCase("array_iff(dynamic([true, false, true]), dynamic([1, 2, 3]), dynamic([4, 5, 6]))", "[1,5,3]")]
    [TestCase("array_iff(dynamic([true, false]), 1, 0)", "[1,0]", Description = "a scalar is used for every element")]
    [TestCase("array_iff(dynamic([true, false, true]), dynamic([1, 2]), dynamic([4, 5, 6]))", "[1,5,null]",
        Description = "a missing element is null")]
    [TestCase("array_iff(dynamic([1, 0, 'x', null]), 'y', 'n')", """["y","n",null,null]""",
        Description = "numbers are converted to bool, other conditions return null")]
    [TestCase("array_iif(dynamic([false, true]), 'a', 'b')", """["b","a"]""", Description = "array_iif is an alias")]
    [Description("array_iff - chooses the element of the second or the third argument by the condition array")]
    public void ArrayIff_ReturnsElementwiseChoice(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    // set_union, set_intersect, set_difference

    [TestCase("set_union(dynamic([1, 2, 2]), dynamic([2, 3]), dynamic([3, 4]))", "[1,2,3,4]")]
    [TestCase("set_union(dynamic([1, '1']), dynamic([1.0]))", """[1,"1",1.0]""", Description = "long, string and real are different elements")]
    [TestCase("set_union(dynamic(['a', 'A']), dynamic(['a']))", """["a","A"]""", Description = "strings are case-sensitive")]
    [TestCase("set_union(dynamic([[1], [1]]), dynamic([]))", "[[1]]", Description = "nested arrays are compared by value")]
    [TestCase("set_union(dynamic([{'a': 1}]), dynamic([{'a': 1}]))", """[{"a":1}]""", Description = "property bags are compared by value")]
    [TestCase("set_union(dynamic([1, null]), dynamic([null]))", "[1]", Description = "null elements are skipped")]
    [TestCase("set_union(dynamic([1]), dynamic(null))", "[1]", Description = "arguments that are not arrays are ignored")]
    [TestCase("set_union(dynamic(null), dynamic([2]))", "[2]")]
    [TestCase("set_union(dynamic('str'), dynamic([2]))", "[2]")]
    [TestCase("set_union(dynamic([]), dynamic([]))", "[]")]
    [Description("set_union - returns the distinct elements of all arrays (in any order)")]
    public void SetUnion_ReturnsDistinctElements(string expression, string expectedJsonArray)
    {
        AssertDynamicJsonUnordered(OpusTestUtils.EvaluateScalar(expression), expectedJsonArray);
    }

    [TestCase("set_intersect(dynamic([1, 2, 2, 3]), dynamic([2, 3, 4]))", "[2,3]")]
    [TestCase("set_intersect(dynamic([1, 2, 3]), dynamic([2, 3]), dynamic([3]))", "[3]", Description = "elements of all arrays")]
    [TestCase("set_intersect(dynamic([1, 2]), dynamic([3]))", "[]")]
    [TestCase("set_intersect(dynamic(['a', 'A']), dynamic(['a']))", """["a"]""")]
    [TestCase("set_intersect(dynamic([1]), dynamic([1.0]))", "[]", Description = "1 (long) and 1.0 (real) are different elements")]
    [TestCase("set_intersect(dynamic([1, null]), dynamic([null]))", "[]", Description = "null is never an element of a set")]
    [TestCase("set_intersect(dynamic([{'a': 1}, {'b': 2}]), dynamic([{'a': 1}]))", """[{"a":1}]""")]
    [TestCase("set_intersect(dynamic({'a': 1}), dynamic([1]))", "[]", Description = "a property bag is not an array")]
    [Description("set_intersect - returns the distinct elements that are in every array (in any order)")]
    public void SetIntersect_ReturnsCommonElements(string expression, string expectedJsonArray)
    {
        AssertDynamicJsonUnordered(OpusTestUtils.EvaluateScalar(expression), expectedJsonArray);
    }

    [TestCase("set_difference(dynamic([1, 2, 2, 3]), dynamic([2]), dynamic([3, 5]))", "[1]")]
    [TestCase("set_difference(dynamic([1, 2]), dynamic([1, 2]))", "[]")]
    [TestCase("set_difference(dynamic(['a', 'A']), dynamic(['a']))", """["A"]""")]
    [TestCase("set_difference(dynamic([1]), dynamic([1.0]))", "[1]", Description = "1.0 (real) doesn't remove 1 (long)")]
    [TestCase("set_difference(dynamic([1, null]), dynamic([]))", "[1]", Description = "null elements are skipped")]
    [TestCase("set_difference(dynamic([[1], [1]]), dynamic([]))", "[[1]]", Description = "the result is distinct")]
    [TestCase("set_difference(dynamic([{'a': 1}, {'b': 2}]), dynamic([{'a': 1}]))", """[{"b":2}]""")]
    [TestCase("set_difference(dynamic([1, 2]), dynamic(null))", "[1,2]", Description = "a null array removes nothing")]
    [TestCase("set_difference(dynamic([1, 2]), dynamic('str'))", "[1,2]")]
    [Description("set_difference - returns the distinct elements of the first array that are not in the other arrays (in any order)")]
    public void SetDifference_ReturnsElementsOnlyInFirstArray(string expression, string expectedJsonArray)
    {
        AssertDynamicJsonUnordered(OpusTestUtils.EvaluateScalar(expression), expectedJsonArray);
    }

    [TestCase("set_intersect(dynamic(null), dynamic([2]))")]
    [TestCase("set_intersect(dynamic([1, 2]), dynamic(null))")]
    [TestCase("set_difference(dynamic(null), dynamic([2]))")]
    [Description("set_intersect / set_difference - a null array (the first one for set_difference) returns null")]
    public void SetIntersectAndDifference_NullArgument_ReturnsNull(string expression)
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(new KustoDatabase(), $"print r = {expression}, nullResult = isnull({expression})");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "r"), "null");
        OpusTestUtils.AssertTypedValue(Col(rows[0], "nullResult"), true);
    }

    [Test]
    [Description("set_intersect / set_difference / set_union - per row of a dynamic column (in any order)")]
    public void SetFunctions_DynamicColumn_ReturnsSetPerRow()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            CreateArraysDatabase(),
            "Arrays | project id, common = set_intersect(arr, dynamic([1, 'a'])), withoutThree = set_difference(arr, dynamic([3])), "
            + "withoutA = set_difference(arr, dynamic(['a'])), distinctItems = set_union(arr, dynamic(null)), "
            + "unionSorted = array_sort_asc(set_union(arr, dynamic([2])))");

        AssertDynamicJsonUnorderedById(SingleById(rows, "common"),
            (1, "[1]"), (2, "[]"), (3, "null"), (4, """["a"]"""), (5, "[]"), (6, "[]"), (7, "[1]"));
        AssertDynamicJsonUnorderedById(SingleById(rows, "withoutThree"),
            (1, "[1,2]"), (2, "[]"), (3, "null"), (4, """["b","a"]"""), (5, "[]"), (6, "[]"), (7, "[1,2.5]"));
        AssertDynamicJsonUnorderedById(SingleById(rows, "withoutA"),
            (1, "[3,1,2]"), (2, "[]"), (3, "null"), (4, """["b"]"""), (5, "[]"), (6, "[]"), (7, "[1,2.5]"));
        AssertDynamicJsonUnorderedById(SingleById(rows, "distinctItems"),
            (1, "[3,1,2]"), (2, "[]"), (3, "null"), (4, """["b","a"]"""), (5, "[]"), (6, "[]"), (7, "[1,2.5]"));
        AssertDynamicJsonById(SingleById(rows, "unionSorted"),
            (1, "[1,2,3]"), (2, "[2]"), (3, "[2]"), (4, """[2,"a","b"]"""), (5, "[2]"), (6, "[2]"), (7, "[1,2,2.5]"));
    }

    // set_has_element

    [TestCase("set_has_element(dynamic([1, 2]), 1)", true)]
    [TestCase("set_has_element(dynamic([1, 2]), int(2))", true, Description = "int and long values are equal")]
    [TestCase("set_has_element(dynamic([1.0]), 1)", true, Description = "a real element equal to the long value matches")]
    [TestCase("set_has_element(dynamic([1.5]), 1.5)", true)]
    [TestCase("set_has_element(pack_array(datetime(2020-01-01)), datetime(2020-01-01))", true)]
    [TestCase("set_has_element(dynamic([1, 2]), '1')", false, Description = "strings and numbers are not converted")]
    [TestCase("set_has_element(dynamic(['1']), 1)", false)]
    [TestCase("set_has_element(dynamic(['a']), 'A')", false, Description = "strings are case-sensitive")]
    [TestCase("set_has_element(dynamic(['hello world']), 'hello')", false, Description = "whole elements, not terms")]
    [TestCase("set_has_element(dynamic([[1], 2]), 1)", false, Description = "nested arrays are not flattened")]
    [TestCase("set_has_element(dynamic([]), 1)", false)]
    [TestCase("set_has_element(dynamic({'a': 2}), 2)", false, Description = "a property bag is not an array")]
    [TestCase("set_has_element(dynamic(null), 2)", null)]
    [Description("set_has_element - returns whether the array contains the value")]
    public void SetHasElement_ReturnsWhetherArrayContainsValue(string expression, bool? expected)
    {
        AssertTypedOrNull(OpusTestUtils.EvaluateScalar(expression), expected);
    }

    [Test]
    [Description("set_has_element - the value must be a scalar, a dynamic value fails")]
    public void SetHasElement_DynamicValue_Fails()
    {
        OpusTestUtils.AssertQueryFails(new KustoDatabase(), "print r = set_has_element(dynamic([1, 2]), dynamic(1))");
    }

    // result types and combinations

    [Test]
    [Description("getschema - array_length and array_index_of return long, array_sum returns real, set_has_element returns bool, "
                 + "the other functions return dynamic")]
    public void ArrayAndSetFunctions_GetSchema_ReturnsResultTypes()
    {
        var actual = ExecuteAndGetSchema(
            new KustoDatabase(),
            "print len = array_length(dynamic([1])), total = array_sum(dynamic([1])), idx = array_index_of(dynamic([1]), 1), "
            + "hasOne = set_has_element(dynamic([1]), 1), sliced = array_slice(dynamic([1]), 0, 0), "
            + "concatenated = array_concat(dynamic([1]), dynamic([2])), reversed = array_reverse(dynamic([1])), "
            + "sortedValues = array_sort_asc(dynamic([1])), chosen = array_iff(dynamic([true]), 1, 0), "
            + "merged = set_union(dynamic([1]), dynamic([2])), common = set_intersect(dynamic([1]), dynamic([1])), "
            + "remaining = set_difference(dynamic([1]), dynamic([2])) | getschema");

        Assert.That(actual, Is.EqualTo(new[]
        {
            "len:long", "total:real", "idx:long", "hasOne:bool", "sliced:dynamic", "concatenated:dynamic", "reversed:dynamic",
            "sortedValues:dynamic", "chosen:dynamic", "merged:dynamic", "common:dynamic", "remaining:dynamic"
        }));
    }

    [TestCase("array_sort_asc(set_intersect(dynamic([3, 1, 2, 2]), dynamic([2, 3, 4])))", "[2,3]")]
    [TestCase("array_length(set_union(dynamic([1, 2, 2]), dynamic([2, 3])))", "3")]
    [TestCase("array_sum(array_slice(dynamic([1, 2, 3, 4]), 1, 2))", "5.0")]
    [TestCase("set_has_element(bag_keys(dynamic({'a': 1, 'b': 2})), 'a')", "true")]
    [TestCase("array_reverse(array_sort_asc(dynamic([3, 1, 2])))", "[3,2,1]")]
    [TestCase("array_index_of(array_sort_desc(dynamic([3, 1, 2])), 1)", "2")]
    [TestCase("array_concat(array_slice(dynamic([5, 6, 7]), 0, 0), array_slice(dynamic([5, 6, 7]), -1, -1))", "[5,7]")]
    [TestCase("array_length(array_concat(dynamic([1]), pack_array(2, 3)))", "3")]
    [Description("array and set functions - results of one function can be used as arguments of another one")]
    public void ArrayAndSetFunctions_Combined_ReturnsExpectedValue(string expression, string expectedJson)
    {
        AssertDynamicJson(OpusTestUtils.EvaluateScalar(expression), expectedJson);
    }

    // immutability

    [Test]
    [Description("immutability - sorting, reversing and slicing return new arrays, the array of a let statement isn't changed")]
    public void ArrayFunctions_LetArray_IsNotModified()
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(
            new KustoDatabase(),
            """
            let a = dynamic([3, 1, 2]);
            print sortedAsc = array_sort_asc(a), reversed = array_reverse(a), sliced = array_slice(a, 0, 0), original = a
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertDynamicJson(Col(rows[0], "sortedAsc"), "[1,2,3]");
        AssertDynamicJson(Col(rows[0], "reversed"), "[2,1,3]");
        AssertDynamicJson(Col(rows[0], "sliced"), "[3]");
        AssertDynamicJson(Col(rows[0], "original"), "[3,1,2]");
    }

    [Test]
    [Description("immutability - array functions applied to a dynamic column don't change the column, neither in the query nor in the table")]
    public void ArrayFunctions_DynamicColumn_IsNotModified()
    {
        var kustoDatabase = CreateArraysDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Arrays | extend sortedAsc = array_sort_asc(arr), rev = array_reverse(arr), concatenated = array_concat(arr, dynamic([9])), "
            + "merged = set_union(arr, dynamic([0])) | project id, arr, sortedAsc");
        AssertOriginalArrays(SingleById(rows, "arr"));
        AssertDynamicJson(SingleById(rows, "sortedAsc")[1], "[1,2,3]");

        var again = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Arrays | project id, arr");
        AssertOriginalArrays(SingleById(again, "arr"));
    }

    private static void AssertOriginalArrays(IReadOnlyDictionary<long, object?> actual)
    {
        AssertDynamicJsonById(actual,
            (1, "[3,1,2]"),
            (2, "[]"),
            (3, "null"),
            (4, """["b","a"]"""),
            (5, """{"a":1}"""),
            (6, "\"str\""),
            (7, "[1,null,2.5]"));
    }

    private static KustoDatabase CreateArraysDatabase()
    {
        var table = OpusTestUtils.NewTable("Arrays")
            .WithColumn<long>("id")
            .WithColumn<object>("arr")
            .WithRow(1L, Arr(3L, 1L, 2L))
            .WithRow(2L, Arr())
            .WithRow(3L, null)
            .WithRow(4L, Arr("b", "a"))
            .WithRow(5L, Bag(("a", 1L)))
            .WithRow(6L, "str")
            .WithRow(7L, Arr(1L, null, 2.5))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateItemsDatabase()
    {
        var table = OpusTestUtils.NewTable("Items")
            .WithColumn<long>("id")
            .WithColumn<object>("d")
            .WithRow(1L, Bag(("tag", "a"), ("n", 1L)))
            .WithRow(2L, Bag(("tag", "b"), ("n", 2L)))
            .WithRow(3L, Bag(("tag", "z"), ("n", 3L)))
            .WithRow(4L, Bag(("n", "1")))
            .WithRow(5L, null)
            .WithRow(6L, Bag(("tag", Arr("a"))))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateLabelsDatabase()
    {
        var labels = OpusTestUtils.NewTable("Labels")
            .WithColumn<long>("id")
            .WithColumn<string>("tag")
            .WithRow(1L, "a")
            .WithRow(2L, "B")
            .WithRow(3L, "c")
            .WithRow(4L, "")
            .WithRow(5L, "b")
            .WithRow(6L, "x")
            .Build();
        var allowed = OpusTestUtils.NewTable("Allowed")
            .WithColumn<long>("id")
            .WithRow(1L)
            .WithRow(3L)
            .WithRow(5L)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(labels);
        kustoDatabase.AddTable(allowed);
        return kustoDatabase;
    }
}
