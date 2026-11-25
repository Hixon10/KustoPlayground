namespace KustoPlayground.Core.Tests;

public class NeonBinFunctionTests
{
    [Test]
    [Description("bin() - official doc example rounding real value down to integer bin size")]
    public void Bin_RealValueWithIntegerBinSize_RoundsDown()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(4.5, 1)");

        NeonTestUtils.AssertNumericEqual(actual, 4.0);
    }

    [Test]
    [Description("bin() - real values with fractional bin sizes")]
    public void Bin_RealValuesWithFractionalBinSize_RoundsDownToNearestMultiple()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, double)>
            {
                (1, 0.0),
                (2, 0.49),
                (3, 0.5),
                (4, 4.5),
                (5, 4.99),
                (6, 6.5),
                (7, 7.49)
            },
            columnName1: "Id",
            columnName2: "Val",
            tableName: "Metrics");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "Metrics | extend B05 = bin(Val, 0.5), B25 = bin(Val, 2.5) | sort by Id asc");

        Assert.That(rows, Has.Count.EqualTo(7));
        double[] expectedB05 = [0.0, 0.0, 0.5, 4.5, 4.5, 6.5, 7.0];
        double[] expectedB25 = [0.0, 0.0, 0.0, 2.5, 2.5, 5.0, 5.0];

        for (var i = 0; i < rows.Count; i++)
        {
            NeonTestUtils.AssertNumericEqual(rows[i]["B05"], expectedB05[i]);
            NeonTestUtils.AssertNumericEqual(rows[i]["B25"], expectedB25[i]);
        }
    }

    [Test]
    [Description("bin() - negative real values round down toward negative infinity")]
    public void Bin_NegativeRealValues_RoundsTowardNegativeInfinity()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, double)>
            {
                (1, -0.1),
                (2, -1.0),
                (3, -4.5),
                (4, -4.0),
                (5, -6.5)
            },
            columnName1: "Id",
            columnName2: "Val",
            tableName: "NegReals");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "NegReals | extend B1 = bin(Val, 1.0), B25 = bin(Val, 2.5) | sort by Id asc");

        double[] expectedB1 = [-1.0, -1.0, -5.0, -4.0, -7.0];
        double[] expectedB25 = [-2.5, -2.5, -5.0, -5.0, -7.5];

        for (var i = 0; i < rows.Count; i++)
        {
            NeonTestUtils.AssertNumericEqual(rows[i]["B1"], expectedB1[i]);
            NeonTestUtils.AssertNumericEqual(rows[i]["B25"], expectedB25[i]);
        }
    }

    [Test]
    [Description("bin() - integer values including exact multiples, zero, and negatives")]
    public void Bin_IntColumn_PositiveZeroAndNegativeValues()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, int)>
            {
                (1, -11),
                (2, -10),
                (3, -7),
                (4, -5),
                (5, -1),
                (6, 0),
                (7, 1),
                (8, 4),
                (9, 5),
                (10, 7),
                (11, 14),
                (12, 15)
            },
            columnName1: "Id",
            columnName2: "Val",
            tableName: "Ints");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "Ints | extend B = bin(Val, 5) | sort by Id asc");

        long[] expected = [-15, -10, -10, -5, -5, 0, 0, 0, 5, 5, 10, 15];
        for (var i = 0; i < rows.Count; i++)
        {
            NeonTestUtils.AssertNumericEqual(rows[i]["B"], expected[i]);
        }
    }

    [Test]
    [Description("bin() - long values beyond 32-bit integer range")]
    public void Bin_LongColumn_LargePositiveAndNegativeValues()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, long)>
            {
                (1, 3_000_000_000L),
                (2, 3_000_000_007L),
                (3, -3_000_000_000L),
                (4, -3_000_000_001L)
            },
            columnName1: "Id",
            columnName2: "Val",
            tableName: "BigNums");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "BigNums | extend B = bin(Val, 10) | sort by Id asc");

        long[] expected = [3_000_000_000L, 3_000_000_000L, -3_000_000_000L, -3_000_000_010L];
        for (var i = 0; i < rows.Count; i++)
        {
            NeonTestUtils.AssertNumericEqual(rows[i]["B"], expected[i]);
        }
    }

    [Test]
    [Description("bin() - bin size of 1 acts as identity for integers and larger bin size rounds down properly")]
    public void Bin_EdgeBinSizes_OneAndLargerThanValue()
    {
        var db = new KustoDatabase();

        NeonTestUtils.AssertNumericEqual(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(42, 1)"), 42L);
        NeonTestUtils.AssertNumericEqual(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(-42, 1)"), -42L);
        NeonTestUtils.AssertNumericEqual(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(3, 100)"), 0L);
        NeonTestUtils.AssertNumericEqual(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(-3, 100)"), -100L);
    }

    [Test]
    [Description("bin() - mixed numeric types (int value with real bin size)")]
    public void Bin_IntValueWithRealBinSize()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(int(7), 2.5)");
        NeonTestUtils.AssertNumericEqual(actual, 5.0);
    }

    [Test]
    [Description("bin() - official doc example for timespan values")]
    public void Bin_Timespan_OfficialDocExample()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<TimeSpan>(db, "print bin(time(16d), 7d)");

        Assert.That(actual, Is.EqualTo(TimeSpan.FromDays(14)));
    }

    [Test]
    [Description("bin() - timespan values across exact multiples, sub-day units, zero, and negatives")]
    public void Bin_TimespanColumn_VariousUnitsAndNegatives()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, TimeSpan)>
            {
                (1, TimeSpan.FromDays(14)),
                (2, TimeSpan.FromDays(16)),
                (3, TimeSpan.FromDays(-14)),
                (4, TimeSpan.FromDays(-16)),
                (5, TimeSpan.Zero),
                (6, new TimeSpan(1, 23, 45)),
                (7, new TimeSpan(0, -1, 0))
            },
            columnName1: "Id",
            columnName2: "Duration",
            tableName: "Durations");
        db.AddTable(table);

        var rowsDays = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "Durations | where Id <= 5 | extend B = bin(Duration, 7d) | sort by Id asc");

        TimeSpan[] expectedDays =
        [
            TimeSpan.FromDays(14),
            TimeSpan.FromDays(14),
            TimeSpan.FromDays(-14),
            TimeSpan.FromDays(-21),
            TimeSpan.Zero
        ];
        for (var i = 0; i < expectedDays.Length; i++)
        {
            Assert.That(rowsDays[i]["B"], Is.EqualTo(expectedDays[i]));
        }

        var rowsMinutes = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "Durations | where Id >= 6 | extend B = bin(Duration, 15m) | sort by Id asc");

        Assert.That(rowsMinutes[0]["B"], Is.EqualTo(new TimeSpan(1, 15, 0)));
        Assert.That(rowsMinutes[1]["B"], Is.EqualTo(TimeSpan.FromMinutes(-15)));
    }

    [Test]
    [Description("bin() - sub-second timespan binning with milliseconds")]
    public void Bin_Timespan_Milliseconds()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<TimeSpan>(db, "print bin(time(550ms), 200ms)");

        Assert.That(actual, Is.EqualTo(TimeSpan.FromMilliseconds(400)));
    }

    [Test]
    [Description("bin() - official doc example for datetime with 1d bin")]
    public void Bin_DateTime_OfficialDocExample()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<DateTime>(
            db,
            "print bin(datetime(1970-05-11 13:45:07), 1d)");

        Assert.That(actual, Is.EqualTo(new DateTime(1970, 5, 11, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    [Description("bin() - datetime across multiple granularities (1s, 5m, 15m, 1h, 6h, 12h, 1d)")]
    public void Bin_DateTime_MultipleGranularities()
    {
        var db = new KustoDatabase();

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            print
                b1s  = bin(datetime(2024-06-15 14:37:49.876), 1s),
                b5m  = bin(datetime(2024-06-15 14:37:49.876), 5m),
                b15m = bin(datetime(2024-06-15 14:37:49.876), 15m),
                b1h  = bin(datetime(2024-06-15 14:37:49.876), 1h),
                b6h  = bin(datetime(2024-06-15 14:37:49.876), 6h),
                b12h = bin(datetime(2024-06-15 14:37:49.876), 12h),
                b1d  = bin(datetime(2024-06-15 14:37:49.876), 1d)
            """);

        var row = rows[0];
        Assert.That(row["b1s"], Is.EqualTo(new DateTime(2024, 6, 15, 14, 37, 49, DateTimeKind.Utc)));
        Assert.That(row["b5m"], Is.EqualTo(new DateTime(2024, 6, 15, 14, 35, 0, DateTimeKind.Utc)));
        Assert.That(row["b15m"], Is.EqualTo(new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Utc)));
        Assert.That(row["b1h"], Is.EqualTo(new DateTime(2024, 6, 15, 14, 0, 0, DateTimeKind.Utc)));
        Assert.That(row["b6h"], Is.EqualTo(new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc)));
        Assert.That(row["b12h"], Is.EqualTo(new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc)));
        Assert.That(row["b1d"], Is.EqualTo(new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    [Description("bin() - datetime exact boundary, leap day, and pre-1970 dates")]
    public void Bin_DateTime_BoundaryLeapDayAndPre1970()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, DateTime)>
            {
                (1, new DateTime(2024, 2, 29, 23, 59, 59, DateTimeKind.Utc)),
                (2, new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
                (3, new DateTime(1969, 12, 31, 23, 45, 30, DateTimeKind.Utc))
            },
            columnName1: "Id",
            columnName2: "Ts",
            tableName: "Events");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "Events | extend DayBin = bin(Ts, 1d), HourBin = bin(Ts, 1h) | sort by Id asc");

        Assert.That(rows[0]["DayBin"], Is.EqualTo(new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(rows[0]["HourBin"], Is.EqualTo(new DateTime(2024, 2, 29, 23, 0, 0, DateTimeKind.Utc)));

        Assert.That(rows[1]["DayBin"], Is.EqualTo(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(rows[1]["HourBin"], Is.EqualTo(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.That(rows[2]["DayBin"], Is.EqualTo(new DateTime(1969, 12, 31, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(rows[2]["HourBin"], Is.EqualTo(new DateTime(1969, 12, 31, 23, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    [Description("bin() - null input values return null for all supported value types")]
    public void Bin_NullInputValues_ReturnsNull()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(int(null), 5)"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(long(null), 5)"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(real(null), 1.0)"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(datetime(null), 1d)"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(timespan(null), 1h)"), Is.Null);
    }

    [Test]
    [Description("bin() - null bin size returns null")]
    public void Bin_NullBinSize_ReturnsNull()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(10, int(null))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(10.5, real(null))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(datetime(2024-01-01 12:30:00), timespan(null))"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(time(2h), timespan(null))"), Is.Null);
    }

    [Test]
    [Description("bin() - zero bin size returns null, except a real zero yields NaN; a negative bin size returns null only for datetime, numbers and timespans are floored as floor(value / size) * size")]
    public void Bin_ZeroWidthReturnsNullExceptRealZero_NegativeWidthFloorsExceptDatetime()
    {
        var db = new KustoDatabase();

        NeonTestUtils.AssertNumericEqual(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(10, -5)"), 10L);
        NeonTestUtils.AssertNumericEqual(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(4.5, -1.0)"), 5.0);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(datetime(2024-01-01 12:30:00), -1h)"), Is.Null);
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<TimeSpan>(db, "print bin(time(16d), -7d)"),
            Is.EqualTo(TimeSpan.FromDays(21)));

        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(10, 0)"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<double>(db, "print bin(4.5, 0.0)"), Is.NaN);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(datetime(2024-01-01 12:30:00), 0s)"), Is.Null);
        Assert.That(NeonTestUtils.ExecuteAndGetSingleObject(db, "print bin(time(16d), 0s)"), Is.Null);
    }

    [Test]
    [Description("bin() - nullable table column preserves nulls while binning non-null rows")]
    public void Bin_NullableColumn_PreservesNullRows()
    {
        var db = new KustoDatabase();
        var table = NeonTestUtils.GenerateTableWith2ColumnsNullable2<int, int>(
            new List<(int, int?)>
            {
                (1, 13),
                (2, null),
                (3, -3),
                (4, 20)
            },
            columnName1: "Id",
            columnName2: "Val",
            tableName: "NullableInts");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "NullableInts | extend B = bin(Val, 10) | sort by Id asc");

        Assert.That(rows, Has.Count.EqualTo(4));
        NeonTestUtils.AssertNumericEqual(rows[0]["B"], 10L);
        Assert.That(rows[1]["B"], Is.Null);
        NeonTestUtils.AssertNumericEqual(rows[2]["B"], -10L);
        NeonTestUtils.AssertNumericEqual(rows[3]["B"], 20L);
    }

    [Test]
    [Description("floor() - alias is equivalent to bin() across numeric, timespan, and datetime types")]
    public void Floor_AliasEquivalentToBin()
    {
        var db = new KustoDatabase();

        NeonTestUtils.AssertNumericEqual(NeonTestUtils.ExecuteAndGetSingleObject(db, "print floor(4.5, 1)"), 4.0);
        NeonTestUtils.AssertNumericEqual(NeonTestUtils.ExecuteAndGetSingleObject(db, "print floor(-4.5, 1)"), -5.0);
        NeonTestUtils.AssertNumericEqual(NeonTestUtils.ExecuteAndGetSingleObject(db, "print floor(7, 5)"), 5L);
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<TimeSpan>(db, "print floor(time(16d), 7d)"),
            Is.EqualTo(TimeSpan.FromDays(14)));
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<DateTime>(db, "print floor(datetime(1970-05-11 13:45:07), 1d)"),
            Is.EqualTo(new DateTime(1970, 5, 11, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    [Description("bin() - used in where filter and distinct bucketing pipeline")]
    public void Bin_InWhereFilterAndDistinctBucketing()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(string, DateTime)>
            {
                ("e1", new DateTime(2025, 1, 10, 10, 5, 0, DateTimeKind.Utc)),
                ("e2", new DateTime(2025, 1, 10, 10, 45, 0, DateTimeKind.Utc)),
                ("e3", new DateTime(2025, 1, 10, 11, 10, 0, DateTimeKind.Utc)),
                ("e4", new DateTime(2025, 1, 10, 12, 0, 0, DateTimeKind.Utc))
            },
            columnName1: "Name",
            columnName2: "Ts",
            tableName: "Logs");
        db.AddTable(table);

        var filtered = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Name",
            db,
            "Logs | where bin(Ts, 1h) == datetime(2025-01-10 10:00:00) | project Name");
        Assert.That(filtered, Is.EquivalentTo(new List<string> { "e1", "e2" }));

        var buckets = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(
            "HourBucket",
            db,
            "Logs | extend HourBucket = bin(Ts, 1h) | distinct HourBucket | sort by HourBucket asc");
        Assert.That(buckets, Is.EqualTo(new List<DateTime>
        {
            new(2025, 1, 10, 10, 0, 0, DateTimeKind.Utc),
            new(2025, 1, 10, 11, 0, 0, DateTimeKind.Utc),
            new(2025, 1, 10, 12, 0, 0, DateTimeKind.Utc)
        }));
    }

    [Test]
    [Description("bin() - dynamic column as bin size and expression as value")]
    public void Bin_DynamicColumnBinSizeAndNestedExpression()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith3Columns(
            new List<(int, int, int)>
            {
                (1, 17, 5),
                (2, 17, 10),
                (3, 17, 4)
            },
            columnName1: "Id",
            columnName2: "Val",
            columnName3: "Step",
            tableName: "DynamicBins");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "DynamicBins | extend Direct = bin(Val, Step), Expr = bin(Val * 2 + 1, Step) | sort by Id asc");

        NeonTestUtils.AssertNumericEqual(rows[0]["Direct"], 15L);
        NeonTestUtils.AssertNumericEqual(rows[0]["Expr"], 35L);

        NeonTestUtils.AssertNumericEqual(rows[1]["Direct"], 10L);
        NeonTestUtils.AssertNumericEqual(rows[1]["Expr"], 30L);

        NeonTestUtils.AssertNumericEqual(rows[2]["Direct"], 16L);
        NeonTestUtils.AssertNumericEqual(rows[2]["Expr"], 32L);
    }

    [Test]
    [Description("bin() - invalid arity and a string value return execution errors, while a numeric bin size for a datetime is accepted (as seconds)")]
    public void Bin_InvalidArguments_ReturnsError()
    {
        var db = new KustoDatabase();

        NeonTestUtils.ExecuteAndExpectError(db, "print bin()");
        NeonTestUtils.ExecuteAndExpectError(db, "print bin(10)");
        NeonTestUtils.ExecuteAndExpectError(db, "print bin(10, 5, 1)");
        NeonTestUtils.ExecuteAndExpectError(db, "print bin('not_a_number', 5)");
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<DateTime>(db, "print bin(datetime(2024-01-01), 5)"),
            Is.EqualTo(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }
}
