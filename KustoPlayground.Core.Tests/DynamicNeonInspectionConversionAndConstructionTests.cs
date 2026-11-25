using static KustoPlayground.Core.Tests.DynamicNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// TDD specification tests for:
/// - Dynamic type inspection (gettype, isnull, isnotnull)
/// - Dynamic type conversions (tostring, toint, tolong, todouble, toreal, tobool, todatetime, totimespan, toguid)
///   plus filtering, grouping, and aggregating converted dynamic values
/// - Bag and array construction (bag_pack/pack, pack_array, pack_all)
/// </summary>
public class DynamicNeonInspectionConversionAndConstructionTests
{
    private static readonly DateTime SampleUtc = new(2025, 8, 23, 6, 20, 0, DateTimeKind.Utc);
    private static readonly TimeSpan SampleSpan = new(2, 3, 4, 5);
    private static readonly Guid SampleGuid = new("74be27de-1e4e-49d9-b579-fe0b331d3642");

    // =========================================================================
    // 1. gettype(), isnull(), isnotnull()
    // =========================================================================

    [Test]
    public void GetType_ReturnsExactKustoTypeNameForEveryDynamicSubtype()
    {
        var table = CreateKeyedDynamicTable("Typed", [
            (1, null),
            (2, "abc"),
            (3, 7),
            (4, 9_000_000_000_000L),
            (5, 2.5),
            (6, true),
            (7, SampleUtc),
            (8, SampleSpan),
            (9, SampleGuid),
            (10, Arr(1, 2)),
            (11, Bag(("k", "v"))),
            (12, Arr()),
            (13, Bag())
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Typed
            | sort by Id asc
            | project Id, TypeName = gettype(Payload), IsNull = isnull(Payload), IsNotNull = isnotnull(Payload)
            """);

        Assert.That(rows, Has.Count.EqualTo(13));
        // Kusto has no int subtype inside dynamic: an int value stored in dynamic reports "long".
        string[] expectedTypes =
        [
            "null", "string", "long", "long", "double", "bool",
            "datetime", "timespan", "guid", "array", "dictionary", "array", "dictionary"
        ];

        for (int i = 0; i < expectedTypes.Length; i++)
        {
            Assert.That(rows[i]["TypeName"], Is.EqualTo(expectedTypes[i]), $"Type mismatch at Id={i + 1}");
            Assert.That(rows[i]["IsNull"], Is.EqualTo(i == 0), $"isnull mismatch at Id={i + 1}");
            Assert.That(rows[i]["IsNotNull"], Is.EqualTo(i != 0), $"isnotnull mismatch at Id={i + 1}");
        }
    }

    [Test]
    public void IsNull_DistinguishesNullFromEmptyContainersEmptyStringZeroAndFalse()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                D = dynamic({
                    "nullVal": null,
                    "emptyStr": "",
                    "zero": 0,
                    "boolFalse": false,
                    "emptyArr": [],
                    "emptyBag": {}
                })
            | project
                NullVal = isnull(D.nullVal),
                MissingVal = isnull(D.doesNotExist),
                EmptyStr = isnull(D.emptyStr),
                Zero = isnull(D.zero),
                BoolFalse = isnull(D.boolFalse),
                EmptyArr = isnull(D.emptyArr),
                EmptyBag = isnull(D.emptyBag)
            """);

        Assert.That(row["NullVal"], Is.EqualTo(true));
        Assert.That(row["MissingVal"], Is.EqualTo(true));
        Assert.That(row["EmptyStr"], Is.EqualTo(false));
        Assert.That(row["Zero"], Is.EqualTo(false));
        Assert.That(row["BoolFalse"], Is.EqualTo(false));
        Assert.That(row["EmptyArr"], Is.EqualTo(false));
        Assert.That(row["EmptyBag"], Is.EqualTo(false));
    }

    // =========================================================================
    // 2. Type Conversions on Dynamic Values
    // =========================================================================

    [Test]
    public void ToString_ConvertsDynamicPrimitivesNullAndContainers()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                D = dynamic({
                    "s": "hello",
                    "n": 42,
                    "r": 3.5,
                    "b": true,
                    "nil": null,
                    "arr": [1, "x"],
                    "bag": {"k": 2}
                })
            | project
                S = tostring(D.s),
                N = tostring(D.n),
                R = tostring(D.r),
                B = tostring(D.b),
                Nil = tostring(D.nil),
                Absent = tostring(D.absent),
                BracketMissing = tostring(D['missing']),
                ArrStr = tostring(D.arr),
                BagStr = tostring(D.bag)
            """);

        Assert.That(row["S"], Is.EqualTo("hello"));
        Assert.That(row["N"], Is.EqualTo("42"));
        Assert.That(row["R"], Is.EqualTo("3.5"));
        Assert.That(row["B"]?.ToString()?.ToLowerInvariant(), Is.EqualTo("true"));
        Assert.That(row["Nil"], Is.EqualTo(string.Empty), "tostring(null) must return empty string.");
        Assert.That(row["Absent"], Is.EqualTo(string.Empty), "tostring(absent) must return empty string.");
        Assert.That(row["BracketMissing"], Is.EqualTo(string.Empty), "tostring(D['missing']) must return empty string.");
        AssertDynamicArrayEqual(row["ArrStr"], 1L, "x");
        AssertDynamicBagEqual(row["BagStr"], ("k", 2L));
    }

    [Test]
    public void ToIntAndToLong_ConvertNumericStringAndBoolDynamics_AndReturnNullOnInvalid()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                D = dynamic({
                    "i": 42,
                    "neg": -7,
                    "big": 9000000000000,
                    "flt": 3.9,
                    "negFlt": -3.9,
                    "numStr": "123",
                    "bigStr": "9000000000000",
                    "boolT": true,
                    "boolF": false,
                    "bad": "abc",
                    "nil": null,
                    "arr": [1],
                    "bag": {"a": 1}
                })
            | project
                IntFromInt = toint(D.i),
                IntFromNeg = toint(D.neg),
                IntFromFlt = toint(D.flt),
                IntFromNegFlt = toint(D.negFlt),
                IntFromStr = toint(D.numStr),
                IntFromBigStr = toint(D.bigStr),
                IntFromBoolT = toint(D.boolT),
                IntFromBoolF = toint(D.boolF),
                IntOverflowWrap = toint(D.big),
                IntBad = toint(D.bad),
                IntNull = toint(D.nil),
                IntArr = toint(D.arr),
                IntBag = toint(D.bag),
                LongFromBig = tolong(D.big),
                LongFromBigStr = tolong(D.bigStr),
                LongFromFlt = tolong(D.flt),
                LongFromBoolT = tolong(D.boolT),
                LongBad = tolong(D.bad),
                LongNull = tolong(D.nil),
                LongArr = tolong(D.arr)
            """);

        AssertLongEqual(row["IntFromInt"], 42);
        AssertLongEqual(row["IntFromNeg"], -7);
        AssertLongEqual(row["IntFromFlt"], 3);
        AssertLongEqual(row["IntFromNegFlt"], -3);
        AssertLongEqual(row["IntFromStr"], 123);
        AssertLongEqual(row["IntFromBigStr"], unchecked((int)9_000_000_000_000L),
            "toint() of a numeric string held in dynamic wraps like unchecked (int) in ADX (a plain string out of Int32 range gives null).");
        AssertLongEqual(row["IntFromBoolT"], 1);
        AssertLongEqual(row["IntFromBoolF"], 0);
        AssertLongEqual(row["IntOverflowWrap"], unchecked((int)9_000_000_000_000L),
            "Narrowing an out-of-range long integer via toint() wraps like unchecked (int) in ADX.");
        Assert.That(row["IntBad"], Is.Null);
        Assert.That(row["IntNull"], Is.Null);
        Assert.That(row["IntArr"], Is.Null);
        Assert.That(row["IntBag"], Is.Null);

        AssertLongEqual(row["LongFromBig"], 9_000_000_000_000L);
        AssertLongEqual(row["LongFromBigStr"], 9_000_000_000_000L);
        AssertLongEqual(row["LongFromFlt"], 3L);
        AssertLongEqual(row["LongFromBoolT"], 1L);
        Assert.That(row["LongBad"], Is.Null);
        Assert.That(row["LongNull"], Is.Null);
        Assert.That(row["LongArr"], Is.Null);
    }

    [Test]
    public void ToRealAndToDouble_ConvertNumericStringAndBoolDynamics_AndReturnNullOnInvalid()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                D = dynamic({
                    "i": 10,
                    "r": 2.75,
                    "str": "-1.5e2",
                    "boolT": true,
                    "boolF": false,
                    "bad": "not-a-number",
                    "nil": null,
                    "bag": {}
                })
            | project
                RInt = toreal(D.i),
                RReal = toreal(D.r),
                RStr = toreal(D.str),
                RBoolT = toreal(D.boolT),
                RBoolF = toreal(D.boolF),
                RBad = toreal(D.bad),
                RNull = toreal(D.nil),
                RBag = toreal(D.bag),
                DReal = todouble(D.r),
                DStr = todouble(D.str),
                DBad = todouble(D.bad)
            """);

        AssertNumericEqual(row["RInt"], 10.0);
        AssertNumericEqual(row["RReal"], 2.75);
        AssertNumericEqual(row["RStr"], -150.0);
        AssertNumericEqual(row["RBoolT"], 1.0);
        AssertNumericEqual(row["RBoolF"], 0.0);
        Assert.That(row["RBad"], Is.Null);
        Assert.That(row["RNull"], Is.Null);
        Assert.That(row["RBag"], Is.Null);
        AssertNumericEqual(row["DReal"], 2.75);
        AssertNumericEqual(row["DStr"], -150.0);
        Assert.That(row["DBad"], Is.Null);
    }

    [Test]
    public void ToBool_ConvertsDynamicBooleansStringsAndNumbers_AndReturnNullOnInvalid()
    {
        var db = CreateDatabase();

        var row = ExecuteSingleRow(db, """
            print
                D = dynamic({
                    "bt": true,
                    "bf": false,
                    "st": "true",
                    "sT": "TRUE",
                    "sf": "False",
                    "s1": "1",
                    "s0": "0",
                    "n1": 1,
                    "n5": 5,
                    "n0": 0,
                    "bad": "yes",
                    "nil": null,
                    "arr": [true]
                })
            | project
                BT = tobool(D.bt),
                BF = tobool(D.bf),
                ST = tobool(D.st),
                STU = tobool(D.sT),
                SF = tobool(D.sf),
                S1 = tobool(D.s1),
                S0 = tobool(D.s0),
                N1 = tobool(D.n1),
                N5 = tobool(D.n5),
                N0 = tobool(D.n0),
                Bad = tobool(D.bad),
                Nil = tobool(D.nil),
                ArrB = tobool(D.arr),
                AliasT = toboolean(D.st)
            """);

        Assert.That(row["BT"], Is.EqualTo(true));
        Assert.That(row["BF"], Is.EqualTo(false));
        Assert.That(row["ST"], Is.EqualTo(true));
        Assert.That(row["STU"], Is.EqualTo(true));
        Assert.That(row["SF"], Is.EqualTo(false));
        Assert.That(row["S1"], Is.EqualTo(true));
        Assert.That(row["S0"], Is.EqualTo(false));
        Assert.That(row["N1"], Is.EqualTo(true));
        Assert.That(row["N5"], Is.EqualTo(true));
        Assert.That(row["N0"], Is.EqualTo(false));
        Assert.That(row["Bad"], Is.Null);
        Assert.That(row["Nil"], Is.Null);
        Assert.That(row["ArrB"], Is.Null);
        Assert.That(row["AliasT"], Is.EqualTo(true));
    }

    [Test]
    public void ToDateTimeToTimeSpanAndToGuid_ConvertNativeAndStringDynamics_AndReturnNullOnInvalid()
    {
        var table = CreateKeyedDynamicTable("Temporal", [
            (1, Bag(
                ("dtNative", SampleUtc),
                ("dtIso", "2025-08-23T06:20:00Z"),
                ("dtDateOnly", "2025-08-23"),
                ("dtBad", "not-a-date"),
                ("tsNative", SampleSpan),
                ("tsClock", "01:02:03"),
                ("tsDays", "2.03:04:05"),
                ("tsBad", "not-a-span"),
                ("gNative", SampleGuid),
                ("gHyphen", "74be27de-1e4e-49d9-b579-fe0b331d3642"),
                ("gUpper", "74BE27DE-1E4E-49D9-B579-FE0B331D3642"),
                ("gCompact", "74be27de1e4e49d9b579fe0b331d3642"),
                ("gBad", "not-a-guid"),
                ("nil", null),
                ("bag", Bag(("x", 1)))
            ))
        ]);
        var db = CreateDatabase(table);

        var row = ExecuteSingleRow(db, """
            Temporal
            | project
                DtNative = todatetime(Payload.dtNative),
                DtIso = todatetime(Payload.dtIso),
                DtDateOnly = todatetime(Payload.dtDateOnly),
                DtBad = todatetime(Payload.dtBad),
                DtNull = todatetime(Payload.nil),
                DtBag = todatetime(Payload.bag),
                TsNative = totimespan(Payload.tsNative),
                TsClock = totimespan(Payload.tsClock),
                TsDays = totimespan(Payload.tsDays),
                TsBad = totimespan(Payload.tsBad),
                TsNull = totimespan(Payload.nil),
                GNative = toguid(Payload.gNative),
                GHyphen = toguid(Payload.gHyphen),
                GUpper = toguid(Payload.gUpper),
                GCompact = toguid(Payload.gCompact),
                GBad = toguid(Payload.gBad),
                GNull = toguid(Payload.nil)
            """);

        AssertDateTimeEqual(row["DtNative"], SampleUtc);
        AssertDateTimeEqual(row["DtIso"], SampleUtc);
        AssertDateTimeEqual(row["DtDateOnly"], new DateTime(2025, 8, 23, 0, 0, 0, DateTimeKind.Utc));
        Assert.That(row["DtBad"], Is.Null);
        Assert.That(row["DtNull"], Is.Null);
        Assert.That(row["DtBag"], Is.Null);

        AssertTimeSpanEqual(row["TsNative"], SampleSpan);
        AssertTimeSpanEqual(row["TsClock"], new TimeSpan(1, 2, 3));
        AssertTimeSpanEqual(row["TsDays"], SampleSpan);
        Assert.That(row["TsBad"], Is.Null);
        Assert.That(row["TsNull"], Is.Null);

        AssertGuidEqual(row["GNative"], SampleGuid);
        AssertGuidEqual(row["GHyphen"], SampleGuid);
        AssertGuidEqual(row["GUpper"], SampleGuid);
        AssertGuidEqual(row["GCompact"], SampleGuid);
        Assert.That(row["GBad"], Is.Null);
        Assert.That(row["GNull"], Is.Null);
    }

    [Test]
    public void TypeConversions_GetSchemaReportsTargetScalarTypes()
    {
        var db = CreateDatabase();

        var schemaRows = Execute(db, """
            print D = dynamic({"s": "a", "i": 1, "l": 2, "r": 3.5, "b": true, "dt": "2025-08-23T06:20:00Z", "ts": "01:02:03", "g": "74be27de-1e4e-49d9-b579-fe0b331d3642"})
            | project
                S = tostring(D.s),
                I = toint(D.i),
                L = tolong(D.l),
                R = toreal(D.r),
                Dbl = todouble(D.r),
                B = tobool(D.b),
                Dt = todatetime(D.dt),
                Ts = totimespan(D.ts),
                G = toguid(D.g)
            | getschema
            """);

        AssertSchemaColumn(schemaRows, "S", "string");
        AssertSchemaColumn(schemaRows, "I", "int");
        AssertSchemaColumn(schemaRows, "L", "long");
        AssertSchemaColumn(schemaRows, "R", "real");
        AssertSchemaColumn(schemaRows, "Dbl", "real");
        AssertSchemaColumn(schemaRows, "B", "bool");
        AssertSchemaColumn(schemaRows, "Dt", "datetime");
        AssertSchemaColumn(schemaRows, "Ts", "timespan");
        AssertSchemaColumn(schemaRows, "G", "guid");
    }

    [Test]
    public void TypeConversions_EnableFilteringGroupingSortingAndAggregationOverDynamicPayload()
    {
        var table = CreateKeyedDynamicTable("Orders", [
            (1, Bag(("region", "east"), ("amount", 100.5), ("qty", "2"), ("active", true), ("ts", "2025-08-23T06:00:00Z"))),
            (2, Bag(("region", "east"), ("amount", 49.5), ("qty", "3"), ("active", true), ("ts", "2025-08-23T07:00:00Z"))),
            (3, Bag(("region", "west"), ("amount", 200.0), ("qty", "1"), ("active", false), ("ts", "2025-08-23T08:00:00Z"))),
            (4, Bag(("region", "west"), ("amount", 80.0), ("qty", "4"), ("active", true), ("ts", "2025-08-23T09:00:00Z"))),
            (5, Bag(("region", "west"), ("amount", "bad"), ("qty", null), ("active", true), ("ts", "2025-08-23T10:00:00Z")))
        ]);
        var db = CreateDatabase(table);

        var rows = Execute(db, """
            Orders
            | where tobool(Payload.active) == true and todatetime(Payload.ts) >= datetime(2025-08-23T06:00:00Z)
            | extend Region = tostring(Payload.region), Amount = toreal(Payload.amount), Qty = tolong(Payload.qty)
            | where isnotnull(Amount)
            | summarize TotalAmount = sum(Amount), TotalQty = sum(Qty), OrderCount = count() by Region
            | sort by Region asc
            """);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Region"], Is.EqualTo("east"));
        AssertNumericEqual(rows[0]["TotalAmount"], 150.0);
        AssertLongEqual(rows[0]["TotalQty"], 5L);
        AssertLongEqual(rows[0]["OrderCount"], 2L);

        Assert.That(rows[1]["Region"], Is.EqualTo("west"));
        AssertNumericEqual(rows[1]["TotalAmount"], 80.0);
        AssertLongEqual(rows[1]["TotalQty"], 4L);
        AssertLongEqual(rows[1]["OrderCount"], 1L);
    }

    // =========================================================================
    // 3. bag_pack() / pack()
    // =========================================================================

    [Test]
    public void BagPack_ConstructsBagFromMixedTypesExpressionsDynamicKeysAndNestedStructures()
    {
        var table = CreateTable("Items", [
            new Column<string>("KeyName", isNullable: false),
            new Column<int>("Count", isNullable: false),
            new Column<double>("Price", isNullable: false),
            new Column<object>("Note", isNullable: true)
        ], [
            new Dictionary<string, object?> { { "KeyName", "sku" }, { "Count", 3 }, { "Price", 12.5 }, { "Note", null } }
        ]);
        var db = CreateDatabase(table);

        var row = ExecuteSingleRow(db, """
            Items
            | extend
                Packed = bag_pack(
                    KeyName, "A-100",
                    "total", Count * Price,
                    "note", Note,
                    "when", datetime(2025-08-23T06:20:00Z),
                    "dur", timespan(30m),
                    "id", guid(74be27de-1e4e-49d9-b579-fe0b331d3642),
                    "nested", bag_pack("tags", pack_array("x", "y"))
                ),
                ViaAlias = pack("k1", 1, "k2", "two")
            | project
                Packed,
                ViaAlias,
                HasNullNote = bag_has_key(Packed, "note"),
                WhenType = gettype(Packed.when),
                DurType = gettype(Packed.dur),
                IdType = gettype(Packed.id)
            """);

        AssertDynamicBagEqual(row["Packed"],
            ("sku", "A-100"),
            ("total", 37.5),
            ("note", null),
            ("when", SampleUtc),
            ("dur", TimeSpan.FromMinutes(30)),
            ("id", SampleGuid),
            ("nested", Bag(("tags", Arr("x", "y")))));

        AssertDynamicBagEqual(row["ViaAlias"], ("k1", 1L), ("k2", "two"));
        Assert.That(row["HasNullNote"], Is.EqualTo(true), "bag_pack must retain keys whose value is null.");
        Assert.That(row["WhenType"], Is.EqualTo("datetime"));
        Assert.That(row["DurType"], Is.EqualTo("timespan"));
        Assert.That(row["IdType"], Is.EqualTo("guid"));
    }

    [Test]
    public void BagPack_OddArgumentCount_ReturnsExecutionError()
    {
        var db = CreateDatabase();
        ExecuteAndExpectError(db, """print bag_pack("a", 1, "b")""");
    }

    // =========================================================================
    // 4. pack_array()
    // =========================================================================

    [Test]
    public void PackArray_PreservesOrderDuplicatesMixedTypesNullsAndKustoTypes()
    {
        var table = CreateTable("Src", [
            new Column<int>("A", isNullable: false),
            new Column<object>("B", isNullable: true),
            new Column<bool>("C", isNullable: false)
        ], [
            new Dictionary<string, object?> { { "A", 10 }, { "B", null }, { "C", true } }
        ]);
        var db = CreateDatabase(table);

        var row = ExecuteSingleRow(db, """
            Src
            | extend
                ArrVal = pack_array(A, "dup", "dup", B, C, A * 2, datetime(2025-08-23T06:20:00Z), timespan(5m), guid(74be27de-1e4e-49d9-b579-fe0b331d3642), pack_array(1, 2)),
                SingleVal = pack_array(42),
                AllCols = pack_array(*)
            | project
                ArrVal,
                SingleVal,
                AllCols,
                DtType = gettype(ArrVal[6]),
                TsType = gettype(ArrVal[7]),
                GuidType = gettype(ArrVal[8])
            """);

        AssertDynamicArrayEqual(row["ArrVal"],
            10L, "dup", "dup", null, true, 20L,
            SampleUtc, TimeSpan.FromMinutes(5), SampleGuid, Arr(1L, 2L));
        AssertDynamicArrayEqual(row["SingleVal"], 42L);
        AssertDynamicArrayEqual(row["AllCols"], 10L, null, true);
        Assert.That(row["DtType"], Is.EqualTo("datetime"));
        Assert.That(row["TsType"], Is.EqualTo("timespan"));
        Assert.That(row["GuidType"], Is.EqualTo("guid"));

        ExecuteAndExpectError(db, "print pack_array()");
    }

    // =========================================================================
    // 5. pack_all()
    // =========================================================================

    [Test]
    public void PackAll_PacksAllColumnsInScopeIncludingExistingDynamicAndPreservesNullsByDefault()
    {
        var table = CreateTable("Records", [
            new Column<int>("Id", isNullable: false),
            new Column<object>("Name", isNullable: true),
            new Column<string>("Empty", isNullable: true),
            new Column<int>("Zero", isNullable: false),
            new Column<bool>("Flag", isNullable: false),
            new Column<object>("Meta", isNullable: true)
        ], [
            new Dictionary<string, object?>
            {
                { "Id", 1 },
                { "Name", null },
                { "Empty", "" },
                { "Zero", 0 },
                { "Flag", false },
                { "Meta", Bag(("nested", true)) }
            }
        ]);
        var db = CreateDatabase(table);

        var defaultRow = ExecuteSingleRow(db, """
            Records
            | extend Computed = Id + 10
            | project Id, Name, Empty, Zero, Meta, Computed
            | extend AllDefault = pack_all()
            | project
                AllDefault,
                DefaultHasName = bag_has_key(AllDefault, "Name"),
                DefaultHasEmpty = bag_has_key(AllDefault, "Empty"),
                NestedMetaType = gettype(AllDefault.Meta)
            """);

        var falseRow = ExecuteSingleRow(db, """
            Records
            | extend Computed = Id + 10
            | project Id, Name, Empty, Zero, Meta, Computed
            | project AllFalse = pack_all(false)
            """);

        var ignoreRow = ExecuteSingleRow(db, """
            Records
            | extend Computed = Id + 10
            | project Id, Name, Empty, Zero, Meta, Computed
            | extend AllIgnoreNullEmpty = pack_all(true)
            | project
                AllIgnoreNullEmpty,
                IgnoreHasName = bag_has_key(AllIgnoreNullEmpty, "Name"),
                IgnoreHasEmpty = bag_has_key(AllIgnoreNullEmpty, "Empty"),
                IgnoreHasZero = bag_has_key(AllIgnoreNullEmpty, "Zero")
            """);

        AssertDynamicBagEqual(defaultRow["AllDefault"],
            ("Id", 1L),
            ("Name", null),
            ("Empty", ""),
            ("Zero", 0L),
            ("Meta", Bag(("nested", true))),
            ("Computed", 11L));

        AssertDynamicBagEqual(falseRow["AllFalse"],
            ("Id", 1L),
            ("Name", null),
            ("Empty", ""),
            ("Zero", 0L),
            ("Meta", Bag(("nested", true))),
            ("Computed", 11L));

        AssertDynamicBagEqual(ignoreRow["AllIgnoreNullEmpty"],
            ("Id", 1L),
            ("Zero", 0L),
            ("Meta", Bag(("nested", true))),
            ("Computed", 11L));

        Assert.That(defaultRow["DefaultHasName"], Is.EqualTo(true));
        Assert.That(defaultRow["DefaultHasEmpty"], Is.EqualTo(true));
        Assert.That(ignoreRow["IgnoreHasName"], Is.EqualTo(false));
        Assert.That(ignoreRow["IgnoreHasEmpty"], Is.EqualTo(false));
        Assert.That(ignoreRow["IgnoreHasZero"], Is.EqualTo(true));
        Assert.That(defaultRow["NestedMetaType"], Is.EqualTo("dictionary"),
            "Existing dynamic columns packed via pack_all() should remain structured, not double-encoded.");
    }

    [Test]
    public void PackAll_IgnoreNullEmpty_KeepsEmptyContainersAndZeroAndFalse()
    {
        var table = CreateTable("EdgePack", [
            new Column<int>("Id", isNullable: false),
            new Column<bool>("Flag", isNullable: false),
            new Column<object>("EmptyArr", isNullable: true),
            new Column<object>("EmptyBag", isNullable: true),
            new Column<object>("NullDyn", isNullable: true)
        ], [
            new Dictionary<string, object?>
            {
                { "Id", 0 },
                { "Flag", false },
                { "EmptyArr", Arr() },
                { "EmptyBag", Bag() },
                { "NullDyn", null }
            }
        ]);
        var db = CreateDatabase(table);

        var row = ExecuteSingleRow(db, """
            EdgePack
            | project Packed = pack_all(true)
            """);

        AssertDynamicBagEqual(row["Packed"],
            ("Id", 0L),
            ("Flag", false),
            ("EmptyArr", Arr()),
            ("EmptyBag", Bag()));
    }
}
