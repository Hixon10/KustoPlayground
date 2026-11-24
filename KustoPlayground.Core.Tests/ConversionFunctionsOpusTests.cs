namespace KustoPlayground.Core.Tests;

/// <summary>
/// toint()    - https://learn.microsoft.com/en-us/kusto/query/toint-function
/// tolong()   - https://learn.microsoft.com/en-us/kusto/query/tolong-function
/// toreal()   - https://learn.microsoft.com/en-us/kusto/query/toreal-function
/// tostring() - https://learn.microsoft.com/en-us/kusto/query/tostring-function
/// tobool()   - https://learn.microsoft.com/en-us/kusto/query/tobool-function
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class ConversionFunctionsOpusTests
{
    [TestCase("toint('123')", 123)]
    [TestCase("toint(' 123 ')", 123)]
    [TestCase("toint('+5')", 5)]
    [TestCase("toint('-5')", -5)]
    [TestCase("toint('007')", 7)]
    [TestCase("toint('0x1F')", 31)]
    [TestCase("toint('0X1f')", 31)]
    [TestCase("toint('-2147483648')", int.MinValue)]
    [TestCase("toint(2.7)", 2)]
    [TestCase("toint(-2.7)", -2)]
    [TestCase("toint(decimal(3.9))", 3)]
    [TestCase("toint(true)", 1)]
    [TestCase("toint(false)", 0)]
    [TestCase("toint(tolong(42))", 42)]
    [Description("toint - parses decimal/hex strings (whitespace allowed), truncates reals towards zero, converts bools")]
    public void ToInt_ValidInput_ReturnsInt(string expression, int expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("toint(2147483648)", int.MinValue)]
    [TestCase("toint(4294967297)", 1)]
    [Description("toint - long values outside of the int range are wrapped (not null)")]
    public void ToInt_LongOutOfRange_Wraps(string expression, int expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("toint('12.7')")]
    [TestCase("toint('abc')")]
    [TestCase("toint('')")]
    [TestCase("toint('1e3')")]
    [TestCase("toint(' 1 2 ')")]
    [TestCase("toint('1,000')")]
    [TestCase("toint('-0x10')")]
    [TestCase("toint(' 0x1F ')")]
    [TestCase("toint('2147483648')")]
    [TestCase("toint('-2147483649')")]
    [TestCase("toint('NaN')")]
    [TestCase("toint(real(nan))")]
    [TestCase("toint(int(null))")]
    [Description("toint - invalid strings, out of range strings, NaN and null return null")]
    public void ToInt_InvalidInput_ReturnsNull(string expression)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        Assert.That(actual, Is.Null);
    }

    [TestCase("tolong('123')", 123L)]
    [TestCase("tolong(' 42 ')", 42L)]
    [TestCase("tolong('  -0 ')", 0L)]
    [TestCase("tolong('0xFF')", 255L)]
    [TestCase("tolong('9223372036854775807')", long.MaxValue)]
    [TestCase("tolong('-9223372036854775808')", long.MinValue)]
    [TestCase("tolong(12.9)", 12L)]
    [TestCase("tolong(-12.9)", -12L)]
    [TestCase("tolong(1e19)", long.MinValue)]
    [TestCase("tolong(decimal(5.5))", 5L)]
    [TestCase("tolong(true)", 1L)]
    [TestCase("tolong(false)", 0L)]
    [TestCase("tolong(toint(7))", 7L)]
    [Description("tolong - parses decimal/hex strings, truncates reals towards zero, converts bools and ints")]
    public void ToLong_ValidInput_ReturnsLong(string expression, long expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("tolong(datetime(1970-01-01))", 621355968000000000L)]
    [TestCase("tolong(datetime(2025-08-23))", 638915040000000000L)]
    [TestCase("tolong(1s)", 10000000L)]
    [TestCase("tolong(-1d)", -864000000000L)]
    [Description("tolong - datetime and timespan are converted to ticks (100ns units, datetime ticks start at 0001-01-01)")]
    public void ToLong_DateTimeAndTimeSpan_ReturnsTicks(string expression, long expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("tolong('abc')")]
    [TestCase("tolong('4.5')")]
    [TestCase("tolong('')")]
    [TestCase("tolong('1e3')")]
    [TestCase("tolong('5L')")]
    [TestCase("tolong('0x')")]
    [TestCase("tolong('9223372036854775808')")]
    [TestCase("tolong(real(+inf))")]
    [TestCase("tolong(long(null))")]
    [Description("tolong - invalid or out of range strings, infinity and null return null")]
    public void ToLong_InvalidInput_ReturnsNull(string expression)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        Assert.That(actual, Is.Null);
    }

    [TestCase("toreal('123.4')", 123.4)]
    [TestCase("toreal('1e3')", 1000.0)]
    [TestCase("toreal('1E-2')", 0.01)]
    [TestCase("toreal('  3.5 ')", 3.5)]
    [TestCase("toreal('.5')", 0.5)]
    [TestCase("toreal('5.')", 5.0)]
    [TestCase("toreal('+7.5')", 7.5)]
    [TestCase("toreal('-0.25')", -0.25)]
    [TestCase("toreal('1,5')", 15.0)]
    [TestCase("toreal('1,000.5')", 1000.5)]
    [TestCase("toreal(5)", 5.0)]
    [TestCase("toreal(toint(3))", 3.0)]
    [TestCase("toreal(true)", 1.0)]
    [TestCase("toreal(decimal(1.25))", 1.25)]
    [TestCase("toreal(1h)", 36000000000.0)]
    [TestCase("toreal(datetime(1970-01-01))", 621355968000000000.0)]
    [TestCase("todouble('2.5')", 2.5)]
    [Description("toreal/todouble - parses numbers (exponent, sign, leading/trailing dot, ',' is a thousands separator), converts other types")]
    public void ToReal_ValidInput_ReturnsReal(string expression, double expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("toreal('inf')", double.PositiveInfinity)]
    [TestCase("toreal('Infinity')", double.PositiveInfinity)]
    [TestCase("toreal('1e400')", double.PositiveInfinity)]
    [TestCase("toreal('-Infinity')", double.NegativeInfinity)]
    [TestCase("toreal('-1e400')", double.NegativeInfinity)]
    [Description("toreal - infinity strings and overflowing numbers are converted to infinity")]
    public void ToReal_InfinityAndOverflow_ReturnsInfinity(string expression, double expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [Test]
    [Description("toreal - 'NaN' string is converted to NaN")]
    public void ToReal_NaNString_ReturnsNaN()
    {
        var actual = OpusTestUtils.EvaluateScalar("toreal('NaN')");

        Assert.That(actual, Is.TypeOf<double>());
        Assert.That((double)actual!, Is.NaN);
    }

    [TestCase("toreal('abc')")]
    [TestCase("toreal('')")]
    [TestCase("toreal('0x10')")]
    [TestCase("toreal(real(null))")]
    [TestCase("todouble('abc')")]
    [Description("toreal/todouble - invalid strings (including hex) and null return null")]
    public void ToReal_InvalidInput_ReturnsNull(string expression)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        Assert.That(actual, Is.Null);
    }

    [TestCase("tostring(123)", "123")]
    [TestCase("tostring(toint(-7))", "-7")]
    [TestCase("tostring(9223372036854775807)", "9223372036854775807")]
    [TestCase("tostring(true)", "True")]
    [TestCase("tostring(false)", "False")]
    [TestCase("tostring('abc')", "abc")]
    [TestCase("tostring(decimal(10.10))", "10.1")]
    [Description("tostring - ints, longs, bools (capitalized), strings and decimals")]
    public void ToString_ScalarValues_ReturnsString(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("tostring(1.5)", "1.5")]
    [TestCase("tostring(1.0)", "1.0")]
    [TestCase("tostring(100.0)", "100.0")]
    [TestCase("tostring(-0.0)", "-0.0")]
    [TestCase("tostring(1 / 3.0)", "0.3333333333333333")]
    [TestCase("tostring(2.0 / 4)", "0.5")]
    [TestCase("tostring(123456789.125)", "123456789.125")]
    [TestCase("tostring(1e15)", "1000000000000000.0")]
    [TestCase("tostring(1e20)", "100000000000000000000.0")]
    [TestCase("tostring(1e21)", "1e21")]
    [TestCase("tostring(1e-4)", "0.0001")]
    [TestCase("tostring(1e-5)", "0.00001")]
    [TestCase("tostring(1.5e-7)", "1.5e-7")]
    [TestCase("tostring(real(nan))", "NaN")]
    [TestCase("tostring(real(+inf))", "inf")]
    [TestCase("tostring(real(-inf))", "-inf")]
    [Description("tostring - reals keep '.0' for whole numbers, use the shortest round-trip form and switch to exponent from 1e21 / below 1e-6")]
    public void ToString_RealValues_UsesKustoRealFormat(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("tostring(datetime(2020-01-02 03:04:05))", "2020-01-02T03:04:05.0000000Z")]
    [TestCase("tostring(datetime(2020-01-02 03:04:05.123))", "2020-01-02T03:04:05.1230000Z")]
    [TestCase("tostring(1h)", "01:00:00")]
    [TestCase("tostring(90m)", "01:30:00")]
    [TestCase("tostring(1d)", "1.00:00:00")]
    [TestCase("tostring(-1h)", "-01:00:00")]
    [TestCase("tostring(1ms)", "00:00:00.0010000")]
    [TestCase("tostring(time(1.02:03:04))", "1.02:03:04")]
    [TestCase("tostring(guid(74BE27DE-1E4E-49D9-B579-FE0B331D3642))", "74be27de-1e4e-49d9-b579-fe0b331d3642")]
    [Description("tostring - datetime is ISO 8601 with 7 fractional digits, timespan is [-][d.]hh:mm:ss[.fffffff], guid is lower case")]
    public void ToString_DateTimeTimeSpanGuid_ReturnsString(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("tostring(int(null))")]
    [TestCase("tostring(long(null))")]
    [TestCase("tostring(real(null))")]
    [TestCase("tostring(bool(null))")]
    [TestCase("tostring(datetime(null))")]
    [TestCase("tostring(timespan(null))")]
    [TestCase("tostring(guid(null))")]
    [Description("tostring - null of any type is converted to an empty string (never null)")]
    public void ToString_NullValues_ReturnsEmptyString(string expression)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, "");
    }

    [TestCase("tobool('true')")]
    [TestCase("tobool('TRUE')")]
    [TestCase("tobool('True')")]
    [TestCase("tobool(' true ')")]
    [TestCase("tobool('1')")]
    [TestCase("tobool(' 1 ')")]
    [TestCase("tobool('2')")]
    [TestCase("tobool('-1')")]
    [TestCase("tobool(1)")]
    [TestCase("tobool(123)")]
    [TestCase("tobool(-1)")]
    [TestCase("tobool(0.5)")]
    [TestCase("toboolean('true')")]
    [Description("tobool/toboolean - 'true' (any case) and any non-zero number (or integer string) is true")]
    public void ToBool_TruthyInput_ReturnsTrue(string expression)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, true);
    }

    [TestCase("tobool('false')")]
    [TestCase("tobool('FaLsE')")]
    [TestCase("tobool('0')")]
    [TestCase("tobool(0)")]
    [TestCase("tobool(0.0)")]
    [TestCase("toboolean('false')")]
    [Description("tobool/toboolean - 'false' (any case) and zero is false")]
    public void ToBool_FalsyInput_ReturnsFalse(string expression)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, false);
    }

    [TestCase("tobool('yes')")]
    [TestCase("tobool('t')")]
    [TestCase("tobool('abc')")]
    [TestCase("tobool('')")]
    [TestCase("tobool('0.0')")]
    [TestCase("tobool('1.5')")]
    [TestCase("tobool(bool(null))")]
    [Description("tobool - other strings (including real numbers as strings) and null return null")]
    public void ToBool_InvalidInput_ReturnsNull(string expression)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        Assert.That(actual, Is.Null);
    }

    [TestCase("toint('1')", "System.Int32", "int")]
    [TestCase("tolong('1')", "System.Int64", "long")]
    [TestCase("toreal('1')", "System.Double", "real")]
    [TestCase("todouble('1')", "System.Double", "real")]
    [TestCase("tostring(1)", "System.String", "string")]
    [TestCase("tobool('true')", "System.SByte", "bool")]
    [TestCase("toboolean('true')", "System.SByte", "bool")]
    [TestCase("toint('abc')", "System.Int32", "int")]
    [Description("conversion functions - result type is fixed (even when the result is null)")]
    public void Conversion_ResultType_IsReportedByGetSchema(string expression, string expectedDataType, string expectedColumnType)
    {
        var schema = OpusTestUtils.ExecuteGetSchema(new KustoDatabase(), $"print r = {expression} | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("r", 0, expectedDataType, expectedColumnType)
        }));
    }

    [TestCase("tostring(toint(toreal('2.7')))", "2")]
    [TestCase("tostring(tolong('0xFF'))", "255")]
    [TestCase("tostring(toreal('1e3'))", "1000.0")]
    [TestCase("tostring(tobool('TRUE'))", "True")]
    [TestCase("tostring(toint('abc'))", "")]
    [Description("conversion functions - chained conversions to string")]
    public void Conversion_ChainedToString_ReturnsString(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [Test]
    [Description("conversion functions - chained conversions between numbers and bools")]
    public void Conversion_ChainedNumbersAndBools_ReturnsTypedValues()
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("tolong(tostring(tolong('0xFF')))"), 255L);
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("toint(tobool('true'))"), 1);
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("tobool(tostring(true))"), true);
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("toreal(tostring(1.5))"), 1.5);
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar("toint(toreal('-2.7'))"), -2);
    }

    [Test]
    [Description("conversion functions - string column is converted to int, long and real per row")]
    public void Conversion_StringColumn_ConvertedPerRow()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["42", " 7 ", "0x10", "abc", ""], "Raw", "RawValues");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "RawValues | project Raw, AsInt = toint(Raw), AsLong = tolong(Raw), AsReal = toreal(Raw)");

        var actualData = rows.Select(row => (row["Raw"], row["AsInt"], row["AsLong"], row["AsReal"])).ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(object?, object?, object?, object?)>
        {
            ("42", 42, 42L, 42.0),
            (" 7 ", 7, 7L, 7.0),
            ("0x10", 16, 16L, null),
            ("abc", null, null, null),
            ("", null, null, null)
        }));
        Assert.That(rows.Select(row => row["AsInt"]).Where(v => v is not null), Is.All.TypeOf<int>());
        Assert.That(rows.Select(row => row["AsLong"]).Where(v => v is not null), Is.All.TypeOf<long>());
        Assert.That(rows.Select(row => row["AsReal"]).Where(v => v is not null), Is.All.TypeOf<double>());
    }

    [Test]
    [Description("tobool - string column is converted per row, unknown strings become null")]
    public void ToBool_StringColumn_ConvertedPerRow()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["true", "False", "1", "0", "yes"], "Raw", "Flags");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(kustoDatabase, "Flags | project Raw, Flag = tobool(Raw)");

        var actualData = rows.Select(row => (row["Raw"], row["Flag"])).ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(object?, object?)>
        {
            ("true", true),
            ("False", false),
            ("1", true),
            ("0", false),
            ("yes", null)
        }));
    }

    [Test]
    [Description("tobool - converted string column can be used as a where predicate")]
    public void ToBool_StringColumnInWhere_FiltersRows()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["true", "False", "1", "0", "yes"], "Raw", "Flags");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Raw",
            kustoDatabase,
            "Flags | where tobool(Raw) == true");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "true", "1" }));
    }

    [Test]
    [Description("tostring - int column converted to string can be compared in where")]
    public void ToString_IntColumnInWhere_FiltersRows()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Count",
            kustoDatabase,
            "StormEvents | where tostring(DamageProperty) == '5000' | count");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 2 }));
    }

    [Test]
    [Description("toint - round trip int -> string -> int inside where")]
    public void ToInt_RoundTripInWhere_FiltersRows()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "EventType",
            kustoDatabase,
            "StormEvents | where toint(tostring(DamageProperty)) > 10000 | project EventType");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "Hurricane" }));
    }

    [Test]
    [Description("tolong/toreal - int column is converted to long and real")]
    public void Conversion_IntColumn_ConvertedToLongAndReal()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, long, double>(
            "EventType",
            "AsLong",
            "AsReal",
            kustoDatabase,
            "StormEvents | project EventType, AsLong = tolong(DamageProperty), AsReal = toreal(DamageProperty)");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, double)>
        {
            ("Hurricane", 20000, 20000.0),
            ("Flood", 5000, 5000.0),
            ("Tornado", 5000, 5000.0)
        }));
    }

    [Test]
    [Description("tolong - datetime column is converted to ticks")]
    public void ToLong_DateTimeColumn_ReturnsTicks()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, long>(
            "EventType",
            "Ticks",
            kustoDatabase,
            "StormEvents | project EventType, Ticks = tolong(StartTime)");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long)>
        {
            ("Hurricane", new DateTime(2025, 8, 23, 6, 20, 0).Ticks),
            ("Flood", new DateTime(2023, 3, 28, 10, 30, 0).Ticks),
            ("Tornado", new DateTime(2024, 6, 1, 16, 50, 30).Ticks)
        }));
    }

    [Test]
    [Description("tostring - datetime column is converted to ISO 8601 strings")]
    public void ToString_DateTimeColumn_ReturnsIso8601()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "EventType",
            "Text",
            kustoDatabase,
            "StormEvents | project EventType, Text = tostring(StartTime)");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string)>
        {
            ("Hurricane", "2025-08-23T06:20:00.0000000Z"),
            ("Flood", "2023-03-28T10:30:00.0000000Z"),
            ("Tornado", "2024-06-01T16:50:30.0000000Z")
        }));
    }

    [Test]
    [Description("conversion functions - null values of a nullable column")]
    public void Conversion_NullableLongColumn_NullHandling()
    {
        var table = OpusTestUtils.NewTable("Scores")
            .WithColumn<string>("Name")
            .WithColumn<long>("Score")
            .WithRow("a", 5L)
            .WithRow("b", null)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            "Scores | project Name, AsString = tostring(Score), AsInt = toint(Score), AsReal = toreal(Score), AsBool = tobool(Score)");

        var actualData = rows.Select(row => (row["Name"], row["AsString"], row["AsInt"], row["AsReal"], row["AsBool"])).ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(object?, object?, object?, object?, object?)>
        {
            ("a", "5", 5, 5.0, true),
            ("b", "", null, null, null)
        }));
        Assert.That(rows.Select(row => row["AsInt"]).Where(v => v is not null), Is.All.TypeOf<int>());
        Assert.That(rows.Select(row => row["AsReal"]).Where(v => v is not null), Is.All.TypeOf<double>());
        Assert.That(rows.Select(row => row["AsBool"]).Where(v => v is not null), Is.All.TypeOf<bool>());
    }

    [Test]
    [Description("conversion functions - real column with fractions is truncated by toint/tolong and formatted by tostring")]
    public void Conversion_RealColumn_TruncatedAndFormatted()
    {
        var table = TestUtils.GenerateTableWithColumn<double>([2.7, -2.7, 1.0], "Value", "Reals");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<int, long, string>(
            "AsInt",
            "AsLong",
            "AsString",
            kustoDatabase,
            "Reals | project AsInt = toint(Value), AsLong = tolong(Value), AsString = tostring(Value)");

        Assert.That(actualData, Is.EquivalentTo(new List<(int, long, string)>
        {
            (2, 2, "2.7"),
            (-2, -2, "-2.7"),
            (1, 1, "1.0")
        }));
    }
}
