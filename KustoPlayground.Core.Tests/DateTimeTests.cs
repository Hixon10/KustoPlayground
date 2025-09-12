namespace KustoPlayground.Core.Tests;

public class DateTimeTests
{
    /// TODO
    /// T | where expr !between (leftRange..rightRange)
    /// T | where expr between (leftRange..rightRange)
    /// between can operate on any numeric,
    /// datetime, or timespan expression.
    /// leftRange - int, long, real, or datetime - inclusive
    /// rightRange - int, long, real, datetime, or timespan - inclusive
    ///              This value can only be of type timespan if
    ///              expr and leftRange are both of type datetime.
    /// return Rows in T for which the predicate of
    ///  (expr >= leftRange and expr <= rightRange) evaluates to true.
    [Test]
    public void TimeSpanTypeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        string query = """ 
                       StormEvents
                           | extend column2days = timespan(2)
                           | extend column15seconds = timespan(15 seconds)
                           | extend column3 = timespan(1.12:34:56)
                           | extend column10microsecond = timespan(10microsecond)
                           | extend column10microsecond2 = 10microsecond
                           | extend column100ms = 100ms
                           | extend column10s = 10s
                           | extend column30m = 30m
                           | extend column1and5h = 1.5h
                           | extend column2d = 2d
                           | extend columnMakeTimeSpan = make_timespan(2,12,30,35)
                           | extend columnToTimespan = totimespan("0.00:03:00")
                       """;

        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null);
        Assert.That(results.ResultRows!, Is.Not.Empty);

        foreach (var row in results.ResultRows)
        {
            Assert.That(row["column2days"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["column15seconds"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["column3"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["column10microsecond"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["column10microsecond2"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["column100ms"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["column10s"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["column30m"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["column1and5h"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["column2d"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["columnMakeTimeSpan"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));
            Assert.That(row["columnToTimespan"]!.GetType(), Is.EqualTo(typeof(TimeSpan)));

            Assert.That(row["column2days"]!, Is.EqualTo(TimeSpan.FromDays(2)));
            Assert.That(row["column15seconds"]!, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(row["column3"]!, Is.EqualTo(new TimeSpan(1, 12, 34, 56)));
            Assert.That(row["column10microsecond"]!, Is.EqualTo(TimeSpan.FromMicroseconds(10)));
            Assert.That(row["column10microsecond2"]!, Is.EqualTo(TimeSpan.FromMicroseconds(10)));
            Assert.That(row["column100ms"]!, Is.EqualTo(TimeSpan.FromMilliseconds(100)));
            Assert.That(row["column10s"]!, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(row["column30m"]!, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(row["column1and5h"]!, Is.EqualTo(TimeSpan.FromHours(1.5)));
            Assert.That(row["column2d"]!, Is.EqualTo(TimeSpan.FromDays(2)));
            Assert.That(row["columnMakeTimeSpan"]!, Is.EqualTo(new TimeSpan(2, 12, 30, 35)));
            Assert.That(row["columnToTimespan"]!, Is.EqualTo(new TimeSpan(0, 0, 3, 0)));
        }
    }

    [Test]
    public void WhereAgoAndNowTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        DateTime utcNow = DateTime.UtcNow;

        List<DateTime> tableRows =
        [
            utcNow.Subtract(TimeSpan.FromHours(5)),
            utcNow.Subtract(TimeSpan.FromHours(4)),
            utcNow.Subtract(TimeSpan.FromHours(3)),
            utcNow.Subtract(TimeSpan.FromHours(2)),
            utcNow.Subtract(TimeSpan.FromMinutes(61)),
            utcNow,
            utcNow.Add(TimeSpan.FromHours(1)),
            utcNow.Add(TimeSpan.FromHours(2)),
            utcNow.Add(TimeSpan.FromHours(3))
        ];
        const string columnName = "Timestamp";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<DateTime> actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where Timestamp > ago(1h)");
        Assert.That(actualData, Is.EquivalentTo(new List<DateTime>
        {
            utcNow,
            utcNow.Add(TimeSpan.FromHours(1)),
            utcNow.Add(TimeSpan.FromHours(2)),
            utcNow.Add(TimeSpan.FromHours(3))
        }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where Timestamp > ago(1d)");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where Timestamp > now(1d)");
        Assert.That(actualData, Is.EquivalentTo(new List<DateTime>()));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where Timestamp < now(-1min)");
        Assert.That(actualData, Is.EquivalentTo(new List<DateTime>
        {
            utcNow.Subtract(TimeSpan.FromHours(5)),
            utcNow.Subtract(TimeSpan.FromHours(4)),
            utcNow.Subtract(TimeSpan.FromHours(3)),
            utcNow.Subtract(TimeSpan.FromHours(2)),
            utcNow.Subtract(TimeSpan.FromMinutes(61))
        }));
    }

    [Test]
    public void AgoFunctionTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        string query = """ 
                       StormEvents
                           | extend AgoMinus2Days = ago(-2d)
                           | extend AgoPlus13Minutes = ago(13min)
                           | extend AgoPlus13Minutes2 = ago(+13min)
                       """;

        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null);
        Assert.That(results.ResultRows!, Is.Not.Empty);

        foreach (var row in results.ResultRows)
        {
            Assert.That(row["AgoMinus2Days"]!.GetType(), Is.EqualTo(typeof(DateTime)));
            Assert.That(row["AgoPlus13Minutes"]!.GetType(), Is.EqualTo(typeof(DateTime)));
            Assert.That(row["AgoPlus13Minutes2"]!.GetType(), Is.EqualTo(typeof(DateTime)));

            TimeSpan delta = TimeSpan.FromMinutes(1);

            DateTime nowPlus2Days = DateTime.UtcNow.Add(TimeSpan.FromDays(2));
            Assert.That(row["AgoMinus2Days"]!, Is.InRange(nowPlus2Days - delta, nowPlus2Days + delta),
                $"Expected {row["AgoMinus2Days"]!} to be within +-1 minute of {nowPlus2Days:o}");

            DateTime nowMinus13Min = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(13));
            Assert.That(row["AgoPlus13Minutes"]!, Is.InRange(nowMinus13Min - delta, nowMinus13Min + delta),
                $"Expected {row["AgoPlus13Minutes"]!} to be within +-1 minute of {nowMinus13Min:o}");
            Assert.That(row["AgoPlus13Minutes2"]!, Is.InRange(nowMinus13Min - delta, nowMinus13Min + delta),
                $"Expected {row["AgoPlus13Minutes2"]!} to be within +-1 minute of {nowMinus13Min:o}");
        }
    }

    [Test]
    public void NowFunctionTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        string query = """ 
                       StormEvents
                           | extend NowColumn = now()
                           | extend NowMinus2Days = now(-2d)
                           | extend NowPlus13Minutes = now(13min)
                           | extend NowPlus13Minutes2 = now(+13min)
                       """;

        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null);
        Assert.That(results.ResultRows!, Is.Not.Empty);

        foreach (var row in results.ResultRows)
        {
            Assert.That(row["NowColumn"]!.GetType(), Is.EqualTo(typeof(DateTime)));
            Assert.That(row["NowMinus2Days"]!.GetType(), Is.EqualTo(typeof(DateTime)));
            Assert.That(row["NowPlus13Minutes"]!.GetType(), Is.EqualTo(typeof(DateTime)));
            Assert.That(row["NowPlus13Minutes2"]!.GetType(), Is.EqualTo(typeof(DateTime)));

            DateTime now = DateTime.UtcNow;
            TimeSpan delta = TimeSpan.FromMinutes(1);

            Assert.That(row["NowColumn"]!, Is.InRange(now - delta, now + delta),
                $"Expected {row["NowColumn"]!} to be within +-1 minute of {now:o}");

            DateTime nowMinus2Days = DateTime.UtcNow.Subtract(TimeSpan.FromDays(2));
            Assert.That(row["NowMinus2Days"]!, Is.InRange(nowMinus2Days - delta, nowMinus2Days + delta),
                $"Expected {row["NowMinus2Days"]!} to be within +-1 minute of {nowMinus2Days:o}");

            DateTime nowPlus13Min = DateTime.UtcNow.Add(TimeSpan.FromMinutes(13));
            Assert.That(row["NowPlus13Minutes"]!, Is.InRange(nowPlus13Min - delta, nowPlus13Min + delta),
                $"Expected {row["NowPlus13Minutes"]!} to be within +-1 minute of {nowPlus13Min:o}");
            Assert.That(row["NowPlus13Minutes2"]!, Is.InRange(nowPlus13Min - delta, nowPlus13Min + delta),
                $"Expected {row["NowPlus13Minutes2"]!} to be within +-1 minute of {nowPlus13Min:o}");
        }
    }

    [Test]
    public void DateTimeTypeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        string query = """ 
                       StormEvents
                           | extend EndTime = datetime(2025-12-31)
                           | extend EndTime2 = datetime(2015-07-25 22:54:59)
                           | extend EndTime3 = datetime()
                           | extend EndTime4 = todatetime("2015-12-31 23:59:59")
                       """;

        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null);
        Assert.That(results.ResultRows!, Is.Not.Empty);

        foreach (var row in results.ResultRows)
        {
            Assert.That(row["EndTime"]!.GetType(), Is.EqualTo(typeof(DateTime)));
            Assert.That(row["EndTime2"]!.GetType(), Is.EqualTo(typeof(DateTime)));
            Assert.That(row["EndTime3"]!.GetType(), Is.EqualTo(typeof(DateTime)));
            Assert.That(row["EndTime4"]!.GetType(), Is.EqualTo(typeof(DateTime)));

            Assert.That(row["EndTime"]!, Is.EqualTo(new DateTime(2025, 12, 31)));
            Assert.That(row["EndTime2"]!, Is.EqualTo(new DateTime(2015, 07, 25, 22, 54, 59)));

            DateTime now = DateTime.UtcNow;
            TimeSpan delta = TimeSpan.FromMinutes(1);
            Assert.That(row["EndTime3"]!, Is.InRange(now - delta, now + delta),
                $"Expected {row["EndTime3"]!} to be within +-1 minute of {now:o}");

            Assert.That(row["EndTime4"]!, Is.EqualTo(new DateTime(2015, 12, 31, 23, 59, 59)));
        }
    }

    [Test]
    public void BetweenDateTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<string> tableRows = ["1", "-2", "1", "3.1"];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<string> actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "3.1", "1", "1", "-2" }));
    }
}