namespace KustoPlayground.Core.Tests;

public class BinAstraTests
{
    [Test]
    public void Bin_LongColumns_FloorsEachRowUsingItsOwnWidth()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWith2Columns<long, long>(
            [(-6, 5), (-5, 5), (-1, 5), (0, 5), (4, 5), (5, 5), (13, 5), (13, 4), (9007199254740995L, 2)],
            "N", "Width", "Samples"));

        var rows = TestUtils.ExecuteAndGetDataFor3Columns<long, long, long>("N", "Width", "Bucket", database,
            "Samples | extend Bucket = bin(N, Width)");

        Assert.That(rows, Is.EquivalentTo(new (long, long, long)[]
        {
            (-6, 5, -10), (-5, 5, -5), (-1, 5, -5), (0, 5, 0), (4, 5, 0), (5, 5, 5),
            (13, 5, 10), (13, 4, 12), (9007199254740995L, 2, 9007199254740994L)
        }));
    }

    [Test]
    public void Bin_IntOperandsProduceAnIntegralLongBucket()
    {
        Assert.That(AstraTestUtils.Print<long>("bin(int(13), int(5))"), Is.EqualTo(10L));
    }

    [TestCase("bin(4.5, 1)", 4.0)]
    [TestCase("bin(7, 2.5)", 5.0)]
    [TestCase("bin(-0.125, 0.25)", -0.25)]
    [TestCase("bin(-0.5, 0.25)", -0.5)]
    [TestCase("bin(0.875, 0.25)", 0.75)]
    [TestCase("bin(-8.5, -2.0)", -8.0)]
    public void Bin_RealOrMixedOperands(string expression, double expected)
    {
        Assert.That(AstraTestUtils.Print<double>(expression), Is.EqualTo(expected));
    }

    [TestCase("bin(long(null), 5)")]
    [TestCase("bin(42, long(null))")]
    [TestCase("bin(42, 0)")]
    [TestCase("bin(datetime(null), 1d)")]
    [TestCase("bin(datetime(2024-02-29), 0s)")]
    [TestCase("bin(15s, timespan(null))")]
    public void Bin_NullOperandOrZeroWidthReturnsNull(string expression)
    {
        Assert.That(AstraTestUtils.Print<object?>(expression), Is.Null);
    }

    [Test]
    public void Bin_NegativeTimespanWidthFloorsInsteadOfReturningNull()
    {
        Assert.That(AstraTestUtils.Print<TimeSpan>("bin(15s, -1s)"), Is.EqualTo(TimeSpan.FromSeconds(15)));
    }

    [Test]
    public void Bin_NumericTextMustBeConvertedExplicitly()
    {
        Assert.That(AstraTestUtils.Print<long>("bin(tolong('42'), 5)"), Is.EqualTo(40L));
        AstraTestUtils.AssertRejected(new KustoDatabase(), "print Value = bin('42', 5)");
    }

    [Test]
    public void Bin_DatetimeAcceptsATimespanOrANumericWidth()
    {
        Assert.That(AstraTestUtils.Print<DateTime>("bin(datetime(2024-02-29T23:59:59Z), 1d)"),
            Is.EqualTo(new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(AstraTestUtils.Print<DateTime>("bin(datetime(2024-02-29), 1)"),
            Is.EqualTo(new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void Bin_MultiDayTimespanKeepsWholeWeeks()
    {
        Assert.That(AstraTestUtils.Print<TimeSpan>("bin(time(17.06:00:00), 7d)"), Is.EqualTo(TimeSpan.FromDays(14)));
    }

    [Test]
    public void Bin_TimespansOnEitherSideOfASecondBoundary()
    {
        var database = new KustoDatabase();
        var second = TimeSpan.FromSeconds(1);
        var tick = TimeSpan.FromTicks(1);
        database.AddTable(TestUtils.GenerateTableWithColumn(
            new[] { -second - tick, -second, -tick, TimeSpan.Zero, second - tick, second }, "Elapsed", "Timers"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<TimeSpan, TimeSpan>("Elapsed", "Bucket", database,
            "Timers | extend Bucket = bin(Elapsed, 1s)");

        Assert.That(rows, Is.EquivalentTo(new[]
        {
            (-second - tick, -second - second), (-second, -second), (-tick, -second),
            (TimeSpan.Zero, TimeSpan.Zero), (second - tick, TimeSpan.Zero), (second, second)
        }));
    }

    [Test]
    public void Bin_DatetimeUsesUtcQuarterHoursAcrossMidnight()
    {
        var database = new KustoDatabase();
        var midnight = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        database.AddTable(TestUtils.GenerateTableWithColumn(
            new[] { midnight.AddTicks(-1), midnight, midnight.AddMinutes(15).AddTicks(-1), midnight.AddMinutes(15) },
            "Stamp", "Events"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, DateTime>("Stamp", "Bucket", database,
            "Events | extend Bucket = bin(Stamp, 15m)");

        Assert.That(rows, Is.EquivalentTo(new[]
        {
            (midnight.AddTicks(-1), midnight.AddMinutes(-15)), (midnight, midnight),
            (midnight.AddMinutes(15).AddTicks(-1), midnight), (midnight.AddMinutes(15), midnight.AddMinutes(15))
        }));
    }

    [Test]
    public void Bin_InSummarizeGroupsDuplicateDaysWithoutInventingMissingDays()
    {
        var database = new KustoDatabase();
        var day = new DateTime(2024, 2, 28, 0, 0, 0, DateTimeKind.Utc);
        database.AddTable(TestUtils.GenerateTableWithColumn(
            new[] { day, day.AddHours(23), day.AddDays(2).AddHours(1) }, "Stamp", "Events"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<DateTime, long>("Day", "EventsInDay", database,
            "Events | summarize EventsInDay = count() by Day = bin(Stamp, 1d)");

        Assert.That(rows, Is.EquivalentTo(new[] { (day, 2L), (day.AddDays(2), 1L) }));
    }
}
