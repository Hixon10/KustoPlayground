using static KustoPlayground.Core.Tests.DynamicNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// TDD specification tests for:
/// - Property bag inspection & manipulation: bag_keys(), bag_has_key(), bag_merge(), bag_set_key()
/// - Term search on dynamic values: has, !has, has_cs, !has_cs, has_any over property names and nested values
/// </summary>
public class DynamicNeonPropertyBagAndTermSearchTests
{
    private static readonly DateTime SampleUtc = new(2025, 8, 23, 6, 20, 0, DateTimeKind.Utc);
    private static readonly Guid SampleGuid = new("74be27de-1e4e-49d9-b579-fe0b331d3642");

    // =========================================================================
    // 1. bag_keys()
    // =========================================================================

    [Test]
    public void BagKeys_ReturnsTopLevelKeysIncludingNullValuedKeys_AndNullForNonBags()
    {
        var table = CreateKeyedDynamicTable("Bags", [
            (1, Bag(("a", 1L), ("b", null), ("nested", Bag(("inner", 99L))), ("", "emptyKey"), ("a.b", true))),
            (2, Bag()),
            (3, Arr("a", "b")),
            (4, "string-scalar"),
            (5, 123L),
            (6, null)
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Bags
            | sort by Id asc
            | project Id, Keys = bag_keys(Payload)
            """);

        Assert.That(rows, Has.Count.EqualTo(6));
        AssertDynamicSetEquivalent(rows[0]["Keys"], "a", "b", "nested", "", "a.b");
        AssertDynamicSetEquivalent(rows[1]["Keys"]);
        Assert.That(rows[2]["Keys"], Is.Null);
        Assert.That(rows[3]["Keys"], Is.Null);
        Assert.That(rows[4]["Keys"], Is.Null);
        Assert.That(rows[5]["Keys"], Is.Null);
    }

    // =========================================================================
    // 2. bag_has_key()
    // =========================================================================

    [Test]
    public void BagHasKey_DistinguishesPresentNullKeyFromAbsentKey_AndSupportsDynamicKeysAndJsonPath()
    {
        var table = CreateTable("BagCheck", [
            new Column<int>("Id", isNullable: false),
            new Column<string>("SearchKey", isNullable: true),
            new Column<object>("Payload", isNullable: true)
        ], [
            new Dictionary<string, object?>
            {
                { "Id", 1 },
                { "SearchKey", "nullProp" },
                {
                    "Payload",
                    Bag(
                        ("present", 123L),
                        ("nullProp", null),
                        ("", "empty-name"),
                        ("nested", Bag(("child", "ok"), ("nullChild", null)))
                    )
                }
            },
            new Dictionary<string, object?>
            {
                { "Id", 2 },
                { "SearchKey", "absentProp" },
                { "Payload", Bag(("present", 123L)) }
            },
            new Dictionary<string, object?>
            {
                { "Id", 3 },
                { "SearchKey", "present" },
                { "Payload", Arr("present") }
            },
            new Dictionary<string, object?>
            {
                { "Id", 4 },
                { "SearchKey", "present" },
                { "Payload", null }
            }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            BagCheck
            | sort by Id asc
            | project
                Id,
                HasPresent = bag_has_key(Payload, "present"),
                HasWrongCase = bag_has_key(Payload, "Present"),
                HasNullKey = bag_has_key(Payload, "nullProp"),
                HasAbsentKey = bag_has_key(Payload, "doesNotExist"),
                HasEmptyKey = bag_has_key(Payload, ""),
                HasDynamicKey = bag_has_key(Payload, SearchKey),
                HasNestedPath = bag_has_key(Payload, "$.nested.child"),
                HasNestedNullPath = bag_has_key(Payload, "$.nested.nullChild"),
                HasNestedMissing = bag_has_key(Payload, "$.nested.missing")
            """);

        Assert.That(rows, Has.Count.EqualTo(4));

        // Row 1: full bag
        Assert.That(rows[0]["HasPresent"], Is.EqualTo(true));
        Assert.That(rows[0]["HasWrongCase"], Is.EqualTo(false));
        Assert.That(rows[0]["HasNullKey"], Is.EqualTo(true), "A present key whose value is null MUST return true from bag_has_key.");
        Assert.That(rows[0]["HasAbsentKey"], Is.EqualTo(false));
        Assert.That(rows[0]["HasEmptyKey"], Is.EqualTo(true));
        Assert.That(rows[0]["HasDynamicKey"], Is.EqualTo(true));
        Assert.That(rows[0]["HasNestedPath"], Is.EqualTo(true));
        Assert.That(rows[0]["HasNestedNullPath"], Is.EqualTo(true));
        Assert.That(rows[0]["HasNestedMissing"], Is.EqualTo(false));

        // Row 2: missing search key
        Assert.That(rows[1]["HasPresent"], Is.EqualTo(true));
        Assert.That(rows[1]["HasNullKey"], Is.EqualTo(false));
        Assert.That(rows[1]["HasDynamicKey"], Is.EqualTo(false));

        // Row 3: array (not a property bag)
        Assert.That(rows[2]["HasPresent"], Is.EqualTo(false));

        // Row 4: null dynamic
        Assert.That(rows[3]["HasPresent"], Is.EqualTo(false));
    }

    // =========================================================================
    // 3. bag_merge()
    // =========================================================================

    [Test]
    public void BagMerge_LeftmostWinsIncludingExplicitNull_AndMergeIsShallow()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                Basic = bag_merge(
                    dynamic({"A1": 12, "B1": 2, "C1": 3}),
                    dynamic({"A2": 81, "B2": 82, "A1": 1})
                ),
                ThreeBags = bag_merge(
                    dynamic({"k": "first", "only1": 1}),
                    dynamic({"k": "second", "m": "from2"}),
                    dynamic({"k": "third", "m": "from3", "only3": 3})
                ),
                LeftNullWins = bag_merge(
                    dynamic({"a": null, "b": 1}),
                    dynamic({"a": "right-value", "c": 2})
                ),
                ShallowNested = bag_merge(
                    dynamic({"severity": "high", "context": {"source": "parser"}}),
                    dynamic({"severity": "low", "context": {"vendor": "Contoso"}, "action": "deny"})
                ),
                WithEmptyBags = bag_merge(dynamic({}), dynamic({"x": 10}), dynamic({}))
            | project
                Basic,
                ThreeBags,
                LeftNullWins,
                LeftNullKeyPresent = bag_has_key(LeftNullWins, "a"),
                ShallowNested,
                ShallowHasVendor = bag_has_key(ShallowNested.context, "vendor"),
                WithEmptyBags
            """);

        AssertDynamicBagEqual(row["Basic"],
            ("A1", 12L), ("B1", 2L), ("C1", 3L), ("A2", 81L), ("B2", 82L));

        AssertDynamicBagEqual(row["ThreeBags"],
            ("k", "first"), ("only1", 1L), ("m", "from2"), ("only3", 3L));

        AssertDynamicBagEqual(row["LeftNullWins"],
            ("a", null), ("b", 1L), ("c", 2L));
        Assert.That(row["LeftNullKeyPresent"], Is.EqualTo(true),
            "Present null key in the leftmost bag must win over non-null value in right bag.");

        AssertDynamicBagEqual(row["ShallowNested"],
            ("severity", "high"),
            ("context", Bag(("source", "parser"))),
            ("action", "deny"));
        Assert.That(row["ShallowHasVendor"], Is.EqualTo(false),
            "bag_merge must be shallow: winning nested bag from leftmost argument is kept whole.");

        AssertDynamicBagEqual(row["WithEmptyBags"], ("x", 10L));
    }

    [Test]
    public void BagMerge_PerRowOverrideAndDefaultColumns()
    {
        var table = CreateTable("Configs", [
            new Column<int>("Id", isNullable: false),
            new Column<object>("Overrides", isNullable: true),
            new Column<object>("Defaults", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "Id", 1 }, { "Overrides", Bag(("timeout", 5L)) }, { "Defaults", Bag(("timeout", 30L), ("retries", 3L)) } },
            new Dictionary<string, object?> { { "Id", 2 }, { "Overrides", Bag() }, { "Defaults", Bag(("timeout", 30L), ("retries", 3L)) } }
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Configs
            | extend Effective = bag_merge(Overrides, Defaults)
            | sort by Id asc
            | project Id, Effective
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        AssertDynamicBagEqual(rows[0]["Effective"], ("timeout", 5L), ("retries", 3L));
        AssertDynamicBagEqual(rows[1]["Effective"], ("timeout", 30L), ("retries", 3L));
    }

    // =========================================================================
    // 4. bag_set_key()
    // =========================================================================

    [Test]
    public void BagSetKey_AddsOverridesSetsNullPreservesImmutabilityAndSupportsJsonPathAndLetKeys()
    {
        var table = CreateTable("Docs", [
            new Column<int>("ValCol", isNullable: false),
            new Column<object>("Source", isNullable: true),
            new Column<object>("NonBagArrCol", isNullable: true),
            new Column<object>("NonBagNullCol", isNullable: true)
        ], [
            new Dictionary<string, object?>
            {
                { "ValCol", 77 },
                { "Source", Bag(("a", 1L), ("nested", Bag(("keep", "yes"), ("replace", "old")))) },
                { "NonBagArrCol", Arr(1L, 2L) },
                { "NonBagNullCol", null }
            }
        ]);
        var db = CreateDatabase(table);

        var row = ExecuteSingleRow(db, """
            let dynKey = "dynamicKey";
            Docs
            | extend
                Added = bag_set_key(Source, "b", "two"),
                Updated = bag_set_key(Source, "a", 999),
                SetToNull = bag_set_key(Source, "a", dynamic(null)),
                LetKeyAdded = bag_set_key(Source, dynKey, ValCol),
                NestedUpdated = bag_set_key(Source, "$.nested.replace", "new"),
                NestedCreated = bag_set_key(Source, "$.deep.branch.leaf", 42),
                WithTypedVal = bag_set_key(dynamic({}), "ts", datetime(2025-08-23T06:20:00Z)),
                WithGuidVal = bag_set_key(dynamic({}), "id", guid(74be27de-1e4e-49d9-b579-fe0b331d3642)),
                NonBagArr = bag_set_key(NonBagArrCol, "k", 1),
                NonBagNull = bag_set_key(NonBagNullCol, "k", 1)
            | project
                Source,
                Added,
                Updated,
                SetToNull,
                SetToNullHasKey = bag_has_key(SetToNull, "a"),
                LetKeyAdded,
                NestedUpdated,
                NestedCreated,
                WithTypedVal,
                TsType = gettype(WithTypedVal.ts),
                WithGuidVal,
                GuidType = gettype(WithGuidVal.id),
                NonBagArr,
                NonBagNull
            """);

        // Original bag remains unchanged
        AssertDynamicBagEqual(row["Source"],
            ("a", 1L),
            ("nested", Bag(("keep", "yes"), ("replace", "old"))));

        AssertDynamicBagEqual(row["Added"],
            ("a", 1L),
            ("b", "two"),
            ("nested", Bag(("keep", "yes"), ("replace", "old"))));

        AssertDynamicBagEqual(row["Updated"],
            ("a", 999L),
            ("nested", Bag(("keep", "yes"), ("replace", "old"))));

        AssertDynamicBagEqual(row["SetToNull"],
            ("a", null),
            ("nested", Bag(("keep", "yes"), ("replace", "old"))));
        Assert.That(row["SetToNullHasKey"], Is.EqualTo(true));

        AssertDynamicBagEqual(row["LetKeyAdded"],
            ("a", 1L),
            ("dynamicKey", 77L),
            ("nested", Bag(("keep", "yes"), ("replace", "old"))));

        AssertDynamicBagEqual(row["NestedUpdated"],
            ("a", 1L),
            ("nested", Bag(("keep", "yes"), ("replace", "new"))));

        AssertDynamicBagEqual(row["NestedCreated"],
            ("a", 1L),
            ("nested", Bag(("keep", "yes"), ("replace", "old"))),
            ("deep", Bag(("branch", Bag(("leaf", 42L))))));

        AssertDynamicBagEqual(row["WithTypedVal"], ("ts", SampleUtc));
        Assert.That(row["TsType"], Is.EqualTo("datetime"));
        AssertDynamicBagEqual(row["WithGuidVal"], ("id", SampleGuid));
        Assert.That(row["GuidType"], Is.EqualTo("guid"));
        Assert.That(row["NonBagArr"], Is.Null);
        Assert.That(row["NonBagNull"], Is.Null);
    }

    // =========================================================================
    // 5. Term Search on Dynamic Values (has, !has, has_cs, !has_cs, has_any)
    // =========================================================================

    [Test]
    public void TermSearch_Has_MatchesWholeTermsInPropertyNamesAndNestedValues_NotSubstrings()
    {
        var table = CreateKeyedDynamicTable("Logs", [
            (1, Bag(("service", "billing-api"), ("meta", Bag(("region_code", "north-east"), ("tags", Arr("critical", "err-404")))))),
            (2, Bag(("service", "KustoExplorerQueryRun"), ("meta", Bag(("region", "westus2"))))),
            (3, Bag(("alertCategory", "latency"), ("nested", Arr(Bag(("status", "Timeout")))))),
            (4, Bag()),
            (5, Arr()),
            (6, null)
        ]);
        var db = CreateDatabase(table);

        // Whole term inside hyphen/underscore-delimited value
        var hasBilling = Execute(db, "Logs | where Payload has 'billing' | project Id");
        Assert.That(hasBilling.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 1 }));

        // Whole term inside nested array element ("err-404" contains term "404" and term "err")
        var has404 = Execute(db, "Logs | where Payload has '404' | project Id");
        Assert.That(has404.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 1 }));

        // Whole term in a property NAME (nested key "region_code" contains term "region" and "code", and top-level key "alertCategory")
        var hasCodeKey = Execute(db, "Logs | where Payload has 'code' | project Id");
        Assert.That(hasCodeKey.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 1 }));

        var hasAlertKey = Execute(db, "Logs | where Payload has 'alertCategory' | project Id");
        Assert.That(hasAlertKey.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 3 }));

        // Whole term inside deeply nested array-of-objects (case-insensitive)
        var hasTimeout = Execute(db, "Logs | where Payload has 'timeout' | project Id");
        Assert.That(hasTimeout.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 3 }));

        // Substring inside an alphanumeric identifier MUST NOT match whole-term has!
        var substringMiss = Execute(db, "Logs | where Payload has 'Explorer' | project Id");
        Assert.That(substringMiss, Is.Empty, "has must match whole terms, not substrings inside 'KustoExplorerQueryRun'.");

        var keySubstringMiss = Execute(db, "Logs | where Payload has 'Category' | project Id");
        Assert.That(keySubstringMiss, Is.Empty, "has must not match substring 'Category' inside key 'alertCategory'.");
    }

    [Test]
    public void TermSearch_HasVsExactArrayMembership_AreDistinct()
    {
        var table = CreateKeyedDynamicTable("Alerts", [
            (1, Arr("error-404", "retry-ok")),
            (2, Arr("error", "ok"))
        ]);
        var db = CreateDatabase(table);
        ExecuteAndExpectError(db, """Alerts | project ExactIn = "error" in (Payload)""");

        var rows = Execute(db, """
            Alerts
            | sort by Id asc
            | project
                Id,
                HasErrorTerm = Payload has "error",
                ExactIn = tostring(Payload[0]) in ("error"),
                ExactSetHas = set_has_element(Payload, "error")
            """);

        Assert.That(rows, Has.Count.EqualTo(2));

        // Row 1: "error-404" contains whole term "error", but "error" is NOT an exact element of the array!
        Assert.That(rows[0]["HasErrorTerm"], Is.EqualTo(true));
        Assert.That(rows[0]["ExactIn"], Is.EqualTo(false));
        Assert.That(rows[0]["ExactSetHas"], Is.EqualTo(false));

        // Row 2: "error" matches both term search and exact array element membership
        Assert.That(rows[1]["HasErrorTerm"], Is.EqualTo(true));
        Assert.That(rows[1]["ExactIn"], Is.EqualTo(true));
        Assert.That(rows[1]["ExactSetHas"], Is.EqualTo(true));
    }

    [Test]
    public void TermSearch_CaseSensitivityAndNegationAndNulls()
    {
        var table = CreateKeyedDynamicTable("CaseLogs", [
            (1, Bag(("msg", "Critical Failure"))),
            (2, Bag(("msg", "critical warning"))),
            (3, Bag(("msg", "healthy"))),
            (4, null)
        ]);
        var db = CreateDatabase(table);

        var hasCi = Execute(db, "CaseLogs | where Payload has 'CRITICAL' | sort by Id asc | project Id");
        Assert.That(hasCi.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 1, 2 }));

        var hasCs = Execute(db, "CaseLogs | where Payload has_cs 'Critical' | sort by Id asc | project Id");
        Assert.That(hasCs.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 1 }));

        var notHas = Execute(db, "CaseLogs | where Payload !has 'critical' | sort by Id asc | project Id");
        Assert.That(notHas.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 3, 4 }));

        var notHasCs = Execute(db, "CaseLogs | where Payload !has_cs 'Critical' | sort by Id asc | project Id");
        Assert.That(notHasCs.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 2, 3, 4 }));
    }

    [Test]
    public void TermSearch_HasAny_SupportsScalarListDynamicArrayLiteralAndLetVariable()
    {
        var table = CreateKeyedDynamicTable("SearchDocs", [
            (1, Bag(("env", "prod"), ("msg", "disk-full"))),
            (2, Bag(("env", "staging"), ("msg", "memory-leak"))),
            (3, Bag(("env", "dev"), ("msg", "diskfull"))), // substring only -> should NOT match "disk" or "full"
            (4, Bag(("customMetric", "ok"), ("tags", Arr("gpu", "ml")))),
            (5, null)
        ]);
        var db = CreateDatabase(table);

        var viaList = Execute(db, """
            SearchDocs
            | where Payload has_any ("disk", "gpu")
            | sort by Id asc
            | project Id
            """);
        Assert.That(viaList.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 1, 4 }));

        var viaDynamicLiteral = Execute(db, """
            SearchDocs
            | where Payload has_any (dynamic(["memory", "customMetric"]))
            | sort by Id asc
            | project Id
            """);
        Assert.That(viaDynamicLiteral.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 2, 4 }));

        var viaLetVar = Execute(db, """
            let terms = dynamic(["full", "leak"]);
            SearchDocs
            | where Payload has_any (terms)
            | sort by Id asc
            | project Id
            """);
        Assert.That(viaLetVar.Select(r => Convert.ToInt32(r["Id"])).ToList(), Is.EqualTo(new[] { 1, 2 }));

        var viaEmptyArray = Execute(db, """
            SearchDocs
            | where Payload has_any (dynamic([]))
            | project Id
            """);
        Assert.That(viaEmptyArray, Is.Empty);
    }
}
