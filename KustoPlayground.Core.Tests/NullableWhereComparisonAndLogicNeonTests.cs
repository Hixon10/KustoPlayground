using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Azure Kusto null semantics for typed null literals, comparison operators (==, !=, &lt;, &lt;=, &gt;, &gt;=),
/// 3-valued boolean logic (and, or, not), where filtering, and null-testing functions
/// (isnull, isnotnull, isempty, isnotempty).
/// Reference: https://learn.microsoft.com/en-us/kusto/query/scalar-data-types/null-values
/// </summary>
public class NullableWhereComparisonAndLogicNeonTests
{
    #region Typed null literals

    [TestCase("print x = bool(null)")]
    [TestCase("print x = int(null)")]
    [TestCase("print x = long(null)")]
    [TestCase("print x = real(null)")]
    [TestCase("print x = double(null)")]
    [TestCase("print x = decimal(null)")]
    [TestCase("print x = datetime(null)")]
    [TestCase("print x = timespan(null)")]
    [TestCase("print x = guid(null)")]
    [TestCase("print x = dynamic(null)")]
    [Description("Typed null literals evaluate to null")]
    public void TypedNullLiterals_EvaluateToNull(string query)
    {
        AssertPrintNull(query, "x");
    }

    [Test]
    [Description("datetime(null) evaluates to null while datetime() remains a non-null DateTime")]
    public void DateTimeNullLiteral_DistinguishedFromEmptyDateTime()
    {
        var kustoDatabase = new KustoDatabase();
        var row = ExecuteAndGetSingleRow(kustoDatabase, "print NullDt = datetime(null), NowDt = datetime()");

        Assert.That(row["NullDt"], Is.Null);
        Assert.That(row["NowDt"], Is.InstanceOf<DateTime>());
    }

    #endregion

    #region Equality and inequality truth tables

    [TestCase("print int(null) == int(null)", null)]
    [TestCase("print long(null) == long(null)", null)]
    [TestCase("print real(null) == real(null)", null)]
    [TestCase("print double(null) == double(null)", null)]
    [TestCase("print decimal(null) == decimal(null)", null)]
    [TestCase("print bool(null) == bool(null)", null)]
    [TestCase("print datetime(null) == datetime(null)", null)]
    [TestCase("print timespan(null) == timespan(null)", null)]
    [TestCase("print guid(null) == guid(null)", null)]
    [TestCase("print dynamic(null) == dynamic(null)", null)]
    [TestCase("print int(null) == long(null)", null)]
    [TestCase("print int(null) == real(null)", null)]
    [Description("Kusto equality of two null values returns bool(null), not true")]
    public void Equality_NullEqualsNull_ReturnsNull(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    [TestCase("print int(null) != int(null)", null)]
    [TestCase("print long(null) != long(null)", null)]
    [TestCase("print real(null) != real(null)", null)]
    [TestCase("print decimal(null) != decimal(null)", null)]
    [TestCase("print bool(null) != bool(null)", null)]
    [TestCase("print datetime(null) != datetime(null)", null)]
    [TestCase("print timespan(null) != timespan(null)", null)]
    [TestCase("print guid(null) != guid(null)", null)]
    [TestCase("print dynamic(null) != dynamic(null)", null)]
    [Description("Kusto inequality of two null values returns bool(null), not false")]
    public void Inequality_NullNotEqualsNull_ReturnsNull(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    [TestCase("print int(null) == 0", false)]
    [TestCase("print 0 == int(null)", false)]
    [TestCase("print int(null) == 5", false)]
    [TestCase("print 5 == int(null)", false)]
    [TestCase("print long(null) == 0", false)]
    [TestCase("print real(null) == 0.0", false)]
    [TestCase("print decimal(null) == decimal(0)", false)]
    [TestCase("print bool(null) == false", false)]
    [TestCase("print false == bool(null)", false)]
    [TestCase("print bool(null) == true", false)]
    [TestCase("print datetime(null) == datetime(2024-01-01)", false)]
    [TestCase("print datetime(2024-01-01) == datetime(null)", false)]
    [TestCase("print timespan(null) == 0s", false)]
    [TestCase("print 1h == timespan(null)", false)]
    [TestCase("print guid(null) == guid(00000000-0000-0000-0000-000000000000)", false)]
    [Description("Kusto equality between null and any non-null value returns false")]
    public void Equality_NullAndNonNull_ReturnsFalse(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    [TestCase("print int(null) != 0", true)]
    [TestCase("print 0 != int(null)", true)]
    [TestCase("print int(null) != 5", true)]
    [TestCase("print 5 != int(null)", true)]
    [TestCase("print long(null) != 10", true)]
    [TestCase("print real(null) != 3.14", true)]
    [TestCase("print decimal(null) != decimal(1.5)", true)]
    [TestCase("print bool(null) != false", true)]
    [TestCase("print bool(null) != true", true)]
    [TestCase("print true != bool(null)", true)]
    [TestCase("print datetime(null) != datetime(2024-01-01)", true)]
    [TestCase("print timespan(null) != 1h", true)]
    [TestCase("print guid(null) != guid(11111111-1111-1111-1111-111111111111)", true)]
    [Description("Kusto inequality between null and any non-null value returns true")]
    public void Inequality_NullAndNonNull_ReturnsTrue(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    #endregion

    #region Ordering comparisons (<, <=, >, >=)

    [TestCase("print int(null) < 5")]
    [TestCase("print int(null) <= 5")]
    [TestCase("print int(null) > 5")]
    [TestCase("print int(null) >= 5")]
    [TestCase("print 5 < int(null)")]
    [TestCase("print 5 <= int(null)")]
    [TestCase("print 5 > int(null)")]
    [TestCase("print 5 >= int(null)")]
    [TestCase("print int(null) < int(null)")]
    [TestCase("print int(null) <= int(null)")]
    [TestCase("print int(null) > int(null)")]
    [TestCase("print int(null) >= int(null)")]
    [TestCase("print real(null) < 0.0")]
    [TestCase("print real(null) >= -100.5")]
    [TestCase("print decimal(null) > decimal(0)")]
    [TestCase("print datetime(null) < datetime(2025-01-01)")]
    [TestCase("print datetime(2025-01-01) <= datetime(null)")]
    [TestCase("print timespan(null) > 0s")]
    [TestCase("print 1d >= timespan(null)")]
    [TestCase("print bool(null) < true")]
    [TestCase("print false <= bool(null)")]
    [Description("Ordering comparisons with a null operand always evaluate to bool(null)")]
    public void OrderingComparisons_WithNull_AlwaysReturnNull(string query)
    {
        AssertPrintNull(query);
    }

    [Test]
    [Description("Ordering comparisons between nullable real column values (including NaN and infinities) and null produce null")]
    public void OrderingComparisons_SpecialFloatsWithNull_ProduceNull()
    {
        var table = CreateNullableTable2<int?, double?>(
            [
                (1, null),
                (2, double.NaN),
                (3, double.NegativeInfinity),
                (4, double.PositiveInfinity),
                (5, 0.0)
            ],
            tableName: "Metrics",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetNullable3Columns<int?, bool?, bool?>(
            "Id",
            "LtNull",
            "EqNull",
            kustoDatabase,
            "Metrics | extend LtNull = Val < real(null), EqNull = Val == real(null) | project Id, LtNull, EqNull");

        Assert.That(rows, Is.EquivalentTo(new List<(int?, bool?, bool?)>
        {
            (1, null, null),
            (2, null, false),
            (3, null, false),
            (4, null, false),
            (5, null, false)
        }));
    }

    #endregion

    #region 3-valued boolean logic (and, or, not)

    [TestCase("print true and true", true)]
    [TestCase("print true and false", false)]
    [TestCase("print false and true", false)]
    [TestCase("print false and false", false)]
    [TestCase("print true and bool(null)", null)]
    [TestCase("print bool(null) and true", null)]
    [TestCase("print false and bool(null)", false)]
    [TestCase("print bool(null) and false", false)]
    [TestCase("print bool(null) and bool(null)", null)]
    [Description("Complete 3-valued truth table for logical AND")]
    public void LogicalAnd_FullThreeValuedTruthTable(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    [TestCase("print true or true", true)]
    [TestCase("print true or false", true)]
    [TestCase("print false or true", true)]
    [TestCase("print false or false", false)]
    [TestCase("print true or bool(null)", true)]
    [TestCase("print bool(null) or true", true)]
    [TestCase("print false or bool(null)", null)]
    [TestCase("print bool(null) or false", null)]
    [TestCase("print bool(null) or bool(null)", null)]
    [Description("Complete 3-valued truth table for logical OR")]
    public void LogicalOr_FullThreeValuedTruthTable(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    [TestCase("print not(true)", false)]
    [TestCase("print not(false)", true)]
    [TestCase("print not(bool(null))", null)]
    [TestCase("print not(not(bool(null)))", null)]
    [TestCase("print not(int(null) == 5)", true)]
    [TestCase("print not(int(null) != 5)", false)]
    [TestCase("print not(int(null) == int(null))", null)]
    [TestCase("print not(int(null) > 5)", null)]
    [Description("Logical not() preserves bool(null) and negates known booleans")]
    public void LogicalNot_ThreeValuedTruthTable(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    [Test]
    [Description("3-valued logic evaluated across two nullable bool columns satisfies De Morgan's laws")]
    public void LogicalOperators_TwoNullableBoolColumns_DeMorganEquivalence()
    {
        bool?[] values = [true, false, null];
        var builder = new NullableTableBuilder("Flags")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<bool>("A")
            .WithColumn<bool>("B");

        int id = 1;
        foreach (bool? a in values)
        {
            foreach (bool? b in values)
            {
                builder.WithRow(id++, a, b);
            }
        }

        var kustoDatabase = CreateDatabase(builder.Build(shuffle: false));

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Flags | extend AndRes = A and B, OrRes = A or B, NotAnd = not(A and B), DeMorgan1 = not(A) or not(B), " +
            "NotOr = not(A or B), DeMorgan2 = not(A) and not(B)");

        Assert.That(rows, Has.Count.EqualTo(9));
        foreach (var row in rows)
        {
            bool? a = (bool?)row["A"];
            bool? b = (bool?)row["B"];
            bool? expectedAnd = (a == false || b == false) ? false : (a == true && b == true) ? true : null;
            bool? expectedOr = (a == true || b == true) ? true : (a == false && b == false) ? false : null;

            Assert.That(row["AndRes"], Is.EqualTo(expectedAnd));
            Assert.That(row["OrRes"], Is.EqualTo(expectedOr));
            Assert.That(row["NotAnd"], Is.EqualTo(row["DeMorgan1"]));
            Assert.That(row["NotOr"], Is.EqualTo(row["DeMorgan2"]));
        }
    }

    #endregion

    #region where operator null behavior and gotchas

    [Test]
    [Description("where Col == 5 drops nulls, while where Col != 5 keeps nulls (Azure Kusto semantics)")]
    public void Where_EqualsDropsNulls_NotEqualsKeepsNulls()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 5), (2, 10), (3, null), (4, 0)],
            tableName: "Numbers",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var eqIds = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where Val == 5");
        Assert.That(eqIds, Is.EquivalentTo(new int?[] { 1 }));

        var neqIds = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where Val != 5");
        Assert.That(neqIds, Is.EquivalentTo(new int?[] { 2, 3, 4 }));
    }

    [Test]
    [Description("where Col == int(null) drops ALL rows (including nulls); where Col != int(null) keeps non-null rows only")]
    public void Where_CompareAgainstNullLiteral_FollowsThreeValuedLogic()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 5), (2, null), (3, 0), (4, null)],
            tableName: "Numbers",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var eqNullIds = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where Val == int(null)");
        Assert.That(eqNullIds, Is.Empty);

        var neqNullIds = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where Val != int(null)");
        Assert.That(neqNullIds, Is.EquivalentTo(new int?[] { 1, 3 }));
    }

    [Test]
    [Description("where Col == Col drops null rows; where Col != Col drops all rows")]
    public void Where_SelfComparison_DropsNullsForBothEqualsAndNotEquals()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 5), (2, null), (3, -7)],
            tableName: "Numbers",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var selfEq = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where Val == Val");
        Assert.That(selfEq, Is.EquivalentTo(new int?[] { 1, 3 }));

        var selfNeq = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where Val != Val");
        Assert.That(selfNeq, Is.Empty);
    }

    [Test]
    [Description("where comparing two nullable columns: (null, null) is dropped by both == and !=; (null, non-null) is kept by !=")]
    public void Where_TwoNullableColumnsComparison()
    {
        var table = new NullableTableBuilder("Pairs")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<int>("Left")
            .WithColumn<int>("Right")
            .WithRow(1, 10, 10)
            .WithRow(2, 10, 20)
            .WithRow(3, 10, null)
            .WithRow(4, null, 20)
            .WithRow(5, null, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Pairs | where Left == Right"),
            Is.EquivalentTo(new int?[] { 1 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Pairs | where Left != Right"),
            Is.EquivalentTo(new int?[] { 2, 3, 4 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Pairs | where Left < Right"),
            Is.EquivalentTo(new int?[] { 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Pairs | where Left <= Right"),
            Is.EquivalentTo(new int?[] { 1, 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Pairs | where Left > Right"),
            Is.Empty);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Pairs | where Left >= Right"),
            Is.EquivalentTo(new int?[] { 1 }));
    }

    [Test]
    [Description("where not(Col == 5) keeps nulls (not(false) == true), whereas where not(Col > 5) drops nulls (not(null) == null)")]
    public void Where_NotWrappingComparison_DistinguishesFalseFromNull()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 3), (2, 5), (3, 8), (4, null)],
            tableName: "Numbers",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var notEq = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where not(Val == 5)");
        Assert.That(notEq, Is.EquivalentTo(new int?[] { 1, 3, 4 }));

        var notGt = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where not(Val > 5)");
        Assert.That(notGt, Is.EquivalentTo(new int?[] { 1, 2 }));

        var notLt = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where not(Val < 5)");
        Assert.That(notLt, Is.EquivalentTo(new int?[] { 2, 3 }));
    }

    [Test]
    [Description("where on a nullable bool column: bare Flag keeps true only, not(Flag) keeps false only, Flag != true keeps false and null")]
    public void Where_NullableBoolColumn_FiltersCorrectly()
    {
        var table = CreateNullableTable2<int?, bool?>(
            [(1, true), (2, false), (3, null), (4, true)],
            tableName: "Flags",
            columnName1: "Id",
            columnName2: "Active");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Flags | where Active"),
            Is.EquivalentTo(new int?[] { 1, 4 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Flags | where not(Active)"),
            Is.EquivalentTo(new int?[] { 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Flags | where Active == true"),
            Is.EquivalentTo(new int?[] { 1, 4 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Flags | where Active == false"),
            Is.EquivalentTo(new int?[] { 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Flags | where Active != true"),
            Is.EquivalentTo(new int?[] { 2, 3 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Flags | where Active != false"),
            Is.EquivalentTo(new int?[] { 1, 3, 4 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Flags | where isnull(Active)"),
            Is.EquivalentTo(new int?[] { 3 }));
    }

    [Test]
    [Description("where combining ordering comparison with or isnull(Col) keeps matching rows and nulls")]
    public void Where_OrIsNull_IncludesNullRows()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 1), (2, 5), (3, null), (4, 10)],
            tableName: "Numbers",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var ids = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where Val > 3 or isnull(Val)");
        Assert.That(ids, Is.EquivalentTo(new int?[] { 2, 3, 4 }));

        var chained = ExecuteAndGetNullableColumn<int?>(
            "Id",
            kustoDatabase,
            "Numbers | where isnotnull(Val) | where Val <= 5");
        Assert.That(chained, Is.EquivalentTo(new int?[] { 1, 2 }));
    }

    [Test]
    [Description("where filtering works cleanly on nullable long, double, decimal, datetime, timespan, and guid columns")]
    public void Where_AllNullableScalarTypes()
    {
        var g1 = new Guid("11111111-1111-1111-1111-111111111111");
        var g2 = new Guid("22222222-2222-2222-2222-222222222222");
        var dt1 = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var dt2 = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        var table = new NullableTableBuilder("Typed")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<long>("L")
            .WithColumn<double>("D")
            .WithColumn<decimal>("M")
            .WithColumn<DateTime>("Dt")
            .WithColumn<TimeSpan>("Ts")
            .WithColumn<Guid>("G")
            .WithRow(1, 100L, 1.5, 10.25m, dt1, TimeSpan.FromMinutes(30), g1)
            .WithRow(2, null, null, null, null, null, null)
            .WithRow(3, 500L, 9.5, 50.75m, dt2, TimeSpan.FromHours(3), g2)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        Assert.That(ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where L > 200"),
            Is.EquivalentTo(new int?[] { 3 }));
        Assert.That(ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where D <= 5.0"),
            Is.EquivalentTo(new int?[] { 1 }));
        Assert.That(ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where M != decimal(10.25)"),
            Is.EquivalentTo(new int?[] { 2, 3 }));
        Assert.That(ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where Dt >= datetime(2024-03-01)"),
            Is.EquivalentTo(new int?[] { 3 }));
        Assert.That(ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where Ts < 1h"),
            Is.EquivalentTo(new int?[] { 1 }));
        Assert.That(ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where G == guid(11111111-1111-1111-1111-111111111111)"),
            Is.EquivalentTo(new int?[] { 1 }));
        Assert.That(ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where G != guid(11111111-1111-1111-1111-111111111111)"),
            Is.EquivalentTo(new int?[] { 2, 3 }));
    }

    #endregion

    #region isnull, isnotnull, isempty, isnotempty

    [TestCase("print isnull(int(null))", true)]
    [TestCase("print isnull(long(null))", true)]
    [TestCase("print isnull(real(null))", true)]
    [TestCase("print isnull(decimal(null))", true)]
    [TestCase("print isnull(bool(null))", true)]
    [TestCase("print isnull(datetime(null))", true)]
    [TestCase("print isnull(timespan(null))", true)]
    [TestCase("print isnull(guid(null))", true)]
    [TestCase("print isnull(dynamic(null))", true)]
    [TestCase("print isnull(0)", false)]
    [TestCase("print isnull(0.0)", false)]
    [TestCase("print isnull(false)", false)]
    [TestCase("print isnull(0s)", false)]
    [TestCase("print isnull(\"\")", false)]
    [TestCase("print isnull(\"abc\")", false)]
    [TestCase("print isnull(tostring(int(null)))", false)]
    [Description("isnull() returns true for null values and false for zero, false, empty string, and non-null values")]
    public void IsNull_ScalarCases(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    [TestCase("print isnotnull(int(null))", false)]
    [TestCase("print isnotnull(datetime(null))", false)]
    [TestCase("print isnotnull(bool(null))", false)]
    [TestCase("print isnotnull(0)", true)]
    [TestCase("print isnotnull(false)", true)]
    [TestCase("print isnotnull(\"\")", true)]
    [TestCase("print isnotnull(tostring(int(null)))", true)]
    [Description("isnotnull() is the exact boolean complement of isnull()")]
    public void IsNotNull_ScalarCases(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    [TestCase("print isempty(\"\")", true)]
    [TestCase("print isempty(\" \")", false)]
    [TestCase("print isempty(\"abc\")", false)]
    [TestCase("print isempty(int(null))", true)]
    [TestCase("print isempty(datetime(null))", true)]
    [TestCase("print isempty(bool(null))", true)]
    [TestCase("print isempty(0)", false)]
    [TestCase("print isempty(false)", false)]
    [TestCase("print isnotempty(\"\")", false)]
    [TestCase("print isnotempty(\" \")", true)]
    [TestCase("print isnotempty(\"abc\")", true)]
    [TestCase("print isnotempty(int(null))", false)]
    [TestCase("print isnotempty(0)", true)]
    [TestCase("print isnotempty(false)", true)]
    [Description("isempty() and isnotempty() treat both empty string and null as empty, while zero/false/whitespace are not empty")]
    public void IsEmptyAndIsNotEmpty_ScalarCases(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    [Test]
    [Description("isnull() and isnotnull() evaluate expressions that produce null at runtime")]
    public void IsNullAndIsNotNull_OnComputedExpressions()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 10), (2, 0), (3, null)],
            tableName: "Items",
            columnName1: "Id",
            columnName2: "Divisor");
        var kustoDatabase = CreateDatabase(table);

        var results = ExecuteAndGetNullable3Columns<int?, bool?, bool?>(
            "Id",
            "DivNull",
            "DivNotNull",
            kustoDatabase,
            "Items | extend Ratio = 100 / Divisor | extend DivNull = isnull(Ratio), DivNotNull = isnotnull(Ratio) | project Id, DivNull, DivNotNull");

        Assert.That(results, Is.EquivalentTo(new List<(int?, bool?, bool?)>
        {
            (1, false, true),
            (2, true, false),
            (3, true, false)
        }));
    }

    [Test]
    [Description("where isempty() and isnotempty() filter nullable string and nullable numeric columns")]
    public void Where_IsEmptyAndIsNotEmpty_OnNullableColumns()
    {
        var table = new NullableTableBuilder("Records")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<string>("Name")
            .WithColumn<int>("Score")
            .WithRow(1, "Alice", 10)
            .WithRow(2, "", 0)
            .WithRow(3, null, null)
            .WithRow(4, " ", 5)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Records | where isempty(Name)"),
            Is.EquivalentTo(new int?[] { 2, 3 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Records | where isnotempty(Name)"),
            Is.EquivalentTo(new int?[] { 1, 4 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Records | where isempty(Score)"),
            Is.EquivalentTo(new int?[] { 3 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Records | where isnotempty(Score)"),
            Is.EquivalentTo(new int?[] { 1, 2, 4 }));
    }

    #endregion
}
