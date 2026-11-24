using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Azure Kusto null semantics for scalar membership operators (in, in~, !in, !in~)
/// and range operators (between, !between) with literal lists, column expressions, and tabular subqueries.
/// References:
/// - https://learn.microsoft.com/en-us/kusto/query/scalar-data-types/null-values
/// - https://learn.microsoft.com/en-us/kusto/query/in-cs-operator
/// - https://learn.microsoft.com/en-us/kusto/query/between-operator
/// </summary>
public class NullableInAndBetweenNeonTests
{
    #region in / !in scalar truth tables (Kusto OR-of-== / AND-of-!=)

    [TestCase("print int(null) in (1, 2)", false)]
    [TestCase("print int(null) !in (1, 2)", true)]
    [TestCase("print int(null) in (1, int(null))", null)]
    [TestCase("print int(null) !in (1, int(null))", null)]
    [TestCase("print int(null) in (int(null))", null)]
    [TestCase("print int(null) !in (int(null))", null)]
    [TestCase("print 1 in (1, int(null))", true)]
    [TestCase("print 1 !in (1, int(null))", false)]
    [TestCase("print 2 in (1, int(null))", false)]
    [TestCase("print 2 !in (1, int(null))", true)]
    [TestCase("print 2 in (int(null))", false)]
    [TestCase("print 2 !in (int(null))", true)]
    [TestCase("print bool(null) in (true)", false)]
    [TestCase("print bool(null) !in (true)", true)]
    [TestCase("print bool(null) in (true, bool(null))", null)]
    [TestCase("print bool(null) !in (true, bool(null))", null)]
    [TestCase("print datetime(null) in (datetime(2024-01-01))", false)]
    [TestCase("print datetime(null) !in (datetime(2024-01-01))", true)]
    [TestCase("print datetime(null) in (datetime(2024-01-01), datetime(null))", null)]
    [TestCase("print datetime(null) !in (datetime(2024-01-01), datetime(null))", null)]
    [Description("in behaves like OR of == comparisons and !in behaves like AND of != comparisons under Kusto null semantics")]
    public void InAndNotIn_ScalarTruthTable(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    #endregion

    #region in / !in / in~ / !in~ on nullable columns

    [Test]
    [Description("where Val in (1, 2) drops nulls; where Val !in (1, 2) keeps nulls")]
    public void Where_InAndNotIn_NonNullList_WithNullableIntColumn()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 1), (2, 2), (3, 3), (4, null)],
            tableName: "Items",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Items | where Val in (1, 2)"),
            Is.EquivalentTo(new int?[] { 1, 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Items | where Val !in (1, 2)"),
            Is.EquivalentTo(new int?[] { 3, 4 }));
    }

    [Test]
    [Description("When the RHS list contains int(null), null rows are dropped by BOTH in and !in, while non-null unmatched rows are kept by !in")]
    public void Where_InAndNotIn_ListContainingNull_DropsNullRowsForBoth()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 1), (2, 2), (3, null)],
            tableName: "Items",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Items | where Val in (1, int(null))"),
            Is.EquivalentTo(new int?[] { 1 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Items | where Val !in (1, int(null))"),
            Is.EquivalentTo(new int?[] { 2 }));
    }

    [Test]
    [Description("extend with in and !in yields exact 3-valued bool? results per row")]
    public void Extend_InAndNotIn_ProducesExactThreeValuedBooleans()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 1), (2, 5), (3, null)],
            tableName: "Items",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Items | extend InNoNull = Val in (1, 2), NotInNoNull = Val !in (1, 2), " +
            "InWithNull = Val in (1, int(null)), NotInWithNull = Val !in (1, int(null))");

        var byId = rows.ToDictionary(r => (int)r["Id"]!);
        Assert.That(byId[1]["InNoNull"], Is.EqualTo(true));
        Assert.That(byId[1]["NotInNoNull"], Is.EqualTo(false));
        Assert.That(byId[1]["InWithNull"], Is.EqualTo(true));
        Assert.That(byId[1]["NotInWithNull"], Is.EqualTo(false));

        Assert.That(byId[2]["InNoNull"], Is.EqualTo(false));
        Assert.That(byId[2]["NotInNoNull"], Is.EqualTo(true));
        Assert.That(byId[2]["InWithNull"], Is.EqualTo(false));
        Assert.That(byId[2]["NotInWithNull"], Is.EqualTo(true));

        Assert.That(byId[3]["InNoNull"], Is.EqualTo(false));
        Assert.That(byId[3]["NotInNoNull"], Is.EqualTo(true));
        Assert.That(byId[3]["InWithNull"], Is.Null);
        Assert.That(byId[3]["NotInWithNull"], Is.Null);
    }

    [Test]
    [Description("in, in~, !in, !in~ on nullable string columns with case-sensitivity and empty string")]
    public void Where_InCaseSensitiveAndInsensitive_NullableStringColumn()
    {
        var table = CreateNullableTable2<int?, string?>(
            [(1, "Alpha"), (2, "alpha"), (3, "Beta"), (4, ""), (5, null)],
            tableName: "Tags",
            columnName1: "Id",
            columnName2: "Label");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Tags | where Label in (\"Alpha\", \"Gamma\")"),
            Is.EquivalentTo(new int?[] { 1 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Tags | where Label !in (\"Alpha\", \"Gamma\")"),
            Is.EquivalentTo(new int?[] { 2, 3, 4, 5 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Tags | where Label in~ (\"ALPHA\")"),
            Is.EquivalentTo(new int?[] { 1, 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Tags | where Label !in~ (\"ALPHA\")"),
            Is.EquivalentTo(new int?[] { 3, 4, 5 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Tags | where Label in (\"\")"),
            Is.EquivalentTo(new int?[] { 4, 5 }));
    }

    [Test]
    [Description("in / !in with computed expression on LHS and column references on RHS when nulls are present")]
    public void Where_InAndNotIn_ComputedLhsAndColumnRhs_WithNulls()
    {
        var table = new NullableTableBuilder("Pairs")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<int>("A")
            .WithColumn<int>("B")
            .WithRow(1, 2, 3)
            .WithRow(2, 5, null)
            .WithRow(3, null, 3)
            .WithRow(4, null, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        // (A + 1) in (3, 10): row 1 -> 3 in (3, 10) = true; row 2 -> 6 = false; rows 3,4 -> null = false
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Pairs | where (A + 1) in (3, 10)"),
            Is.EquivalentTo(new int?[] { 1 }));

        // (A + 1) !in (3, 10): row 1 -> false; rows 2, 3, 4 -> true
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Pairs | where (A + 1) !in (3, 10)"),
            Is.EquivalentTo(new int?[] { 2, 3, 4 }));

        // A in (B, 5): row 1 -> 2 in (3, 5) = false; row 2 -> 5 in (null, 5) = true;
        // row 3 -> null in (3, 5) = false; row 4 -> null in (null, 5) = null (dropped)
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Pairs | where A in (B, 5)"),
            Is.EquivalentTo(new int?[] { 2 }));

        // A !in (B, 5): row 1 -> true; row 2 -> false; row 3 -> true; row 4 -> null (dropped)
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Pairs | where A !in (B, 5)"),
            Is.EquivalentTo(new int?[] { 1, 3 }));
    }

    [Test]
    [Description("in and !in across nullable datetime, timespan, double, decimal, and guid columns")]
    public void Where_InAndNotIn_OtherNullableScalarTypes()
    {
        var g1 = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var dt1 = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var table = new NullableTableBuilder("Typed")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<DateTime>("Dt")
            .WithColumn<TimeSpan>("Ts")
            .WithColumn<Guid>("G")
            .WithRow(1, dt1, TimeSpan.FromHours(1), g1)
            .WithRow(2, null, null, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where Dt in (datetime(2024-05-01))"),
            Is.EquivalentTo(new int?[] { 1 }));
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where Dt !in (datetime(2024-05-01))"),
            Is.EquivalentTo(new int?[] { 2 }));
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where Ts in (1h, 2h)"),
            Is.EquivalentTo(new int?[] { 1 }));
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where Ts !in (1h, 2h)"),
            Is.EquivalentTo(new int?[] { 2 }));
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where G in (guid(aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa))"),
            Is.EquivalentTo(new int?[] { 1 }));
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Typed | where G !in (guid(aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa))"),
            Is.EquivalentTo(new int?[] { 2 }));
    }

    #endregion

    #region in / !in with tabular subqueries

    [Test]
    [Description("Tabular subquery in / !in when outer table has nulls and subquery has NO nulls")]
    public void Where_InAndNotIn_SubqueryWithoutNulls_OuterHasNulls()
    {
        var orders = CreateNullableTable2<int?, int?>(
            [(1, 10), (2, 20), (3, 30), (4, null)],
            tableName: "Orders",
            columnName1: "OrderId",
            columnName2: "CustomerId");
        var vip = CreateNullableTable<int?>([10, 20], tableName: "Vip", columnName: "CustomerId");
        var kustoDatabase = CreateDatabase(orders, vip);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("OrderId", kustoDatabase, "Orders | where CustomerId in (Vip | project CustomerId)"),
            Is.EquivalentTo(new int?[] { 1, 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("OrderId", kustoDatabase, "Orders | where CustomerId !in (Vip | project CustomerId)"),
            Is.EquivalentTo(new int?[] { 3, 4 }));
    }

    [Test]
    [Description("Tabular in / !in never matches a null key, even if the subquery also contains null")]
    public void Where_InAndNotIn_SubqueryWithNulls_OuterHasNulls()
    {
        var orders = CreateNullableTable2<int?, int?>(
            [(1, 10), (2, 20), (3, null)],
            tableName: "Orders",
            columnName1: "OrderId",
            columnName2: "CustomerId");
        var blocked = CreateNullableTable<int?>([10, null], tableName: "Blocked", columnName: "CustomerId");
        var kustoDatabase = CreateDatabase(orders, blocked);

        // Only 10 matches the subquery.
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("OrderId", kustoDatabase, "Orders | where CustomerId in (Blocked | project CustomerId)"),
            Is.EquivalentTo(new int?[] { 1 }));

        // Unlike a scalar list, subquery membership is a definite bool for a null key.
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("OrderId", kustoDatabase, "Orders | where CustomerId !in (Blocked | project CustomerId)"),
            Is.EquivalentTo(new int?[] { 2, 3 }));
    }

    [Test]
    [Description("Tabular subquery returning zero rows: in drops all rows (including nulls) and !in keeps all rows (including nulls)")]
    public void Where_InAndNotIn_EmptySubquery_WithNulls()
    {
        var orders = CreateNullableTable2<int?, int?>(
            [(1, 10), (2, null)],
            tableName: "Orders",
            columnName1: "OrderId",
            columnName2: "CustomerId");
        var empty = CreateEmptyNullableTable("Empty", ("CustomerId", typeof(int)));
        var kustoDatabase = CreateDatabase(orders, empty);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("OrderId", kustoDatabase, "Orders | where CustomerId in (Empty | project CustomerId)"),
            Is.Empty);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("OrderId", kustoDatabase, "Orders | where CustomerId !in (Empty | project CustomerId)"),
            Is.EquivalentTo(new int?[] { 1, 2 }));
    }

    [Test]
    [Description("Case-insensitive in~ and !in~ with tabular subquery on nullable string columns")]
    public void Where_InTildeAndNotInTilde_Subquery_WithNullableStrings()
    {
        var items = CreateNullableTable2<int?, string?>(
            [(1, "Red"), (2, "blue"), (3, null), (4, "")],
            tableName: "Items",
            columnName1: "Id",
            columnName2: "Color");
        var allowed = CreateNullableTable<string?>(["RED", "GREEN"], tableName: "Allowed", columnName: "Color");
        var kustoDatabase = CreateDatabase(items, allowed);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Items | where Color in~ (Allowed | project Color)"),
            Is.EquivalentTo(new int?[] { 1 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Items | where Color !in~ (Allowed | project Color)"),
            Is.EquivalentTo(new int?[] { 2, 3, 4 }));
    }

    #endregion

    #region between / !between scalar truth tables

    [TestCase("print int(null) between (1 .. 10)", null)]
    [TestCase("print int(null) !between (1 .. 10)", null)]
    [TestCase("print 5 between (int(null) .. 10)", null)]
    [TestCase("print 5 between (int(null) .. 3)", false)]
    [TestCase("print 5 between (1 .. int(null))", null)]
    [TestCase("print 5 between (10 .. int(null))", false)]
    [TestCase("print 5 between (int(null) .. int(null))", null)]
    [TestCase("print int(null) between (int(null) .. int(null))", null)]
    [TestCase("print 5 !between (int(null) .. 10)", null)]
    [TestCase("print 5 !between (int(null) .. 3)", true)]
    [TestCase("print 5 !between (1 .. int(null))", null)]
    [TestCase("print 5 !between (10 .. int(null))", true)]
    [TestCase("print 5 !between (int(null) .. int(null))", null)]
    [TestCase("print int(null) !between (int(null) .. int(null))", null)]
    [TestCase("print datetime(null) between (datetime(2024-01-01) .. datetime(2024-12-31))", null)]
    [TestCase("print datetime(null) !between (datetime(2024-01-01) .. datetime(2024-12-31))", null)]
    [TestCase("print datetime(2024-06-01) between (datetime(null) .. datetime(2024-03-01))", false)]
    [TestCase("print datetime(2024-06-01) !between (datetime(null) .. datetime(2024-03-01))", true)]
    [TestCase("print datetime(2024-06-01) between (datetime(2024-09-01) .. datetime(null))", false)]
    [TestCase("print datetime(2024-06-01) !between (datetime(2024-09-01) .. datetime(null))", true)]
    [TestCase("print datetime(2024-06-01) between (datetime(null) .. 10d)", null)]
    [TestCase("print datetime(2024-06-01) between (datetime(2024-06-01) .. timespan(null))", null)]
    [TestCase("print datetime(2024-06-01) !between (datetime(2024-06-01) .. timespan(null))", null)]
    [TestCase("print timespan(null) between (1m .. 1h)", null)]
    [TestCase("print timespan(null) !between (1m .. 1h)", null)]
    [Description("between is equivalent to (x >= left and x <= right) and !between to (x < left or x > right) under 3-valued logic")]
    public void BetweenAndNotBetween_ScalarTruthTable(string query, bool? expected)
    {
        AssertPrintBool(query, expected);
    }

    #endregion

    #region between / !between on nullable columns

    [Test]
    [Description("Both where Col between (a .. b) and where Col !between (a .. b) drop null rows")]
    public void Where_BetweenAndNotBetween_DropNullRows()
    {
        var table = CreateNullableTable2<int?, int?>(
            [(1, 0), (2, 1), (3, 5), (4, 10), (5, 11), (6, null)],
            tableName: "Numbers",
            columnName1: "Id",
            columnName2: "Val");
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where Val between (1 .. 10)"),
            Is.EquivalentTo(new int?[] { 2, 3, 4 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where Val !between (1 .. 10)"),
            Is.EquivalentTo(new int?[] { 1, 5 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Numbers | where Val !between (1 .. 10) or isnull(Val)"),
            Is.EquivalentTo(new int?[] { 1, 5, 6 }));
    }

    [Test]
    [Description("between and !between on nullable columns with nullable bound columns")]
    public void ExtendAndWhere_Between_WithNullableBoundColumns()
    {
        var table = new NullableTableBuilder("Ranges")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<int>("Val")
            .WithColumn<int>("Low")
            .WithColumn<int>("High")
            .WithRow(1, 5, 1, 10)      // in range -> between=true, !between=false
            .WithRow(2, 5, null, 10)   // null and true -> null; null or false -> null
            .WithRow(3, 5, null, 3)    // null and false -> false; null or true -> true
            .WithRow(4, 5, 10, null)   // false and null -> false; true or null -> true
            .WithRow(5, 5, 1, null)    // true and null -> null; false or null -> null
            .WithRow(6, null, 1, 10)   // null; null
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndGetNullable3Columns<int?, bool?, bool?>(
            "Id",
            "InRange",
            "OutRange",
            kustoDatabase,
            "Ranges | extend InRange = Val between (Low .. High), OutRange = Val !between (Low .. High) | project Id, InRange, OutRange");

        Assert.That(rows, Is.EquivalentTo(new List<(int?, bool?, bool?)>
        {
            (1, true, false),
            (2, null, null),
            (3, false, true),
            (4, false, true),
            (5, null, null),
            (6, null, null)
        }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Ranges | where Val !between (Low .. High)"),
            Is.EquivalentTo(new int?[] { 3, 4 }));
    }

    [Test]
    [Description("between and !between on nullable datetime (including datetime .. timespan), timespan, double, decimal, and long columns")]
    public void Where_BetweenAndNotBetween_DateTimeTimeSpanAndNumericNullableColumns()
    {
        var table = new NullableTableBuilder("Events")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<DateTime>("Ts")
            .WithColumn<TimeSpan>("Dur")
            .WithColumn<double>("Rate")
            .WithColumn<decimal>("Cost")
            .WithRow(1, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(10), 1.5, 10.0m)
            .WithRow(2, new DateTime(2024, 1, 3, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(45), 5.0, 25.5m)
            .WithRow(3, new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(2), 9.9, 100.0m)
            .WithRow(4, null, null, null, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        // datetime .. timespan shorthand (2024-01-01 to 2024-01-06 inclusive)
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Events | where Ts between (datetime(2024-01-01) .. 5d)"),
            Is.EquivalentTo(new int?[] { 1, 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Events | where Ts !between (datetime(2024-01-01) .. 5d)"),
            Is.EquivalentTo(new int?[] { 3 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Events | where Dur between (15m .. 1h)"),
            Is.EquivalentTo(new int?[] { 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Events | where Dur !between (15m .. 1h)"),
            Is.EquivalentTo(new int?[] { 1, 3 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Events | where Rate between (1.5 .. 5.0)"),
            Is.EquivalentTo(new int?[] { 1, 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Events | where Cost between (decimal(20.0) .. decimal(50.0))"),
            Is.EquivalentTo(new int?[] { 2 }));
    }

    #endregion
}
