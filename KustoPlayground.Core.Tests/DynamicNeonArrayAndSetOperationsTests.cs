using static KustoPlayground.Core.Tests.DynamicNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// TDD specification tests for:
/// - Array inspection & utility functions: array_length/arraylength, array_slice, array_concat,
///   array_index_of, array_reverse, array_sort_asc, array_sort_desc, array_sum, array_iff/array_iif
/// - Dynamic set operations: set_union, set_intersect, set_difference, set_has_element
/// - Dynamic arrays on the right-hand side of in / !in / in~ / !in~
/// </summary>
public class DynamicNeonArrayAndSetOperationsTests
{
    // =========================================================================
    // 1. array_length() / arraylength()
    // =========================================================================

    [Test]
    public void ArrayLength_CountsTopLevelElementsIncludingNulls_AndReturnsNullForNonArrays()
    {
        var table = CreateKeyedDynamicTable("Items", [
            (1, Arr(1, 2, 3)),
            (2, Arr(1, null, "x", Arr(10, 20), Bag(("a", 1)))),
            (3, Arr()),
            (4, Bag(("a", 1), ("b", 2))),
            (5, "not-an-array"),
            (6, 42L),
            (7, null)
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Items
            | sort by Id asc
            | project Id, Len = array_length(Payload), AliasLen = arraylength(Payload)
            """);

        Assert.That(rows, Has.Count.EqualTo(7));
        AssertLongEqual(rows[0]["Len"], 3L);
        AssertLongEqual(rows[0]["AliasLen"], 3L);
        AssertLongEqual(rows[1]["Len"], 5L); // nested array counts as 1 element; null counts as 1 element
        AssertLongEqual(rows[2]["Len"], 0L);
        Assert.That(rows[3]["Len"], Is.Null);
        Assert.That(rows[4]["Len"], Is.Null);
        Assert.That(rows[5]["Len"], Is.Null);
        Assert.That(rows[6]["Len"], Is.Null);
    }

    // =========================================================================
    // 2. array_slice()
    // =========================================================================

    [Test]
    public void ArraySlice_InclusiveBoundsNegativeIndicesClampingAndInvalidInputs()
    {
        var nonArrays = CreateTable("NonArrays", [
            new Column<object>("NullDyn", isNullable: true),
            new Column<object>("BagDyn", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "NullDyn", null }, { "BagDyn", Bag(("a", 1L)) } }
        ]);
        var db = CreateDatabase(nonArrays);

        var row = ExecuteSingleRow(db, """
            let arr = dynamic([10, 20, 30, 40, 50]);
            NonArrays
            | project
                Mid = array_slice(arr, 1, 3),
                Single = array_slice(arr, 2, 2),
                Full = array_slice(arr, 0, -1),
                TailNeg = array_slice(arr, -3, -1),
                MixedNeg = array_slice(arr, 1, -2),
                SingleNeg = array_slice(arr, -2, -2),
                ClampLeft = array_slice(arr, -100, 1),
                ClampRight = array_slice(arr, 3, 100),
                ClampBoth = array_slice(arr, -100, 100),
                Inverted = array_slice(arr, 3, 1),
                OutRight = array_slice(arr, 10, 20),
                OutLeft = array_slice(arr, -20, -10),
                FromEmpty = array_slice(dynamic([]), 0, 1),
                FromNull = array_slice(NullDyn, 0, 1),
                FromBag = array_slice(BagDyn, 0, 1)
            """);

        AssertDynamicArrayEqual(row["Mid"], 20L, 30L, 40L);
        AssertDynamicArrayEqual(row["Single"], 30L);
        AssertDynamicArrayEqual(row["Full"], 10L, 20L, 30L, 40L, 50L);
        AssertDynamicArrayEqual(row["TailNeg"], 30L, 40L, 50L);
        AssertDynamicArrayEqual(row["MixedNeg"], 20L, 30L, 40L);
        AssertDynamicArrayEqual(row["SingleNeg"], 40L);
        AssertDynamicArrayEqual(row["ClampLeft"], 10L, 20L);
        AssertDynamicArrayEqual(row["ClampRight"], 40L, 50L);
        AssertDynamicArrayEqual(row["ClampBoth"], 10L, 20L, 30L, 40L, 50L);
        AssertDynamicArrayEqual(row["Inverted"]);
        AssertDynamicArrayEqual(row["OutRight"]);
        AssertDynamicArrayEqual(row["OutLeft"]);
        AssertDynamicArrayEqual(row["FromEmpty"]);
        Assert.That(row["FromNull"], Is.Null);
        Assert.That(row["FromBag"], Is.Null);
    }

    // =========================================================================
    // 3. array_concat()
    // =========================================================================

    [Test]
    public void ArrayConcat_PreservesDuplicatesAndNullsAndIgnoresNonArrayArguments()
    {
        var nonArrays = CreateTable("NonArrays", [
            new Column<object>("NullDyn", isNullable: true),
            new Column<object>("BagDyn", isNullable: true),
            new Column<object>("ScalarDyn", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "NullDyn", null }, { "BagDyn", Bag(("a", 1L)) }, { "ScalarDyn", 2L } }
        ]);
        var db = CreateDatabase(nonArrays);

        var row = ExecuteSingleRow(db, """
            NonArrays
            | project
                Two = array_concat(dynamic([1, 2]), dynamic([2, 3])),
                Three = array_concat(dynamic(["a"]), dynamic([]), dynamic([null, 4, {"k": 1}])),
                AllEmpty = array_concat(dynamic([]), dynamic([])),
                WithNullArg = array_concat(dynamic([1, 2]), NullDyn),
                WithBagArg = array_concat(dynamic([1]), BagDyn),
                WithScalarArg = array_concat(dynamic([1]), ScalarDyn)
            """);

        AssertDynamicArrayEqual(row["Two"], 1L, 2L, 2L, 3L);
        AssertDynamicArrayEqual(row["Three"], "a", null, 4L, Bag(("k", 1L)));
        AssertDynamicArrayEqual(row["AllEmpty"]);
        AssertDynamicArrayEqual(row["WithNullArg"], 1L, 2L);
        AssertDynamicArrayEqual(row["WithBagArg"], 1L);
        AssertDynamicArrayEqual(row["WithScalarArg"], 1L);
    }

    // =========================================================================
    // 4. array_index_of()
    // =========================================================================

    [Test]
    public void ArrayIndexOf_SupportsStartLengthOccurrenceNegativeStartAndComplexElements()
    {
        var nonArrays = CreateTable("NonArrays", [
            new Column<object>("NullDyn", isNullable: true),
            new Column<object>("BagDyn", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "NullDyn", null }, { "BagDyn", Bag(("a", 1L)) } }
        ]);
        var db = CreateDatabase(nonArrays);

        var row = ExecuteSingleRow(db, """
            let arr = dynamic(["a", "b", "a", "c", "a", "A"]);
            NonArrays
            | project
                First = array_index_of(arr, "a"),
                Second = array_index_of(arr, "a", 0, -1, 2),
                Third = array_index_of(arr, "a", 0, -1, 3),
                FourthMissing = array_index_of(arr, "a", 0, -1, 4),
                CaseSensitive = array_index_of(arr, "A"),
                NotFound = array_index_of(arr, "z"),
                WithStart = array_index_of(arr, "a", 1),
                WithStartAndLengthHit = array_index_of(arr, "c", 1, 3),
                WithStartAndLengthMiss = array_index_of(arr, "c", 1, 2),
                ZeroLength = array_index_of(arr, "a", 0, 0),
                NegStartFound = array_index_of(arr, "a", -2),
                NegStartSecond = array_index_of(arr, "a", -4, 3, 2),
                MatchBag = array_index_of(dynamic([{"k": 1}, {"k": 2}]), dynamic({"k": 2})),
                BadOcc = array_index_of(arr, "a", 0, -1, -1),
                BadLen = array_index_of(arr, "a", 0, -2),
                NonArray = array_index_of(BagDyn, "a"),
                NullArray = array_index_of(NullDyn, "a")
            """);

        AssertLongEqual(row["First"], 0L);
        AssertLongEqual(row["Second"], 2L);
        AssertLongEqual(row["Third"], 4L);
        AssertLongEqual(row["FourthMissing"], -1L);
        AssertLongEqual(row["CaseSensitive"], 5L);
        AssertLongEqual(row["NotFound"], -1L);
        AssertLongEqual(row["WithStart"], 2L);
        AssertLongEqual(row["WithStartAndLengthHit"], 3L);
        AssertLongEqual(row["WithStartAndLengthMiss"], -1L);
        AssertLongEqual(row["ZeroLength"], -1L);
        AssertLongEqual(row["NegStartFound"], 4L);
        AssertLongEqual(row["NegStartSecond"], 4L);
        AssertLongEqual(row["MatchBag"], 1L);
        Assert.That(row["BadOcc"], Is.Null);
        Assert.That(row["BadLen"], Is.Null);
        AssertLongEqual(row["NonArray"], -1L);
        Assert.That(row["NullArray"], Is.Null);
    }

    // =========================================================================
    // 5. array_reverse()
    // =========================================================================

    [Test]
    public void ArrayReverse_ReversesTopLevelOrderOnly_AndReturnsNullForNonArrays()
    {
        var nonArrays = CreateTable("NonArrays", [
            new Column<object>("NullDyn", isNullable: true),
            new Column<object>("BagDyn", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "NullDyn", null }, { "BagDyn", Bag(("a", 1L)) } }
        ]);
        var db = CreateDatabase(nonArrays);

        var row = ExecuteSingleRow(db, """
            NonArrays
            | project
                Basic = array_reverse(dynamic([1, "two", null, [10, 20]])),
                Single = array_reverse(dynamic(["only"])),
                Empty = array_reverse(dynamic([])),
                FromBag = array_reverse(BagDyn),
                FromNull = array_reverse(NullDyn)
            """);

        AssertDynamicArrayEqual(row["Basic"], Arr(10L, 20L), null, "two", 1L);
        AssertDynamicArrayEqual(row["Single"], "only");
        AssertDynamicArrayEqual(row["Empty"]);
        Assert.That(row["FromBag"], Is.Null);
        Assert.That(row["FromNull"], Is.Null);
    }

    // =========================================================================
    // 6. array_sort_asc() and array_sort_desc()
    // =========================================================================

    [Test]
    public void ArraySortAscAndDesc_SingleArrayNumbersStringsDatesNullPlacementAndMixedTypes()
    {
        var nonArrays = CreateTable("NonArrays", [
            new Column<object>("NullDyn", isNullable: true),
            new Column<object>("BagDyn", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "NullDyn", null }, { "BagDyn", Bag(("a", 1L)) } }
        ]);
        var db = CreateDatabase(nonArrays);

        var row = ExecuteSingleRow(db, """
            NonArrays
            | project
                AscNums = array_sort_asc(dynamic([3, 1, 2, 1])),
                DescNums = array_sort_desc(dynamic([3, 1, 2, 1])),
                AscStr = array_sort_asc(dynamic(["cherry", "apple", "banana"])),
                DescStr = array_sort_desc(dynamic(["cherry", "apple", "banana"])),
                AscDates = array_sort_asc(dynamic([datetime(2025-08-25T00:00:00Z), datetime(2025-08-23T00:00:00Z), datetime(2025-08-24T00:00:00Z)])),
                AscNullsLastDefault = array_sort_asc(dynamic([3, null, 1, null, 2])),
                DescNullsLastDefault = array_sort_desc(dynamic([3, null, 1, null, 2])),
                AscNullsFirst = array_sort_asc(dynamic([3, null, 1, null, 2]), false),
                DescNullsFirst = array_sort_desc(dynamic([3, null, 1, null, 2]), false),
                AscMixed = array_sort_asc(dynamic(["b", 10, "a", 2])),
                EmptyAsc = array_sort_asc(dynamic([])),
                NullAsc = array_sort_asc(NullDyn),
                BagDesc = array_sort_desc(BagDyn)
            """);

        AssertDynamicArrayEqual(row["AscNums"], 1L, 1L, 2L, 3L);
        AssertDynamicArrayEqual(row["DescNums"], 3L, 2L, 1L, 1L);
        AssertDynamicArrayEqual(row["AscStr"], "apple", "banana", "cherry");
        AssertDynamicArrayEqual(row["DescStr"], "cherry", "banana", "apple");
        AssertDynamicArrayEqual(row["AscDates"],
            new DateTime(2025, 8, 23, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 8, 24, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 8, 25, 0, 0, 0, DateTimeKind.Utc));
        AssertDynamicArrayEqual(row["AscNullsLastDefault"], 1L, 2L, 3L, null, null);
        AssertDynamicArrayEqual(row["DescNullsLastDefault"], 3L, 2L, 1L, null, null);
        AssertDynamicArrayEqual(row["AscNullsFirst"], null, null, 1L, 2L, 3L);
        AssertDynamicArrayEqual(row["DescNullsFirst"], null, null, 3L, 2L, 1L);
        AssertDynamicArrayEqual(row["AscMixed"], 2L, 10L, "a", "b");
        AssertDynamicArrayEqual(row["EmptyAsc"]);
        Assert.That(row["NullAsc"], Is.Null);
        Assert.That(row["BagDesc"], Is.Null);
    }

    [Test]
    public void ArraySortAscAndDesc_MultipleParallelArrays_ReordersLinkedArraysAndNullsMismatchedLengths()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                Keys = dynamic([30, 10, 20]),
                Names = dynamic(["thirty", "ten", "twenty"]),
                Short = dynamic(["too-short"])
            | extend (SortedKeysAsc, SortedNamesAsc) = array_sort_asc(Keys, Names)
            | extend (SortedKeysDesc, SortedNamesDesc, Mismatched) = array_sort_desc(Keys, Names, Short)
            | extend IndexedSecond = array_sort_asc(Keys, Names)[1]
            | project SortedKeysAsc, SortedNamesAsc, SortedKeysDesc, SortedNamesDesc, Mismatched, IndexedSecond
            """);

        AssertDynamicArrayEqual(row["SortedKeysAsc"], 10L, 20L, 30L);
        AssertDynamicArrayEqual(row["SortedNamesAsc"], "ten", "twenty", "thirty");
        AssertDynamicArrayEqual(row["SortedKeysDesc"], 30L, 20L, 10L);
        AssertDynamicArrayEqual(row["SortedNamesDesc"], "thirty", "twenty", "ten");
        Assert.That(row["Mismatched"], Is.Null, "Linked array whose length differs from the first array must become null.");
        AssertDynamicArrayEqual(row["IndexedSecond"], "ten", "twenty", "thirty");
    }

    // =========================================================================
    // 7. array_sum()
    // =========================================================================

    [Test]
    public void ArraySum_SumsOnlyNumericElementsAndReturnsNullForNonArrays()
    {
        var nonArrays = CreateTable("NonArrays", [
            new Column<object>("NullDyn", isNullable: true),
            new Column<object>("BagDyn", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "NullDyn", null }, { "BagDyn", Bag(("a", 1L)) } }
        ]);
        var db = CreateDatabase(nonArrays);

        var row = ExecuteSingleRow(db, """
            NonArrays
            | project
                Ints = array_sum(dynamic([1, 2, 3, 4])),
                MixedNum = array_sum(dynamic([1, 2.5, -0.5, 10])),
                Single = array_sum(dynamic([42])),
                WithStr = array_sum(dynamic([1, "2", 3])),
                WithBool = array_sum(dynamic([1, true])),
                WithNull = array_sum(dynamic([1, null, 2])),
                WithNested = array_sum(dynamic([1, [2]])),
                FromBag = array_sum(BagDyn),
                FromNull = array_sum(NullDyn)
            """);

        AssertNumericEqual(row["Ints"], 10.0);
        AssertNumericEqual(row["MixedNum"], 13.0);
        AssertNumericEqual(row["Single"], 42.0);
        AssertNumericEqual(row["WithStr"], 4.0);
        AssertNumericEqual(row["WithBool"], 1.0);
        AssertNumericEqual(row["WithNull"], 3.0);
        AssertNumericEqual(row["WithNested"], 1.0);
        Assert.That(row["FromBag"], Is.Null);
        Assert.That(row["FromNull"], Is.Null);
    }

    [Test]
    public void ArraySum_GetSchemaReportsRealType()
    {
        var db = CreateDatabase();

        var schemaRows = Execute(db, """
            print Total = array_sum(dynamic([1, 2, 3]))
            | getschema
            """);

        AssertSchemaColumn(schemaRows, "Total", "real");
    }

    // =========================================================================
    // 8. array_iff() / array_iif()
    // =========================================================================

    [Test]
    public void ArrayIff_SupportsBoolNumericMixedConditionsScalarBroadcastAndLengthMismatch()
    {
        var nonArrays = CreateTable("NonArrays", [
            new Column<object>("NullDyn", isNullable: true),
            new Column<object>("BoolDyn", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "NullDyn", null }, { "BoolDyn", true } }
        ]);
        var db = CreateDatabase(nonArrays);

        var row = ExecuteSingleRow(db, """
            NonArrays
            | project
                BoolCond = array_iff(dynamic([true, false, true]), dynamic([1, 2, 3]), dynamic([10, 20, 30])),
                NumCond = array_iff(dynamic([1, 0, -5, 0.0, 2.5]), dynamic(["t0", "t1", "t2", "t3", "t4"]), dynamic(["f0", "f1", "f2", "f3", "f4"])),
                InvalidCondSlots = array_iff(dynamic([true, null, "not-bool", false, {}]), dynamic([1, 2, 3, 4, 5]), dynamic([10, 20, 30, 40, 50])),
                ScalarBroadcast = array_iff(dynamic([true, false, true]), "YES", 0),
                ShorterAndLonger = array_iff(dynamic([true, false, true, false]), dynamic([1, 2]), dynamic([10, 20, 30, 40, 50])),
                AliasCheck = array_iif(dynamic([false, true]), "a", "b"),
                EmptyCond = array_iff(dynamic([]), dynamic([1]), dynamic([2])),
                NonArrayCond = array_iff(BoolDyn, dynamic([1]), dynamic([2])),
                NullCond = array_iff(NullDyn, dynamic([1]), dynamic([2]))
            """);

        AssertDynamicArrayEqual(row["BoolCond"], 1L, 20L, 3L);
        AssertDynamicArrayEqual(row["NumCond"], "t0", "f1", "t2", "f3", "t4");
        AssertDynamicArrayEqual(row["InvalidCondSlots"], 1L, null, null, 40L, null);
        AssertDynamicArrayEqual(row["ScalarBroadcast"], "YES", 0L, "YES");
        AssertDynamicArrayEqual(row["ShorterAndLonger"], 1L, 20L, null, 40L);
        AssertDynamicArrayEqual(row["AliasCheck"], "b", "a");
        AssertDynamicArrayEqual(row["EmptyCond"]);
        Assert.That(row["NonArrayCond"], Is.Null);
        Assert.That(row["NullCond"], Is.Null);
    }

    // =========================================================================
    // 9. Dynamic Set Operations: set_union, set_intersect, set_difference, set_has_element
    // =========================================================================

    [Test]
    public void SetUnion_DeduplicatesAcrossTwoAndMoreArraysAndHandlesNullsAndEmpty()
    {
        var nonArrays = CreateTable("NonArrays", [
            new Column<object>("NullDyn", isNullable: true),
            new Column<object>("BagDyn", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "NullDyn", null }, { "BagDyn", Bag(("a", 1L)) } }
        ]);
        var db = CreateDatabase(nonArrays);

        var row = ExecuteSingleRow(db, """
            NonArrays
            | project
                Two = set_union(dynamic([1, 2, 2, 3]), dynamic([3, 4, 4])),
                Three = set_union(dynamic(["a", "b"]), dynamic(["b", "c"]), dynamic(["c", "d"])),
                MixedAndNull = set_union(dynamic([1, "1", null]), dynamic([null, true, 1])),
                WithEmpty = set_union(dynamic([1, 2]), dynamic([])),
                BothEmpty = set_union(dynamic([]), dynamic([])),
                WithNullArg = set_union(dynamic([1]), NullDyn),
                WithBagArg = set_union(dynamic([1]), BagDyn)
            """);

        AssertDynamicSetEquivalent(row["Two"], 1L, 2L, 3L, 4L);
        AssertDynamicSetEquivalent(row["Three"], "a", "b", "c", "d");
        AssertDynamicSetEquivalent(row["MixedAndNull"], 1L, "1", true);
        AssertDynamicSetEquivalent(row["WithEmpty"], 1L, 2L);
        AssertDynamicSetEquivalent(row["BothEmpty"]);
        AssertDynamicSetEquivalent(row["WithNullArg"], 1L);
        AssertDynamicSetEquivalent(row["WithBagArg"], 1L);
    }

    [Test]
    public void SetIntersect_ReturnsDeduplicatedCommonElementsAcrossTwoAndThreeArrays()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                Two = set_intersect(dynamic([1, 2, 2, 3, 4]), dynamic([2, 2, 4, 5])),
                Three = set_intersect(dynamic(["a", "b", "c"]), dynamic(["b", "c", "d"]), dynamic(["c", "b", "e"])),
                Disjoint = set_intersect(dynamic([1, 2]), dynamic([3, 4])),
                WithEmpty = set_intersect(dynamic([1, 2]), dynamic([])),
                CaseSensitive = set_intersect(dynamic(["Alpha", "beta"]), dynamic(["alpha", "beta"])),
                WithNullArg = set_intersect(dynamic([1]), dynamic(null))
            """);

        AssertDynamicSetEquivalent(row["Two"], 2L, 4L);
        AssertDynamicSetEquivalent(row["Three"], "b", "c");
        AssertDynamicSetEquivalent(row["Disjoint"]);
        AssertDynamicSetEquivalent(row["WithEmpty"]);
        AssertDynamicSetEquivalent(row["CaseSensitive"], "beta");
        Assert.That(row["WithNullArg"], Is.Null);
    }

    [Test]
    public void SetDifference_IsNonCommutativeSupportsThreeArraysAndDeduplicates()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                AMinusB = set_difference(dynamic([1, 2, 2, 3, 4]), dynamic([2, 4, 5])),
                BMinusA = set_difference(dynamic([2, 4, 5]), dynamic([1, 2, 3, 4])),
                ThreeWay = set_difference(dynamic([1, 2, 3, 4, 5]), dynamic([2, 3]), dynamic([5])),
                AllRemoved = set_difference(dynamic([1, 2]), dynamic([1, 2, 3])),
                EmptyLeft = set_difference(dynamic([]), dynamic([1, 2])),
                EmptyRight = set_difference(dynamic([1, 1, 2]), dynamic([])),
                WithNullArg = set_difference(dynamic([1]), dynamic(null))
            """);

        AssertDynamicSetEquivalent(row["AMinusB"], 1L, 3L);
        AssertDynamicSetEquivalent(row["BMinusA"], 5L);
        AssertDynamicSetEquivalent(row["ThreeWay"], 1L, 4L);
        AssertDynamicSetEquivalent(row["AllRemoved"]);
        AssertDynamicSetEquivalent(row["EmptyLeft"]);
        AssertDynamicSetEquivalent(row["EmptyRight"], 1L, 2L);
        AssertDynamicSetEquivalent(row["WithNullArg"], 1L);
    }

    [Test]
    public void SetHasElement_MatchesExactElementsAcrossTypesCaseSensitively()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            let arr = dynamic(["alpha", "error-404", 42, 3.5, true]);
            print
                StrHit = set_has_element(arr, "alpha"),
                StrCaseMiss = set_has_element(arr, "Alpha"),
                SubstringMiss = set_has_element(arr, "error"),
                LongHit = set_has_element(arr, 42),
                RealHit = set_has_element(arr, 3.5),
                BoolHit = set_has_element(arr, true),
                BoolMiss = set_has_element(arr, false),
                TypeMismatch = set_has_element(arr, "42"),
                EmptyArr = set_has_element(dynamic([]), "alpha")
            """);

        Assert.That(row["StrHit"], Is.EqualTo(true));
        Assert.That(row["StrCaseMiss"], Is.EqualTo(false));
        Assert.That(row["SubstringMiss"], Is.EqualTo(false), "set_has_element must check exact element membership, not term search.");
        Assert.That(row["LongHit"], Is.EqualTo(true));
        Assert.That(row["RealHit"], Is.EqualTo(true));
        Assert.That(row["BoolHit"], Is.EqualTo(true));
        Assert.That(row["BoolMiss"], Is.EqualTo(false));
        Assert.That(row["TypeMismatch"], Is.EqualTo(false));
        Assert.That(row["EmptyArr"], Is.EqualTo(false));
    }

    // =========================================================================
    // 10. Dynamic Arrays on the Right Side of in / !in / in~ / !in~
    // =========================================================================

    [Test]
    public void InOperator_WithDynamicArrayLiteralAndLetVariable_SupportsCaseSensitivityAndNegation()
    {
        var table = CreateTable("Events", [
            new Column<int>("Id", isNullable: false),
            new Column<string>("Tag", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Tag", "alpha" } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Tag", "ALPHA" } },
            new Dictionary<string, object?> { { "Id", 3 }, { "Tag", "beta" } },
            new Dictionary<string, object?> { { "Id", 4 }, { "Tag", "alpha-1" } },
            new Dictionary<string, object?> { { "Id", 5 }, { "Tag", null } }
        ]);
        var db = CreateDatabase(table);

        var csMatches = Execute(db, """
            let allowed = dynamic(["alpha", "beta"]);
            Events
            | where Tag in (allowed)
            | sort by Id asc
            | project Id
            """);
        Assert.That(csMatches.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 1, 3 }));

        var ciMatches = Execute(db, """
            Events
            | where Tag in~ (dynamic(["alpha", "beta"]))
            | sort by Id asc
            | project Id
            """);
        Assert.That(ciMatches.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 1, 2, 3 }));

        var notInMatches = Execute(db, """
            Events
            | where Tag !in (dynamic(["alpha", "beta"]))
            | sort by Id asc
            | project Id
            """);
        Assert.That(notInMatches.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 2, 4, 5 }));

        var emptyRhs = Execute(db, """
            Events
            | where Tag in (dynamic([]))
            | project Id
            """);
        Assert.That(emptyRhs, Is.Empty);
    }

    [Test]
    public void InOperator_WithDynamicColumnPerRowAndNestedArrayFlattening()
    {
        var table = CreateTable("Access", [
            new Column<int>("Id", isNullable: false),
            new Column<string>("Role", isNullable: false),
            new Column<object>("AllowedRoles", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Role", "admin" }, { "AllowedRoles", Arr("reader", "admin") } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Role", "writer" }, { "AllowedRoles", Arr("reader", "viewer") } },
            new Dictionary<string, object?> { { "Id", 3 }, { "Role", "auditor" }, { "AllowedRoles", Arr("reader", Arr("auditor", "secops")) } },
            new Dictionary<string, object?> { { "Id", 4 }, { "Role", "admin" }, { "AllowedRoles", Arr() } },
            new Dictionary<string, object?> { { "Id", 5 }, { "Role", "admin" }, { "AllowedRoles", null } }
        ]);
        var db = CreateDatabase(table);

        ExecuteAndExpectError(db, """
            Access
            | where Role in (AllowedRoles)
            | sort by Id asc
            | project Id
            """);

        var matches = Execute(db, "Access | where set_has_element(AllowedRoles, Role) | project Id");
        Assert.That(matches.Select(r => (int)r["Id"]!), Is.EquivalentTo(new[] { 1 }));

        var nestedNumeric = Execute(db, """
            datatable(N:long)[1, 2, 3, 4]
            | where N in (dynamic([1, [2, 3]]))
            | sort by N asc
            | project N
            """);
        Assert.That(nestedNumeric.Select(r => Convert.ToInt64(r["N"])).ToList(), Is.EqualTo(new[] { 1L, 2L, 3L }));
    }
}
