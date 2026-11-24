namespace KustoPlayground.Core.Tests;

public class NeonToIntToLongToRealToStringToBoolTests
{
    #region toint() Tests

    [Test]
    [Description("toint() - converts valid numeric strings including boundaries and negatives")]
    public void ToInt_ValidStrings_ReturnsInt()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "0"),
                (2, "123"),
                (3, "-456"),
                (4, "2147483647"),
                (5, "-2147483648")
            },
            columnName1: "Id",
            columnName2: "Raw",
            tableName: "IntStrings");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<int, int>(
            "Id",
            "Parsed",
            db,
            "IntStrings | extend Parsed = toint(Raw) | sort by Id asc");

        Assert.That(actual, Is.EqualTo(new List<(int, int)>
        {
            (1, 0),
            (2, 123),
            (3, -456),
            (4, int.MaxValue),
            (5, int.MinValue)
        }));
    }

    [Test]
    [Description("toint() - truncates positive and negative real numbers toward zero")]
    public void ToInt_RealValues_TruncatesTowardZero()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(2.3)"), Is.EqualTo(2));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(2.9)"), Is.EqualTo(2));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(-2.3)"), Is.EqualTo(-2));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(-2.9)"), Is.EqualTo(-2));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(0.9)"), Is.EqualTo(0));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(-0.9)"), Is.EqualTo(0));
    }

    [Test]
    [Description("toint() - converts int, in-range long, and bool to int")]
    public void ToInt_FromIntLongAndBool()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(int(42))"), Is.EqualTo(42));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(long(1000))"), Is.EqualTo(1000));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(true)"), Is.EqualTo(1));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(false)"), Is.EqualTo(0));
    }

    [Test]
    [Description("toint() - invalid strings and special reals return null; narrowing a long wraps")]
    public void ToInt_InvalidTextAndNullReturnNull_LongOverflowWraps()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint('abc')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint('')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint('123abc')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint('2147483648')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint('-2147483649')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint(long(3000000000))"), Is.EqualTo(-1294967296));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint(long(-3000000000))"), Is.EqualTo(1294967296));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint(int(null))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint(real(null))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint(real(nan))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint(real(+inf))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<int>(db, "print toint(real(-inf))"), Is.Null);
    }

    [Test]
    [Description("toint() - mixed valid, invalid, and null rows in table column")]
    public void ToInt_MixedTableColumn()
    {
        var db = new KustoDatabase();
        var table = NeonTestUtils.GenerateTableWith2ColumnsNullableString2(
            new List<(int, string?)>
            {
                (1, "42"),
                (2, "bad"),
                (3, null),
                (4, "-7"),
                (5, "999999999999")
            },
            columnName1: "Id",
            columnName2: "Raw",
            tableName: "Mixed");
        db.AddTable(table);

        var actual = NeonTestUtils.ExecuteAndGet2ColumnsNullable2<int, int>(
            "Id",
            "Num",
            db,
            "Mixed | extend Num = toint(Raw) | sort by Id asc");

        Assert.That(actual, Is.EqualTo(new List<(int, int?)>
        {
            (1, 42),
            (2, null),
            (3, null),
            (4, -7),
            (5, null)
        }));
    }

    #endregion

    #region tolong() Tests

    [Test]
    [Description("tolong() - converts strings including 64-bit values beyond int32 and min/max boundaries")]
    public void ToLong_ValidStrings_ReturnsLong()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "0"),
                (2, "123"),
                (3, "-999"),
                (4, "3000000000"),
                (5, "9223372036854775807"),
                (6, "-9223372036854775808")
            },
            columnName1: "Id",
            columnName2: "Raw",
            tableName: "LongStrings");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<int, long>(
            "Id",
            "Parsed",
            db,
            "LongStrings | extend Parsed = tolong(Raw) | sort by Id asc");

        Assert.That(actual, Is.EqualTo(new List<(int, long)>
        {
            (1, 0L),
            (2, 123L),
            (3, -999L),
            (4, 3_000_000_000L),
            (5, long.MaxValue),
            (6, long.MinValue)
        }));
    }

    [Test]
    [Description("tolong() - converts int, long, real (truncating), and bool to long")]
    public void ToLong_FromNumericAndBool()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<long>(db, "print tolong(int(42))"), Is.EqualTo(42L));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<long>(db, "print tolong(long(5000000000))"), Is.EqualTo(5_000_000_000L));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<long>(db, "print tolong(3.9)"), Is.EqualTo(3L));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<long>(db, "print tolong(-3.9)"), Is.EqualTo(-3L));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<long>(db, "print tolong(true)"), Is.EqualTo(1L));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<long>(db, "print tolong(false)"), Is.EqualTo(0L));
    }

    [Test]
    [Description("tolong() - invalid strings, overflow beyond int64, special reals, and nulls return null")]
    public void ToLong_InvalidOverflowAndNull_ReturnsNull()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<long>(db, "print tolong('abc')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<long>(db, "print tolong('')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<long>(db, "print tolong('9223372036854775808')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<long>(db, "print tolong('-9223372036854775809')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<long>(db, "print tolong(long(null))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<long>(db, "print tolong(int(null))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<long>(db, "print tolong(real(nan))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<long>(db, "print tolong(real(+inf))"), Is.Null);
    }

    #endregion

    #region toreal() and todouble() Tests

    [Test]
    [Description("toreal() - converts decimal, negative, integer, and scientific-notation strings")]
    public void ToReal_ValidStrings_ReturnsDouble()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "123.4"),
                (2, "-0.5"),
                (3, "0"),
                (4, "42"),
                (5, "1e5"),
                (6, "1.5e-2"),
                (7, "-3.25E+2")
            },
            columnName1: "Id",
            columnName2: "Raw",
            tableName: "RealStrings");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<int, double>(
            "Id",
            "Parsed",
            db,
            "RealStrings | extend Parsed = toreal(Raw) | sort by Id asc");

        double[] expected = [123.4, -0.5, 0.0, 42.0, 100000.0, 0.015, -325.0];
        Assert.That(actual, Has.Count.EqualTo(expected.Length));
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual[i].Item2, Is.EqualTo(expected[i]).Within(1e-9));
        }
    }

    [Test]
    [Description("toreal() - converts int, long, real, and bool to double")]
    public void ToReal_FromNumericAndBool()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<double>(db, "print toreal(int(7))"), Is.EqualTo(7.0));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<double>(db, "print toreal(long(5000000000))"), Is.EqualTo(5_000_000_000.0));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<double>(db, "print toreal(3.14)"), Is.EqualTo(3.14).Within(1e-9));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<double>(db, "print toreal(true)"), Is.EqualTo(1.0));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<double>(db, "print toreal(false)"), Is.EqualTo(0.0));
    }

    [Test]
    [Description("toreal() - invalid strings and nulls return null")]
    public void ToReal_InvalidAndNull_ReturnsNull()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<double>(db, "print toreal('abc')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<double>(db, "print toreal('')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<double>(db, "print toreal('12.34.56')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<double>(db, "print toreal(real(null))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<double>(db, "print toreal(int(null))"), Is.Null);
    }

    [Test]
    [Description("todouble() - alias is equivalent to toreal()")]
    public void ToDouble_AliasEquivalentToToReal()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<double>(db, "print todouble('123.4')"), Is.EqualTo(123.4).Within(1e-9));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<double>(db, "print todouble(42)"), Is.EqualTo(42.0));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<double>(db, "print todouble('invalid')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<double>(db, "print todouble(real(null))"), Is.Null);
    }

    #endregion

    #region tostring() Tests

    [Test]
    [Description("tostring() - converts int, long, real, and string values to string")]
    public void ToString_NumericAndStringValues()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(int(123))"), Is.EqualTo("123"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(int(-42))"), Is.EqualTo("-42"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(long(3000000000))"), Is.EqualTo("3000000000"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(12.5)"), Is.EqualTo("12.5"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring('already a string')"), Is.EqualTo("already a string"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring('')"), Is.EqualTo(""));
    }

    [Test]
    [Description("tostring() - null values of any type convert to empty string (never null)")]
    public void ToString_NullValues_ReturnsEmptyString()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(int(null))"), Is.EqualTo(""));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(long(null))"), Is.EqualTo(""));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(real(null))"), Is.EqualTo(""));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(bool(null))"), Is.EqualTo(""));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(datetime(null))"), Is.EqualTo(""));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(timespan(null))"), Is.EqualTo(""));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(guid(null))"), Is.EqualTo(""));
    }

    [Test]
    [Description("tostring() - converts bool, datetime, timespan, and guid to non-empty string representations")]
    public void ToString_BoolDateTimeTimespanAndGuid()
    {
        var db = new KustoDatabase();

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(true)"),
            Is.EqualTo("true").IgnoreCase);
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(false)"),
            Is.EqualTo("false").IgnoreCase);

        var dtStr = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "print tostring(datetime(2024-05-11 13:45:07))");
        Assert.That(dtStr, Does.Contain("2024"));
        Assert.That(dtStr, Does.Contain("13:45:07"));

        var tsStr = NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print tostring(time(01:02:03))");
        Assert.That(tsStr, Does.Contain("01:02:03"));

        var guidStr = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "print tostring(guid(74be27de-1e4e-49d9-b579-fe0b331d3642))");
        Assert.That(guidStr, Is.EqualTo("74be27de-1e4e-49d9-b579-fe0b331d3642").IgnoreCase);
    }

    [Test]
    [Description("tostring() - nullable column converts null rows to empty string")]
    public void ToString_NullableColumn_NullsBecomeEmptyString()
    {
        var db = new KustoDatabase();
        var table = NeonTestUtils.GenerateTableWith2ColumnsNullable2<int, int>(
            new List<(int, int?)>
            {
                (1, 10),
                (2, null),
                (3, -5)
            },
            columnName1: "Id",
            columnName2: "Val",
            tableName: "NullableNums");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            "Id",
            "Str",
            db,
            "NullableNums | extend Str = tostring(Val) | sort by Id asc");

        Assert.That(actual, Is.EqualTo(new List<(int, string)>
        {
            (1, "10"),
            (2, ""),
            (3, "-5")
        }));
    }

    #endregion

    #region tobool() and toboolean() Tests

    [Test]
    [Description("tobool() - official doc examples and case-insensitive boolean strings")]
    public void ToBool_Strings_CaseInsensitive()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "true"),
                (2, "false"),
                (3, "TRUE"),
                (4, "FALSE"),
                (5, "True"),
                (6, "False"),
                (7, "1"),
                (8, "0")
            },
            columnName1: "Id",
            columnName2: "Raw",
            tableName: "BoolStrings");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<int, bool>(
            "Id",
            "Flag",
            db,
            "BoolStrings | extend Flag = tobool(Raw) | sort by Id asc");

        Assert.That(actual, Is.EqualTo(new List<(int, bool)>
        {
            (1, true),
            (2, false),
            (3, true),
            (4, false),
            (5, true),
            (6, false),
            (7, true),
            (8, false)
        }));
    }

    [Test]
    [Description("tobool() - numeric and bool inputs: zero is false, non-zero is true")]
    public void ToBool_NumericAndBoolInputs()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(1)"), Is.True);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(123)"), Is.True);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(-1)"), Is.True);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(0)"), Is.False);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(0.0)"), Is.False);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(2.5)"), Is.True);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(true)"), Is.True);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(false)"), Is.False);
    }

    [Test]
    [Description("tobool() - invalid strings and nulls return null")]
    public void ToBool_InvalidAndNull_ReturnsNull()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<bool>(db, "print tobool('yes')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<bool>(db, "print tobool('no')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<bool>(db, "print tobool('abc')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<bool>(db, "print tobool('')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<bool>(db, "print tobool(bool(null))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<bool>(db, "print tobool(int(null))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<bool>(db, "print tobool(real(null))"), Is.Null);
    }

    [Test]
    [Description("toboolean() - alias is equivalent to tobool()")]
    public void ToBoolean_AliasEquivalentToToBool()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print toboolean('true')"), Is.True);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print toboolean('false')"), Is.False);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print toboolean(123)"), Is.True);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print toboolean(0)"), Is.False);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<bool>(db, "print toboolean('maybe')"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleNullableValue<bool>(db, "print toboolean(bool(null))"), Is.Null);
    }

    #endregion

    #region Roundtrip, Pipeline, and Error Tests

    [Test]
    [Description("Conversion roundtrips between tostring() and toint()/tolong()/toreal()/tobool()")]
    public void Conversions_RoundtripsAndWhereFiltering()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<int>(db, "print toint(tostring(42))"), Is.EqualTo(42));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<long>(db, "print tolong(tostring(3000000000))"), Is.EqualTo(3_000_000_000L));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<double>(db, "print toreal(tostring(3.14))"), Is.EqualTo(3.14).Within(1e-9));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(tostring(true))"), Is.True);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<bool>(db, "print tobool(tostring(false))"), Is.False);

        var table = TestUtils.GenerateTableWith3Columns(
            new List<(string, string, string)>
            {
                ("svc-a", "85", "true"),
                ("svc-b", "40", "true"),
                ("svc-c", "95", "false"),
                ("svc-d", "invalid", "true")
            },
            columnName1: "Service",
            columnName2: "ScoreStr",
            columnName3: "EnabledStr",
            tableName: "Services");
        db.AddTable(table);

        var matched = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Service",
            db,
            "Services | where toint(ScoreStr) >= 80 and tobool(EnabledStr) == true | project Service");

        Assert.That(matched, Is.EquivalentTo(new List<string> { "svc-a" }));
    }

    [Test]
    [Description("Conversion functions - wrong argument counts return execution errors")]
    public void Conversions_InvalidArgumentCount_ReturnsError()
    {
        var db = new KustoDatabase();

        NeonTestUtils.ExecuteAndExpectError(db, "print toint()");
        NeonTestUtils.ExecuteAndExpectError(db, "print toint(1, 2)");
        NeonTestUtils.ExecuteAndExpectError(db, "print tolong()");
        NeonTestUtils.ExecuteAndExpectError(db, "print tolong(1, 2)");
        NeonTestUtils.ExecuteAndExpectError(db, "print toreal()");
        NeonTestUtils.ExecuteAndExpectError(db, "print toreal(1, 2)");
        NeonTestUtils.ExecuteAndExpectError(db, "print tostring()");
        NeonTestUtils.ExecuteAndExpectError(db, "print tostring(1, 2)");
        NeonTestUtils.ExecuteAndExpectError(db, "print tobool()");
        NeonTestUtils.ExecuteAndExpectError(db, "print tobool(true, false)");
    }

    #endregion
}
