using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Azure Kusto null semantics for bin(), bin_at(), numeric arithmetic (+, -, *, /, %, unary +/-),
/// datetime/timespan arithmetic, and date/time scalar functions (todatetime, totimespan, make_timespan, ago, now).
/// References:
/// - https://learn.microsoft.com/en-us/kusto/query/bin-function
/// - https://learn.microsoft.com/en-us/kusto/query/bin-at-function
/// - https://learn.microsoft.com/en-us/kusto/query/numerical-operators
/// - https://learn.microsoft.com/en-us/kusto/query/datetime-timespan-arithmetic
/// </summary>
public class NullableBinArithmeticAndDateTimeNeonTests
{
    #region bin() with null values, null bin sizes, and non-positive bin sizes

    [TestCase("print bin(int(null), 10)")]
    [TestCase("print bin(long(null), 10)")]
    [TestCase("print bin(real(null), 2.5)")]
    [TestCase("print bin(decimal(null), decimal(10))")]
    [TestCase("print bin(datetime(null), 1h)")]
    [TestCase("print bin(timespan(null), 5m)")]
    [TestCase("print bin(25, int(null))")]
    [TestCase("print bin(25.5, real(null))")]
    [TestCase("print bin(datetime(2024-06-15 10:35:00), timespan(null))")]
    [TestCase("print bin(1h, timespan(null))")]
    [TestCase("print bin(int(null), int(null))")]
    [TestCase("print bin(datetime(null), timespan(null))")]
    [TestCase("print bin(25, 0)")]
    [TestCase("print bin(25, -10)")]
    [TestCase("print bin(25.5, -2.5)")]
    [TestCase("print bin(datetime(2024-06-15 10:35:00), 0s)")]
    [TestCase("print bin(datetime(2024-06-15 10:35:00), -1h)")]
    [TestCase("print bin(1h, 0s)")]
    [TestCase("print bin(1h, -5m)")]
    [Description("bin() returns null when the value is null, bin size is null, or bin size is zero/negative")]
    public void Bin_NullOrNonPositiveArguments_ReturnsNull(string query)
    {
        AssertPrintNull(query);
    }

    [Test]
    public void Bin_RealWithZeroSize_ReturnsNaN()
    {
        var rows = ExecuteAndAssertSuccess(CreateDatabase(), "print bin(25.5, 0.0)");
        Assert.That((double)rows.Single()["print_0"]!, Is.NaN);
    }

    [Test]
    [Description("bin() over nullable numeric, datetime, and timespan columns preserves nulls and floors valid values")]
    public void Bin_OverNullableColumns_PreservesNullsAndBinsNonNulls()
    {
        var table = new NullableTableBuilder("Telemetry")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<int>("Val")
            .WithColumn<double>("RealVal")
            .WithColumn<DateTime>("Ts")
            .WithColumn<TimeSpan>("Dur")
            .WithRow(1, 27, 4.7, new DateTime(2024, 6, 15, 10, 42, 30, DateTimeKind.Utc), TimeSpan.FromMinutes(23))
            .WithRow(2, -1, -0.2, new DateTime(2024, 6, 15, 0, 5, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(0))
            .WithRow(3, null, null, null, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Telemetry | extend BInt = bin(Val, 10), BReal = bin(RealVal, 2.0), BTs = bin(Ts, 1h), BDur = bin(Dur, 10m)");

        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        Assert.That(AsNullableLong(byId[1]["BInt"]), Is.EqualTo(20L));
        Assert.That(AsNullableDouble(byId[1]["BReal"]), Is.EqualTo(4.0).Within(1e-9));
        Assert.That(byId[1]["BTs"], Is.EqualTo(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc)));
        Assert.That(byId[1]["BDur"], Is.EqualTo(TimeSpan.FromMinutes(20)));

        Assert.That(AsNullableLong(byId[2]["BInt"]), Is.EqualTo(-10L));
        Assert.That(AsNullableDouble(byId[2]["BReal"]), Is.EqualTo(-2.0).Within(1e-9));
        Assert.That(byId[2]["BTs"], Is.EqualTo(new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(byId[2]["BDur"], Is.EqualTo(TimeSpan.Zero));

        Assert.That(byId[3]["BInt"], Is.Null);
        Assert.That(byId[3]["BReal"], Is.Null);
        Assert.That(byId[3]["BTs"], Is.Null);
        Assert.That(byId[3]["BDur"], Is.Null);
    }

    [Test]
    [Description("bin() with a dynamic nullable bin-size column returns null for rows with null or non-positive sizes")]
    public void Bin_WithNullableBinSizeColumn()
    {
        var table = new NullableTableBuilder("DynamicBins")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<int>("Val")
            .WithColumn<int>("Step")
            .WithRow(1, 27, 10)
            .WithRow(2, 27, null)
            .WithRow(3, 27, 0)
            .WithRow(4, 27, -5)
            .WithRow(5, null, 10)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(kustoDatabase, "DynamicBins | extend Bucket = bin(Val, Step)");
        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        Assert.That(AsNullableLong(byId[1]["Bucket"]), Is.EqualTo(20L));
        Assert.That(byId[2]["Bucket"], Is.Null);
        Assert.That(byId[3]["Bucket"], Is.Null);
        Assert.That(byId[4]["Bucket"], Is.Null);
        Assert.That(byId[5]["Bucket"], Is.Null);
    }

    #endregion

    #region bin_at() with null values, null sizes, null anchors, and non-positive sizes

    [TestCase("print bin_at(int(null), 10, 5)")]
    [TestCase("print bin_at(15, int(null), 5)")]
    [TestCase("print bin_at(15, 10, int(null))")]
    [TestCase("print bin_at(int(null), int(null), int(null))")]
    [TestCase("print bin_at(15, 0, 5)")]
    [TestCase("print bin_at(15, -10, 5)")]
    [TestCase("print bin_at(datetime(null), 1d, datetime(2024-01-01 12:00:00))")]
    [TestCase("print bin_at(datetime(2024-01-02 06:00:00), timespan(null), datetime(2024-01-01 12:00:00))")]
    [TestCase("print bin_at(datetime(2024-01-02 06:00:00), 1d, datetime(null))")]
    [TestCase("print bin_at(datetime(2024-01-02 06:00:00), 0s, datetime(2024-01-01 12:00:00))")]
    [TestCase("print bin_at(datetime(2024-01-02 06:00:00), -1d, datetime(2024-01-01 12:00:00))")]
    [TestCase("print bin_at(timespan(null), 1h, 15m)")]
    [TestCase("print bin_at(2h, timespan(null), 15m)")]
    [TestCase("print bin_at(2h, 1h, timespan(null))")]
    [Description("bin_at() returns null if value, bin size, or fixed point is null, or if bin size is non-positive")]
    public void BinAt_NullOrNonPositiveArguments_ReturnsNull(string query)
    {
        AssertPrintNull(query);
    }

    [Test]
    [Description("bin_at() over nullable numeric and datetime columns aligns non-nulls to anchor and preserves nulls")]
    public void BinAt_OverNullableColumns_AlignsToFixedPointAndPreservesNulls()
    {
        var table = new NullableTableBuilder("Aligned")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<int>("Val")
            .WithColumn<DateTime>("Ts")
            .WithRow(1, 12, new DateTime(2024, 1, 2, 6, 0, 0, DateTimeKind.Utc))
            .WithRow(2, 4, new DateTime(2024, 1, 2, 18, 0, 0, DateTimeKind.Utc))
            .WithRow(3, null, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Aligned | extend N = bin_at(Val, 10, 5), D = bin_at(Ts, 1d, datetime(2024-01-01 12:00:00))");
        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        Assert.That(AsNullableLong(byId[1]["N"]), Is.EqualTo(5L));
        Assert.That(byId[1]["D"], Is.EqualTo(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc)));

        Assert.That(AsNullableLong(byId[2]["N"]), Is.EqualTo(-5L));
        Assert.That(byId[2]["D"], Is.EqualTo(new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc)));

        Assert.That(byId[3]["N"], Is.Null);
        Assert.That(byId[3]["D"], Is.Null);
    }

    #endregion

    #region Numeric arithmetic (+, -, *, /, %, unary +/-) and division/modulo by zero

    [TestCase("print int(null) + 5")]
    [TestCase("print 5 + int(null)")]
    [TestCase("print int(null) + int(null)")]
    [TestCase("print int(null) - 5")]
    [TestCase("print 5 - int(null)")]
    [TestCase("print int(null) * 5")]
    [TestCase("print 0 * int(null)")]
    [TestCase("print int(null) / 5")]
    [TestCase("print 5 / int(null)")]
    [TestCase("print int(null) % 5")]
    [TestCase("print 5 % int(null)")]
    [TestCase("print long(null) + 10")]
    [TestCase("print real(null) * 2.5")]
    [TestCase("print decimal(null) - decimal(1.5)")]
    [TestCase("print -int(null)")]
    [TestCase("print +int(null)")]
    [TestCase("print -real(null)")]
    [TestCase("print -timespan(null)")]
    [TestCase("print +timespan(null)")]
    [TestCase("print 10 / 0")]
    [TestCase("print 0 / 0")]
    [TestCase("print 10 % 0")]
    [TestCase("print 10.5 % 0.0")]
    [Description("Arithmetic binary/unary operators are sticky with null, and integer division/modulo by zero yields null")]
    public void Arithmetic_NullOperandsAndDivisionByZero_ReturnNull(string query)
    {
        AssertPrintNull(query);
    }

    [Test]
    [Description("Arithmetic expressions across two nullable numeric columns propagate nulls row-by-row without affecting valid rows")]
    public void Arithmetic_AcrossTwoNullableColumns_PropagatesNullsPerRow()
    {
        var table = new NullableTableBuilder("Calc")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<int>("A")
            .WithColumn<int>("B")
            .WithRow(1, 20, 4)
            .WithRow(2, 20, 0)
            .WithRow(3, 20, null)
            .WithRow(4, null, 4)
            .WithRow(5, null, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Calc | extend Add = A + B, Sub = A - B, Mul = A * B, Div = A / B, Mod = A % B, NegA = -A");
        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        Assert.That(AsNullableDouble(byId[1]["Add"]), Is.EqualTo(24.0));
        Assert.That(AsNullableDouble(byId[1]["Sub"]), Is.EqualTo(16.0));
        Assert.That(AsNullableDouble(byId[1]["Mul"]), Is.EqualTo(80.0));
        Assert.That(AsNullableDouble(byId[1]["Div"]), Is.EqualTo(5.0));
        Assert.That(AsNullableDouble(byId[1]["Mod"]), Is.EqualTo(0.0));
        Assert.That(AsNullableDouble(byId[1]["NegA"]), Is.EqualTo(-20.0));

        Assert.That(AsNullableDouble(byId[2]["Add"]), Is.EqualTo(20.0));
        Assert.That(AsNullableDouble(byId[2]["Mul"]), Is.EqualTo(0.0));
        Assert.That(byId[2]["Div"], Is.Null);
        Assert.That(byId[2]["Mod"], Is.Null);

        foreach (int nullRowId in new[] { 3, 4, 5 })
        {
            Assert.That(byId[nullRowId]["Add"], Is.Null);
            Assert.That(byId[nullRowId]["Sub"], Is.Null);
            Assert.That(byId[nullRowId]["Mul"], Is.Null);
            Assert.That(byId[nullRowId]["Div"], Is.Null);
            Assert.That(byId[nullRowId]["Mod"], Is.Null);
        }

        Assert.That(AsNullableDouble(byId[3]["NegA"]), Is.EqualTo(-20.0));
        Assert.That(byId[4]["NegA"], Is.Null);
        Assert.That(byId[5]["NegA"], Is.Null);
    }

    #endregion

    #region DateTime and TimeSpan arithmetic

    [TestCase("print datetime(null) + 1h")]
    [TestCase("print datetime(2024-01-01) + timespan(null)")]
    [TestCase("print datetime(null) - 1h")]
    [TestCase("print datetime(2024-01-01) - timespan(null)")]
    [TestCase("print datetime(null) - datetime(2024-01-01)")]
    [TestCase("print datetime(2024-01-01) - datetime(null)")]
    [TestCase("print datetime(null) - datetime(null)")]
    [TestCase("print timespan(null) + 30m")]
    [TestCase("print 30m + timespan(null)")]
    [TestCase("print timespan(null) - 30m")]
    [TestCase("print 30m - timespan(null)")]
    [TestCase("print timespan(null) * 2")]
    [TestCase("print 2 * timespan(null)")]
    [TestCase("print 1h * int(null)")]
    [TestCase("print timespan(null) / 2")]
    [TestCase("print 1h / int(null)")]
    [TestCase("print 1h / 0")]
    [TestCase("print 1h / timespan(null)")]
    [TestCase("print timespan(null) / 10m")]
    [TestCase("print 1h / 0s")]
    [Description("DateTime and TimeSpan arithmetic propagates null and returns null on division by zero")]
    public void DateTimeAndTimeSpanArithmetic_NullAndZeroDivisor_ReturnsNull(string query)
    {
        AssertPrintNull(query);
    }

    [Test]
    [Description("DateTime and TimeSpan arithmetic across nullable columns computes non-null rows and leaves nulls as null")]
    public void DateTimeAndTimeSpanArithmetic_NullableColumns()
    {
        var t0 = new DateTime(2024, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var t1 = new DateTime(2024, 3, 1, 12, 30, 0, DateTimeKind.Utc);

        var table = new NullableTableBuilder("Jobs")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<DateTime>("Start")
            .WithColumn<DateTime>("End")
            .WithColumn<TimeSpan>("Buffer")
            .WithRow(1, t0, t1, TimeSpan.FromMinutes(30))
            .WithRow(2, t0, null, TimeSpan.FromMinutes(30))
            .WithRow(3, null, t1, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Jobs | extend Duration = End - Start, ExtendedEnd = End + Buffer, DoubleBuffer = Buffer * 2");
        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        Assert.That(byId[1]["Duration"], Is.EqualTo(TimeSpan.FromHours(2.5)));
        Assert.That(byId[1]["ExtendedEnd"], Is.EqualTo(new DateTime(2024, 3, 1, 13, 0, 0, DateTimeKind.Utc)));
        Assert.That(byId[1]["DoubleBuffer"], Is.EqualTo(TimeSpan.FromHours(1)));

        Assert.That(byId[2]["Duration"], Is.Null);
        Assert.That(byId[2]["ExtendedEnd"], Is.Null);
        Assert.That(byId[2]["DoubleBuffer"], Is.EqualTo(TimeSpan.FromHours(1)));

        Assert.That(byId[3]["Duration"], Is.Null);
        Assert.That(byId[3]["ExtendedEnd"], Is.Null);
        Assert.That(byId[3]["DoubleBuffer"], Is.Null);
    }

    #endregion

    #region Date/time scalar functions: todatetime, totimespan, make_timespan, ago, now

    [TestCase("print todatetime(datetime(null))")]
    [TestCase("print todatetime(\"\")")]
    [TestCase("print todatetime(\"not-a-date\")")]
    [TestCase("print totimespan(timespan(null))")]
    [TestCase("print totimespan(\"\")")]
    [TestCase("print totimespan(\"not-a-timespan\")")]
    [TestCase("print make_timespan(int(null), 30)")]
    [TestCase("print make_timespan(1, int(null))")]
    [TestCase("print make_timespan(int(null), 30, 15)")]
    [TestCase("print make_timespan(1, int(null), 15)")]
    [TestCase("print make_timespan(1, 30, int(null))")]
    [TestCase("print make_timespan(int(null), 1, 30, 15)")]
    [TestCase("print make_timespan(2, int(null), 30, 15)")]
    [TestCase("print make_timespan(2, 1, int(null), 15)")]
    [TestCase("print make_timespan(2, 1, 30, int(null))")]
    [TestCase("print ago(timespan(null))")]
    [TestCase("print now(timespan(null))")]
    [Description("Date/time functions return null on null or invalid inputs rather than throwing or defaulting null to 0/UtcNow")]
    public void DateTimeScalarFunctions_NullOrInvalidInputs_ReturnNull(string query)
    {
        AssertPrintNull(query);
    }

    [Test]
    [Description("todatetime() handles nullable string columns (valid/invalid/empty/null) and passes through nullable DateTime columns")]
    public void ToDateTime_NullableStringAndDateTimeColumns()
    {
        var dt = new DateTime(2024, 7, 4, 14, 30, 0, DateTimeKind.Utc);
        var table = new NullableTableBuilder("Dates")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<string>("RawText")
            .WithColumn<DateTime>("ExistingDt")
            .WithRow(1, "2024-07-04 14:30:00", dt)
            .WithRow(2, "", null)
            .WithRow(3, null, dt)
            .WithRow(4, "invalid-date", null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Dates | extend Parsed = todatetime(RawText), Passed = todatetime(ExistingDt)");
        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        Assert.That(byId[1]["Parsed"], Is.EqualTo(new DateTime(2024, 7, 4, 14, 30, 0)));
        Assert.That(byId[1]["Passed"], Is.EqualTo(dt));

        Assert.That(byId[2]["Parsed"], Is.Null);
        Assert.That(byId[2]["Passed"], Is.Null);

        Assert.That(byId[3]["Parsed"], Is.Null);
        Assert.That(byId[3]["Passed"], Is.EqualTo(dt));

        Assert.That(byId[4]["Parsed"], Is.Null);
        Assert.That(byId[4]["Passed"], Is.Null);
    }

    [Test]
    [Description("totimespan() handles nullable string columns (valid/invalid/empty/null) and passes through nullable TimeSpan columns")]
    public void ToTimeSpan_NullableStringAndTimeSpanColumns()
    {
        var ts = new TimeSpan(1, 20, 0);
        var table = new NullableTableBuilder("Spans")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<string>("RawText")
            .WithColumn<TimeSpan>("ExistingTs")
            .WithRow(1, "01:20:00", ts)
            .WithRow(2, "", null)
            .WithRow(3, null, ts)
            .WithRow(4, "bad-span", null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Spans | extend Parsed = totimespan(RawText), Passed = totimespan(ExistingTs)");
        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        Assert.That(byId[1]["Parsed"], Is.EqualTo(ts));
        Assert.That(byId[1]["Passed"], Is.EqualTo(ts));

        Assert.That(byId[2]["Parsed"], Is.Null);
        Assert.That(byId[2]["Passed"], Is.Null);

        Assert.That(byId[3]["Parsed"], Is.Null);
        Assert.That(byId[3]["Passed"], Is.EqualTo(ts));

        Assert.That(byId[4]["Parsed"], Is.Null);
        Assert.That(byId[4]["Passed"], Is.Null);
    }

    [Test]
    [Description("make_timespan() over nullable component columns produces null whenever any component is null")]
    public void MakeTimeSpan_NullableComponentColumns()
    {
        var table = new NullableTableBuilder("Parts")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<int>("D")
            .WithColumn<int>("H")
            .WithColumn<int>("M")
            .WithColumn<int>("S")
            .WithRow(1, 1, 2, 30, 45)
            .WithRow(2, null, 2, 30, 45)
            .WithRow(3, 1, null, 30, 45)
            .WithRow(4, 1, 2, null, 45)
            .WithRow(5, 1, 2, 30, null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Parts | extend Ts4 = make_timespan(D, H, M, S), Ts2 = make_timespan(H, M)");
        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        Assert.That(byId[1]["Ts4"], Is.EqualTo(new TimeSpan(1, 2, 30, 45)));
        Assert.That(byId[1]["Ts2"], Is.EqualTo(new TimeSpan(2, 30, 0)));

        Assert.That(byId[2]["Ts4"], Is.Null);
        Assert.That(byId[2]["Ts2"], Is.EqualTo(new TimeSpan(2, 30, 0)));

        Assert.That(byId[3]["Ts4"], Is.Null);
        Assert.That(byId[3]["Ts2"], Is.Null);

        Assert.That(byId[4]["Ts4"], Is.Null);
        Assert.That(byId[4]["Ts2"], Is.Null);

        Assert.That(byId[5]["Ts4"], Is.Null);
        Assert.That(byId[5]["Ts2"], Is.EqualTo(new TimeSpan(2, 30, 0)));
    }

    [Test]
    [Description("ago() and now() over a nullable TimeSpan offset column return null for null rows and valid DateTime for non-null rows")]
    public void AgoAndNow_NullableOffsetColumnAndWhereNullFilter()
    {
        var table = new NullableTableBuilder("Windows")
            .WithColumn<int>("Id", isNullable: false)
            .WithColumn<TimeSpan>("Offset")
            .WithColumn<DateTime>("EventTime")
            .WithRow(1, TimeSpan.FromHours(1), DateTime.UtcNow)
            .WithRow(2, null, DateTime.UtcNow)
            .WithRow(3, TimeSpan.FromHours(1), null)
            .Build();
        var kustoDatabase = CreateDatabase(table);

        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "Windows | extend AgoVal = ago(Offset), NowVal = now(Offset)");
        var byId = rows.ToDictionary(r => (int)r["Id"]!);

        DateTime utcNow = DateTime.UtcNow;
        TimeSpan tolerance = TimeSpan.FromMinutes(1);

        Assert.That(byId[1]["AgoVal"], Is.InstanceOf<DateTime>());
        Assert.That((DateTime)byId[1]["AgoVal"]!, Is.InRange(utcNow - TimeSpan.FromHours(1) - tolerance, utcNow - TimeSpan.FromHours(1) + tolerance));
        Assert.That(byId[1]["NowVal"], Is.InstanceOf<DateTime>());
        Assert.That((DateTime)byId[1]["NowVal"]!, Is.InRange(utcNow + TimeSpan.FromHours(1) - tolerance, utcNow + TimeSpan.FromHours(1) + tolerance));

        Assert.That(byId[2]["AgoVal"], Is.Null);
        Assert.That(byId[2]["NowVal"], Is.Null);

        // Comparing EventTime > ago(timespan(null)) drops all rows (comparison with null yields null)
        var filtered = ExecuteAndAssertSuccess(kustoDatabase, "Windows | where EventTime > ago(timespan(null))");
        Assert.That(filtered, Is.Empty);

        // Comparing EventTime > ago(Offset) keeps only row 1 (row 2 has null Offset, row 3 has null EventTime)
        var filteredByCol = ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "Windows | where EventTime > ago(Offset)");
        Assert.That(filteredByCol, Is.EquivalentTo(new int?[] { 1 }));
    }

    #endregion
}
