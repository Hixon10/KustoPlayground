using static KustoPlayground.Core.Tests.DynamicNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// TDD specification tests for:
/// - mv-expand on arrays (repeating columns, null/empty/scalar rules, with_itemindex, limit,
///   cast-only to typeof(...), alias vs replace, parallel zipping with null padding, chained Cartesian product)
/// - mv-expand on property bags (kind=bag / bagexpansion=bag single-entry bags, kind=array / bagexpansion=array
///   [key, value] pairs, with_itemindex, limit, parallel bag/array expansion)
/// - mv-apply operator (per-row subquery isolation e.g. top 2 per row, filtering, sorting, aggregation,
///   outer column access, with_itemindex, limit, to typeof(...), parallel arrays, aliased vs unaliased,
///   empty/null edge cases, inner mv-expand for bags)
/// </summary>
public class DynamicNeonMvExpandAndMvApplyTests
{
    private static readonly DateTime SampleUtc = new(2025, 8, 23, 6, 20, 0, DateTimeKind.Utc);
    private static readonly Guid SampleGuid = new("74be27de-1e4e-49d9-b579-fe0b331d3642");

    // =========================================================================
    // 1. mv-expand for Arrays
    // =========================================================================

    [Test]
    public void MvExpand_Array_RepeatsOtherColumns_HandlesNullEmptyScalarAndMixedTypes()
    {
        var table = CreateTable("T", [
            new Column<int>("Id", isNullable: false),
            new Column<string>("Label", isNullable: false),
            new Column<object>("Items", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Label", "arr" }, { "Items", Arr(10L, "two", true) } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Label", "empty" }, { "Items", Arr() } },
            new Dictionary<string, object?> { { "Id", 3 }, { "Label", "nullRow" }, { "Items", null } },
            new Dictionary<string, object?> { { "Id", 4 }, { "Label", "scalar" }, { "Items", "solo" } },
            new Dictionary<string, object?> { { "Id", 5 }, { "Label", "withNullElem" }, { "Items", Arr("x", null) } }
        ], shuffle: false);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            T
            | mv-expand Items
            """);

        // Id=1 -> 3 rows, Id=2 ([]) -> 0 rows, Id=3 (null) -> 1 row (null), Id=4 (scalar) -> 1 row, Id=5 -> 2 rows => 7 total
        Assert.That(rows, Has.Count.EqualTo(7));

        var byId = rows.GroupBy(r => Convert.ToInt32(r["Id"])).ToDictionary(g => g.Key, g => g.ToList());
        Assert.That(byId.ContainsKey(2), Is.False, "Empty array [] must emit 0 rows.");

        Assert.That(byId[1], Has.Count.EqualTo(3));
        Assert.That(byId[1].All(r => (string?)r["Label"] == "arr"), Is.True);
        AssertDynamicEqual(byId[1][0]["Items"], 10L);
        AssertDynamicEqual(byId[1][1]["Items"], "two");
        AssertDynamicEqual(byId[1][2]["Items"], true);

        Assert.That(byId[3], Has.Count.EqualTo(1), "Single-input null must emit exactly 1 row.");
        Assert.That(byId[3][0]["Label"], Is.EqualTo("nullRow"));
        Assert.That(byId[3][0]["Items"], Is.Null);

        Assert.That(byId[4], Has.Count.EqualTo(1), "Scalar dynamic must emit 1 row.");
        AssertDynamicEqual(byId[4][0]["Items"], "solo");

        Assert.That(byId[5], Has.Count.EqualTo(2));
        AssertDynamicEqual(byId[5][0]["Items"], "x");
        Assert.That(byId[5][1]["Items"], Is.Null);
    }

    [Test]
    public void MvExpand_Array_WithItemIndex_AddsZeroBasedLongIndexResettingPerRow()
    {
        var table = CreateKeyedDynamicTable("T", [
            (1, Arr("a", "b", "c")),
            (2, Arr("x", "y"))
        ], shuffle: false);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            T
            | mv-expand with_itemindex=Idx Payload
            | sort by Id asc, Idx asc
            """);

        Assert.That(rows, Has.Count.EqualTo(5));
        var expected = new (int Id, long Idx, string Val)[]
        {
            (1, 0, "a"), (1, 1, "b"), (1, 2, "c"),
            (2, 0, "x"), (2, 1, "y")
        };

        for (int i = 0; i < expected.Length; i++)
        {
            AssertLongEqual(rows[i]["Id"], expected[i].Id);
            AssertLongEqual(rows[i]["Idx"], expected[i].Idx);
            AssertDynamicEqual(rows[i]["Payload"], expected[i].Val);
        }

        var schemaRows = Execute(db, "T | mv-expand with_itemindex=Idx Payload | getschema");
        AssertSchemaColumn(schemaRows, "Idx", "long");
        AssertSchemaColumn(schemaRows, "Payload", "dynamic");
    }

    [Test]
    public void MvExpand_Array_LimitIsPerInputRowNotGlobal()
    {
        var table = CreateKeyedDynamicTable("T", [
            (1, Arr(10L, 20L, 30L, 40L)),
            (2, Arr(100L, 200L, 300L)),
            (3, Arr(999L))
        ], shuffle: false);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            T
            | mv-expand with_itemindex=Idx Payload limit 2
            | sort by Id asc, Idx asc
            """);

        // Row 1 capped at 2, Row 2 capped at 2, Row 3 has 1 => 5 total rows
        Assert.That(rows, Has.Count.EqualTo(5));
        var id1 = rows.Where(r => Convert.ToInt32(r["Id"]) == 1).Select(r => Convert.ToInt64(r["Payload"])).ToList();
        var id2 = rows.Where(r => Convert.ToInt32(r["Id"]) == 2).Select(r => Convert.ToInt64(r["Payload"])).ToList();
        var id3 = rows.Where(r => Convert.ToInt32(r["Id"]) == 3).Select(r => Convert.ToInt64(r["Payload"])).ToList();

        Assert.That(id1, Is.EqualTo(new[] { 10L, 20L }));
        Assert.That(id2, Is.EqualTo(new[] { 100L, 200L }));
        Assert.That(id3, Is.EqualTo(new[] { 999L }));
    }

    [Test]
    public void MvExpand_Array_ToTypeof_IsCastOnlyNotConversion()
    {
        var table = CreateKeyedDynamicTable("T", [
            (1, Arr(10L, "20", true, 3.5, null, SampleUtc, SampleGuid))
        ], shuffle: false);
        var db = CreateDatabase(table);

        // Cast to long: only numeric/compatible integer 10L survives; "20" MUST become null (cast-only, not tolong!)
        var longRows = Execute(db, """
            T
            | mv-expand with_itemindex=Idx Val = Payload to typeof(long)
            | sort by Idx asc
            | project Idx, Val
            """);

        Assert.That(longRows, Has.Count.EqualTo(7));
        AssertLongEqual(longRows[0]["Val"], 10L);
        Assert.That(longRows[1]["Val"], Is.Null, "to typeof(long) is cast-only: string '20' must become null, not 20.");
        Assert.That(longRows[2]["Val"], Is.Null, "to typeof(long) is cast-only: bool true must become null.");
        Assert.That(longRows[4]["Val"], Is.Null);
        Assert.That(longRows[5]["Val"], Is.Null);
        Assert.That(longRows[6]["Val"], Is.Null);

        var longSchema = Execute(db, "T | mv-expand Val = Payload to typeof(long) | getschema");
        AssertSchemaColumn(longSchema, "Val", "long");
        AssertSchemaColumn(longSchema, "Payload", "dynamic"); // aliased -> original column preserved!

        // Unlike a numeric target, the string target formats scalar values.
        var strRows = Execute(db, """
            T
            | mv-expand with_itemindex=Idx Val = Payload to typeof(string)
            | sort by Idx asc
            | project Idx, Val
            """);
        Assert.That(strRows[1]["Val"], Is.EqualTo("20"));
        Assert.That(strRows[0]["Val"], Is.EqualTo("10"));

        // Cast to datetime and guid
        var dtRows = Execute(db, """
            T
            | mv-expand with_itemindex=Idx Val = Payload to typeof(datetime)
            | sort by Idx asc
            | project Idx, Val
            """);
        Assert.That(dtRows[0]["Val"], Is.Null);
        AssertDateTimeEqual(dtRows[5]["Val"], SampleUtc);

        var guidRows = Execute(db, """
            T
            | mv-expand with_itemindex=Idx Val = Payload to typeof(guid)
            | sort by Idx asc
            | project Idx, Val
            """);
        Assert.That(guidRows[0]["Val"], Is.Null);
        AssertGuidEqual(guidRows[6]["Val"], SampleGuid);
    }

    [Test]
    public void MvExpand_Array_AliasPreservesOriginalColumnAndSupportsInlineExpression()
    {
        var table = CreateKeyedDynamicTable("T", [
            (1, Bag(("tags", Arr("a", "b"))))
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            T
            | mv-expand with_itemindex=Idx Tag = Payload.tags to typeof(string)
            | sort by Idx asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].ContainsKey("Payload"), Is.True, "Aliased mv-expand must preserve the original column.");
        Assert.That(rows[0]["Tag"], Is.EqualTo("a"));
        Assert.That(rows[1]["Tag"], Is.EqualTo("b"));
    }

    [Test]
    public void MvExpand_ParallelMultipleArrays_AlignsByIndexWithNullPadding_VsChainedCartesianProduct()
    {
        var table = CreateTable("Pairs", [
            new Column<int>("Id", isNullable: false),
            new Column<object>("A", isNullable: true),
            new Column<object>("B", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "A", Arr(1L, 2L, 3L) }, { "B", Arr("x", "y") } },
            new Dictionary<string, object?> { { "Id", 2 }, { "A", Arr() }, { "B", Arr("only1", "only2") } },
            new Dictionary<string, object?> { { "Id", 3 }, { "A", Arr() }, { "B", Arr() } },
            new Dictionary<string, object?> { { "Id", 4 }, { "A", null }, { "B", null } }
        ], shuffle: false);
        var db = CreateDatabase(table);

        // Parallel zipping
        var zipped = Execute(db, """
            Pairs
            | mv-expand with_itemindex=Idx A, B
            | sort by Id asc, Idx asc
            """);

        // Id=1: 3 rows (3rd has B=null); Id=2: 2 rows (A=null); Id=3: 0 rows; Id=4: 1 row (null, null) => 6 rows
        Assert.That(zipped, Has.Count.EqualTo(6));
        Assert.That(zipped.Any(r => Convert.ToInt32(r["Id"]) == 3), Is.False, "Both empty arrays [] must emit 0 rows.");

        var r1 = zipped.Where(r => Convert.ToInt32(r["Id"]) == 1).ToList();
        Assert.That(r1, Has.Count.EqualTo(3));
        AssertDynamicEqual(r1[0]["A"], 1L); AssertDynamicEqual(r1[0]["B"], "x");
        AssertDynamicEqual(r1[1]["A"], 2L); AssertDynamicEqual(r1[1]["B"], "y");
        AssertDynamicEqual(r1[2]["A"], 3L); Assert.That(r1[2]["B"], Is.Null);

        var r2 = zipped.Where(r => Convert.ToInt32(r["Id"]) == 2).ToList();
        Assert.That(r2, Has.Count.EqualTo(2));
        Assert.That(r2[0]["A"], Is.Null); AssertDynamicEqual(r2[0]["B"], "only1");
        Assert.That(r2[1]["A"], Is.Null); AssertDynamicEqual(r2[1]["B"], "only2");

        var r4 = zipped.Where(r => Convert.ToInt32(r["Id"]) == 4).ToList();
        Assert.That(r4, Has.Count.EqualTo(1));
        Assert.That(r4[0]["A"], Is.Null);
        Assert.That(r4[0]["B"], Is.Null);

        // Chained mv-expand -> Cartesian product on Id=1 (3 x 2 = 6 rows)
        var cartesian = Execute(db, """
            Pairs
            | where Id == 1
            | mv-expand A
            | mv-expand B
            """);
        Assert.That(cartesian, Has.Count.EqualTo(6));
    }

    [Test]
    public void MvExpand_NestedArrays_ExpandsOneLevelPerOperator()
    {
        var table = CreateKeyedDynamicTable("Matrix", [
            (1, Arr(Arr(1L, 2L), Arr(3L)))
        ]);
        var db = CreateDatabase(table);

        var oneLevel = Execute(db, "Matrix | mv-expand Payload");
        Assert.That(oneLevel, Has.Count.EqualTo(2));
        AssertDynamicArrayEqual(oneLevel[0]["Payload"], 1L, 2L);
        AssertDynamicArrayEqual(oneLevel[1]["Payload"], 3L);

        var twoLevels = Execute(db, "Matrix | mv-expand Payload | mv-expand Payload to typeof(long) | sort by Payload asc");
        Assert.That(twoLevels, Has.Count.EqualTo(3));
        Assert.That(twoLevels.Select(r => Convert.ToInt64(r["Payload"])).ToList(), Is.EqualTo(new[] { 1L, 2L, 3L }));
    }

    // =========================================================================
    // 2. mv-expand for Property Bags
    // =========================================================================

    [Test]
    public void MvExpand_PropertyBag_DefaultKindBag_EmitsSingleEntryBagsAndHandlesEmptyAndNull()
    {
        var table = CreateKeyedDynamicTable("BagTable", [
            (1, Bag(("k1", 10L), ("k2", null))),
            (2, Bag()),
            (3, null)
        ], shuffle: false);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            BagTable
            | mv-expand Payload
            """);

        // Id=1 -> 2 single-entry bags; Id=2 ({}) -> 0 rows; Id=3 (null) -> 1 row (null) => 3 total rows
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.Any(r => Convert.ToInt32(r["Id"]) == 2), Is.False, "Empty bag {} must emit 0 rows.");

        var id1Bags = rows.Where(r => Convert.ToInt32(r["Id"]) == 1).Select(r => ExtractBag(r["Payload"])).ToList();
        Assert.That(id1Bags, Has.Count.EqualTo(2));
        Assert.That(id1Bags.Any(b => b.Count == 1 && b.ContainsKey("k1") && AreDynamicEqual(b["k1"], 10L)), Is.True);
        Assert.That(id1Bags.Any(b => b.Count == 1 && b.ContainsKey("k2") && b["k2"] is null), Is.True,
            "Property with explicit null value must emit its single-entry bag {k2: null}.");

        var id3 = rows.Single(r => Convert.ToInt32(r["Id"]) == 3);
        Assert.That(id3["Payload"], Is.Null);
    }

    [Test]
    public void MvExpand_PropertyBag_KindArrayAndLegacyBagExpansionArray_EmitsKeyValuePairs()
    {
        var table = CreateKeyedDynamicTable("BagTable", [
            (1, Bag(("cpu", 80L), ("mem", 64L), ("note", null)))
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            BagTable
            | mv-expand kind=array with_itemindex=Idx Entry = Payload
            | extend Key = tostring(Entry[0]), Val = Entry[1]
            | sort by Key asc
            | project Id, Idx, Key, Val
            """);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.Select(r => r["Key"]).ToList(), Is.EqualTo(new[] { "cpu", "mem", "note" }));
        AssertLongEqual(rows[0]["Val"], 80L);
        AssertLongEqual(rows[1]["Val"], 64L);
        Assert.That(rows[2]["Val"], Is.Null);
        Assert.That(rows.Select(r => Convert.ToInt64(r["Idx"])).OrderBy(x => x).ToList(), Is.EqualTo(new[] { 0L, 1L, 2L }));

        // Legacy syntax bagexpansion=array
        var legacyRows = Execute(db, """
            BagTable
            | mv-expand bagexpansion=array Payload
            | extend Key = tostring(Payload[0])
            | sort by Key asc
            | project Key
            """);
        Assert.That(legacyRows.Select(r => r["Key"]).ToList(), Is.EqualTo(new[] { "cpu", "mem", "note" }));
    }

    [Test]
    public void MvExpand_PropertyBag_LimitAndParallelBagAndArrayExpansion()
    {
        var table = CreateTable("MixedExpand", [
            new Column<int>("Id", isNullable: false),
            new Column<object>("Props", isNullable: true),
            new Column<object>("Nums", isNullable: true)
        ], [
            new Dictionary<string, object?>
            {
                { "Id", 1 },
                { "Props", Bag(("a", 1L), ("b", 2L), ("c", 3L)) },
                { "Nums", Arr(100L, 200L) }
            }
        ]);
        var db = CreateDatabase(table);

        var limited = Execute(db, """
            MixedExpand
            | mv-expand kind=bag Props limit 2
            """);
        Assert.That(limited, Has.Count.EqualTo(2));

        var parallel = Execute(db, """
            MixedExpand
            | mv-expand kind=array Props, Nums
            """);
        Assert.That(parallel, Has.Count.EqualTo(3));
        Assert.That(parallel.Count(r => r["Nums"] is null), Is.EqualTo(1),
            "Shorter array must be null-padded when expanded in parallel with a larger property bag.");
    }

    // =========================================================================
    // 3. mv-apply Operator
    // =========================================================================

    [Test]
    public void MvApply_Top2InsideSubquery_ReturnsUpToTwoElementsPerInputRowNotGlobally()
    {
        var table = CreateKeyedDynamicTable("Scores", [
            (1, Arr(10L, 50L, 30L, 40L)),
            (2, Arr(5L, 90L, 70L)),
            (3, Arr(8L))
        ], dynamicColumn: "Vals");
        var db = CreateDatabase(table);

        // Without summarize inside mv-apply: should emit 2 + 2 + 1 = 5 rows total (NOT 2 globally!)
        var expanded = Execute(db, """
            Scores
            | mv-apply V = Vals to typeof(long) on (
                top 2 by V desc
            )
            | sort by Id asc, V desc
            | project Id, V
            """);

        Assert.That(expanded, Has.Count.EqualTo(5),
            "top 2 inside mv-apply must return up to 2 elements per input row, not 2 overall.");
        Assert.That(
            expanded.Select(r => (Convert.ToInt32(r["Id"]), Convert.ToInt64(r["V"]))).ToList(),
            Is.EqualTo(new[] { (1, 50L), (1, 40L), (2, 90L), (2, 70L), (3, 8L) }));

        // With summarize inside mv-apply: one row per input row containing its own Top2 list and Sum
        var summarized = Execute(db, """
            Scores
            | mv-apply V = Vals to typeof(long) on (
                top 2 by V desc
                | summarize Top2 = make_list(V), Top2Sum = sum(V)
            )
            | sort by Id asc
            | project Id, Top2, Top2Sum
            """);

        Assert.That(summarized, Has.Count.EqualTo(3));
        AssertDynamicArrayEqual(summarized[0]["Top2"], 50L, 40L);
        AssertLongEqual(summarized[0]["Top2Sum"], 90L);
        AssertDynamicArrayEqual(summarized[1]["Top2"], 90L, 70L);
        AssertLongEqual(summarized[1]["Top2Sum"], 160L);
        AssertDynamicArrayEqual(summarized[2]["Top2"], 8L);
        AssertLongEqual(summarized[2]["Top2Sum"], 8L);
    }

    [Test]
    public void MvApply_SubqueryReferencesOuterRowColumns_AndSupportsFilteringAndAliasing()
    {
        var table = CreateTable("Sensors", [
            new Column<int>("Id", isNullable: false),
            new Column<long>("Threshold", isNullable: false),
            new Column<object>("Readings", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Threshold", 20L }, { "Readings", Arr(10L, 25L, 30L, 15L) } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Threshold", 50L }, { "Readings", Arr(10L, 20L) } },
            new Dictionary<string, object?> { { "Id", 3 }, { "Threshold", 5L }, { "Readings", Arr(6L, 12L) } }
        ]);
        var db = CreateDatabase(table);

        // Aliased: keeps Readings column; row 2 has no elements > 50 so its subquery returns 0 rows and row 2 is dropped
        var filtered = Execute(db, """
            Sensors
            | mv-apply R = Readings to typeof(long) on (
                where R > Threshold
                | sort by R asc
                | project Exceeding = R, Delta = R - Threshold
            )
            | sort by Id asc, Exceeding asc
            """);

        Assert.That(filtered, Has.Count.EqualTo(4));
        Assert.That(filtered.Any(r => Convert.ToInt32(r["Id"]) == 2), Is.False);
        Assert.That(filtered[0].ContainsKey("Readings"), Is.True, "Aliased mv-apply must retain the source column.");
        Assert.That(
            filtered.Select(r => (Convert.ToInt32(r["Id"]), Convert.ToInt64(r["Exceeding"]), Convert.ToInt64(r["Delta"]))).ToList(),
            Is.EqualTo(new[] { (1, 25L, 5L), (1, 30L, 10L), (3, 6L, 1L), (3, 12L, 7L) }));

        // Unaliased: removes source column Readings from outer row unless projected by subquery
        var unaliased = Execute(db, """
            Sensors
            | where Id == 1
            | mv-apply Readings to typeof(long) on (
                summarize MaxReading = max(Readings)
            )
            """);

        Assert.That(unaliased, Has.Count.EqualTo(1));
        Assert.That(unaliased[0].ContainsKey("Readings"), Is.False, "Unaliased mv-apply removes the expanded column from outer row.");
        AssertLongEqual(unaliased[0]["MaxReading"], 30L);
    }

    [Test]
    public void MvApply_WithItemIndexAndLimitAndCastOnlyTypeof()
    {
        var table = CreateKeyedDynamicTable("Indexed", [
            (1, Arr(10L, "bad", 30L, 40L, 50L)),
            (2, Arr(100L, 200L, 300L))
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Indexed
            | mv-apply with_itemindex=Idx V = Payload to typeof(long) limit 4 on (
                where isnotnull(V) and Idx % 2 == 0
                | summarize EvenValues = make_list(V), EvenIndexes = make_list(Idx)
            )
            | sort by Id asc
            | project Id, EvenValues, EvenIndexes
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        // Row 1 limited to first 4 elements: (0:10), (1:null), (2:30), (3:40) -> even non-null: 10 at 0, 30 at 2
        AssertDynamicArrayEqual(rows[0]["EvenValues"], 10L, 30L);
        AssertDynamicArrayEqual(rows[0]["EvenIndexes"], 0L, 2L);
        // Row 2: (0:100), (1:200), (2:300) -> even non-null: 100 at 0, 300 at 2
        AssertDynamicArrayEqual(rows[1]["EvenValues"], 100L, 300L);
        AssertDynamicArrayEqual(rows[1]["EvenIndexes"], 0L, 2L);
    }

    [Test]
    public void MvApply_ParallelMultipleArraysWithNullPadding()
    {
        var table = CreateTable("Paired", [
            new Column<int>("Id", isNullable: false),
            new Column<object>("Keys", isNullable: true),
            new Column<object>("Weights", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Keys", Arr("a", "b", "c") }, { "Weights", Arr(20L, 50L, 10L) } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Keys", Arr("x", "y", "z") }, { "Weights", Arr(5L) } }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Paired
            | mv-apply K = Keys to typeof(string), W = Weights to typeof(long) on (
                top 2 by W desc
                | summarize TopKeys = make_list(K), TopWeights = make_list(W)
            )
            | sort by Id asc
            | project Id, TopKeys, TopWeights
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertDynamicArrayEqual(rows[0]["TopKeys"], "b", "a");
        AssertDynamicArrayEqual(rows[0]["TopWeights"], 50L, 20L);
        // Row 2 has (x, 5), (y, null), (z, null) -> top 2 by W desc puts 5 first; make_list skips nulls for TopWeights
        AssertLongEqual(ExtractArray(rows[1]["TopWeights"])[0], 5L);
        Assert.That(ExtractArray(rows[1]["TopKeys"])[0], Is.EqualTo("x"));
    }

    [Test]
    public void MvApply_EmptyAndNullArrayEdgeCases()
    {
        var table = CreateKeyedDynamicTable("EdgeApply", [
            (1, Arr(10L, 20L)),
            (2, Arr()),
            (3, null)
        ]);
        var db = CreateDatabase(table);

        // Non-aggregating subquery: empty [] yields 0 rows; null yields 1 row with null
        var filtered = Execute(db, """
            EdgeApply
            | mv-apply V = Payload on (
                project Elem = V, WasNull = isnull(V)
            )
            | sort by Id asc
            """);
        Assert.That(filtered, Has.Count.EqualTo(3));
        Assert.That(filtered.Any(r => Convert.ToInt32(r["Id"]) == 2), Is.False, "Empty array produces 0-row subtable.");
        var nullRow = filtered.Single(r => Convert.ToInt32(r["Id"]) == 3);
        Assert.That(nullRow["Elem"], Is.Null);
        Assert.That(nullRow["WasNull"], Is.EqualTo(true));

        // Ungrouped summarize count() on empty [] subtable returns 1 row with Cnt = 0!
        var counted = Execute(db, """
            EdgeApply
            | mv-apply V = Payload on (
                summarize Cnt = count()
            )
            | sort by Id asc
            | project Id, Cnt
            """);
        Assert.That(counted, Has.Count.EqualTo(3));
        AssertLongEqual(counted[0]["Cnt"], 2L);
        AssertLongEqual(counted[1]["Cnt"], 0L);
        AssertLongEqual(counted[2]["Cnt"], 1L);
    }

    [Test]
    public void MvApply_FilterPropertyBagEntriesViaInnerMvExpandKindArray()
    {
        var table = CreateKeyedDynamicTable("BagFilter", [
            (1, Bag(("keep_a", 10L), ("drop_b", 2L), ("keep_c", 25L))),
            (2, Bag(("x", 100L), ("y", 1L)))
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            BagFilter
            | mv-apply BagEntry = Payload on (
                mv-expand kind=array Entry = BagEntry
                | extend K = tostring(Entry[0]), V = tolong(Entry[1])
                | where V >= 10
                | summarize Filtered = make_bag(bag_pack(K, V))
            )
            | sort by Id asc
            | project Id, Filtered
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertDynamicBagEqual(rows[0]["Filtered"], ("keep_a", 10L), ("keep_c", 25L));
        AssertDynamicBagEqual(rows[1]["Filtered"], ("x", 100L));
    }

    [Test]
    public void MvExpandAndMvApply_EmptyInputTable_EmitsZeroRows()
    {
        var empty = new Table("EmptyDyn", [
            new Column<int>("Id", isNullable: false),
            new Column<object>("Items", isNullable: true)
        ]);
        var db = CreateDatabase(empty);

        var expanded = Execute(db, "EmptyDyn | mv-expand Items");
        Assert.That(expanded, Is.Empty);

        var applied = Execute(db, """
            EmptyDyn
            | mv-apply Items on (
                summarize Cnt = count()
            )
            """);
        Assert.That(applied, Is.Empty);
    }
}
