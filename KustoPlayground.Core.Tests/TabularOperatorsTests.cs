namespace KustoPlayground.Core.Tests;

public class TabularOperatorsTests
{
    [Test]
    [Description("Count on int column without filter")]
    public void Count_Int_NoFilter()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> tableRows = new List<int> { 1, 2, 3, 4, 5 };
        const string dataColumnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: dataColumnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            columnName: "Count",
            kustoDatabase: kustoDatabase,
            "table1 | count"
        );

        Assert.That(actualData, Is.EqualTo(new List<long> { 5 }));
    }

    [Test]
    [Description("Count on int column with filter")]
    public void Count_Int_WithFilter()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> tableRows = new List<int> { 1, 2, 3, 4, 5 };
        const string dataColumnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: dataColumnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            columnName: "Count",
            kustoDatabase: kustoDatabase,
            "table1 | where column1 > 2 | count"
        );

        Assert.That(actualData, Is.EqualTo(new List<long> { 3 }));
    }

    [Test]
    [Description("Count on long column without filter")]
    public void Count_Long_NoFilter()
    {
        var kustoDatabase = new KustoDatabase();
        List<long> tableRows = new List<long> { 100L, 200L, 300L };
        const string dataColumnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: dataColumnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            columnName: "Count",
            kustoDatabase: kustoDatabase,
            "table1 | count"
        );

        Assert.That(actualData, Is.EqualTo(new List<long> { 3 }));
    }

    [Test]
    [Description("Count on double column with filter")]
    public void Count_Double_WithFilter()
    {
        var kustoDatabase = new KustoDatabase();
        List<double> tableRows = new List<double> { 1.1, 2.2, 3.3, 4.4 };
        const string dataColumnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: dataColumnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            columnName: "Count",
            kustoDatabase: kustoDatabase,
            "table1 | where column1 >= 2.0 | count"
        );

        Assert.That(actualData, Is.EqualTo(new List<long> { 3 }));
    }

    [Test]
    [Description("Count on datetime column without filter")]
    public void Count_DateTime_NoFilter()
    {
        var kustoDatabase = new KustoDatabase();
        List<DateTime> tableRows = new List<DateTime>
        {
            new(2020, 1, 1),
            new(2020, 2, 1),
            new(2020, 3, 1)
        };
        const string dataColumnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: dataColumnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            columnName: "Count",
            kustoDatabase: kustoDatabase,
            "table1 | count"
        );

        Assert.That(actualData, Is.EqualTo(new List<long> { 3 }));
    }

    [Test]
    [Description("Count on datetime column with filter")]
    public void Count_DateTime_WithFilter()
    {
        var kustoDatabase = new KustoDatabase();
        List<DateTime> tableRows = new List<DateTime>
        {
            new(2020, 1, 1),
            new(2020, 2, 1),
            new(2020, 3, 1)
        };
        const string dataColumnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: dataColumnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            columnName: "Count",
            kustoDatabase: kustoDatabase,
            "table1 | where column1 >= datetime(2020-02-01) | count"
        );

        Assert.That(actualData, Is.EqualTo(new List<long> { 2 }));
    }
}