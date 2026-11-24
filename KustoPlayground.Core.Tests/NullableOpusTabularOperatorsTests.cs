using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Nullable columns in the tabular operators project, extend, take, count, distinct, sort and print,
/// as in Azure Data Explorer:
/// - project, extend and take keep the rows with nulls and the null values as they are;
/// - count counts the rows with nulls; a 'where' before it drops the rows whose predicate is null;
/// - distinct treats all the nulls of a column as one value, different from every non-null value;
/// - sort puts the nulls first for 'asc' and last for 'desc' (the default direction), unless 'nulls first' or
///   'nulls last' is given; a NaN of a real column is sorted next to the nulls;
/// - print of a typed null literal (long(null), datetime(null), ...) is null.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class NullableOpusTabularOperatorsTests
{
    // Numbers(Id: int, X: long?) = 1: 1, 2: 3, 3: 5, 4: null, 5: null
    private static Table BuildNumbersTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Numbers")
            .WithColumn<int>("Id")
            .WithColumn<long>("X")
            .WithRow(1, 1L)
            .WithRow(2, 3L)
            .WithRow(3, 5L)
            .WithRow(4, null)
            .WithRow(5, null)
            .Build();
    }

    // Tuples(X: long?, S: string, B: bool?) =
    // (1, "a", true), (null, "", null), (null, "", null), (1, "a", true), (null, "b", null), (1, "", null), (null, "", false)
    private static Table BuildTuplesTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Tuples")
            .WithColumn<long>("X")
            .WithColumn<string>("S")
            .WithColumn<bool>("B")
            .WithRow(1L, "a", true)
            .WithRow(null, "", null)
            .WithRow(null, "", null)
            .WithRow(1L, "a", true)
            .WithRow(null, "b", null)
            .WithRow(1L, "", null)
            .WithRow(null, "", false)
            .Build();
    }

    // NullPairs(A: long?, B: long?) = (1, null), (1, null), (null, null), (null, null), (null, 1)
    private static Table BuildNullPairsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("NullPairs")
            .WithColumn<long>("A")
            .WithColumn<long>("B")
            .WithRow(1L, null)
            .WithRow(1L, null)
            .WithRow(null, null)
            .WithRow(null, null)
            .WithRow(null, 1L)
            .Build();
    }

    // Reals(Id: int, R: real?) = 1: 1.5, 2: null, 3: NaN, 4: -2.0
    private static Table BuildRealsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Reals")
            .WithColumn<int>("Id")
            .WithColumn<double>("R")
            .WithRow(1, 1.5)
            .WithRow(2, null)
            .WithRow(3, double.NaN)
            .WithRow(4, -2.0)
            .Build();
    }

    // MultiKey(Id: int, A: long?, B: long?) = 1: (1, 10), 2: (null, 20), 3: (1, null), 4: (null, null), 5: (2, 5), 6: (1, 30)
    private static Table BuildMultiKeyTable()
    {
        return new SummarizeOperatorOpusTableBuilder("MultiKey")
            .WithColumn<int>("Id")
            .WithColumn<long>("A")
            .WithColumn<long>("B")
            .WithRow(1, 1L, 10L)
            .WithRow(2, null, 20L)
            .WithRow(3, 1L, null)
            .WithRow(4, null, null)
            .WithRow(5, 2L, 5L)
            .WithRow(6, 1L, 30L)
            .Build();
    }

    // Words(Id: int, S: string?) = 1: "b", 2: null, 3: "c", 4: "", 5: "a"
    private static Table BuildWordsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Words")
            .WithColumn<int>("Id")
            .WithColumn<string>("S")
            .WithRow(1, "b")
            .WithRow(2, null)
            .WithRow(3, "c")
            .WithRow(4, "")
            .WithRow(5, "a")
            .Build();
    }

    // Typed(Id: int, V: T?) = 1: high, 2: null, 3: low
    private static KustoDatabase CreateTypedDatabase<T>(T low, T high)
        where T : notnull
    {
        return CreateDatabase(new SummarizeOperatorOpusTableBuilder("Typed")
            .WithColumn<int>("Id")
            .WithColumn<T>("V")
            .WithRow(1, high)
            .WithRow(2, null)
            .WithRow(3, low)
            .Build());
    }

    // On Typed(1: high, 2: null, 3: low): the null is first for asc and last for desc (the default),
    // unless 'nulls first' / 'nulls last' says otherwise
    private static void AssertSortWithNull(KustoDatabase kustoDatabase)
    {
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Typed | sort by V asc"), Is.EqualTo(new[] { 2, 3, 1 }),
            "sort by V asc");
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Typed | sort by V desc"), Is.EqualTo(new[] { 1, 3, 2 }),
            "sort by V desc");
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Typed | sort by V"), Is.EqualTo(new[] { 1, 3, 2 }),
            "sort by V (desc by default)");
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Typed | sort by V asc nulls last"),
            Is.EqualTo(new[] { 3, 1, 2 }), "sort by V asc nulls last");
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Typed | sort by V desc nulls first"),
            Is.EqualTo(new[] { 2, 1, 3 }), "sort by V desc nulls first");
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Typed | order by V asc"), Is.EqualTo(new[] { 2, 3, 1 }),
            "order by V asc");
    }

    #region project / extend

    [TestCase("Numbers | project Id, X", "X")]
    [TestCase("Numbers | project Id, Y = X", "Y")]
    [TestCase("Numbers | extend Y = X", "Y")]
    [TestCase("Numbers | project X, Id | project Id, Copy = X", "Copy")]
    [TestCase("Numbers | extend Y = X | project Id, Y", "Y")]
    [Description("project / extend keep the rows with nulls and copy the null values as they are")]
    public void ProjectAndExtend_KeepNullValues(string query, string columnName)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetValuesById<long>(kustoDatabase, query, columnName);

        Assert.That(actual, Is.EqualTo(ExpectedById<long>(1, 3, 5, null, null)));
    }

    [Test]
    [Description("project with an expression over a nullable column gives null for the null rows")]
    public void Project_ExpressionOverNullableColumn()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetNumbersById(kustoDatabase, "Numbers | project Id, Y = X + 1", "Y");

        Assert.That(actual, Is.EqualTo(ExpectedById<double>(2, 4, 6, null, null)));
    }

    [Test]
    [Description("extend with typed null literals adds null columns to every row")]
    public void Extend_TypedNullLiterals()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());
        string[] nullColumns = ["NI", "NL", "NR", "NDc", "NB", "ND", "NT", "NG"];

        var rows = ExecuteAndGetRows(kustoDatabase,
            "Numbers | extend NI = int(null), NL = long(null), NR = real(null), NDc = decimal(null), " +
            "NB = bool(null), ND = datetime(null), NT = timespan(null), NG = guid(null)");

        Assert.That(rows, Has.Count.EqualTo(5));
        foreach (var row in rows)
        {
            foreach (string columnName in nullColumns)
            {
                AssertIsNull(row, columnName);
            }
        }
    }

    [TestCase("Big", new[] { 2, 3 })]
    [TestCase("not(Big)", new[] { 1 })]
    [TestCase("isnull(Big)", new[] { 4, 5 })]
    [TestCase("isnotnull(Big)", new[] { 1, 2, 3 })]
    [TestCase("Big == true", new[] { 2, 3 })]
    [TestCase("Big != true", new[] { 1, 4, 5 })]
    [TestCase("Big == false", new[] { 1 })]
    [TestCase("Big != false", new[] { 2, 3, 4, 5 })]
    [TestCase("Big == Big", new[] { 1, 2, 3 })]
    [TestCase("Big or isnull(Big)", new[] { 2, 3, 4, 5 })]
    [Description("A bool column computed by extend (Big = X > 2) is null for a null X; 'where' filters it with the " +
                 "null rules: 'where Big' and 'where not(Big)' both drop the nulls, while Big != true keeps them")]
    public void Extend_ComputedBoolWithNulls_ThenWhere(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Numbers | extend Big = X > 2 | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [Test]
    [Description("Chained extend: every expression that uses a null value is null, and isnull() of it is true")]
    public void Extend_ChainedExpressionsOverNulls()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());
        const string query = "Numbers | extend Y = X * 2 | extend Z = Y + 1, Big = Y > 5 | extend ZIsNull = isnull(Z)";

        Assert.That(ExecuteAndGetNumbersById(kustoDatabase, query, "Z"),
            Is.EqualTo(ExpectedById<double>(3, 7, 11, null, null)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Big"),
            Is.EqualTo(ExpectedById<bool>(false, true, true, null, null)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "ZIsNull"),
            Is.EqualTo(ExpectedById<bool>(false, false, false, true, true)));
    }

    #endregion

    #region take

    [Test]
    [Description("take keeps the rows with null values")]
    public void Take_KeepsNullRows()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetValuesById<long>(kustoDatabase, "Numbers | take 10", "X");

        Assert.That(actual, Is.EqualTo(ExpectedById<long>(1, 3, 5, null, null)));
    }

    [Test]
    [Description("take after 'where isnull()' returns a null row")]
    public void Take_AfterWhereIsNull()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Numbers | where isnull(X) | take 1");

        Assert.That(rows, Has.Count.EqualTo(1));
        AssertIsNull(rows[0], "X");
        Assert.That(GetValue<int>(rows[0], "Id"), Is.AnyOf(4, 5));
    }

    #endregion

    #region count

    [TestCase("Numbers | count", 5L)]
    [TestCase("Numbers | where isnull(X) | count", 2L)]
    [TestCase("Numbers | where isnotnull(X) | count", 3L)]
    [TestCase("Numbers | where X != 3 | count", 4L)]
    [TestCase("Numbers | where X > 1 | count", 2L)]
    [TestCase("Numbers | where X < 3 | count", 1L)]
    [TestCase("Numbers | where not(X > 1) | count", 1L)]
    [TestCase("Numbers | where X == long(null) | count", 0L)]
    [TestCase("Numbers | where X != long(null) | count", 3L)]
    [TestCase("Numbers | where X == X | count", 3L)]
    [TestCase("Numbers | project X | count", 5L)]
    [TestCase("Numbers | distinct X | count", 4L)]
    [Description("count counts the rows with nulls; a 'where' before it keeps only the rows whose predicate is true " +
                 "(X != 3 is true for a null X, X > 1 and X == X are null for it)")]
    public void Count_WithNulls(string query, long expectedCount)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        Assert.That(ExecuteAndGetCount(kustoDatabase, query), Is.EqualTo(expectedCount));
    }

    #endregion

    #region distinct

    [Test]
    [Description("distinct of a nullable column returns one null for all its nulls")]
    public void Distinct_SingleColumn_NullsAreOneValue()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetColumn<long>(kustoDatabase, "Numbers | distinct X", "X");

        Assert.That(actual, Is.EquivalentTo(new long?[] { 1, 3, 5, null }));
    }

    [Test]
    [Description("distinct over several columns: two rows are the same when every column is equal or null in both; " +
                 "(1, \"a\", true) and (null, \"\", null) appear twice and are returned once")]
    public void Distinct_MultipleColumns_NullsAreEqualToEachOther()
    {
        var kustoDatabase = CreateDatabase(BuildTuplesTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "Tuples | distinct X, S, B");
        var actual = rows.Select(row => (
            GetNullableValue<long>(row, "X"), GetStringOrNull(row, "S"), GetNullableValue<bool>(row, "B"))).ToList();

        Assert.That(actual, Is.EquivalentTo(new List<(long?, string?, bool?)>
        {
            (1L, "a", true), (null, "", null), (null, "b", null), (1L, "", null), (null, "", false)
        }));
        Assert.That(ExecuteAndGetCount(kustoDatabase, "Tuples | distinct * | count"), Is.EqualTo(5L));
    }

    [Test]
    [Description("distinct * on rows that differ only in where the nulls are: (1, null), (null, null) and (null, 1) " +
                 "are three different rows")]
    public void Distinct_Star_NullPositionsMatter()
    {
        var kustoDatabase = CreateDatabase(BuildNullPairsTable());

        var rows = ExecuteAndGetRows(kustoDatabase, "NullPairs | distinct *");
        var actual = rows.Select(row => (GetNullableValue<long>(row, "A"), GetNullableValue<long>(row, "B"))).ToList();

        Assert.That(actual, Is.EquivalentTo(new List<(long?, long?)> { (1L, null), (null, null), (null, 1L) }));
        Assert.That(ExecuteAndGetCount(kustoDatabase, "NullPairs | count"), Is.EqualTo(5L));
        Assert.That(ExecuteAndGetCount(kustoDatabase, "NullPairs | distinct A, B | count"), Is.EqualTo(3L));
    }

    [TestCase("NullPairs | where A != 1 | count", 3L)]
    [TestCase("NullPairs | where A == B | count", 0L)]
    [TestCase("NullPairs | where A != B | count", 3L)]
    [TestCase("NullPairs | where A != B | distinct A, B | count", 2L)]
    [TestCase("NullPairs | where isnull(A) and isnull(B) | distinct A, B | count", 1L)]
    [Description("where + distinct + count over two nullable columns: null == null and null != null are null, " +
                 "while 1 != null is true")]
    public void Distinct_AfterWhereOnTwoNullableColumns(string query, long expectedCount)
    {
        var kustoDatabase = CreateDatabase(BuildNullPairsTable());

        Assert.That(ExecuteAndGetCount(kustoDatabase, query), Is.EqualTo(expectedCount));
    }

    [Test]
    [Description("distinct of a nullable bool column returns true, false and null")]
    public void Distinct_Bool()
    {
        var kustoDatabase = CreateDatabase(new SummarizeOperatorOpusTableBuilder("Flags")
            .WithColumn<bool>("B")
            .WithRow(true)
            .WithRow(null)
            .WithRow(false)
            .WithRow(true)
            .WithRow(null)
            .Build());

        var actual = ExecuteAndGetColumn<bool>(kustoDatabase, "Flags | distinct B", "B");

        Assert.That(actual, Is.EquivalentTo(new bool?[] { true, false, null }));
    }

    [Test]
    [Description("distinct of a nullable datetime column returns one null")]
    public void Distinct_DateTime()
    {
        var day1 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var kustoDatabase = CreateDatabase(new SummarizeOperatorOpusTableBuilder("Dates")
            .WithColumn<DateTime>("D")
            .WithRow(day2)
            .WithRow(null)
            .WithRow(day1)
            .WithRow(null)
            .WithRow(day2)
            .Build());

        var actual = ExecuteAndGetColumn<DateTime>(kustoDatabase, "Dates | distinct D", "D");

        Assert.That(actual, Is.EquivalentTo(new DateTime?[] { day1, day2, null }));
    }

    [Test]
    [Description("distinct of a real column: NaN and null are two different values, each returned once")]
    public void Distinct_RealWithNaN()
    {
        var kustoDatabase = CreateDatabase(new SummarizeOperatorOpusTableBuilder("RealValues")
            .WithColumn<double>("R")
            .WithRow(1.0)
            .WithRow(null)
            .WithRow(double.NaN)
            .WithRow(null)
            .WithRow(double.NaN)
            .WithRow(1.0)
            .Build());

        var actual = ExecuteAndGetColumn<double>(kustoDatabase, "RealValues | distinct R", "R");

        Assert.That(actual, Has.Count.EqualTo(3));
        Assert.That(actual.Count(value => value is null), Is.EqualTo(1), "null");
        Assert.That(actual.Count(value => value is { } number && double.IsNaN(number)), Is.EqualTo(1), "NaN");
        Assert.That(actual.Count(value => value == 1.0), Is.EqualTo(1), "1.0");
    }

    [Test]
    [Description("distinct of a bool column computed by extend: X > 2 is null for a null X")]
    public void Distinct_ComputedColumnWithNulls()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetColumn<bool>(kustoDatabase, "Numbers | extend Big = X > 2 | distinct Big", "Big");

        Assert.That(actual, Is.EquivalentTo(new bool?[] { false, true, null }));
    }

    [Test]
    [Description("distinct + where / sort: the null of a distinct result can be filtered and sorted like any null")]
    public void Distinct_ThenWhereAndSort()
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        Assert.That(ExecuteAndGetCount(kustoDatabase, "Numbers | distinct X | where isnull(X) | count"),
            Is.EqualTo(1L));
        Assert.That(ExecuteAndGetColumn<long>(kustoDatabase, "Numbers | where X != 3 | distinct X", "X"),
            Is.EquivalentTo(new long?[] { 1, 5, null }));
        Assert.That(ExecuteAndGetColumnAsText<long>(kustoDatabase, "Numbers | distinct X | sort by X asc", "X"),
            Is.EqualTo("null, 1, 3, 5"));
        Assert.That(ExecuteAndGetColumnAsText<long>(kustoDatabase, "Numbers | distinct X | sort by X desc", "X"),
            Is.EqualTo("5, 3, 1, null"));
    }

    [Test]
    [Description("distinct and 'summarize by' group a string column with null cells the same way")]
    public void Distinct_StringColumn_SameGroupsAsSummarizeBy()
    {
        var kustoDatabase = CreateDatabase(new SummarizeOperatorOpusTableBuilder("Texts")
            .WithColumn<string>("S")
            .WithRow("a")
            .WithRow(null)
            .WithRow("a")
            .WithRow(null)
            .WithRow("b")
            .Build());

        Assert.That(ExecuteAndGetCount(kustoDatabase, "Texts | distinct S | count"), Is.EqualTo(3L));
        Assert.That(ExecuteAndGetCount(kustoDatabase, "Texts | summarize by S | count"), Is.EqualTo(3L));
    }

    #endregion

    #region sort

    [TestCase("Numbers | sort by X asc", "null, null, 1, 3, 5")]
    [TestCase("Numbers | sort by X desc", "5, 3, 1, null, null")]
    [TestCase("Numbers | sort by X", "5, 3, 1, null, null")]
    [TestCase("Numbers | sort by X asc nulls first", "null, null, 1, 3, 5")]
    [TestCase("Numbers | sort by X asc nulls last", "1, 3, 5, null, null")]
    [TestCase("Numbers | sort by X desc nulls first", "null, null, 5, 3, 1")]
    [TestCase("Numbers | sort by X desc nulls last", "5, 3, 1, null, null")]
    [TestCase("Numbers | order by X asc", "null, null, 1, 3, 5")]
    [TestCase("Numbers | where X != 3 | sort by X asc", "null, null, 1, 5")]
    [Description("sort: nulls first for asc, nulls last for desc (the default direction), " +
                 "and 'nulls first' / 'nulls last' override it")]
    public void Sort_Long_NullsPosition(string query, string expectedValues)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        Assert.That(ExecuteAndGetColumnAsText<long>(kustoDatabase, query, "X"), Is.EqualTo(expectedValues));
    }

    [TestCase("Numbers | sort by X asc, Id asc", new[] { 4, 5, 1, 2, 3 })]
    [TestCase("Numbers | sort by X asc, Id desc", new[] { 5, 4, 1, 2, 3 })]
    [TestCase("Numbers | sort by X desc, Id desc", new[] { 3, 2, 1, 5, 4 })]
    [TestCase("Numbers | sort by X asc nulls last, Id desc", new[] { 1, 2, 3, 5, 4 })]
    [Description("sort: the rows with a null key are ordered by the next sort key")]
    public void Sort_NullKeyTiesBrokenByNextKey(string query, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, query), Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("sort an int column with a null")]
    public void Sort_Int()
    {
        AssertSortWithNull(CreateTypedDatabase(1, 2));
    }

    [Test]
    [Description("sort a long column with a null (negative values are greater than null)")]
    public void Sort_Long()
    {
        AssertSortWithNull(CreateTypedDatabase(-5L, 0L));
    }

    [Test]
    [Description("sort a real column with a null")]
    public void Sort_Real()
    {
        AssertSortWithNull(CreateTypedDatabase(-1.5, 2.5));
    }

    [Test]
    [Description("sort a decimal column with a null")]
    public void Sort_Decimal()
    {
        AssertSortWithNull(CreateTypedDatabase(0.5m, 1.5m));
    }

    [Test]
    [Description("sort a bool column with a null: asc is null, false, true")]
    public void Sort_Bool()
    {
        AssertSortWithNull(CreateTypedDatabase(false, true));
    }

    [Test]
    [Description("sort a datetime column with a null")]
    public void Sort_DateTime()
    {
        AssertSortWithNull(CreateTypedDatabase(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    [Description("sort a timespan column with a null (a negative timespan is greater than null)")]
    public void Sort_TimeSpan()
    {
        AssertSortWithNull(CreateTypedDatabase(TimeSpan.FromHours(-1), TimeSpan.FromHours(2)));
    }

    [TestCase("Reals | sort by R asc", new[] { 2, 3, 4, 1 })]
    [TestCase("Reals | sort by R asc nulls last", new[] { 4, 1, 3, 2 })]
    [TestCase("Reals | sort by R desc", new[] { 1, 4, 3, 2 })]
    [TestCase("Reals | sort by R desc nulls first", new[] { 2, 3, 1, 4 })]
    [Description("sort a real column with null (Id 2) and NaN (Id 3): NaN is sorted between the nulls and the numbers")]
    public void Sort_RealWithNaN(string query, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildRealsTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, query), Is.EqualTo(expectedIds));
    }

    [TestCase("MultiKey | sort by A asc, B desc", new[] { 2, 4, 6, 1, 3, 5 })]
    [TestCase("MultiKey | sort by A asc nulls last, B asc nulls first", new[] { 3, 1, 6, 5, 4, 2 })]
    [TestCase("MultiKey | sort by A nulls first, B", new[] { 2, 4, 5, 6, 1, 3 })]
    [TestCase("MultiKey | sort by A desc nulls first, B asc", new[] { 4, 2, 5, 3, 1, 6 })]
    [TestCase("MultiKey | sort by A desc, B asc nulls last", new[] { 5, 1, 6, 3, 2, 4 })]
    [Description("sort by two nullable columns: every key has its own direction and nulls position")]
    public void Sort_MultipleNullableKeys(string query, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildMultiKeyTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, query), Is.EqualTo(expectedIds));
    }

    [Test]
    [Description("sort a string column with a null and an empty string cell: both are the smallest value " +
                 "(Azure Data Explorer has only \"\"), so they come first for asc and last for desc")]
    public void Sort_StringWithNullAndEmpty()
    {
        var kustoDatabase = CreateDatabase(BuildWordsTable());

        AssertIdsInOrderWithTies(ExecuteAndGetIds(kustoDatabase, "Words | sort by S asc"), [2, 4], [5], [1], [3]);
        AssertIdsInOrderWithTies(ExecuteAndGetIds(kustoDatabase, "Words | sort by S desc"), [3], [1], [5], [2, 4]);
        AssertIdsInOrderWithTies(ExecuteAndGetIds(kustoDatabase, "Words | sort by S"), [3], [1], [5], [2, 4]);
    }

    [TestCase("Numbers | sort by X asc | take 2")]
    [TestCase("Numbers | sort by X asc nulls first | take 2")]
    [TestCase("Numbers | sort by X desc nulls first | take 2")]
    [Description("sort + take: the two null rows (in any order) are taken when the nulls are sorted first")]
    public void Sort_NullsFirst_ThenTake_TakesNullRows(string query)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, query), Is.EquivalentTo(new[] { 4, 5 }));
    }

    [TestCase("Numbers | sort by X desc | take 2", new[] { 3, 2 })]
    [TestCase("Numbers | sort by X | take 3", new[] { 3, 2, 1 })]
    [TestCase("Numbers | sort by X asc nulls last | take 2", new[] { 1, 2 })]
    [TestCase("Numbers | where isnotnull(X) | sort by X asc | take 1", new[] { 1 })]
    [Description("sort + take: no null row is taken when the nulls are sorted last")]
    public void Sort_NullsLast_ThenTake_TakesNonNullRows(string query, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, query), Is.EqualTo(expectedIds));
    }

    #endregion

    #region print

    [Test]
    [Description("print of typed null literals returns null for every type")]
    public void Print_TypedNullLiterals()
    {
        var kustoDatabase = CreateDatabase();
        string[] columns = ["I", "L", "R", "Dbl", "Dc", "B", "D", "T", "G"];

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "print I = int(null), L = long(null), R = real(null), Dbl = double(null), Dc = decimal(null), " +
            "B = bool(null), D = datetime(null), T = timespan(null), G = guid(null)");

        AssertColumnsInOrder(row, columns);
        foreach (string columnName in columns)
        {
            AssertIsNull(row, columnName);
        }
    }

    [Test]
    [Description("print mixes null and non-null columns; isnull() / isnotnull() of a typed null literal")]
    public void Print_NullAndNonNullColumns()
    {
        var kustoDatabase = CreateDatabase();

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "print A = 1, B = long(null), C = 'x', D = isnull(long(null)), E = isnotnull(datetime(null)), " +
            "F = isnull(0), G = isnull(timespan(null))");

        AssertColumnsInOrder(row, "A", "B", "C", "D", "E", "F", "G");
        Assert.That(GetNumber(row, "A"), Is.EqualTo(1));
        AssertIsNull(row, "B");
        Assert.That(GetValue<string>(row, "C"), Is.EqualTo("x"));
        Assert.That(GetValue<bool>(row, "D"), Is.True);
        Assert.That(GetValue<bool>(row, "E"), Is.False);
        Assert.That(GetValue<bool>(row, "F"), Is.False);
        Assert.That(GetValue<bool>(row, "G"), Is.True);
    }

    [TestCase("print X = long(null) | where isnull(X) | count", 1L)]
    [TestCase("print X = long(null) | where isnotnull(X) | count", 0L)]
    [TestCase("print X = long(null) | where X == 1 | count", 0L)]
    [TestCase("print X = long(null) | where X != 1 | count", 1L)]
    [TestCase("print X = long(null) | where X > 1 | count", 0L)]
    [TestCase("print X = long(null) | where not(X > 1) | count", 0L)]
    [TestCase("print X = long(null) | where X == X | count", 0L)]
    [TestCase("print X = long(null) | extend Y = X + 1 | where isnull(Y) | count", 1L)]
    [Description("print a null and filter it: the null rules of 'where' apply to the printed row")]
    public void Print_NullThenWhere(string query, long expectedCount)
    {
        var kustoDatabase = CreateDatabase();

        Assert.That(ExecuteAndGetCount(kustoDatabase, query), Is.EqualTo(expectedCount));
    }

    #endregion
}
