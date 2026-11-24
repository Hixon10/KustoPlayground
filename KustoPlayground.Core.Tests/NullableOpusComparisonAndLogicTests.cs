using static KustoPlayground.Core.Tests.NullableOpusTestUtils;
using static KustoPlayground.Core.Tests.SummarizeOperatorOpusTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Null semantics of the comparison, logical and arithmetic operators, as in Azure Data Explorer
/// (https://learn.microsoft.com/en-us/kusto/query/scalar-data-types/null-values):
/// - comparing a null with a non-null value: == is false and != is true, while &lt;, &lt;=, &gt; and &gt;= are null;
/// - comparing two nulls (null == null, null != null, x == x for a null x) is null;
/// - the logic is three-valued: true and null = null, false and null = false, true or null = true,
///   false or null = null, not(null) = null;
/// - 'where' keeps only the rows whose predicate is true: a null predicate drops the row;
/// - arithmetic with a null operand is null.
/// The expected values were verified on the help cluster (https://help.kusto.windows.net).
/// </summary>
public class NullableOpusComparisonAndLogicTests
{
    // Pairs(Id: int, A: long?, B: long?) = 1: (5, 5), 2: (5, 7), 3: (5, null), 4: (null, 5), 5: (null, null)
    private static Table BuildPairsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Pairs")
            .WithColumn<int>("Id")
            .WithColumn<long>("A")
            .WithColumn<long>("B")
            .WithRow(1, 5L, 5L)
            .WithRow(2, 5L, 7L)
            .WithRow(3, 5L, null)
            .WithRow(4, null, 5L)
            .WithRow(5, null, null)
            .Build();
    }

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

    // BoolPairs(Id: int, P: bool?, Q: bool?) = every combination of true, false and null:
    // 1: (true, true), 2: (true, false), 3: (true, null), 4: (false, true), 5: (false, false), 6: (false, null),
    // 7: (null, true), 8: (null, false), 9: (null, null)
    private static Table BuildBoolPairsTable()
    {
        return new SummarizeOperatorOpusTableBuilder("BoolPairs")
            .WithColumn<int>("Id")
            .WithColumn<bool>("P")
            .WithColumn<bool>("Q")
            .WithRow(1, true, true)
            .WithRow(2, true, false)
            .WithRow(3, true, null)
            .WithRow(4, false, true)
            .WithRow(5, false, false)
            .WithRow(6, false, null)
            .WithRow(7, null, true)
            .WithRow(8, null, false)
            .WithRow(9, null, null)
            .Build();
    }

    // Typed(Id: int, V: T?) = 1: low, 2: high, 3: null
    private static KustoDatabase CreateTypedDatabase<T>(T low, T high)
        where T : notnull
    {
        return CreateDatabase(new SummarizeOperatorOpusTableBuilder("Typed")
            .WithColumn<int>("Id")
            .WithColumn<T>("V")
            .WithRow(1, low)
            .WithRow(2, high)
            .WithRow(3, null)
            .Build());
    }

    // Times(Id: int, D: datetime?, T: timespan?) = 1: (2020-01-01, 1h), 2: (null, 2h), 3: (2020-01-03, null), 4: (null, null)
    private static Table BuildTimesTable()
    {
        return new SummarizeOperatorOpusTableBuilder("Times")
            .WithColumn<int>("Id")
            .WithColumn<DateTime>("D")
            .WithColumn<TimeSpan>("T")
            .WithRow(1, Utc(2020, 1, 1), TimeSpan.FromHours(1))
            .WithRow(2, null, TimeSpan.FromHours(2))
            .WithRow(3, Utc(2020, 1, 3), null)
            .WithRow(4, null, null)
            .Build();
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0)
    {
        return new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc);
    }

    // On Typed(1: low, 2: high, 3: null): == and != compare a null V as "different" (false / true), in both operand orders
    private static void AssertEqualityWithNullValue(KustoDatabase kustoDatabase, string lowLiteral)
    {
        string query = $"Typed | extend Eq = V == {lowLiteral}, Ne = V != {lowLiteral}, " +
                       $"EqLeft = {lowLiteral} == V, NeLeft = {lowLiteral} != V";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Eq"),
            Is.EqualTo(ExpectedById<bool>(true, false, false)), "V == low");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Ne"),
            Is.EqualTo(ExpectedById<bool>(false, true, true)), "V != low");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "EqLeft"),
            Is.EqualTo(ExpectedById<bool>(true, false, false)), "low == V");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "NeLeft"),
            Is.EqualTo(ExpectedById<bool>(false, true, true)), "low != V");
        Assert.That(ExecuteAndGetIds(kustoDatabase, $"Typed | where V == {lowLiteral} | project Id"),
            Is.EquivalentTo(new[] { 1 }), "where V == low");
        Assert.That(ExecuteAndGetIds(kustoDatabase, $"Typed | where V != {lowLiteral} | project Id"),
            Is.EquivalentTo(new[] { 2, 3 }), "where V != low keeps the null row");
    }

    // On Typed(1: low, 2: high, 3: null): <, <=, > and >= are null for a null V, so 'where' drops it either way
    private static void AssertOrderingWithNullValue(KustoDatabase kustoDatabase, string lowLiteral)
    {
        string query = $"Typed | extend Gt = V > {lowLiteral}, Ge = V >= {lowLiteral}, " +
                       $"Lt = V < {lowLiteral}, Le = V <= {lowLiteral}";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Gt"),
            Is.EqualTo(ExpectedById<bool>(false, true, null)), "V > low");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Ge"),
            Is.EqualTo(ExpectedById<bool>(true, true, null)), "V >= low");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Lt"),
            Is.EqualTo(ExpectedById<bool>(false, false, null)), "V < low");
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Le"),
            Is.EqualTo(ExpectedById<bool>(true, false, null)), "V <= low");
        Assert.That(ExecuteAndGetIds(kustoDatabase, $"Typed | where V >= {lowLiteral} | project Id"),
            Is.EquivalentTo(new[] { 1, 2 }), "where V >= low");
        Assert.That(ExecuteAndGetIds(kustoDatabase, $"Typed | where not(V >= {lowLiteral}) | project Id"),
            Is.Empty, "where not(V >= low) drops the null row too");
    }

    #region two nullable columns

    [TestCase("A == B", true, false, false, false, null)]
    [TestCase("A != B", false, true, true, true, null)]
    [TestCase("A < B", false, true, null, null, null)]
    [TestCase("A <= B", true, true, null, null, null)]
    [TestCase("A > B", false, false, null, null, null)]
    [TestCase("A >= B", true, false, null, null, null)]
    [TestCase("A == A", true, true, true, null, null)]
    [TestCase("A != A", false, false, false, null, null)]
    [TestCase("not(A == B)", false, true, true, true, null)]
    [TestCase("not(A < B)", true, false, null, null, null)]
    [Description("Two nullable long columns, per row: one null operand gives false for ==, true for != and null " +
                 "for the ordering operators; two null operands give null (also for A == A)")]
    public void TwoNullableColumns_ComparisonPerRow(string expression,
        bool? id1, bool? id2, bool? id3, bool? id4, bool? id5)
    {
        var kustoDatabase = CreateDatabase(BuildPairsTable());

        var actual = ExecuteAndGetValuesById<bool>(kustoDatabase, $"Pairs | extend R = {expression}", "R");

        Assert.That(actual, Is.EqualTo(ExpectedById<bool>(id1, id2, id3, id4, id5)));
    }

    [TestCase("A == B", new[] { 1 })]
    [TestCase("A != B", new[] { 2, 3, 4 })]
    [TestCase("A < B", new[] { 2 })]
    [TestCase("A <= B", new[] { 1, 2 })]
    [TestCase("A > B", new int[] { })]
    [TestCase("A >= B", new[] { 1 })]
    [TestCase("A == A", new[] { 1, 2, 3 })]
    [TestCase("A != A", new int[] { })]
    [TestCase("not(A == B)", new[] { 2, 3, 4 })]
    [TestCase("not(A < B)", new[] { 1 })]
    [TestCase("A == B or A != B", new[] { 1, 2, 3, 4 })]
    [TestCase("isnull(A == B)", new[] { 5 })]
    [TestCase("isnull(A < B)", new[] { 3, 4, 5 })]
    [Description("Two nullable long columns in 'where': rows whose predicate is null are dropped, " +
                 "so (null, null) matches neither A == B nor A != B")]
    public void TwoNullableColumns_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildPairsTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Pairs | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    #endregion

    #region a nullable column compared with a constant

    [TestCase("X == 5", new[] { 3 })]
    [TestCase("X != 5", new[] { 1, 2, 4, 5 })]
    [TestCase("5 == X", new[] { 3 })]
    [TestCase("5 != X", new[] { 1, 2, 4, 5 })]
    [TestCase("X > 3", new[] { 3 })]
    [TestCase("X >= 3", new[] { 2, 3 })]
    [TestCase("X < 3", new[] { 1 })]
    [TestCase("X <= 3", new[] { 1, 2 })]
    [TestCase("3 > X", new[] { 1 })]
    [TestCase("X == long(null)", new int[] { })]
    [TestCase("X != long(null)", new[] { 1, 2, 3 })]
    [TestCase("long(null) == X", new int[] { })]
    [TestCase("long(null) != X", new[] { 1, 2, 3 })]
    [TestCase("X > long(null)", new int[] { })]
    [TestCase("X <= long(null)", new int[] { })]
    [TestCase("X == X", new[] { 1, 2, 3 })]
    [TestCase("X != X", new int[] { })]
    [TestCase("not(X == 5)", new[] { 1, 2, 4, 5 })]
    [TestCase("not(X != 5)", new[] { 3 })]
    [TestCase("not(X > 3)", new[] { 1, 2 })]
    [TestCase("not(X <= 3)", new[] { 3 })]
    [TestCase("isnull(X)", new[] { 4, 5 })]
    [TestCase("isnotnull(X)", new[] { 1, 2, 3 })]
    [TestCase("not(isnull(X))", new[] { 1, 2, 3 })]
    [TestCase("X > 3 or X == 1", new[] { 1, 3 })]
    [TestCase("X > 3 or isnull(X)", new[] { 3, 4, 5 })]
    [TestCase("isnull(X) or X > 3", new[] { 3, 4, 5 })]
    [TestCase("X < 3 and X != 5", new[] { 1 })]
    [TestCase("isnotnull(X) and X != 5", new[] { 1, 2 })]
    [TestCase("X > 3 and isnull(X)", new int[] { })]
    [TestCase("not(X > 3) or isnull(X)", new[] { 1, 2, 4, 5 })]
    [TestCase("not(X > 3 and X < 10)", new[] { 1, 2 })]
    [Description("A nullable long column compared with constants in 'where': X != 5 keeps the null rows, " +
                 "the ordering operators drop them, and so does not() of an ordering operator")]
    public void ColumnAndConstant_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Numbers | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [TestCase("X == 5", false, false, true, false, false)]
    [TestCase("X != 5", true, true, false, true, true)]
    [TestCase("X > 3", false, false, true, null, null)]
    [TestCase("X <= 3", true, true, false, null, null)]
    [TestCase("X == long(null)", false, false, false, null, null)]
    [TestCase("X != long(null)", true, true, true, null, null)]
    [TestCase("X < long(null)", null, null, null, null, null)]
    [TestCase("isnull(X)", false, false, false, true, true)]
    [TestCase("isnotnull(X)", true, true, true, false, false)]
    [Description("A nullable long column compared with constants, per row (extend)")]
    public void ColumnAndConstant_PerRow(string expression, bool? id1, bool? id2, bool? id3, bool? id4, bool? id5)
    {
        var kustoDatabase = CreateDatabase(BuildNumbersTable());

        var actual = ExecuteAndGetValuesById<bool>(kustoDatabase, $"Numbers | extend R = {expression}", "R");

        Assert.That(actual, Is.EqualTo(ExpectedById<bool>(id1, id2, id3, id4, id5)));
    }

    #endregion

    #region typed null literals

    [TestCase("int(null)", "int(5)")]
    [TestCase("long(null)", "long(5)")]
    [TestCase("real(null)", "1.5")]
    [TestCase("double(null)", "1.5")]
    [TestCase("decimal(null)", "decimal(1.5)")]
    [TestCase("bool(null)", "true")]
    [TestCase("datetime(null)", "datetime(2020-01-01)")]
    [TestCase("timespan(null)", "1h")]
    [TestCase("guid(null)", "guid(00000000-0000-0000-0000-000000000001)")]
    [Description("Typed null literals: null == value is false and null != value is true (in both operand orders), " +
                 "while null == null and null != null are null")]
    public void TypedNullLiteral_EqualityWithValueAndWithNull(string nullLiteral, string valueLiteral)
    {
        var kustoDatabase = CreateDatabase();

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            $"print Eq = {nullLiteral} == {valueLiteral}, Ne = {nullLiteral} != {valueLiteral}, " +
            $"EqLeft = {valueLiteral} == {nullLiteral}, NeLeft = {valueLiteral} != {nullLiteral}, " +
            $"EqNull = {nullLiteral} == {nullLiteral}, NeNull = {nullLiteral} != {nullLiteral}");

        Assert.That(GetValue<bool>(row, "Eq"), Is.False);
        Assert.That(GetValue<bool>(row, "Ne"), Is.True);
        Assert.That(GetValue<bool>(row, "EqLeft"), Is.False);
        Assert.That(GetValue<bool>(row, "NeLeft"), Is.True);
        AssertIsNull(row, "EqNull");
        AssertIsNull(row, "NeNull");
    }

    [TestCase("int(null)", "int(5)")]
    [TestCase("long(null)", "long(5)")]
    [TestCase("real(null)", "1.5")]
    [TestCase("decimal(null)", "decimal(1.5)")]
    [TestCase("datetime(null)", "datetime(2020-01-01)")]
    [TestCase("timespan(null)", "1h")]
    [Description("Typed null literals: <, <=, > and >= with a null operand are null")]
    public void TypedNullLiteral_OrderingIsNull(string nullLiteral, string valueLiteral)
    {
        var kustoDatabase = CreateDatabase();

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            $"print Lt = {nullLiteral} < {valueLiteral}, Le = {nullLiteral} <= {valueLiteral}, " +
            $"Gt = {valueLiteral} > {nullLiteral}, Ge = {valueLiteral} >= {nullLiteral}, " +
            $"GtNull = {nullLiteral} > {nullLiteral}");

        AssertIsNull(row, "Lt");
        AssertIsNull(row, "Le");
        AssertIsNull(row, "Gt");
        AssertIsNull(row, "Ge");
        AssertIsNull(row, "GtNull");
    }

    #endregion

    #region comparisons per column type

    [Test]
    [Description("int column with a null value: == / != are false / true, the ordering operators are null")]
    public void IntColumn_ComparisonsWithNullValue()
    {
        var kustoDatabase = CreateTypedDatabase(5, 7);

        AssertEqualityWithNullValue(kustoDatabase, "5");
        AssertOrderingWithNullValue(kustoDatabase, "5");
    }

    [Test]
    [Description("long column with a null value: == / != are false / true, the ordering operators are null")]
    public void LongColumn_ComparisonsWithNullValue()
    {
        var kustoDatabase = CreateTypedDatabase(5L, 7L);

        AssertEqualityWithNullValue(kustoDatabase, "5");
        AssertOrderingWithNullValue(kustoDatabase, "5");
    }

    [Test]
    [Description("real column with a null value: == / != are false / true, the ordering operators are null")]
    public void RealColumn_ComparisonsWithNullValue()
    {
        var kustoDatabase = CreateTypedDatabase(1.5, 2.5);

        AssertEqualityWithNullValue(kustoDatabase, "1.5");
        AssertOrderingWithNullValue(kustoDatabase, "1.5");
    }

    [Test]
    [Description("decimal column with a null value: == / != are false / true, the ordering operators are null")]
    public void DecimalColumn_ComparisonsWithNullValue()
    {
        var kustoDatabase = CreateTypedDatabase(1.5m, 2.5m);

        AssertEqualityWithNullValue(kustoDatabase, "decimal(1.5)");
        AssertOrderingWithNullValue(kustoDatabase, "decimal(1.5)");
    }

    [Test]
    [Description("datetime column with a null value: == / != are false / true, the ordering operators are null")]
    public void DatetimeColumn_ComparisonsWithNullValue()
    {
        var kustoDatabase = CreateTypedDatabase(Utc(2020, 1, 1), Utc(2021, 1, 1));

        AssertEqualityWithNullValue(kustoDatabase, "datetime(2020-01-01)");
        AssertOrderingWithNullValue(kustoDatabase, "datetime(2020-01-01)");
    }

    [Test]
    [Description("timespan column with a null value: == / != are false / true, the ordering operators are null")]
    public void TimespanColumn_ComparisonsWithNullValue()
    {
        var kustoDatabase = CreateTypedDatabase(TimeSpan.FromHours(1), TimeSpan.FromHours(2));

        AssertEqualityWithNullValue(kustoDatabase, "1h");
        AssertOrderingWithNullValue(kustoDatabase, "1h");
    }

    [Test]
    [Description("guid column with a null value: == is false and != is true")]
    public void GuidColumn_EqualityWithNullValue()
    {
        var kustoDatabase = CreateTypedDatabase(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"));

        AssertEqualityWithNullValue(kustoDatabase, "guid(00000000-0000-0000-0000-000000000001)");
    }

    [Test]
    [Description("bool column with a null value: V == false is false and V != false is true for the null row")]
    public void BoolColumn_EqualityWithNullValue()
    {
        var kustoDatabase = CreateTypedDatabase(false, true);

        AssertEqualityWithNullValue(kustoDatabase, "false");
    }

    [Test]
    [Description("Two nullable datetime columns: (d, d) is equal, (d, null) is not equal, (null, null) gives null " +
                 "for every comparison")]
    public void TwoNullableDatetimeColumns_ComparisonPerRow()
    {
        // Dates(Id: int, D1: datetime?, D2: datetime?) = 1: (2020-01-01, 2020-01-01), 2: (2020-01-01, null), 3: (null, null)
        var kustoDatabase = CreateDatabase(new SummarizeOperatorOpusTableBuilder("Dates")
            .WithColumn<int>("Id")
            .WithColumn<DateTime>("D1")
            .WithColumn<DateTime>("D2")
            .WithRow(1, Utc(2020, 1, 1), Utc(2020, 1, 1))
            .WithRow(2, Utc(2020, 1, 1), null)
            .WithRow(3, null, null)
            .Build());
        const string query = "Dates | extend Eq = D1 == D2, Ne = D1 != D2, Lt = D1 < D2";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Eq"),
            Is.EqualTo(ExpectedById<bool>(true, false, null)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Ne"),
            Is.EqualTo(ExpectedById<bool>(false, true, null)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Lt"),
            Is.EqualTo(ExpectedById<bool>(false, null, null)));
    }

    [Test]
    [Description("An int column compared with a long column: a null on one side gives false for == and true " +
                 "for !=, nulls on both sides give null")]
    public void IntAndLongColumns_ComparisonPerRow()
    {
        // Mixed(Id: int, I: int?, L: long?) = 1: (5, 5), 2: (5, null), 3: (null, 5), 4: (null, null)
        var kustoDatabase = CreateDatabase(new SummarizeOperatorOpusTableBuilder("Mixed")
            .WithColumn<int>("Id")
            .WithColumn<int>("I")
            .WithColumn<long>("L")
            .WithRow(1, 5, 5L)
            .WithRow(2, 5, null)
            .WithRow(3, null, 5L)
            .WithRow(4, null, null)
            .Build());
        const string query = "Mixed | extend Eq = I == L, Ne = I != L, Lt = I < L";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Eq"),
            Is.EqualTo(ExpectedById<bool>(true, false, false, null)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Ne"),
            Is.EqualTo(ExpectedById<bool>(false, true, true, null)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Lt"),
            Is.EqualTo(ExpectedById<bool>(false, null, null, null)));
    }

    [Test]
    [Description("NaN is not null: NaN == NaN is false, NaN != NaN is true and NaN > 0 is false, " +
                 "while the same comparisons of a null are null")]
    public void RealColumn_NaNIsNotNull()
    {
        // Reals(Id: int, R: real?) = 1: 1.5, 2: NaN, 3: null
        var kustoDatabase = CreateDatabase(new SummarizeOperatorOpusTableBuilder("Reals")
            .WithColumn<int>("Id")
            .WithColumn<double>("R")
            .WithRow(1, 1.5)
            .WithRow(2, double.NaN)
            .WithRow(3, null)
            .Build());
        const string query = "Reals | extend Eq = R == R, Ne = R != R, Gt = R > 0, RIsNull = isnull(R)";

        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Eq"),
            Is.EqualTo(ExpectedById<bool>(true, false, null)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Ne"),
            Is.EqualTo(ExpectedById<bool>(false, true, null)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "Gt"),
            Is.EqualTo(ExpectedById<bool>(true, false, null)));
        Assert.That(ExecuteAndGetValuesById<bool>(kustoDatabase, query, "RIsNull"),
            Is.EqualTo(ExpectedById<bool>(false, false, true)));
    }

    [Test]
    [Description("A null datetime is neither before nor after now(): both V < now() and not(V < now()) drop it")]
    public void DatetimeColumn_ComparedWithNow_DropsNullRows()
    {
        var kustoDatabase = CreateTypedDatabase(Utc(2020, 1, 1), Utc(2021, 1, 1));

        Assert.That(ExecuteAndGetIds(kustoDatabase, "Typed | where V < now() | project Id"),
            Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Typed | where not(V < now()) | project Id"), Is.Empty);
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Typed | where V > ago(1d) | project Id"), Is.Empty);
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Typed | where isnull(V) or V > ago(1d) | project Id"),
            Is.EquivalentTo(new[] { 3 }));
    }

    #endregion

    #region three-valued logic

    [TestCase("P and Q", true, false, null, false, false, false, null, false, null)]
    [TestCase("P or Q", true, true, true, true, false, null, true, null, null)]
    [TestCase("not(P)", false, false, false, true, true, true, null, null, null)]
    [TestCase("P == Q", true, false, false, false, true, false, false, false, null)]
    [TestCase("P != Q", false, true, true, true, false, true, true, true, null)]
    [TestCase("P == true", true, true, true, false, false, false, false, false, false)]
    [TestCase("P != true", false, false, false, true, true, true, true, true, true)]
    [TestCase("isnull(P)", false, false, false, false, false, false, true, true, true)]
    [Description("Three-valued logic per row over all 9 combinations of true, false and null: " +
                 "false and null = false, true or null = true, every other combination with a null is null")]
    public void ThreeValuedLogic_PerRow(string expression,
        bool? id1, bool? id2, bool? id3, bool? id4, bool? id5, bool? id6, bool? id7, bool? id8, bool? id9)
    {
        var kustoDatabase = CreateDatabase(BuildBoolPairsTable());

        var actual = ExecuteAndGetValuesById<bool>(kustoDatabase, $"BoolPairs | extend R = {expression}", "R");

        Assert.That(actual, Is.EqualTo(ExpectedById<bool>(id1, id2, id3, id4, id5, id6, id7, id8, id9)));
    }

    [TestCase("P", new[] { 1, 2, 3 })]
    [TestCase("not(P)", new[] { 4, 5, 6 })]
    [TestCase("P and Q", new[] { 1 })]
    [TestCase("P or Q", new[] { 1, 2, 3, 4, 7 })]
    [TestCase("not(P and Q)", new[] { 2, 4, 5, 6, 8 })]
    [TestCase("not(P) or not(Q)", new[] { 2, 4, 5, 6, 8 })]
    [TestCase("not(P or Q)", new[] { 5 })]
    [TestCase("P and not(Q)", new[] { 2 })]
    [TestCase("P == true", new[] { 1, 2, 3 })]
    [TestCase("P == false", new[] { 4, 5, 6 })]
    [TestCase("P != true", new[] { 4, 5, 6, 7, 8, 9 })]
    [TestCase("P != false", new[] { 1, 2, 3, 7, 8, 9 })]
    [TestCase("P == Q", new[] { 1, 5 })]
    [TestCase("P != Q", new[] { 2, 3, 4, 6, 7, 8 })]
    [TestCase("isnull(P)", new[] { 7, 8, 9 })]
    [TestCase("P or isnull(P)", new[] { 1, 2, 3, 7, 8, 9 })]
    [TestCase("isnull(P) and isnull(Q)", new[] { 9 })]
    [TestCase("isnull(P and Q)", new[] { 3, 7, 9 })]
    [TestCase("isnull(P or Q)", new[] { 6, 8, 9 })]
    [Description("Three-valued logic in 'where' over all 9 combinations of true, false and null: " +
                 "'where P' and 'where not(P)' both drop the null rows, while 'P != true' keeps them")]
    public void ThreeValuedLogic_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildBoolPairsTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"BoolPairs | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [Test]
    [Description("Three-valued logic with bool(null) literals, in both operand orders")]
    public void ThreeValuedLogic_NullLiterals()
    {
        var kustoDatabase = CreateDatabase();

        var row = ExecuteAndGetSingleRow(kustoDatabase,
            "print TrueAndNull = true and bool(null), NullAndTrue = bool(null) and true, " +
            "FalseAndNull = false and bool(null), NullAndFalse = bool(null) and false, " +
            "TrueOrNull = true or bool(null), NullOrTrue = bool(null) or true, " +
            "FalseOrNull = false or bool(null), NullOrFalse = bool(null) or false, " +
            "NullAndNull = bool(null) and bool(null), NullOrNull = bool(null) or bool(null), NotNull = not(bool(null))");

        AssertIsNull(row, "TrueAndNull");
        AssertIsNull(row, "NullAndTrue");
        Assert.That(GetValue<bool>(row, "FalseAndNull"), Is.False);
        Assert.That(GetValue<bool>(row, "NullAndFalse"), Is.False);
        Assert.That(GetValue<bool>(row, "TrueOrNull"), Is.True);
        Assert.That(GetValue<bool>(row, "NullOrTrue"), Is.True);
        AssertIsNull(row, "FalseOrNull");
        AssertIsNull(row, "NullOrFalse");
        AssertIsNull(row, "NullAndNull");
        AssertIsNull(row, "NullOrNull");
        AssertIsNull(row, "NotNull");
    }

    #endregion

    #region arithmetic

    [TestCase("A + B", 10.0, 12.0)]
    [TestCase("A - B", 0.0, -2.0)]
    [TestCase("A * B", 25.0, 35.0)]
    [TestCase("A * B / A", 5.0, 7.0)]
    [TestCase("A % B", 0.0, 5.0)]
    [Description("A binary arithmetic operator with a null operand gives null (rows 3, 4 and 5 of Pairs)")]
    public void Arithmetic_NullOperand_GivesNull(string expression, double id1, double id2)
    {
        var kustoDatabase = CreateDatabase(BuildPairsTable());

        var actual = ExecuteAndGetNumbersById(kustoDatabase, $"Pairs | extend R = {expression}", "R");

        Assert.That(actual, Is.EqualTo(ExpectedById<double>(id1, id2, null, null, null)));
    }

    [TestCase("-A", -5.0)]
    [TestCase("A + 1", 6.0)]
    [TestCase("1 + A", 6.0)]
    [TestCase("A * 0", 0.0)]
    [TestCase("A - A", 0.0)]
    [Description("Arithmetic on a null value is null, even when the result would not depend on it (null * 0, null - null)")]
    public void Arithmetic_NullValue_GivesNullEvenForAbsorbingOperations(string expression, double valueWhereAIs5)
    {
        var kustoDatabase = CreateDatabase(BuildPairsTable());

        var actual = ExecuteAndGetNumbersById(kustoDatabase, $"Pairs | extend R = {expression}", "R");

        Assert.That(actual,
            Is.EqualTo(ExpectedById<double>(valueWhereAIs5, valueWhereAIs5, valueWhereAIs5, null, null)));
    }

    [Test]
    [Description("Adding a typed null literal gives null for every row")]
    public void Arithmetic_WithNullLiteral_GivesNullForEveryRow()
    {
        var kustoDatabase = CreateDatabase(BuildPairsTable());

        var actual = ExecuteAndGetNumbersById(kustoDatabase, "Pairs | extend R = A + long(null)", "R");

        Assert.That(actual, Is.EqualTo(ExpectedById<double>(null, null, null, null, null)));
    }

    [TestCase("A + B > 10", new[] { 2 })]
    [TestCase("A + B == 10", new[] { 1 })]
    [TestCase("A + B != 10", new[] { 2, 3, 4, 5 })]
    [TestCase("not(A + B > 10)", new[] { 1 })]
    [TestCase("isnull(A + B)", new[] { 3, 4, 5 })]
    [TestCase("A * 0 == 0", new[] { 1, 2, 3 })]
    [Description("A null arithmetic result in 'where' compares like any other null: " +
                 "it is dropped by ==, > and not(>), but kept by !=")]
    public void Arithmetic_Where(string predicate, int[] expectedIds)
    {
        var kustoDatabase = CreateDatabase(BuildPairsTable());

        var actual = ExecuteAndGetIds(kustoDatabase, $"Pairs | where {predicate} | project Id");

        Assert.That(actual, Is.EquivalentTo(expectedIds));
    }

    [Test]
    [Description("datetime arithmetic with a null datetime or timespan operand gives null")]
    public void DatetimeArithmetic_NullOperand_GivesNull()
    {
        var kustoDatabase = CreateDatabase(BuildTimesTable());

        Assert.That(ExecuteAndGetValuesById<DateTime>(kustoDatabase, "Times | extend R = D + 1d", "R"),
            Is.EqualTo(ExpectedById<DateTime>(Utc(2020, 1, 2), null, Utc(2020, 1, 4), null)), "D + 1d");
        Assert.That(ExecuteAndGetValuesById<DateTime>(kustoDatabase, "Times | extend R = D + T", "R"),
            Is.EqualTo(ExpectedById<DateTime>(Utc(2020, 1, 1, hour: 1), null, null, null)), "D + T");
        Assert.That(ExecuteAndGetValuesById<DateTime>(kustoDatabase, "Times | extend R = D - T", "R"),
            Is.EqualTo(ExpectedById<DateTime>(new DateTime(2019, 12, 31, 23, 0, 0, DateTimeKind.Utc), null, null, null)),
            "D - T");
        Assert.That(
            ExecuteAndGetValuesById<TimeSpan>(kustoDatabase, "Times | extend R = D - datetime(2020-01-01)", "R"),
            Is.EqualTo(ExpectedById<TimeSpan>(TimeSpan.Zero, null, TimeSpan.FromDays(2), null)),
            "D - datetime(2020-01-01)");
    }

    [Test]
    [Description("timespan arithmetic with a null operand gives null")]
    public void TimespanArithmetic_NullOperand_GivesNull()
    {
        var kustoDatabase = CreateDatabase(BuildTimesTable());

        Assert.That(ExecuteAndGetValuesById<TimeSpan>(kustoDatabase, "Times | extend R = T * 2", "R"),
            Is.EqualTo(ExpectedById<TimeSpan>(TimeSpan.FromHours(2), TimeSpan.FromHours(4), null, null)), "T * 2");
        Assert.That(ExecuteAndGetValuesById<TimeSpan>(kustoDatabase, "Times | extend R = T + 30m", "R"),
            Is.EqualTo(ExpectedById<TimeSpan>(TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(150), null, null)),
            "T + 30m");
        Assert.That(ExecuteAndGetValuesById<TimeSpan>(kustoDatabase, "Times | extend R = -T", "R"),
            Is.EqualTo(ExpectedById<TimeSpan>(TimeSpan.FromHours(-1), TimeSpan.FromHours(-2), null, null)), "-T");
    }

    [Test]
    [Description("datetime arithmetic with nulls in 'where': D + T > ... drops every row where D or T is null")]
    public void DatetimeArithmetic_Where()
    {
        var kustoDatabase = CreateDatabase(BuildTimesTable());

        Assert.That(ExecuteAndGetIds(kustoDatabase, "Times | where D + T > datetime(2020-01-01) | project Id"),
            Is.EquivalentTo(new[] { 1 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Times | where isnull(D + T) | project Id"),
            Is.EquivalentTo(new[] { 2, 3, 4 }));
        Assert.That(ExecuteAndGetIds(kustoDatabase, "Times | where D + 1d >= datetime(2020-01-02) | project Id"),
            Is.EquivalentTo(new[] { 1, 3 }));
    }

    #endregion
}
