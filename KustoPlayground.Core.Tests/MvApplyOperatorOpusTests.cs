using static KustoPlayground.Core.Tests.DynamicOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// mv-apply - https://learn.microsoft.com/en-us/kusto/query/mv-apply-operator.
/// <list type="bullet">
/// <item>every input row is expanded like mv-expand into a subtable (the elements keep the array order) and the
/// subquery runs separately on the subtable of every input row: <c>top 2</c> returns up to two elements of every input
/// row (not two rows overall) and <c>summarize</c> without by returns exactly one row per input row, also for an empty
/// array (count() and sum() are 0, min() and max() are null, make_list() is []);</item>
/// <item>the results of all subqueries are combined: an input row whose subquery returns no rows disappears and
/// identical input rows are processed separately;</item>
/// <item>the subquery can use the columns of the input row, the input columns which the subquery doesn't return are
/// added back with the values of the input row and a column returned by the subquery wins;</item>
/// <item>the result has the input columns in their order (a column expanded without a name only when the subquery
/// returns it) followed by the new columns of the subquery;</item>
/// <item>the expansion works like mv-expand: null and scalars are one element, [] and {} have no elements, a property
/// bag is expanded into single-property bags, <c>to typeof(T)</c> casts the elements, with_itemindex adds a 0-based
/// index, <c>limit N</c> expands at most N elements of every input row and several arrays are aligned by index and
/// padded with null;</item>
/// <item>bagexpansion / kind aren't supported, only dynamic expressions can be expanded and the subquery can't group
/// by a dynamic value.</item>
/// </list>
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class MvApplyOperatorOpusTests
{
    // Per-row subqueries

    [Test]
    [Description("mv-apply ... on (top 2 ...) - returns up to two elements of every input row, not two rows overall")]
    public void MvApply_TopTwo_ReturnsUpToTwoElementsPerInputRow()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr to typeof(long) on (top 2 by x desc) | project id, x");

        Assert.That(rows, Has.Count.EqualTo(6));
        var values = GroupById(rows, "x");
        Assert.That(values.Keys, Is.EquivalentTo(new[] { 1L, 2L, 4L, 5L }), "the empty array of id 3 produces no rows");
        AssertTypedValues<long>(values[1], 9, 5);
        AssertTypedValues<long>(values[2], 7);
        AssertTypedValues<long>(values[4], (long?)null);
        AssertTypedValues<long>(values[5], 8, 2);
    }

    [Test]
    [Description("mv-expand | top 2 returns two rows overall, mv-apply ... on (top 2 ...) two rows of every input row")]
    public void MvApply_TopTwo_DiffersFromMvExpandFollowedByTop()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var expandRows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-expand x = arr to typeof(long) | top 2 by x desc | project id, x");
        var applyRows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr to typeof(long) on (top 2 by x desc) | project id, x");

        AssertTypedById<long>(SingleById(expandRows, "x"), (1, 9L), (5, 8L));
        Assert.That(applyRows, Has.Count.EqualTo(6));
    }

    [Test]
    [Description("mv-apply arr to typeof(long) on (top 1 by arr desc) - the expanded column gets the largest element "
                 + "of every input row, the other columns are kept")]
    public void MvApply_ColumnWithoutNameTopOne_ReturnsLargestElementOfEveryRow()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply arr to typeof(long) on (top 1 by arr desc)");

        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows[0].Keys, Is.EquivalentTo(new[] { "id", "arr", "s" }));
        AssertTypedById<long>(SingleById(rows, "arr"), (1, 9L), (2, 7L), (4, null), (5, 8L));
        AssertStringsById(SingleById(rows, "s"), (1, "a"), (2, "b"), (4, "d"), (5, "e"));
    }

    [Test]
    [Description("mv-apply x = arr - a named expansion adds the column x, the source column and the other columns of "
                 + "the input row are repeated in every result row")]
    public void MvApply_NamedExpansion_KeepsSourceColumnAndOtherColumns()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr to typeof(long) on (top 2 by x desc)");

        Assert.That(rows, Has.Count.EqualTo(6));
        Assert.That(rows[0].Keys, Is.EquivalentTo(new[] { "id", "arr", "s", "x" }));

        var arrays = GroupById(rows, "arr");
        AssertDynamicJson(arrays[1], "[[5,1,9,3],[5,1,9,3]]", "arr of id 1");
        AssertDynamicJson(arrays[2], "[[7]]", "arr of id 2");
        AssertDynamicJson(arrays[4], "[null]", "arr of id 4");
        AssertDynamicJson(arrays[5], "[[2,8],[2,8]]", "arr of id 5");

        var texts = GroupById(rows, "s");
        Assert.That(texts[1], Is.EqualTo(new object?[] { "a", "a" }));
        Assert.That(texts[2], Is.EqualTo(new object?[] { "b" }));
        Assert.That(texts[4], Is.EqualTo(new object?[] { "d" }));
        Assert.That(texts[5], Is.EqualTo(new object?[] { "e", "e" }));
    }

    [Test]
    [Description("mv-apply x = arr on (take 1) - without to typeof() the elements are dynamic values")]
    public void MvApply_WithoutCast_ElementsAreDynamic()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr on (take 1) | project id, x, t = gettype(x)");

        AssertDynamicJsonById(SingleById(rows, "x"), (1, "5"), (2, "7"), (4, "null"), (5, "2"));
        AssertStringsById(SingleById(rows, "t"), (1, "long"), (2, "long"), (4, "null"), (5, "long"));
    }

    [TestCase("x > 6", new[] { "1:9", "2:7", "5:8" })]
    [TestCase("x in (1, 7, 8)", new[] { "1:1", "2:7", "5:8" })]
    [TestCase("x in (dynamic([1, 7, 8]))", new[] { "1:1", "2:7", "5:8" },
        Description = "a dynamic array on the right side of in")]
    [TestCase("x > id", new[] { "1:5", "1:9", "1:3", "2:7", "5:8" },
        Description = "the subquery compares the elements with a column of the input row")]
    [Description("mv-apply ... on (where ...) - filters the elements of every input row, a row without matching "
                 + "elements disappears")]
    public void MvApply_Where_FiltersElementsOfEveryRow(string condition, string[] expected)
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            $"Metrics | mv-apply x = arr to typeof(long) on (where {condition}) | project id, x");

        Assert.That(rows.Select(row => Col(row, "x")), Has.All.TypeOf<long>());
        Assert.That(rows.Select(row => $"{Col(row, "id")}:{Col(row, "x")}"), Is.EquivalentTo(expected));
    }

    [Test]
    [Description("mv-apply ... on (where ...) - no rows when the subquery rejects every element")]
    public void MvApply_WhereRejectingEveryElement_ReturnsNoRows()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr to typeof(long) on (where x > 100)");

        Assert.That(rows, Is.Empty);
    }

    [Test]
    [Description("mv-apply ... on (extend ...) - the subquery reads the columns of the input row, the elements keep "
                 + "the array order")]
    public void MvApply_ExtendWithInputColumn_CombinesElementWithInputRowValue()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr to typeof(long) on (extend y = strcat(s, '-', tostring(x))) | project id, y");

        var values = GroupById(rows, "y");
        Assert.That(values.Keys, Is.EquivalentTo(new[] { 1L, 2L, 4L, 5L }));
        Assert.That(values[1], Is.EqualTo(new object?[] { "a-5", "a-1", "a-9", "a-3" }));
        Assert.That(values[2], Is.EqualTo(new object?[] { "b-7" }));
        Assert.That(values[4], Is.EqualTo(new object?[] { "d-" }), "the null element of id 4");
        Assert.That(values[5], Is.EqualTo(new object?[] { "e-2", "e-8" }));
    }

    [TestCase("summarize r = sum(v)", new[] { "x:3", "x:3", "y:5" })]
    [TestCase("top 1 by v desc | project r = v", new[] { "x:2", "x:2", "y:5" })]
    [Description("mv-apply - identical input rows are processed separately, their results aren't merged")]
    public void MvApply_IdenticalInputRows_AreProcessedSeparately(string subquery, string[] expected)
    {
        var kustoDatabase = CreateTwinsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            $"Twins | mv-apply v = arr to typeof(long) on ({subquery}) | project s, r");

        Assert.That(rows.Select(row => Col(row, "r")), Has.All.TypeOf<long>());
        Assert.That(rows.Select(row => $"{Col(row, "s")}:{Col(row, "r")}"), Is.EquivalentTo(expected));
    }

    // Aggregation per input row

    [Test]
    [Description("mv-apply ... on (summarize ...) - summarize without by returns one row per input row, also for an "
                 + "empty array (count() and sum() are 0, min() and max() are null) and for null (one null element)")]
    public void MvApply_SummarizeWithoutBy_ReturnsOneRowPerInputRow()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr to typeof(long) on (summarize total = sum(x), c = count(), mn = min(x), mx = max(x))");

        Assert.That(rows, Has.Count.EqualTo(5));
        AssertTypedById<long>(SingleById(rows, "total"), (1, 18L), (2, 7L), (3, 0L), (4, 0L), (5, 10L));
        AssertTypedById<long>(SingleById(rows, "c"), (1, 4L), (2, 1L), (3, 0L), (4, 1L), (5, 2L));
        AssertTypedById<long>(SingleById(rows, "mn"), (1, 1L), (2, 7L), (3, null), (4, null), (5, 2L));
        AssertTypedById<long>(SingleById(rows, "mx"), (1, 9L), (2, 7L), (3, null), (4, null), (5, 8L));
    }

    [Test]
    [Description("mv-apply ... on (summarize ...) - the input columns which summarize doesn't return are added back")]
    public void MvApply_Summarize_AddsBackInputColumns()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr to typeof(long) on (summarize total = sum(x))");

        Assert.That(rows, Has.Count.EqualTo(5));
        Assert.That(rows[0].Keys, Is.EquivalentTo(new[] { "id", "arr", "s", "total" }));
        AssertDynamicJsonById(SingleById(rows, "arr"), (1, "[5,1,9,3]"), (2, "[7]"), (3, "[]"), (4, "null"), (5, "[2,8]"));
        AssertStringsById(SingleById(rows, "s"), (1, "a"), (2, "b"), (3, "c"), (4, "d"), (5, "e"));
    }

    [Test]
    [Description("mv-apply arr on (count) - counts the elements of every input row, the column expanded without "
                 + "a name isn't returned because count doesn't return it")]
    public void MvApply_Count_CountsElementsOfEveryRow()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Metrics | mv-apply arr on (count)");

        Assert.That(rows, Has.Count.EqualTo(5));
        Assert.That(rows[0].Keys, Is.EquivalentTo(new[] { "id", "s", "Count" }));
        AssertTypedById<long>(SingleById(rows, "Count"), (1, 4L), (2, 1L), (3, 0L), (4, 1L), (5, 2L));
        AssertStringsById(SingleById(rows, "s"), (1, "a"), (2, "b"), (3, "c"), (4, "d"), (5, "e"));
    }

    [Test]
    [Description("mv-apply ... on (order by ... | summarize make_list(...)) - sorts the elements of every array")]
    public void MvApply_OrderByThenMakeList_SortsEveryArray()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr to typeof(long) on (order by x asc | summarize sorted = make_list(x))");

        AssertDynamicJsonById(SingleById(rows, "sorted"),
            (1, "[1,3,5,9]"), (2, "[7]"), (3, "[]"), (4, "[]"), (5, "[2,8]"));
    }

    [Test]
    [Description("mv-apply ... on (summarize ... by ...) - one row per group of every input row, an empty array "
                 + "produces no rows because summarize with by returns no rows for an empty input")]
    public void MvApply_SummarizeBy_ReturnsOneRowPerGroupOfEveryRow()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr to typeof(long) on (summarize c = count() by big = x > 4)");

        Assert.That(rows.Select(row => Col(row, "c")), Has.All.TypeOf<long>());
        Assert.That(rows.Select(row => Col(row, "big")).Where(value => value is not null), Has.All.TypeOf<bool>());
        Assert.That(rows.Select(row => $"{Col(row, "id")}:{Col(row, "big") ?? "null"}:{Col(row, "c")}"),
            Is.EquivalentTo(new[] { "1:True:2", "1:False:2", "2:True:1", "4:null:1", "5:False:1", "5:True:1" }));
    }

    [Test]
    [Description("mv-apply ... on (top 2 by x | summarize sum(x)) - the sum of the two largest elements of every array, "
                 + "top sorts in descending order by default (an example of the documentation)")]
    public void MvApply_TopTwoThenSum_SumsTwoLargestElementsOfEveryRow()
    {
        var kustoDatabase = CreateMetricsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Metrics | mv-apply x = arr to typeof(long) on (top 2 by x | summarize sumOfTopTwo = sum(x))");

        AssertTypedById<long>(SingleById(rows, "sumOfTopTwo"), (1, 14L), (2, 7L), (3, 0L), (4, 0L), (5, 10L));
    }

    [Test]
    [Description("mv-apply ... on (distinct x | count) - counts the distinct elements of every array")]
    public void MvApply_DistinctThenCount_CountsDistinctElementsOfEveryRow()
    {
        var kustoDatabase = CreateDupsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Dups | mv-apply x = arr to typeof(long) on (distinct x | count)");

        AssertTypedById<long>(SingleById(rows, "Count"), (1, 3L), (2, 1L));
    }

    [Test]
    [Description("mv-apply ... on (summarize make_set(...), make_list(...)) - make_set removes the duplicates of every "
                 + "array, make_list keeps them")]
    public void MvApply_MakeSetAndMakeList_CollectElementsOfEveryRow()
    {
        var kustoDatabase = CreateDupsDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Dups | mv-apply x = arr to typeof(long) on (summarize uniq = make_set(x), allValues = make_list(x))");

        AssertDynamicJsonUnorderedById(SingleById(rows, "uniq"), (1, "[1,2,3]"), (2, "[4]"));
        AssertDynamicJsonById(SingleById(rows, "allValues"), (1, "[3,1,3,2,1]"), (2, "[4,4]"));
    }

    [Test]
    [Description("mv-apply v = pack_array(col1, col2, col3) - aggregates several columns of every row")]
    public void MvApply_PackArrayOfColumns_AggregatesColumnsOfEveryRow()
    {
        var kustoDatabase = CreateScoresDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Scores | mv-apply v = pack_array(math, physics, chem) to typeof(long) "
            + "on (summarize best = max(v), worst = min(v), avgScore = avg(v))");

        var best = SingleByKey(rows, "student", "best");
        var worst = SingleByKey(rows, "student", "worst");
        var averages = SingleByKey(rows, "student", "avgScore");
        Assert.That(best.Keys, Is.EquivalentTo(new[] { "ann", "bob", "cid" }));
        OpusTestUtils.AssertTypedValue(best["ann"], 90L);
        OpusTestUtils.AssertTypedValue(best["bob"], 88L);
        OpusTestUtils.AssertTypedValue(best["cid"], 95L);
        OpusTestUtils.AssertTypedValue(worst["ann"], 75L);
        OpusTestUtils.AssertTypedValue(worst["bob"], 60L);
        OpusTestUtils.AssertTypedValue(worst["cid"], 77L);
        Assert.That(averages["ann"], Is.TypeOf<double>().And.EqualTo(247.0 / 3).Within(1e-9));
        Assert.That(averages["bob"], Is.TypeOf<double>().And.EqualTo(218.0 / 3).Within(1e-9));
        Assert.That(averages["cid"], Is.TypeOf<double>().And.EqualTo(83.0).Within(1e-9));
    }

    [Test]
    [Description("mv-apply inside mv-apply - sums the nested arrays of every row, an empty nested array is one "
                 + "piece with the sum 0")]
    public void MvApply_Nested_SumsNestedArraysOfEveryRow()
    {
        var kustoDatabase = CreateMatrixDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase,
            "Matrix | mv-apply piece = arr on (mv-apply y = piece to typeof(long) on (summarize pieceSum = sum(y)) "
            + "| summarize total = sum(pieceSum), pieces = count())");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].Keys, Is.EquivalentTo(new[] { "id", "arr", "total", "pieces" }));
        AssertTypedById<long>(SingleById(rows, "total"), (1, 6L), (2, 60L));
        AssertTypedById<long>(SingleById(rows, "pieces"), (1, 2L), (2, 3L));
    }

    [TestCase("dynamic(7)", 1L, "[7]", Description = "a scalar is one element")]
    [TestCase("dynamic('str')", 1L, """["str"]""", Description = "a string is one element, it isn't parsed")]
    [TestCase("dynamic(null)", 1L, "[]", Description = "null is one null element, make_list skips it")]
    [TestCase("dynamic({})", 0L, "[]", Description = "an empty property bag has no elements")]
    [TestCase("dynamic([[1,2],[3]])", 2L, "[1,2,3]",
        Description = "the elements are arrays, make_list adds their elements")]
    [Description("print | mv-apply - expands scalars, null, an empty bag and nested arrays")]
    public void MvApply_AfterPrint_ExpandsAnyDynamicValue(string value, long expectedCount, string expectedList)
    {
        var rows = OpusTestUtils.ExecuteAndGetRows(new KustoDatabase(),
            $"print v = {value} | mv-apply x = v on (summarize c = count(), xs = make_list(x))");

        Assert.That(rows, Has.Count.EqualTo(1));
        OpusTestUtils.AssertTypedValue(Col(rows[0], "c"), expectedCount);
        AssertDynamicJson(Col(rows[0], "xs"), expectedList);
    }

    private static KustoDatabase CreateMetricsDatabase()
    {
        var table = OpusTestUtils.NewTable("Metrics")
            .WithColumn<long>("id")
            .WithColumn<object>("arr")
            .WithColumn<string>("s")
            .WithRow(1L, Arr(5L, 1L, 9L, 3L), "a")
            .WithRow(2L, Arr(7L), "b")
            .WithRow(3L, Arr(), "c")
            .WithRow(4L, null, "d")
            .WithRow(5L, Arr(2L, 8L), "e")
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateTwinsDatabase()
    {
        var table = OpusTestUtils.NewTable("Twins")
            .WithColumn<string>("s")
            .WithColumn<object>("arr")
            .WithRow("x", Arr(1L, 2L))
            .WithRow("x", Arr(1L, 2L))
            .WithRow("y", Arr(5L))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateDupsDatabase()
    {
        var table = OpusTestUtils.NewTable("Dups")
            .WithColumn<long>("id")
            .WithColumn<object>("arr")
            .WithRow(1L, Arr(3L, 1L, 3L, 2L, 1L))
            .WithRow(2L, Arr(4L, 4L))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateScoresDatabase()
    {
        var table = OpusTestUtils.NewTable("Scores")
            .WithColumn<string>("student")
            .WithColumn<long>("math")
            .WithColumn<long>("physics")
            .WithColumn<long>("chem")
            .WithRow("ann", 90L, 75L, 82L)
            .WithRow("bob", 60L, 88L, 70L)
            .WithRow("cid", 77L, 77L, 95L)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }

    private static KustoDatabase CreateMatrixDatabase()
    {
        var table = OpusTestUtils.NewTable("Matrix")
            .WithColumn<long>("id")
            .WithColumn<object>("arr")
            .WithRow(1L, Json("[[1,2],[3]]"))
            .WithRow(2L, Json("[[10],[],[20,30]]"))
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);
        return kustoDatabase;
    }
}
