using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Azure Kusto null semantics for tabular operators:
/// sort / order (including asc/desc defaults and explicit nulls first / nulls last),
/// distinct (single, multi-column, *, collision resistance), project, extend, take / limit, count, and print.
/// References:
/// - https://learn.microsoft.com/en-us/kusto/query/sort-operator
/// - https://learn.microsoft.com/en-us/kusto/query/distinct-operator
/// </summary>
public class NullableSortDistinctProjectExtendTakeCountPrintNeonTests
{
    #region sort / order with null ordering rules

    [Test]
    [Description("sort default (and desc) places nulls LAST; sort asc places nulls FIRST; explicit nulls first/last override defaults")]
    public void Sort_NullableInt_DefaultAndExplicitNullOrdering()
    {
        var table = CreateNullableTable<int?>([3, null, 1, null, 2], tableName: "Nums", columnName: "V");
        var kustoDatabase = CreateDatabase(table);

        // Default is desc nulls last
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V"),
            Is.EqualTo(new int?[] { 3, 2, 1, null, null }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V desc"),
            Is.EqualTo(new int?[] { 3, 2, 1, null, null }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V desc nulls last"),
            Is.EqualTo(new int?[] { 3, 2, 1, null, null }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V desc nulls first"),
            Is.EqualTo(new int?[] { null, null, 3, 2, 1 }));

        // Bare nulls first / nulls last implies desc
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V nulls first"),
            Is.EqualTo(new int?[] { null, null, 3, 2, 1 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V nulls last"),
            Is.EqualTo(new int?[] { 3, 2, 1, null, null }));

        // asc default is asc nulls first
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V asc"),
            Is.EqualTo(new int?[] { null, null, 1, 2, 3 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V asc nulls first"),
            Is.EqualTo(new int?[] { null, null, 1, 2, 3 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V asc nulls last"),
            Is.EqualTo(new int?[] { 1, 2, 3, null, null }));

        // order by is an alias of sort by
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | order by V asc nulls last"),
            Is.EqualTo(new int?[] { 1, 2, 3, null, null }));
    }

    [Test]
    [Description("sort with nulls works across bool, long, decimal, datetime, timespan, guid, and nullable string columns")]
    public void Sort_AllNullableScalarTypes()
    {
        var g1 = new Guid("11111111-1111-1111-1111-111111111111");
        var g2 = new Guid("22222222-2222-2222-2222-222222222222");
        var dt1 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dt2 = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var boolDb = CreateDatabase(CreateNullableTable<bool?>([true, null, false], "B", "V"));
        Assert.That(ExecuteAndGetNullableColumn<bool?>("V", boolDb, "B | sort by V asc"),
            Is.EqualTo(new bool?[] { null, false, true }));
        Assert.That(ExecuteAndGetNullableColumn<bool?>("V", boolDb, "B | sort by V desc"),
            Is.EqualTo(new bool?[] { true, false, null }));
        Assert.That(ExecuteAndGetNullableColumn<bool?>("V", boolDb, "B | sort by V asc nulls last"),
            Is.EqualTo(new bool?[] { false, true, null }));

        var dtDb = CreateDatabase(CreateNullableTable<DateTime?>([dt2, null, dt1], "D", "V"));
        Assert.That(ExecuteAndGetNullableColumn<DateTime?>("V", dtDb, "D | sort by V asc"),
            Is.EqualTo(new DateTime?[] { null, dt1, dt2 }));
        Assert.That(ExecuteAndGetNullableColumn<DateTime?>("V", dtDb, "D | sort by V desc"),
            Is.EqualTo(new DateTime?[] { dt2, dt1, null }));

        var tsDb = CreateDatabase(CreateNullableTable<TimeSpan?>([TimeSpan.FromHours(2), null, TimeSpan.FromMinutes(10)], "T", "V"));
        Assert.That(ExecuteAndGetNullableColumn<TimeSpan?>("V", tsDb, "T | sort by V asc nulls last"),
            Is.EqualTo(new TimeSpan?[] { TimeSpan.FromMinutes(10), TimeSpan.FromHours(2), null }));

        var decDb = CreateDatabase(CreateNullableTable<decimal?>([2.5m, null, -1.5m], "M", "V"));
        Assert.That(ExecuteAndGetNullableColumn<decimal?>("V", decDb, "M | sort by V desc nulls first"),
            Is.EqualTo(new decimal?[] { null, 2.5m, -1.5m }));

        var guidDb = CreateDatabase(CreateNullableTable<Guid?>([g2, null, g1], "G", "V"));
        Assert.That(ExecuteAndGetNullableColumn<Guid?>("V", guidDb, "G | sort by V asc"),
            Is.EqualTo(new Guid?[] { null, g1, g2 }));

        var strDb = CreateDatabase(CreateNullableTable<string?>(["beta", null, "alpha"], "S", "V"));
        Assert.That(ExecuteAndGetNullableColumn<string?>("V", strDb, "S | sort by V asc"),
            Is.EqualTo(new[] { "", "alpha", "beta" }));
        Assert.That(ExecuteAndGetNullableColumn<string?>("V", strDb, "S | sort by V desc"),
            Is.EqualTo(new[] { "beta", "alpha", "" }));
    }

    [Test]
    [Description("Multi-column sort applies independent asc/desc and nulls first/last per sort key")]
    public void Sort_MultiColumn_IndependentNullsFirstAndNullsLast()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 10), (1, null), (1, 20), (null, 5), (null, null), (null, 15), (2, null), (2, 1)],
            tableName: "Pairs",
            columnName1: "A",
            columnName2: "B");
        var kustoDatabase = CreateDatabase(table);

        // A asc nulls last: 1, 2, null
        // Within each A group, B desc nulls first: null, larger, smaller
        var actual = ExecuteAndGetNullable2Columns<int?, int?>(
            "A",
            "B",
            kustoDatabase,
            "Pairs | sort by A asc nulls last, B desc nulls first");

        Assert.That(actual, Is.EqualTo(new List<(int?, int?)>
        {
            (1, null),
            (1, 20),
            (1, 10),
            (2, null),
            (2, 1),
            (null, null),
            (null, 15),
            (null, 5)
        }));
    }

    [Test]
    [Description("sort orders special double values (NaN, -inf, finite, +inf) with null controlled by nulls first/last")]
    public void Sort_DoubleSpecialValuesWithNull()
    {
        var table = CreateNullableTable<double?>(
            [1.5, double.PositiveInfinity, null, double.NaN, -1.5, double.NegativeInfinity, 0.0],
            tableName: "Reals",
            columnName: "V");
        var kustoDatabase = CreateDatabase(table);

        var ascNullsFirst = ExecuteAndGetNullableColumn<double?>("V", kustoDatabase, "Reals | sort by V asc nulls first");
        Assert.That(ascNullsFirst, Has.Count.EqualTo(7));
        Assert.That(ascNullsFirst[0], Is.Null);
        Assert.That(ascNullsFirst[1], Is.NaN);
        Assert.That(ascNullsFirst[2], Is.EqualTo(double.NegativeInfinity));
        Assert.That(ascNullsFirst[3], Is.EqualTo(-1.5));
        Assert.That(ascNullsFirst[4], Is.EqualTo(0.0));
        Assert.That(ascNullsFirst[5], Is.EqualTo(1.5));
        Assert.That(ascNullsFirst[6], Is.EqualTo(double.PositiveInfinity));

        var descNullsLast = ExecuteAndGetNullableColumn<double?>("V", kustoDatabase, "Reals | sort by V desc nulls last");
        Assert.That(descNullsLast, Has.Count.EqualTo(7));
        Assert.That(descNullsLast[0], Is.EqualTo(double.PositiveInfinity));
        Assert.That(descNullsLast[1], Is.EqualTo(1.5));
        Assert.That(descNullsLast[2], Is.EqualTo(0.0));
        Assert.That(descNullsLast[3], Is.EqualTo(-1.5));
        Assert.That(descNullsLast[4], Is.EqualTo(double.NegativeInfinity));
        Assert.That(descNullsLast[5], Is.NaN);
        Assert.That(descNullsLast[6], Is.Null);
    }

    [Test]
    [Description("sort handles all-null columns and empty tables with nullable schema")]
    public void Sort_AllNullsAndEmptyTable()
    {
        var allNulls = CreateNullableTable<int?>([null, null, null], "AllNulls", "V");
        var empty = CreateEmptyNullableTable("Empty", ("V", typeof(int)));
        var kustoDatabase = CreateDatabase(allNulls, empty);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "AllNulls | sort by V asc"),
            Is.EqualTo(new int?[] { null, null, null }));

        Assert.That(
            ExecuteAndAssertSuccess(kustoDatabase, "Empty | sort by V desc nulls first"),
            Is.Empty);
    }

    #endregion

    #region distinct with nulls, multi-column keys, *, and delimiter/value collisions

    [Test]
    [Description("distinct preserves null separately from scalar defaults; missing strings are empty")]
    public void Distinct_SingleNullableColumns_DeduplicatesNullWithoutValueCollision()
    {
        var intDb = CreateDatabase(CreateNullableTable<int?>([null, 0, 1, null, 0, 1], "Ints", "V"));
        Assert.That(ExecuteAndGetNullableColumn<int?>("V", intDb, "Ints | distinct V"),
            Is.EquivalentTo(new int?[] { null, 0, 1 }));

        var boolDb = CreateDatabase(CreateNullableTable<bool?>([null, false, true, null, false], "Bools", "V"));
        Assert.That(ExecuteAndGetNullableColumn<bool?>("V", boolDb, "Bools | distinct V"),
            Is.EquivalentTo(new bool?[] { null, false, true }));

        var dtDb = CreateDatabase(CreateNullableTable<DateTime?>(
            [null, DateTime.MinValue, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, DateTime.MinValue], "Dts", "V"));
        Assert.That(ExecuteAndGetNullableColumn<DateTime?>("V", dtDb, "Dts | distinct V"),
            Is.EquivalentTo(new DateTime?[] { null, DateTime.MinValue, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) }));

        var tsDb = CreateDatabase(CreateNullableTable<TimeSpan?>([null, TimeSpan.Zero, TimeSpan.FromSeconds(5), null], "Tss", "V"));
        Assert.That(ExecuteAndGetNullableColumn<TimeSpan?>("V", tsDb, "Tss | distinct V"),
            Is.EquivalentTo(new TimeSpan?[] { null, TimeSpan.Zero, TimeSpan.FromSeconds(5) }));

        var guidDb = CreateDatabase(CreateNullableTable<Guid?>([null, Guid.Empty, null, Guid.Empty], "Guids", "V"));
        Assert.That(ExecuteAndGetNullableColumn<Guid?>("V", guidDb, "Guids | distinct V"),
            Is.EquivalentTo(new Guid?[] { null, Guid.Empty }));

        var strDb = CreateDatabase(CreateNullableTable<string?>([null, "", "a", null, "", "a"], "Strs", "V"));
        Assert.That(ExecuteAndGetNullableColumn<string?>("V", strDb, "Strs | distinct V"),
            Is.EquivalentTo(new[] { "", "a" }));
    }

    [Test]
    [Description("Multi-column distinct preserves distinct null placements and does not suffer pipe-delimiter or null/empty key collisions")]
    public void Distinct_MultiColumn_NoKeyCollisionsWithNullEmptyOrDelimiters()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(null, 1), (1, null), (null, null), (null, null), (null, 1), (1, 1), (0, null)],
            tableName: "Pairs",
            columnName1: "A",
            columnName2: "B");
        var kustoDatabase = CreateDatabase(table);

        var distinctPairs = ExecuteAndGetNullable2Columns<int?, int?>("A", "B", kustoDatabase, "Pairs | distinct A, B");
        Assert.That(distinctPairs, Is.EquivalentTo(new List<(int?, int?)>
        {
            (null, 1),
            (1, null),
            (null, null),
            (1, 1),
            (0, null)
        }));

        // Delimiters do not collapse distinct tuples; missing and empty strings represent the same key.
        var strPairs = CreateNullableTable2<string?, string?>(
            [("a|b", "c"), ("a", "b|c"), (null, "a"), ("", "a"), ("a", null), ("a", ""), (null, null), ("", ""), (null, null)],
            tableName: "StrPairs",
            columnName1: "S1",
            columnName2: "S2");
        var strDb = CreateDatabase(strPairs);

        var distinctStrPairs = ExecuteAndGetNullable2Columns<string?, string?>("S1", "S2", strDb, "StrPairs | distinct S1, S2");
        Assert.That(distinctStrPairs, Is.EquivalentTo(new List<(string?, string?)>
        {
            ("a|b", "c"),
            ("a", "b|c"),
            ("", "a"),
            ("a", ""),
            ("", "")
        }));
    }

    [Test]
    [Description("distinct * deduplicates identical rows containing nulls; distinct over single column outputs only that column")]
    public void Distinct_StarAndProjectedSchema_WithNulls()
    {
        var table = CreateNullableTable3<int?, string?, bool?>(
            [
                (1, "x", null),
                (1, "x", null),
                (null, null, null),
                (null, null, null),
                (1, null, true)
            ],
            tableName: "Records",
            columnName1: "A",
            columnName2: "B",
            columnName3: "C");
        var kustoDatabase = CreateDatabase(table);

        var starRows = ExecuteAndGetNullable3Columns<int?, string?, bool?>("A", "B", "C", kustoDatabase, "Records | distinct *");
        Assert.That(starRows, Is.EquivalentTo(new List<(int?, string?, bool?)>
        {
            (1, "x", null),
            (null, "", null),
            (1, "", true)
        }));

        var singleColRows = ExecuteAndAssertSuccess(kustoDatabase, "Records | distinct A");
        Assert.That(singleColRows, Has.Count.EqualTo(2));
        Assert.That(singleColRows.Select(r => (int?)r["A"]), Is.EquivalentTo(new int?[] { 1, null }));
        foreach (var row in singleColRows)
        {
            Assert.That(row.Keys, Is.EqualTo(new[] { "A" }));
        }
    }

    [Test]
    [Description("distinct on an all-null column returns one null row; distinct on an empty table returns zero rows")]
    public void Distinct_AllNullsAndEmptyTable()
    {
        var allNulls = CreateNullableTable<int?>([null, null, null], "AllNulls", "V");
        var empty = CreateEmptyNullableTable("Empty", ("V", typeof(int)));
        var kustoDatabase = CreateDatabase(allNulls, empty);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "AllNulls | distinct V"),
            Is.EqualTo(new int?[] { null }));

        Assert.That(
            ExecuteAndAssertSuccess(kustoDatabase, "Empty | distinct V"),
            Is.Empty);
    }

    #endregion

    #region project, extend, take/limit, count, print with nullable columns

    [Test]
    [Description("project preserves null values across direct references, renames, computed expressions, and null literals")]
    public void Project_PreservesNullsAndComputesNullableExpressions()
    {
        var table = CreateNullableTable3<int?, int?, int?>(
            [(1, 10, 5), (2, null, 5), (3, 10, null), (4, null, null)],
            tableName: "Data",
            columnName1: "Id",
            columnName2: "A",
            columnName3: "B");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Data | project Id, RenamedA = A, Sum = A + B, Eq = A == B, Missing = int(null), IsAMissing = isnull(A)");

        Assert.That(rows, Has.Count.EqualTo(4));
        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        Assert.That(byId[1].Keys, Is.EqualTo(new[] { "Id", "RenamedA", "Sum", "Eq", "Missing", "IsAMissing" }));
        Assert.That(byId[1]["RenamedA"], Is.EqualTo(10));
        Assert.That(AsNullableDouble(byId[1]["Sum"]), Is.EqualTo(15.0));
        Assert.That(byId[1]["Eq"], Is.EqualTo(false));
        Assert.That(byId[1]["Missing"], Is.Null);
        Assert.That(byId[1]["IsAMissing"], Is.EqualTo(false));

        Assert.That(byId[2]["RenamedA"], Is.Null);
        Assert.That(byId[2]["Sum"], Is.Null);
        Assert.That(byId[2]["Eq"], Is.EqualTo(false));
        Assert.That(byId[2]["Missing"], Is.Null);
        Assert.That(byId[2]["IsAMissing"], Is.EqualTo(true));

        Assert.That(byId[4]["RenamedA"], Is.Null);
        Assert.That(byId[4]["Sum"], Is.Null);
        Assert.That(byId[4]["Eq"], Is.Null);
        Assert.That(byId[4]["IsAMissing"], Is.EqualTo(true));
    }

    [Test]
    [Description("extend supports chaining newly extended nullable columns and overwriting existing columns with null or non-null values")]
    public void Extend_ChainedNullableColumnsAndOverwrite()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 10), (2, null), (3, 0)],
            tableName: "Data",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Data | extend Doubled = Val * 2, NullCol = datetime(null) | extend PlusOne = Doubled + 1, WasNull = isnull(Doubled) | extend Val = int(null)");

        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        Assert.That(byId[1]["Val"], Is.Null);
        Assert.That(AsNullableDouble(byId[1]["Doubled"]), Is.EqualTo(20.0));
        Assert.That(AsNullableDouble(byId[1]["PlusOne"]), Is.EqualTo(21.0));
        Assert.That(byId[1]["WasNull"], Is.EqualTo(false));
        Assert.That(byId[1]["NullCol"], Is.Null);

        Assert.That(byId[2]["Val"], Is.Null);
        Assert.That(byId[2]["Doubled"], Is.Null);
        Assert.That(byId[2]["PlusOne"], Is.Null);
        Assert.That(byId[2]["WasNull"], Is.EqualTo(true));
        Assert.That(byId[2]["NullCol"], Is.Null);

        Assert.That(AsNullableDouble(byId[3]["PlusOne"]), Is.EqualTo(1.0));
        Assert.That(byId[3]["WasNull"], Is.EqualTo(false));
    }

    [Test]
    [Description("take and limit preserve null rows and respect preceding sort nulls first / nulls last ordering")]
    public void TakeAndLimit_WithNullRowsAndSortOrdering()
    {
        var table = CreateNullableTable<int?>([30, null, 10, null, 20], tableName: "Nums", columnName: "V");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V asc nulls first | take 3"),
            Is.EqualTo(new int?[] { null, null, 10 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V desc nulls last | take 2"),
            Is.EqualTo(new int?[] { 30, 20 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("V", kustoDatabase, "Nums | sort by V desc nulls first | limit 2"),
            Is.EqualTo(new int?[] { null, null }));

        Assert.That(
            ExecuteAndAssertSuccess(kustoDatabase, "Nums | take 0"),
            Is.Empty);

        Assert.That(
            ExecuteAndAssertSuccess(kustoDatabase, "Nums | take 100"),
            Has.Count.EqualTo(5));
    }

    [Test]
    [Description("count counts all rows including all-null rows, and reflects null-aware where filters")]
    public void Count_IncludesAllNullRowsAndReflectsNullFilters()
    {
        var table = CreateNullableTable2<int?, string?>(
            [(1, "a"), (null, null), (null, "b"), (2, null), (null, null)],
            tableName: "Items",
            columnName1: "A",
            columnName2: "B");
        var empty = CreateEmptyNullableTable("Empty", ("A", typeof(int)));
        var kustoDatabase = CreateDatabase(table, empty);

        Assert.That(
            AsNullableLong(ExecuteAndGetSingleRow(kustoDatabase, "Items | count")["Count"]),
            Is.EqualTo(5L));

        Assert.That(
            AsNullableLong(ExecuteAndGetSingleRow(kustoDatabase, "Items | where isnull(A) | count")["Count"]),
            Is.EqualTo(3L));

        Assert.That(
            AsNullableLong(ExecuteAndGetSingleRow(kustoDatabase, "Items | where A != 1 | count")["Count"]),
            Is.EqualTo(4L));

        Assert.That(
            AsNullableLong(ExecuteAndGetSingleRow(kustoDatabase, "Items | where A == int(null) | count")["Count"]),
            Is.EqualTo(0L));

        Assert.That(
            AsNullableLong(ExecuteAndGetSingleRow(kustoDatabase, "Empty | count")["Count"]),
            Is.EqualTo(0L));
    }

    [Test]
    [Description("print supports multiple named and unnamed null literals and null-producing expressions with optional trailing semicolon")]
    public void Print_MultipleNullColumnsNamedAndUnnamed()
    {
        var kustoDatabase = new KustoDatabase();

        var row = ExecuteAndGetSingleRow(
            kustoDatabase,
            "print int(null), MissingDate = datetime(null), SumNull = 5 + int(null), EqNull = int(null) == int(null), StrNull = tostring(int(null));");

        Assert.That(row.Keys, Is.EqualTo(new[] { "print_0", "MissingDate", "SumNull", "EqNull", "StrNull" }));
        Assert.That(row["print_0"], Is.Null);
        Assert.That(row["MissingDate"], Is.Null);
        Assert.That(row["SumNull"], Is.Null);
        Assert.That(row["EqNull"], Is.Null);
        Assert.That(row["StrNull"], Is.EqualTo(""));
    }

    #endregion
}
