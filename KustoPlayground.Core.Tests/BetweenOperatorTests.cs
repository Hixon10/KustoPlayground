namespace KustoPlayground.Core.Tests;

public class BetweenOperatorTests
{
    [Test]
    [Description("Numeric - both ends present (inclusive)")]
    public void Numeric_Between_BothEndsIncluded()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> tableRows = [1, 2, 3, 4, 5];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (2 .. 4)");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 2, 3, 4 }));
    }

    [Test]
    [Description("Numeric - only left endpoint value is present among rows")]
    public void Numeric_Between_OnlyLeftPresent()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> tableRows = [2, 3, 5]; // right endpoint (6) not present
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (2 .. 6)");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 2, 3, 5 }));
    }

    [Test]
    [Description("Numeric - only right endpoint value is present among rows")]
    public void Numeric_Between_OnlyRightPresent()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> tableRows = [1, 3, 5]; // left endpoint (2) not present
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (2 .. 5)");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 3, 5 }));
    }

    [Test]
    [Description("Int - neither endpoint value present among rows (but inner values exist)")]
    public void Int_Between_NeitherEndpointPresent()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> tableRows = [3, 4]; // endpoints 2 and 5 not present
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (2 .. 5)");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 3, 4 }));
    }

    [Test]
    [Description("Long - neither endpoint value present among rows (but inner values exist)")]
    public void Long_Between_NeitherEndpointPresent()
    {
        var kustoDatabase = new KustoDatabase();
        List<long> tableRows = [3, 4]; // endpoints 2 and 5 not present
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (2 .. 5)");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 3, 4 }));
    }

    [Test]
    public void EmptyBetweenTest()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> tableRows = [3, 4];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (5 .. 7)");

        Assert.That(actualData, Is.EquivalentTo(new List<int>()));
    }

    // -------------------------
    // DATETIME tests
    // -------------------------
    [Test]
    [Description("Datetime - both ends present (inclusive)")]
    public void Datetime_Between_BothEndsIncluded()
    {
        var kustoDatabase = new KustoDatabase();
        List<DateTime> tableRows = new()
        {
            new DateTime(2023, 01, 01),
            new DateTime(2023, 01, 02),
            new DateTime(2023, 01, 03),
            new DateTime(2023, 01, 04)
        };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (datetime(2023-01-02) .. datetime(2023-01-03))");

        Assert.That(actualData, Is.EquivalentTo(new List<DateTime>
        {
            new(2023, 01, 02),
            new(2023, 01, 03)
        }));
    }

    [Test]
    [Description("Datetime - only left endpoint value present among rows")]
    public void Datetime_Between_OnlyLeftPresent()
    {
        var kustoDatabase = new KustoDatabase();
        List<DateTime> tableRows = new()
        {
            new DateTime(2023, 01, 02),
            new DateTime(2023, 01, 03),
            new DateTime(2023, 01, 05) // right endpoint 2023-01-06 is absent
        };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (datetime(2023-01-02) .. datetime(2023-01-06))");

        Assert.That(actualData, Is.EquivalentTo(new List<DateTime>
        {
            new(2023, 01, 02),
            new(2023, 01, 03),
            new(2023, 01, 05)
        }));
    }

    [Test]
    [Description("Datetime - only right endpoint value present among rows")]
    public void Datetime_Between_OnlyRightPresent()
    {
        var kustoDatabase = new KustoDatabase();
        List<DateTime> tableRows = new()
        {
            new DateTime(2023, 01, 01),
            new DateTime(2023, 01, 03),
            new DateTime(2023, 01, 05) // left endpoint 2023-01-02 is absent, right 2023-01-05 present
        };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (datetime(2023-01-02) .. datetime(2023-01-05))");

        Assert.That(actualData, Is.EquivalentTo(new List<DateTime>
        {
            new(2023, 01, 03),
            new(2023, 01, 05)
        }));
    }

    [Test]
    [Description("Datetime - neither endpoint present among rows (but inner values exist)")]
    public void Datetime_Between_NeitherEndpointPresent()
    {
        var kustoDatabase = new KustoDatabase();
        List<DateTime> tableRows = new()
        {
            new DateTime(2023, 01, 03),
            new DateTime(2023, 01, 04) // endpoints 2023-01-02 and 2023-01-05 absent
        };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (datetime(2023-01-02) .. datetime(2023-01-05))");

        Assert.That(actualData, Is.EquivalentTo(new List<DateTime>
        {
            new(2023, 01, 03),
            new(2023, 01, 04)
        }));
    }

    // -------------------------
    // TIMESPAN tests
    // -------------------------
    [Test]
    [Description("Timespan - both ends present (inclusive)")]
    public void Timespan_Between_BothEndsIncluded()
    {
        var kustoDatabase = new KustoDatabase();
        List<TimeSpan> tableRows = new()
        {
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(4)
        };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        // timespan literals like 2h and 3h are valid in Kusto
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<TimeSpan>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (2h .. 3h)");

        Assert.That(actualData, Is.EquivalentTo(new List<TimeSpan>
        {
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(3)
        }));
    }

    [Test]
    [Description("Timespan - only left endpoint value present among rows")]
    public void Timespan_Between_OnlyLeftPresent()
    {
        var kustoDatabase = new KustoDatabase();
        List<TimeSpan> tableRows = new()
        {
            TimeSpan.FromHours(2),
            TimeSpan.FromMinutes(150), // 2.5 hours
            TimeSpan.FromHours(5) // right endpoint (6h) not present
        };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<TimeSpan>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (2h .. 6h)");

        Assert.That(actualData, Is.EquivalentTo(new List<TimeSpan>
        {
            TimeSpan.FromHours(2),
            TimeSpan.FromMinutes(150),
            TimeSpan.FromHours(5)
        }));
    }

    [Test]
    [Description("Timespan - only right endpoint value present among rows")]
    public void Timespan_Between_OnlyRightPresent()
    {
        var kustoDatabase = new KustoDatabase();
        List<TimeSpan> tableRows = new()
        {
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(150), // 2.5 hours
            TimeSpan.FromHours(3) // right endpoint 3h present, left 2h absent
        };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<TimeSpan>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (2h .. 3h)");

        Assert.That(actualData, Is.EquivalentTo(new List<TimeSpan>
        {
            TimeSpan.FromMinutes(150),
            TimeSpan.FromHours(3)
        }));
    }

    [Test]
    [Description("Timespan - neither endpoint present among rows (but inner values exist)")]
    public void Timespan_Between_NeitherEndpointPresent()
    {
        var kustoDatabase = new KustoDatabase();
        List<TimeSpan> tableRows = new()
        {
            TimeSpan.FromMinutes(150), // 2.5 hours
            TimeSpan.FromMinutes(165) // 2.75 hours
        };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<TimeSpan>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "table1 | where column1 between (2h .. 3h)");

        Assert.That(actualData, Is.EquivalentTo(new List<TimeSpan>
        {
            TimeSpan.FromMinutes(150),
            TimeSpan.FromMinutes(165)
        }));
    }

    [Test]
    [Description("Datetime + timespan - both ends included")]
    public void Datetime_Timespan_Between_BothEndsIncluded()
    {
        var kustoDatabase = new KustoDatabase();
        DateTime baseDate = new DateTime(2007, 07, 27);
        List<DateTime> tableRows = new()
        {
            baseDate.Subtract(TimeSpan.FromDays(1)),
            baseDate,
            baseDate.AddDays(1),
            baseDate.AddDays(2),
            baseDate.AddDays(3),
            baseDate.AddDays(4)
        };

        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        // 3d relative span means baseDate + 3d = 2007-07-30
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(
            columnName,
            kustoDatabase,
            "table1 | where column1 between (datetime(2007-07-27) .. 3d)");

        Assert.That(actualData, Is.EquivalentTo(new List<DateTime>
        {
            baseDate,
            baseDate.AddDays(1),
            baseDate.AddDays(2),
            baseDate.AddDays(3)
        }));
    }

    [Test]
    [Description("Datetime + timespan - empty set when outside range")]
    public void Datetime_Timespan_Between_EmptySet()
    {
        var kustoDatabase = new KustoDatabase();
        DateTime baseDate = new DateTime(2007, 07, 27);
        List<DateTime> tableRows = new()
        {
            baseDate.AddDays(5),
            baseDate.AddDays(6)
        };

        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(
            columnName,
            kustoDatabase,
            "table1 | where column1 between (datetime(2007-07-27) .. 3d)");

        Assert.That(actualData, Is.Empty);
    }

    [Test]
    [Description("Double - both ends included")]
    public void Double_Between_BothEndsIncluded()
    {
        var kustoDatabase = new KustoDatabase();
        List<double> tableRows = new() { 1.1, 2.5, 3.5, 4.0 };
        const string columnName = "column1";

        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(
            columnName,
            kustoDatabase,
            "table1 | where column1 between (2.5 .. 4.0)");

        Assert.That(actualData, Is.EquivalentTo(new List<double> { 2.5, 3.5, 4.0 }));
    }

    [Test]
    [Description("Double - only inner values, endpoints absent")]
    public void Double_Between_OnlyInner()
    {
        var kustoDatabase = new KustoDatabase();
        List<double> tableRows = new() { 2.6, 3.2 };
        const string columnName = "column1";

        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(
            columnName,
            kustoDatabase,
            "table1 | where column1 between (2.5 .. 4.0)");

        Assert.That(actualData, Is.EquivalentTo(new List<double> { 2.6, 3.2 }));
    }

    [Test]
    [Description("Double - empty result when all values out of range")]
    public void Double_Between_EmptyResult()
    {
        var kustoDatabase = new KustoDatabase();
        List<double> tableRows = new() { 0.5, 1.0, 10.0 };
        const string columnName = "column1";

        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(
            columnName,
            kustoDatabase,
            "table1 | where column1 between (2.5 .. 4.0)");

        Assert.That(actualData, Is.Empty);
    }

    [Test]
    [Description("Numeric - empty set because range misses all values")]
    public void Numeric_Between_EmptySet()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> tableRows = new() { 1, 2, 10 };
        const string columnName = "column1";

        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            "table1 | where column1 between (4 .. 5)");

        Assert.That(actualData, Is.Empty);
    }

    [Test]
    [Description("Timespan - empty set because range misses all values")]
    public void Timespan_Between_EmptySet()
    {
        var kustoDatabase = new KustoDatabase();
        List<TimeSpan> tableRows = new() { TimeSpan.FromHours(10), TimeSpan.FromHours(20) };
        const string columnName = "column1";

        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<TimeSpan>(
            columnName,
            kustoDatabase,
            "table1 | where column1 between (2h .. 3h)");

        Assert.That(actualData, Is.Empty);
    }

    [Test]
    [Description("Verifies the 'between' operator for numeric types with an inclusive range.")]
    public void WhereNumericBetween_InclusiveRange_ReturnsCorrectRows()
    {
        // Arrange
        KustoDatabase kustoDatabase = new KustoDatabase();
        List<int> tableRows =
        [
            1, // Below range
            2, // Lower bound
            5, // Inside range
            10, // Upper bound
            11 // Above range
        ];
        const string columnName = "value";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "NumericTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        // Act
        List<int> actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "NumericTable | where value between (2..10)");

        // Assert
        List<int> expectedData = [2, 5, 10];
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("Verifies the 'between' operator for datetime types with an inclusive range.")]
    public void WhereDateTimeBetween_InclusiveRange_ReturnsCorrectRows()
    {
        // Arrange
        KustoDatabase kustoDatabase = new KustoDatabase();
        List<DateTime> tableRows =
        [
            new(2025, 9, 15, 20, 59, 59), // Below range
            new(2025, 9, 15, 21, 0, 0), // Lower bound
            new(2025, 9, 15, 22, 30, 0), // Inside range
            new(2025, 9, 15, 23, 0, 0), // Upper bound
            new(2025, 9, 15, 23, 0, 1) // Above range
        ];
        const string columnName = "eventTime";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "Events", columnName: columnName);
        kustoDatabase.AddTable(table);

        // Act
        List<DateTime> actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "Events | where eventTime between (datetime(2025-09-15 21:00:00)..datetime(2025-09-15 23:00:00))");

        // Assert
        List<DateTime> expectedData =
        [
            new(2025, 9, 15, 21, 0, 0),
            new(2025, 9, 15, 22, 30, 0),
            new(2025, 9, 15, 23, 0, 0)
        ];
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("Verifies the 'between' operator for timespan types with an inclusive range.")]
    public void WhereTimeSpanBetween_InclusiveRange_ReturnsCorrectRows()
    {
        // Arrange
        KustoDatabase kustoDatabase = new KustoDatabase();
        List<TimeSpan> tableRows =
        [
            TimeSpan.FromMinutes(59), // Below range
            TimeSpan.FromHours(1), // Lower bound
            TimeSpan.FromHours(3), // Inside range
            TimeSpan.FromHours(5), // Upper bound
            TimeSpan.FromHours(5) + TimeSpan.FromTicks(1) // Above range
        ];
        const string columnName = "duration";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "Logs", columnName: columnName);
        kustoDatabase.AddTable(table);

        // Act
        List<TimeSpan> actualData = TestUtils.ExecuteAndGetDataForOneColumn<TimeSpan>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "Logs | where duration between (timespan(1h)..timespan(5h))");

        // Assert
        List<TimeSpan> expectedData =
        [
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(5)
        ];
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("Verifies the 'between' operator for double types with an inclusive range.")]
    public void WhereDoubleBetween_InclusiveRange_ReturnsCorrectRows()
    {
        // Arrange
        KustoDatabase kustoDatabase = new KustoDatabase();
        List<double> tableRows =
        [
            2.499, // Below range
            2.5, // Lower bound
            5.0, // Inside range
            9.99, // Inside range
            10.0, // Upper bound
            10.001 // Above range
        ];
        const string columnName = "measurement";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "SensorReadings", columnName: columnName);
        kustoDatabase.AddTable(table);

        // Act
        List<double> actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "SensorReadings | where measurement between (2.5..10.0)");

        // Assert
        List<double> expectedData = [2.5, 5.0, 9.99, 10.0];
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("Verifies 'between' for a range defined by a start datetime and a timespan duration.")]
    public void WhereDateTimeBetween_DateTimeAndTimespanRange_ReturnsCorrectRows()
    {
        // Arrange
        KustoDatabase kustoDatabase = new KustoDatabase();
        List<DateTime> tableRows =
        [
            new(2007, 7, 26, 23, 59, 59), // Below range
            new(2007, 7, 27, 0, 0, 0), // Lower bound
            new(2007, 7, 28, 12, 0, 0), // Inside range
            new(2007, 7, 30, 0, 0, 0), // Upper bound (start date + 3 days)
            new(2007, 7, 30, 0, 0, 1) // Above range
        ];
        const string columnName = "timestamp";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "Archive", columnName: columnName);
        kustoDatabase.AddTable(table);

        // Act
        List<DateTime> actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "Archive | where timestamp between (datetime(2007-07-27)..3d)");

        // Assert
        List<DateTime> expectedData =
        [
            new(2007, 7, 27, 0, 0, 0),
            new(2007, 7, 28, 12, 0, 0),
            new(2007, 7, 30, 0, 0, 0)
        ];
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("Verifies that 'between' returns an empty result set when no data matches the range.")]
    public void WhereNumericBetween_NoMatchingData_ReturnsEmptyResult()
    {
        // Arrange
        KustoDatabase kustoDatabase = new KustoDatabase();
        List<int> tableRows = [1, 2, 10, 11];
        const string columnName = "id";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "Items", columnName: columnName);
        kustoDatabase.AddTable(table);

        // Act
        List<int> actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "Items | where id between (3..9)");

        // Assert
        Assert.That(actualData, Is.Empty);
    }

    [Test]
    [Description("Verifies the '!between' operator for numeric types.")]
    public void WhereNumericNotBetween_ExclusiveRange_ReturnsCorrectRows()
    {
        // Arrange
        KustoDatabase kustoDatabase = new KustoDatabase();
        List<int> tableRows =
        [
            4, // Below range (should be included)
            5, // Lower bound (should be excluded)
            7, // Inside range (should be excluded)
            9, // Upper bound (should be excluded)
            10 // Above range (should be included)
        ];
        const string columnName = "value";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "NumericTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        // Act
        List<int> actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "NumericTable | where value !between (5..9)");

        // Assert
        List<int> expectedData = [4, 10];
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("Verifies the '!between' operator for datetime types.")]
    public void WhereDateTimeNotBetween_ExclusiveRange_ReturnsCorrectRows()
    {
        // Arrange
        KustoDatabase kustoDatabase = new KustoDatabase();
        List<DateTime> tableRows =
        [
            new(2025, 9, 15, 20, 59, 59), // Below range
            new(2025, 9, 15, 21, 0, 0), // Lower bound
            new(2025, 9, 15, 22, 30, 0), // Inside range
            new(2025, 9, 15, 23, 0, 0), // Upper bound
            new(2025, 9, 15, 23, 0, 1) // Above range
        ];
        const string columnName = "eventTime";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "Events", columnName: columnName);
        kustoDatabase.AddTable(table);

        // Act
        List<DateTime> actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "Events | where eventTime !between (datetime(2025-09-15 21:00:00)..datetime(2025-09-15 23:00:00))");

        // Assert
        List<DateTime> expectedData =
        [
            new(2025, 9, 15, 20, 59, 59),
            new(2025, 9, 15, 23, 0, 1)
        ];
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("Verifies the '!between' operator for timespan types.")]
    public void WhereTimeSpanNotBetween_ExclusiveRange_ReturnsCorrectRows()
    {
        // Arrange
        KustoDatabase kustoDatabase = new KustoDatabase();
        List<TimeSpan> tableRows =
        [
            TimeSpan.FromMinutes(59), // Below range
            TimeSpan.FromHours(1), // Lower bound
            TimeSpan.FromHours(3), // Inside range
            TimeSpan.FromHours(5), // Upper bound
            TimeSpan.FromHours(5) + TimeSpan.FromTicks(1) // Above range
        ];
        const string columnName = "duration";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "Logs", columnName: columnName);
        kustoDatabase.AddTable(table);

        // Act
        List<TimeSpan> actualData = TestUtils.ExecuteAndGetDataForOneColumn<TimeSpan>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "Logs | where duration !between (timespan(1h)..timespan(5h))");

        // Assert
        List<TimeSpan> expectedData =
        [
            TimeSpan.FromMinutes(59),
            TimeSpan.FromHours(5) + TimeSpan.FromTicks(1)
        ];
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("Test integer range with both ends included using between [1,5]")]
    public void WhereIntegerBothEndsIncludedTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<int> tableRows = [0, 1, 3, 5, 7];
        const string columnName = "intColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "intTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<int> actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "intTable | where intColumn between (1 .. 5)");
        Assert.That(actualData, Is.EquivalentTo(new List<int> { 1, 3, 5 }));
    }

    ////

    [Test]
    [Description("Test integer range exclusion using !between")]
    public void WhereIntegerNotBetweenTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<int> tableRows = [0, 1, 3, 5, 7];
        const string columnName = "intColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "intTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<int> actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "intTable | where intColumn !between (1 .. 5)");
        Assert.That(actualData, Is.EquivalentTo(new List<int> { 0, 7 }));
    }

    [Test]
    [Description("Test double range with both ends included using between [1.5,5.5]")]
    public void WhereDoubleBothEndsIncludedTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<double> tableRows = [0.5, 1.5, 3.2, 5.5, 7.1];
        const string columnName = "doubleColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "doubleTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<double> actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "doubleTable | where doubleColumn between (1.5 .. 5.5)");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 1.5, 3.2, 5.5 }));
    }

    [Test]
    [Description("Test double range exclusion using !between")]
    public void WhereDoubleNotBetweenTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<double> tableRows = [0.5, 1.5, 3.2, 5.5, 7.1];
        const string columnName = "doubleColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "doubleTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<double> actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "doubleTable | where doubleColumn !between (1.5 .. 5.5)");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 0.5, 7.1 }));
    }

    [Test]
    [Description("Test DateTime range with both ends included using between [2024-01-01,2024-01-05]")]
    public void WhereDateTimeBothEndsIncludedTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<DateTime> tableRows =
        [
            new(2023, 12, 31),
            new(2024, 1, 1),
            new(2024, 1, 3),
            new(2024, 1, 5),
            new(2024, 1, 7)
        ];
        const string columnName = "dateColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "dateTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<DateTime> actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "dateTable | where dateColumn between (datetime(2024-01-01) .. datetime(2024-01-05))");
        Assert.That(actualData, Is.EquivalentTo(new List<DateTime>
        {
            new(2024, 1, 1),
            new(2024, 1, 3),
            new(2024, 1, 5)
        }));
    }

    [Test]
    [Description("Test DateTime range exclusion using !between")]
    public void WhereDateTimeNotBetweenTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<DateTime> tableRows =
        [
            new(2023, 12, 31),
            new(2024, 1, 1),
            new(2024, 1, 3),
            new(2024, 1, 5),
            new(2024, 1, 7)
        ];
        const string columnName = "dateColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "dateTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<DateTime> actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "dateTable | where dateColumn !between (datetime(2024-01-01) .. datetime(2024-01-05))");
        Assert.That(actualData, Is.EquivalentTo(new List<DateTime>
        {
            new(2023, 12, 31),
            new(2024, 1, 7)
        }));
    }

// String Range Tests (Lexicographic ordering)
    [Test]
    [Description("Test string range with both ends included using between [\"b\",\"d\"]")]
    public void WhereStringBothEndsIncludedTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<string> tableRows = ["a", "b", "c", "d", "e"];
        const string columnName = "stringColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "stringTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<string> actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "stringTable | where stringColumn between (\"b\" .. \"d\")");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "b", "c", "d" }));
    }

    [Test]
    [Description("Test string range exclusion using !between")]
    public void WhereStringNotBetweenTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<string> tableRows = ["a", "b", "c", "d", "e"];
        const string columnName = "stringColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "stringTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<string> actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "stringTable | where stringColumn !between (\"b\" .. \"d\")");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "a", "e" }));
    }

// Long Range Tests
    [Test]
    [Description("Test long range with both ends included using between [10L,50L]")]
    public void WhereLongBothEndsIncludedTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<long> tableRows = [5, 10, 30, 50, 70];
        const string columnName = "longColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "longTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<long> actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "longTable | where longColumn between (10 .. 50)");
        Assert.That(actualData, Is.EquivalentTo(new List<long> { 10L, 30L, 50L }));
    }

    [Test]
    [Description("Test long range exclusion using !between")]
    public void WhereLongNotBetweenTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<long> tableRows = [5, 10, 30, 50, 70];
        const string columnName = "longColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "longTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<long> actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "longTable | where longColumn !between (10 .. 50)");
        Assert.That(actualData, Is.EquivalentTo(new List<long> { 5L, 70L }));
    }

// Decimal Range Tests
    [Test]
    [Description("Test decimal range with both ends included using between [1.5,5.5]")]
    public void WhereDecimalBothEndsIncludedTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<decimal> tableRows = [0.5m, 1.5m, 3.2m, 5.5m, 7.1m];
        const string columnName = "decimalColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "decimalTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<decimal> actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "decimalTable | where decimalColumn between (1.5 .. 5.5)");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 1.5m, 3.2m, 5.5m }));
    }

    [Test]
    [Description("Test decimal range exclusion using !between")]
    public void WhereDecimalNotBetweenTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<decimal> tableRows = [0.5m, 1.5m, 3.2m, 5.5m, 7.1m];
        const string columnName = "decimalColumn";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "decimalTable", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<decimal> actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "decimalTable | where decimalColumn !between (1.5 .. 5.5)");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 0.5m, 7.1m }));
    }
}