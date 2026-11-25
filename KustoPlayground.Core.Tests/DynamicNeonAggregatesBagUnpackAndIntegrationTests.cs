using static KustoPlayground.Core.Tests.DynamicNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// TDD specification tests for:
/// - Collection aggregates: make_list(), make_list_if(), make_list_with_nulls(), make_set(), make_set_if(),
///   make_bag(), make_bag_if(), make_dictionary()
/// - evaluate bag_unpack() plugin (explicit OutputSchema, wildcard schema, automatic discovery & type inference,
///   missing properties, prefix, columnsConflict modes, ignoredProperties)
/// - End-to-end cross-feature integration pipelines combining dynamic features
/// </summary>
public class DynamicNeonAggregatesBagUnpackAndIntegrationTests
{
    private static readonly DateTime Dt1 = new(2025, 8, 23, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Dt2 = new(2025, 8, 23, 7, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Guid1 = new("74be27de-1e4e-49d9-b579-fe0b331d3642");
    private static readonly Guid Guid2 = new("11111111-2222-3333-4444-555555555555");

    // =========================================================================
    // 1. make_list(), make_list_if(), make_list_with_nulls()
    // =========================================================================

    [Test]
    public void MakeList_PreservesDuplicatesAndSortedOrder_SkipsNulls_AndSupportsWithNullsAndIfAndMaxSize()
    {
        var table = CreateTable("Events", [
            new Column<string>("Grp", isNullable: false),
            new Column<int>("Seq", isNullable: false),
            new Column<object>("Val", isNullable: true),
            new Column<bool>("Include", isNullable: false)
        ], [
            new Dictionary<string, object?> { { "Grp", "A" }, { "Seq", 1 }, { "Val", "x" }, { "Include", true } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Seq", 2 }, { "Val", null }, { "Include", true } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Seq", 3 }, { "Val", "y" }, { "Include", false } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Seq", 4 }, { "Val", "x" }, { "Include", true } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Seq", 5 }, { "Val", "z" }, { "Include", true } },
            new Dictionary<string, object?> { { "Grp", "B" }, { "Seq", 1 }, { "Val", null }, { "Include", true } },
            new Dictionary<string, object?> { { "Grp", "B" }, { "Seq", 2 }, { "Val", null }, { "Include", false } }
        ], shuffle: true);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Events
            | sort by Seq asc
            | summarize
                make_list(Val),
                Limited = make_list(Val, 2),
                WithNulls = make_list_with_nulls(Val),
                Filtered = make_list_if(Val, Include)
              by Grp
            | sort by Grp asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));

        // Group A (sorted by Seq 1..5): non-nulls are ["x", "y", "x", "z"]
        Assert.That(rows[0]["Grp"], Is.EqualTo("A"));
        AssertDynamicArrayEqual(rows[0]["list_Val"], "x", "y", "x", "z");
        AssertDynamicArrayEqual(rows[0]["Limited"], "x", "y");
        AssertDynamicArrayEqual(rows[0]["WithNulls"], "x", null, "y", "x", "z");
        AssertDynamicArrayEqual(rows[0]["Filtered"], "x", "x", "z");

        // Group B: all nulls
        Assert.That(rows[1]["Grp"], Is.EqualTo("B"));
        AssertDynamicArrayEqual(rows[1]["list_Val"]);
        AssertDynamicArrayEqual(rows[1]["Limited"]);
        AssertDynamicArrayEqual(rows[1]["WithNulls"], null, null);
        AssertDynamicArrayEqual(rows[1]["Filtered"]);
    }

    [Test]
    public void MakeList_OnDynamicColumn_FlattensTopLevelArraysKeepsBagsSkipsNulls_AndEmptyTableBehavior()
    {
        var table = CreateTable("DynRows", [
            new Column<int>("Seq", isNullable: false),
            new Column<object>("Item", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Seq", 1 }, { "Item", Arr(1L, 2L) } },
            new Dictionary<string, object?> { { "Seq", 2 }, { "Item", null } },
            new Dictionary<string, object?> { { "Seq", 3 }, { "Item", Bag(("k", "v")) } }
        ]);
        var empty = new Table("EmptyT", [
            new Column<string>("Grp", isNullable: false),
            new Column<int>("Val", isNullable: true),
            new Column<object>("BagCol", isNullable: true)
        ]);
        var db = CreateDatabase(table, empty);

        var flattenedRow = ExecuteSingleRow(db, """
            DynRows
            | sort by Seq asc
            | summarize Items = make_list(Item)
            """);
        // In ADX, make_list on a dynamic column flattens top-level dynamic arrays while keeping bags intact and skipping nulls.
        AssertDynamicArrayEqual(flattenedRow["Items"], 1L, 2L, Bag(("k", "v")));

        var ungroupedEmpty = ExecuteSingleRow(db, """
            EmptyT
            | summarize ListVals = make_list(Val), SetVals = make_set(Val), BagVals = make_bag(BagCol)
            """);
        AssertDynamicArrayEqual(ungroupedEmpty["ListVals"]);
        AssertDynamicSetEquivalent(ungroupedEmpty["SetVals"]);
        AssertDynamicBagEqual(ungroupedEmpty["BagVals"]);

        var groupedEmpty = Execute(db, "EmptyT | summarize ListVals = make_list(Val), SetVals = make_set(Val), BagVals = make_bag(BagCol) by Grp");
        Assert.That(groupedEmpty, Is.Empty);
    }

    // =========================================================================
    // 2. make_set() and make_set_if()
    // =========================================================================

    [Test]
    public void MakeSet_DeduplicatesSkipsNullsSupportsMaxSizeAndIfAndFlattensDynamicArrays()
    {
        var table = CreateTable("SetSrc", [
            new Column<string>("Grp", isNullable: false),
            new Column<object>("Tag", isNullable: true),
            new Column<bool>("Ok", isNullable: false),
            new Column<object>("TagList", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Grp", "A" }, { "Tag", "red" }, { "Ok", true }, { "TagList", Arr("r1", "r2") } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Tag", "blue" }, { "Ok", false }, { "TagList", Arr("r2", "r3") } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Tag", "red" }, { "Ok", true }, { "TagList", Arr("r3", null) } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Tag", null }, { "Ok", true }, { "TagList", null } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Tag", "green" }, { "Ok", true }, { "TagList", Arr() } },
            new Dictionary<string, object?> { { "Grp", "B" }, { "Tag", null }, { "Ok", true }, { "TagList", null } }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            SetSrc
            | summarize
                make_set(Tag),
                Limited = make_set(Tag, 2),
                Filtered = make_set_if(Tag, Ok),
                Flattened = make_set(TagList)
              by Grp
            | sort by Grp asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));

        // Group A
        AssertDynamicSetEquivalent(rows[0]["set_Tag"], "red", "blue", "green");
        AssertDynamicSetEquivalent(rows[0]["Filtered"], "red", "green");
        AssertDynamicSetEquivalent(rows[0]["Flattened"], "r1", "r2", "r3");

        var limited = ExtractArray(rows[0]["Limited"]);
        Assert.That(limited, Has.Count.EqualTo(2));
        Assert.That(limited.Distinct().Count(), Is.EqualTo(2));
        Assert.That(limited.All(v => v is string s && (s == "red" || s == "blue" || s == "green")), Is.True);

        // Group B: all nulls -> []
        AssertDynamicSetEquivalent(rows[1]["set_Tag"]);
        AssertDynamicSetEquivalent(rows[1]["Filtered"]);
        AssertDynamicSetEquivalent(rows[1]["Flattened"]);
    }

    // =========================================================================
    // 3. make_bag(), make_bag_if(), make_dictionary()
    // =========================================================================

    [Test]
    public void MakeBag_MergesAcrossGroupsHandlesDuplicateKeysSkipsNullAndNonBagsAndSupportsIfAndMaxSize()
    {
        var table = CreateTable("BagSrc", [
            new Column<string>("Grp", isNullable: false),
            new Column<bool>("Include", isNullable: false),
            new Column<object>("Props", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Grp", "A" }, { "Include", true }, { "Props", Bag(("k1", 1L), ("dup", "v1")) } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Include", true }, { "Props", Bag(("k2", null), ("dup", "v2")) } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Include", false }, { "Props", Bag(("excluded", 99L)) } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Include", true }, { "Props", null } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Include", true }, { "Props", Arr("not-a-bag") } },
            new Dictionary<string, object?> { { "Grp", "A" }, { "Include", true }, { "Props", "scalar-ignored" } },
            new Dictionary<string, object?> { { "Grp", "B" }, { "Include", true }, { "Props", null } }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            BagSrc
            | summarize
                make_bag(Props),
                Filtered = make_bag_if(Props, Include),
                Limited = make_bag(Props, 2),
                ViaLegacy = make_dictionary(Props)
              by Grp
            | sort by Grp asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));

        // Group A: keys k1, k2 (null), dup (either v1 or v2), excluded
        var bagA = ExtractBag(rows[0]["bag_Props"]);
        Assert.That(bagA.Keys, Is.EquivalentTo(new[] { "k1", "k2", "dup", "excluded" }));
        AssertLongEqual(bagA["k1"], 1L);
        Assert.That(bagA["k2"], Is.Null);
        Assert.That(bagA["dup"], Is.AnyOf("v1", "v2"));
        AssertLongEqual(bagA["excluded"], 99L);

        // Filtered excludes "excluded"
        var filteredA = ExtractBag(rows[0]["Filtered"]);
        Assert.That(filteredA.Keys, Is.EquivalentTo(new[] { "k1", "k2", "dup" }));

        // Limited to max 2 distinct keys
        var limitedA = ExtractBag(rows[0]["Limited"]);
        Assert.That(limitedA, Has.Count.EqualTo(2));
        Assert.That(limitedA.Keys.All(k => bagA.ContainsKey(k)), Is.True);

        // Legacy make_dictionary alias
        var legacyA = ExtractBag(rows[0]["ViaLegacy"]);
        Assert.That(legacyA.Keys, Is.EquivalentTo(new[] { "k1", "k2", "dup", "excluded" }));

        // Group B: all null -> empty property bag {}
        AssertDynamicBagEqual(rows[1]["bag_Props"]);
        AssertDynamicBagEqual(rows[1]["Filtered"]);
    }

    [Test]
    public void MakeBag_WithBagPack_DynamicPivotByGroup()
    {
        var table = CreateTable("Metrics", [
            new Column<string>("Host", isNullable: false),
            new Column<string>("Metric", isNullable: false),
            new Column<double>("Val", isNullable: false)
        ], [
            new Dictionary<string, object?> { { "Host", "h1" }, { "Metric", "cpu" }, { "Val", 0.75 } },
            new Dictionary<string, object?> { { "Host", "h1" }, { "Metric", "mem" }, { "Val", 0.50 } },
            new Dictionary<string, object?> { { "Host", "h2" }, { "Metric", "cpu" }, { "Val", 0.25 } }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Metrics
            | summarize Pivoted = make_bag(bag_pack(Metric, Val)) by Host
            | sort by Host asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertDynamicBagEqual(rows[0]["Pivoted"], ("cpu", 0.75), ("mem", 0.50));
        AssertDynamicBagEqual(rows[1]["Pivoted"], ("cpu", 0.25));
    }

    // =========================================================================
    // 4. evaluate bag_unpack() Plugin
    // =========================================================================

    [Test]
    public void BagUnpack_ExplicitOutputSchema_FullAndWildcardPreserveRowCountAndTypes()
    {
        var table = CreateKeyedDynamicTable("Profiles", [
            (1, Bag(("Name", "Alice"), ("Age", 30L), ("Active", true), ("Extra", "ignored"))),
            (2, Bag(("Age", 40L))),
            (3, Bag()),
            (4, null)
        ], dynamicColumn: "Props", shuffle: false);
        var db = CreateDatabase(table);

        // Wildcard explicit schema: keeps Id, removes Props, adds Name/Age/Active/NeverSeen
        var rows = Execute(db, """
            Profiles
            | evaluate bag_unpack(Props) : (*, Name:string, Age:long, Active:bool, NeverSeen:real)
            | sort by Id asc
            """);

        Assert.That(rows, Has.Count.EqualTo(4), "bag_unpack must never change row count.");
        Assert.That(rows[0].ContainsKey("Props"), Is.False, "Unpacked column must be removed.");
        Assert.That(rows[0].ContainsKey("Extra"), Is.False, "Keys outside explicit OutputSchema must not be emitted.");
        Assert.That(rows[0].ContainsKey("NeverSeen"), Is.True, "Explicit OutputSchema columns must exist even if absent in all rows.");

        Assert.That(rows[0]["Name"], Is.EqualTo("Alice"));
        AssertLongEqual(rows[0]["Age"], 30L);
        Assert.That(rows[0]["Active"], Is.EqualTo(true));
        Assert.That(rows[0]["NeverSeen"], Is.Null);

        Assert.That(string.IsNullOrEmpty((string?)rows[1]["Name"]), Is.True);
        AssertLongEqual(rows[1]["Age"], 40L);
        Assert.That(rows[1]["Active"], Is.Null);

        Assert.That(rows[2]["Age"], Is.Null);
        Assert.That(rows[3]["Age"], Is.Null);

        // Verify schema types
        var schemaRows = Execute(db, """
            Profiles
            | evaluate bag_unpack(Props) : (*, Name:string, Age:long, Active:bool, NeverSeen:real)
            | getschema
            """);
        AssertSchemaColumn(schemaRows, "Id", "int");
        AssertSchemaColumn(schemaRows, "Name", "string");
        AssertSchemaColumn(schemaRows, "Age", "long");
        AssertSchemaColumn(schemaRows, "Active", "bool");
        AssertSchemaColumn(schemaRows, "NeverSeen", "real");

        // Non-wildcard explicit schema returns ONLY listed columns
        var explicitOnly = Execute(db, """
            Profiles
            | evaluate bag_unpack(Props) : (Id:int, Name:string)
            | sort by Id asc
            """);
        Assert.That(explicitOnly, Has.Count.EqualTo(4));
        Assert.That(explicitOnly[0].Keys, Is.EquivalentTo(new[] { "Id", "Name" }));
    }

    [Test]
    public void BagUnpack_AutomaticColumnDiscovery_InfersUniformTypesAndFallsBackToDynamicOnMixedTypes()
    {
        var table = CreateKeyedDynamicTable("AutoDocs", [
            (1, Bag(
                ("City", "Seattle"),
                ("Score", 10L),
                ("Ratio", 0.5),
                ("Flag", true),
                ("Created", Dt1),
                ("Duration", TimeSpan.FromMinutes(15)),
                ("Uid", Guid1),
                ("Mixed", 100L),
                ("Nested", Bag(("inner", 1L)))
            )),
            (2, Bag(
                ("City", "Austin"),
                ("Score", 20L),
                ("Ratio", 0.75),
                ("Flag", false),
                ("Created", Dt2),
                ("Duration", TimeSpan.FromMinutes(30)),
                ("Uid", Guid2),
                ("Mixed", "text-conflict"),
                ("OnlyInRow2", Arr(1L, 2L))
            )),
            (3, Bag(("Score", null))),
            (4, null)
        ], dynamicColumn: "Props", shuffle: false);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            AutoDocs
            | evaluate bag_unpack(Props)
            | sort by Id asc
            """);

        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows[0].ContainsKey("Props"), Is.False);
        Assert.That(rows[0].Keys, Is.EquivalentTo(new[]
        {
            "Id", "City", "Score", "Ratio", "Flag", "Created", "Duration", "Uid", "Mixed", "Nested", "OnlyInRow2"
        }));

        Assert.That(rows[0]["City"], Is.EqualTo("Seattle"));
        AssertLongEqual(rows[0]["Score"], 10L);
        AssertNumericEqual(rows[0]["Ratio"], 0.5);
        Assert.That(rows[0]["Flag"], Is.EqualTo(true));
        AssertDateTimeEqual(rows[0]["Created"], Dt1);
        AssertTimeSpanEqual(rows[0]["Duration"], TimeSpan.FromMinutes(15));
        AssertGuidEqual(rows[0]["Uid"], Guid1);
        AssertDynamicEqual(rows[0]["Mixed"], 100L);
        AssertDynamicBagEqual(rows[0]["Nested"], ("inner", 1L));
        Assert.That(rows[0]["OnlyInRow2"], Is.Null);

        AssertTimeSpanEqual(rows[1]["Duration"], TimeSpan.FromMinutes(30));
        AssertDynamicEqual(rows[1]["Mixed"], "text-conflict");
        AssertDynamicArrayEqual(rows[1]["OnlyInRow2"], 1L, 2L);
        Assert.That(rows[1]["Nested"], Is.Null);

        Assert.That(rows[2]["Score"], Is.Null);
        Assert.That(rows[3]["Score"], Is.Null);

        var schemaRows = Execute(db, "AutoDocs | evaluate bag_unpack(Props) | getschema");
        AssertSchemaColumn(schemaRows, "City", "string");
        AssertSchemaColumn(schemaRows, "Ratio", "real");
        AssertSchemaColumn(schemaRows, "Flag", "bool");
        AssertSchemaColumn(schemaRows, "Created", "datetime");
        AssertSchemaColumn(schemaRows, "Duration", "timespan");
        AssertSchemaColumn(schemaRows, "Uid", "guid");
        AssertSchemaColumn(schemaRows, "Mixed", "dynamic");
        AssertSchemaColumn(schemaRows, "Nested", "dynamic");
        AssertSchemaColumn(schemaRows, "OnlyInRow2", "dynamic");
    }

    [Test]
    public void BagUnpack_PrefixConflictResolutionAndIgnoredProperties()
    {
        var table = CreateTable("Conflicts", [
            new Column<int>("Id", isNullable: false),
            new Column<string>("Name", isNullable: false),
            new Column<object>("Props", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Name", "source-alice" }, { "Props", Bag(("Name", "bag-alice"), ("Age", 30L), ("Secret", "hide")) } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Name", "source-bob" }, { "Props", Bag(("Age", 25L), ("Secret", "hide2")) } }
        ]);
        var db = CreateDatabase(table);

        // Default conflict mode ('error') must fail when 'Name' collides
        ExecuteAndExpectError(db, "Conflicts | evaluate bag_unpack(Props)");
        ExecuteAndExpectError(db, "Conflicts | evaluate bag_unpack(Props, columnsConflict='error')");

        // Prefix avoids collision cleanly
        var prefixed = Execute(db, """
            Conflicts
            | evaluate bag_unpack(Props, 'p_')
            | sort by Id asc
            """);
        Assert.That(prefixed, Has.Count.EqualTo(2));
        Assert.That(prefixed[0]["Name"], Is.EqualTo("source-alice"));
        Assert.That(prefixed[0]["p_Name"], Is.EqualTo("bag-alice"));
        AssertLongEqual(prefixed[0]["p_Age"], 30L);

        // replace_source overwrites Name with bag's Name
        var replaced = Execute(db, """
            Conflicts
            | evaluate bag_unpack(Props, columnsConflict='replace_source')
            | sort by Id asc
            """);
        Assert.That(replaced[0]["Name"], Is.EqualTo("bag-alice"));
        Assert.That(string.IsNullOrEmpty((string?)replaced[1]["Name"]), Is.True);
        AssertLongEqual(replaced[1]["Age"], 25L);

        // keep_source preserves source Name and unpacks non-colliding keys
        var kept = Execute(db, """
            Conflicts
            | evaluate bag_unpack(Props, columnsConflict='keep_source')
            | sort by Id asc
            """);
        Assert.That(kept[0]["Name"], Is.EqualTo("source-alice"));
        Assert.That(kept[1]["Name"], Is.EqualTo("source-bob"));
        AssertLongEqual(kept[0]["Age"], 30L);

        // ignoredProperties ignores colliding 'Name' and 'Secret' so default 'error' mode succeeds
        var ignored = Execute(db, """
            Conflicts
            | evaluate bag_unpack(Props, ignoredProperties=dynamic(['Name', 'Secret']))
            | sort by Id asc
            """);
        Assert.That(ignored[0].Keys, Is.EquivalentTo(new[] { "Id", "Name", "Age" }));
        Assert.That(ignored[0]["Name"], Is.EqualTo("source-alice"));
        AssertLongEqual(ignored[0]["Age"], 30L);
    }

    [Test]
    public void BagUnpack_AllEmptyOrNullBagsAndEmptyTable()
    {
        var table = CreateKeyedDynamicTable("AllEmptyBags", [
            (1, Bag()),
            (2, null)
        ], dynamicColumn: "Props");
        var emptyTable = new Table("EmptyBags", [
            new Column<int>("Id", isNullable: false),
            new Column<object>("Props", isNullable: true)
        ]);
        var db = CreateDatabase(table, emptyTable);

        var rows = Execute(db, "AllEmptyBags | evaluate bag_unpack(Props) | sort by Id asc");
        Assert.That(rows, Has.Count.EqualTo(2));
        // No property is discovered in any row, so Kusto returns the input unchanged (the Props column is kept).
        Assert.That(rows[0].Keys, Is.EquivalentTo(new[] { "Id", "Props" }));

        var emptyRows = Execute(db, "EmptyBags | evaluate bag_unpack(Props) : (*, Name:string, Age:long)");
        Assert.That(emptyRows, Is.Empty);
    }

    // =========================================================================
    // 5. Cross-Feature Integration Pipelines
    // =========================================================================

    [Test]
    public void Integration_ParseRawLogsFilterWithHasAnyExpandConvertAndSummarize()
    {
        var table = CreateTable("RawAudit", [
            new Column<int>("Id", isNullable: false),
            new Column<string>("Body", isNullable: false)
        ], [
            new Dictionary<string, object?>
            {
                { "Id", 1 },
                { "Body", """{"service":"auth-api","events":[{"code":500,"latency":120.5,"tag":"timeout"},{"code":200,"latency":15.0,"tag":"ok"}]}""" }
            },
            new Dictionary<string, object?>
            {
                { "Id", 2 },
                { "Body", """{"service":"auth-api","events":[{"code":503,"latency":80.0,"tag":"timeout"}]}""" }
            },
            new Dictionary<string, object?>
            {
                { "Id", 3 },
                { "Body", """{"service":"billing-api","events":[{"code":200,"latency":10.0,"tag":"ok"}]}""" }
            },
            new Dictionary<string, object?>
            {
                { "Id", 4 },
                { "Body", "{corrupted-json-payload timeout" }
            }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            RawAudit
            | extend Doc = parse_json(Body)
            | where Doc has_any ("timeout", "503")
            | where gettype(Doc) == "dictionary"
            | extend Service = tostring(Doc.service)
            | mv-expand Ev = Doc.events
            | extend Code = tolong(Ev.code), Latency = toreal(Ev.latency), Tag = tostring(Ev.tag)
            | where Code in (dynamic([500, 503]))
            | sort by Id asc, Code asc
            | summarize
                Codes = make_list(Code),
                UniqueTags = make_set(Tag),
                TotalLatency = sum(Latency)
              by Service
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Service"], Is.EqualTo("auth-api"));
        AssertDynamicArrayEqual(rows[0]["Codes"], 500L, 503L);
        AssertDynamicSetEquivalent(rows[0]["UniqueTags"], "timeout");
        AssertNumericEqual(rows[0]["TotalLatency"], 200.5);
    }

    [Test]
    public void Integration_PackAllBagSetKeyBagMergeAndBagUnpackRoundTrip()
    {
        var table = CreateTable("Users", [
            new Column<int>("Id", isNullable: false),
            new Column<string>("Name", isNullable: false),
            new Column<string>("Nick", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Name", "Alice" }, { "Nick", "" } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Name", "Bob" }, { "Nick", "Bobby" } }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Users
            | extend Packed = pack_all(true)
            | extend Enriched = bag_merge(bag_set_key(Packed, "Role", "member"), dynamic({"Role": "guest", "Tier": 1}))
            | project Enriched
            | evaluate bag_unpack(Enriched)
            | sort by Id asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertLongEqual(rows[0]["Id"], 1L);
        Assert.That(rows[0]["Name"], Is.EqualTo("Alice"));
        Assert.That(string.IsNullOrEmpty((string?)rows[0]["Nick"]), Is.True);
        Assert.That(rows[0]["Role"], Is.EqualTo("member"));
        AssertLongEqual(rows[0]["Tier"], 1L);

        AssertLongEqual(rows[1]["Id"], 2L);
        Assert.That(rows[1]["Name"], Is.EqualTo("Bob"));
        Assert.That(rows[1]["Nick"], Is.EqualTo("Bobby"));
        Assert.That(rows[1]["Role"], Is.EqualTo("member"));
        AssertLongEqual(rows[1]["Tier"], 1L);
    }

    [Test]
    public void Integration_DynamicPivotWithMakeBagAndBagUnpackAndArrayUtilities()
    {
        var table = CreateTable("Telemetry", [
            new Column<string>("Node", isNullable: false),
            new Column<string>("Metric", isNullable: false),
            new Column<double>("Reading", isNullable: false)
        ], [
            new Dictionary<string, object?> { { "Node", "n1" }, { "Metric", "cpu" }, { "Reading", 40.0 } },
            new Dictionary<string, object?> { { "Node", "n1" }, { "Metric", "mem" }, { "Reading", 60.0 } },
            new Dictionary<string, object?> { { "Node", "n2" }, { "Metric", "cpu" }, { "Reading", 80.0 } },
            new Dictionary<string, object?> { { "Node", "n2" }, { "Metric", "mem" }, { "Reading", 20.0 } }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Telemetry
            | summarize Bag = make_bag(bag_pack(Metric, Reading)) by Node
            | evaluate bag_unpack(Bag) : (*, cpu:real, mem:real)
            | extend Pair = array_sort_desc(pack_array(cpu, mem))
            | extend Total = array_sum(Pair), Highest = toreal(Pair[0])
            | sort by Node asc
            | project Node, cpu, mem, Highest, Total
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Node"], Is.EqualTo("n1"));
        AssertNumericEqual(rows[0]["cpu"], 40.0);
        AssertNumericEqual(rows[0]["mem"], 60.0);
        AssertNumericEqual(rows[0]["Highest"], 60.0);
        AssertNumericEqual(rows[0]["Total"], 100.0);

        Assert.That(rows[1]["Node"], Is.EqualTo("n2"));
        AssertNumericEqual(rows[1]["cpu"], 80.0);
        AssertNumericEqual(rows[1]["mem"], 20.0);
        AssertNumericEqual(rows[1]["Highest"], 80.0);
        AssertNumericEqual(rows[1]["Total"], 100.0);
    }

    [Test]
    public void Integration_MvApplySetIntersectArrayIffAndBagPack()
    {
        var table = CreateTable("Pipelines", [
            new Column<int>("Id", isNullable: false),
            new Column<object>("Tags", isNullable: true),
            new Column<object>("Scores", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Tags", Arr("a", "b", "c") }, { "Scores", Arr(10L, 40L, 30L) } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Tags", Arr("b", "d") }, { "Scores", Arr(5L, 25L) } }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            let targetTags = dynamic(["b", "c", "e"]);
            Pipelines
            | extend Common = set_intersect(Tags, targetTags)
            | extend HasMatch = array_length(Common) > 0
            | mv-apply S = Scores to typeof(long) on (
                top 2 by S desc
                | summarize TopScores = make_list(S), TopSum = sum(S)
            )
            | extend Flags = array_iff(dynamic([true, false]), TopSum, 0)
            | sort by Id asc
            | project Id, CommonCount = array_length(Common), TopScores, TopSum, Flags
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertLongEqual(rows[0]["CommonCount"], 2L);
        AssertDynamicArrayEqual(rows[0]["TopScores"], 40L, 30L);
        AssertLongEqual(rows[0]["TopSum"], 70L);
        AssertDynamicArrayEqual(rows[0]["Flags"], 70L, 0L);

        AssertLongEqual(rows[1]["CommonCount"], 1L);
        AssertDynamicArrayEqual(rows[1]["TopScores"], 25L, 5L);
        AssertLongEqual(rows[1]["TopSum"], 30L);
        AssertDynamicArrayEqual(rows[1]["Flags"], 30L, 0L);
    }
}
