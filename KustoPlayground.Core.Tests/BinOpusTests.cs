namespace KustoPlayground.Core.Tests;

/// <summary>
/// bin() / floor() - https://learn.microsoft.com/en-us/kusto/query/bin-function
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class BinOpusTests
{
    [TestCase("bin(7, 3)", 6L)]
    [TestCase("bin(6, 3)", 6L)]
    [TestCase("bin(-7, 3)", -9L)]
    [TestCase("bin(-6, 3)", -6L)]
    [TestCase("bin(0, 5)", 0L)]
    [TestCase("bin(10, 4)", 8L)]
    [TestCase("bin(9223372036854775807, 2)", 9223372036854775806L)]
    [TestCase("bin(toint(7), toint(3))", 6L)]
    [TestCase("bin(toint(7), 3)", 6L)]
    [TestCase("bin(toint(5000), 10000)", 0L)]
    [TestCase("bin(bin(17, 5), 10)", 10L)]
    [TestCase("floor(7, 3)", 6L)]
    [Description("bin - int/long value is rounded down (towards negative infinity) to a multiple of the bin size, result is long")]
    public void Bin_IntegerValue_RoundsDownAndReturnsLong(string expression, long expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("bin(4.5, 1)", 4.0)]
    [TestCase("bin(4.5, 1.0)", 4.0)]
    [TestCase("bin(-4.5, 1)", -5.0)]
    [TestCase("bin(-0.5, 1)", -1.0)]
    [TestCase("bin(7.5, 2)", 6.0)]
    [TestCase("bin(7, 2.5)", 5.0)]
    [TestCase("bin(4, 0.5)", 4.0)]
    [TestCase("bin(2.5, 0.1)", 2.5)]
    [TestCase("floor(-4.5, 1)", -5.0)]
    [Description("bin - real value (or real bin size) is rounded down to a multiple of the bin size, result is real")]
    public void Bin_RealValue_RoundsDownAndReturnsReal(string expression, double expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [Test]
    [Description("bin - decimal value is rounded down and keeps the decimal type")]
    public void Bin_DecimalValue_ReturnsDecimal()
    {
        var actual = OpusTestUtils.EvaluateScalar("bin(decimal(7.5), 2)");

        OpusTestUtils.AssertTypedValue(actual, 6m);
    }

    [TestCase("bin(datetime(1970-05-11 13:45:07), 1d)", "1970-05-11 00:00:00")]
    [TestCase("bin(datetime(1969-12-31 23:00:00), 1d)", "1969-12-31 00:00:00")]
    [TestCase("bin(datetime(2025-08-23), 1d)", "2025-08-23 00:00:00")]
    [TestCase("bin(datetime(2025-08-23 06:20:45), 1h)", "2025-08-23 06:00:00")]
    [TestCase("bin(datetime(2025-08-23 06:20:45), 15m)", "2025-08-23 06:15:00")]
    [TestCase("bin(datetime(2025-08-23 06:20:45.678), 1s)", "2025-08-23 06:20:45")]
    [TestCase("bin(datetime(2025-08-23 10:00:00), 1.5h)", "2025-08-23 09:00:00")]
    [TestCase("bin(datetime(2025-08-23 10:00:00), 7d)", "2025-08-18 00:00:00")]
    [TestCase("bin(datetime(2025-08-23), 30d)", "2025-08-08 00:00:00")]
    [TestCase("floor(datetime(2025-08-23 06:20:45), 1h)", "2025-08-23 06:00:00")]
    [Description("bin - datetime is rounded down to a multiple of the timespan bin size counted from 0001-01-01 (so 7d bins start on Monday)")]
    public void Bin_DateTimeValue_RoundsDownToDateTime(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, OpusTestUtils.ParseDateTime(expected));
    }

    [TestCase("bin(16d, 7d)", "14.00:00:00")]
    [TestCase("bin(time(16d), 7d)", "14.00:00:00")]
    [TestCase("bin(90m, 1h)", "01:00:00")]
    [TestCase("bin(1.5h, 1h)", "01:00:00")]
    [TestCase("bin(-1h, 1d)", "-1.00:00:00")]
    [TestCase("bin(-90m, 1h)", "-02:00:00")]
    [TestCase("bin(100ms, 30ms)", "00:00:00.090")]
    [Description("bin - timespan is rounded down (towards negative infinity) to a multiple of the bin size")]
    public void Bin_TimeSpanValue_RoundsDownToTimeSpan(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, OpusTestUtils.ParseTimeSpan(expected));
    }

    [TestCase("bin(10, 0)")]
    [TestCase("bin(1h, 0s)")]
    [TestCase("bin(datetime(2025-08-23 06:20:45), 0d)")]
    [TestCase("bin(datetime(2025-08-23 06:20:45), -1d)")]
    [Description("bin - zero bin size (or negative bin size for datetime) returns null instead of failing")]
    public void Bin_ZeroBinSize_ReturnsNull(string expression)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        Assert.That(actual, Is.Null);
    }

    [TestCase("bin(7, 0.0)")]
    [TestCase("bin(7.0, 0)")]
    [Description("bin - real bin with zero size returns NaN (not null)")]
    public void Bin_RealWithZeroBinSize_ReturnsNaN(string expression)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        Assert.That(actual, Is.TypeOf<double>());
        Assert.That((double)actual!, Is.NaN);
    }

    [TestCase("bin(long(null), 5)", "System.Int64", "long")]
    [TestCase("bin(5, long(null))", "System.Int64", "long")]
    [TestCase("bin(real(null), 1.0)", "System.Double", "real")]
    [TestCase("bin(datetime(null), 1d)", "System.DateTime", "datetime")]
    [TestCase("bin(timespan(null), 1h)", "System.TimeSpan", "timespan")]
    [Description("bin - null value or null bin size returns null of the result type")]
    public void Bin_NullArgument_ReturnsTypedNull(string expression, string expectedDataType, string expectedColumnType)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);
        Assert.That(actual, Is.Null);

        var schema = OpusTestUtils.ExecuteGetSchema(new KustoDatabase(), $"print r = {expression} | getschema");
        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("r", 0, expectedDataType, expectedColumnType)
        }));
    }

    [TestCase("bin(7, 3)", "System.Int64", "long")]
    [TestCase("bin(toint(7), toint(3))", "System.Int64", "long")]
    [TestCase("bin(toint(7), 3)", "System.Int64", "long")]
    [TestCase("bin(4.5, 1)", "System.Double", "real")]
    [TestCase("bin(7, 2.5)", "System.Double", "real")]
    [TestCase("bin(decimal(7.5), 2)", "System.Data.SqlTypes.SqlDecimal", "decimal")]
    [TestCase("bin(datetime(2025-08-23 06:20:45), 1h)", "System.DateTime", "datetime")]
    [TestCase("bin(1.5h, 1h)", "System.TimeSpan", "timespan")]
    [Description("bin - result type follows the value type (int is promoted to long, real bin size makes real)")]
    public void Bin_ResultType_FollowsValueType(string expression, string expectedDataType, string expectedColumnType)
    {
        var schema = OpusTestUtils.ExecuteGetSchema(new KustoDatabase(), $"print r = {expression} | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("r", 0, expectedDataType, expectedColumnType)
        }));
    }

    [Test]
    [Description("bin - tostring of a binned datetime uses the ISO 8601 format with 7 fractional digits")]
    public void Bin_ToStringOfBinnedDateTime_IsIso8601()
    {
        var actual = OpusTestUtils.EvaluateScalar("tostring(bin(datetime(2025-08-23 06:20:45), 1h))");

        OpusTestUtils.AssertTypedValue(actual, "2025-08-23T06:00:00.0000000Z");
    }

    [Test]
    [Description("bin - result can be concatenated with strcat")]
    public void Bin_InsideStrcat_FormatsLong()
    {
        var actual = OpusTestUtils.EvaluateScalar("strcat('bucket-', bin(17, 5))");

        OpusTestUtils.AssertTypedValue(actual, "bucket-15");
    }

    [Test]
    [Description("bin - bin size can come from another column, so it is evaluated per row")]
    public void Bin_BinSizeFromColumn_EvaluatedPerRow()
    {
        var table = TestUtils.GenerateTableWith2Columns<long, long>(
            [(7L, 3L), (7L, 2L), (10L, 4L)],
            "Value",
            "BinSize",
            "Numbers");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<long, long, long>(
            "Value",
            "BinSize",
            "Binned",
            kustoDatabase,
            "Numbers | extend Binned = bin(Value, BinSize)");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long, long)>
        {
            (7, 3, 6),
            (7, 2, 6),
            (10, 4, 8)
        }));
    }

    [Test]
    [Description("bin - int column is binned to long buckets and distinct buckets are returned")]
    public void Bin_IntColumnWithDistinct_ReturnsLongBuckets()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Bucket",
            kustoDatabase,
            "StormEvents | project Bucket = bin(DamageProperty, 10000) | distinct Bucket");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 0, 20000 }));
    }

    [Test]
    [Description("bin - datetime column is binned by day")]
    public void Bin_DateTimeColumnByDay_ReturnsStartOfDay()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, DateTime>(
            "EventType",
            "Day",
            kustoDatabase,
            "StormEvents | project EventType, Day = bin(StartTime, 1d)");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, DateTime)>
        {
            ("Hurricane", new DateTime(2025, 8, 23)),
            ("Flood", new DateTime(2023, 3, 28)),
            ("Tornado", new DateTime(2024, 6, 1))
        }));
    }

    [Test]
    [Description("bin - 7d bins are aligned to Monday (0001-01-01 is a Monday)")]
    public void Bin_DateTimeColumnByWeek_StartsOnMonday()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, DateTime>(
            "EventType",
            "Week",
            kustoDatabase,
            "StormEvents | extend Week = bin(StartTime, 7d) | project EventType, Week");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, DateTime)>
        {
            ("Hurricane", new DateTime(2025, 8, 18)),
            ("Flood", new DateTime(2023, 3, 27)),
            ("Tornado", new DateTime(2024, 5, 27))
        }));
    }

    [Test]
    [Description("bin - can be used in where to select an hourly bucket")]
    public void Bin_InWhereByHour_FiltersRows()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "EventType",
            kustoDatabase,
            "StormEvents | where bin(StartTime, 1h) == datetime(2024-06-01 16:00:00) | project EventType");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "Tornado" }));
    }

    [Test]
    [Description("bin - comparison of a daily bucket with a datetime inside where followed by count")]
    public void Bin_InWhereWithCount_CountsRows()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Count",
            kustoDatabase,
            "StormEvents | where bin(StartTime, 1d) >= datetime(2024-01-01) | count");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 2 }));
    }

    [Test]
    [Description("bin - binned datetime column can be sorted")]
    public void Bin_SortByBinnedDateTime_ReturnsOrderedBuckets()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(
            "Day",
            kustoDatabase,
            "StormEvents | project Day = bin(StartTime, 1d) | sort by Day asc");

        Assert.That(actualData, Is.EqualTo(new List<DateTime>
        {
            new(2023, 3, 28),
            new(2024, 6, 1),
            new(2025, 8, 23)
        }));
    }

    [Test]
    [Description("bin - result types of binned table columns are reported by getschema")]
    public void Bin_TableColumns_GetSchemaReportsResultTypes()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var schema = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            "StormEvents | project Day = bin(StartTime, 1d), Bucket = bin(DamageProperty, 1000) | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Day", 0, "System.DateTime", "datetime"),
            ("Bucket", 1, "System.Int64", "long")
        }));
    }

    [Test]
    [Description("bin - timespan column is binned by hour")]
    public void Bin_TimeSpanColumnByHour_RoundsDown()
    {
        var table = TestUtils.GenerateTableWithColumn(
            [TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(25), TimeSpan.FromHours(3)],
            "Duration",
            "Sessions");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<TimeSpan>(
            "Bucket",
            kustoDatabase,
            "Sessions | project Bucket = bin(Duration, 1h)");

        Assert.That(actualData, Is.EquivalentTo(new List<TimeSpan>
        {
            TimeSpan.FromHours(1),
            TimeSpan.Zero,
            TimeSpan.FromHours(3)
        }));
    }

    [Test]
    [Description("bin - null values in a nullable long column stay null, others are rounded down")]
    public void Bin_NullableLongColumn_NullStaysNull()
    {
        var table = OpusTestUtils.NewTable("Measurements")
            .WithColumn<long>("Value")
            .WithRow(12L)
            .WithRow(-3L)
            .WithRow((object?)null)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = OpusTestUtils.ExecuteAndGetValues(
            "Bucket",
            kustoDatabase,
            "Measurements | project Bucket = bin(Value, 5)");

        Assert.That(actualData, Is.EquivalentTo(new List<object?> { 10L, -5L, null }));
        Assert.That(actualData.Where(v => v is not null), Is.All.TypeOf<long>());
    }

    [Test]
    [Description("bin - null values in a nullable real column stay null, others are rounded down")]
    public void Bin_NullableRealColumn_NullStaysNull()
    {
        var table = OpusTestUtils.NewTable("Measurements")
            .WithColumn<double>("Value")
            .WithRow(4.5)
            .WithRow(-4.5)
            .WithRow((object?)null)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = OpusTestUtils.ExecuteAndGetValues(
            "Bucket",
            kustoDatabase,
            "Measurements | project Bucket = bin(Value, 1.0)");

        Assert.That(actualData, Is.EquivalentTo(new List<object?> { 4.0, -5.0, null }));
        Assert.That(actualData.Where(v => v is not null), Is.All.TypeOf<double>());
    }

    [Test]
    [Description("bin - can be applied to the result of an arithmetic expression")]
    public void Bin_OfArithmeticExpression_RoundsDown()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = OpusTestUtils.ExecuteAndGetValues(
            "Bucket",
            kustoDatabase,
            "StormEvents | project Bucket = bin(DamageProperty * 2, 10000) | distinct Bucket");

        Assert.That(actualData, Is.EquivalentTo(new List<object?> { 40000L, 10000L }));
    }

    [TestCase("print r = bin('a', 1)")]
    [TestCase("print r = bin(1)")]
    [TestCase("StormEvents | extend Bucket = bin(State, 1)")]
    [Description("bin - non-numeric value or missing bin size is a semantic error")]
    public void Bin_InvalidArguments_Fails(string query)
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        OpusTestUtils.AssertQueryFails(kustoDatabase, query);
    }
}
