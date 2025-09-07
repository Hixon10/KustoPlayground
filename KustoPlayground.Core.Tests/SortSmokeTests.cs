namespace KustoPlayground.Core.Tests;

public class SortSmokeTests
{
    [Test]
    public void SortStringNumbersTest()
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
        Assert.That(actualData, Is.EqualTo(new List<string> { "3.1", "1", "1", "-2" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1 desc");
        Assert.That(actualData, Is.EqualTo(new List<string> { "3.1", "1", "1", "-2" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1 asc");
        Assert.That(actualData, Is.EqualTo(new List<string> { "-2", "1", "1", "3.1" }));
    }

    [Test]
    public void SortStringsTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<string> tableRows = ["red", "blue", "orange", "white", "black"];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<string> actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1");
        Assert.That(actualData, Is.EqualTo(new List<string> { "white", "red", "orange", "blue", "black" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1 desc");
        Assert.That(actualData, Is.EqualTo(new List<string> { "white", "red", "orange", "blue", "black" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1 asc");
        Assert.That(actualData, Is.EqualTo(new List<string> { "black", "blue", "orange", "red", "white" }));
    }

    [Test]
    public void SortIntNumbersTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<int> tableRows = [5, -1, 10, 0, 6];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<int> actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1");
        Assert.That(actualData, Is.EqualTo(new List<int> { 10, 6, 5, 0, -1 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1 desc");
        Assert.That(actualData, Is.EqualTo(new List<int> { 10, 6, 5, 0, -1 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1 asc");
        Assert.That(actualData, Is.EqualTo(new List<int> { -1, 0, 5, 6, 10 }));
    }

    [Test]
    public void SortDoubleNumbersTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<double> tableRows = [5.2, -1, 10.3, 0, 6.3];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<double> actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1");
        Assert.That(actualData, Is.EqualTo(new List<double> { 10.3, 6.3, 5.2, 0, -1 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1 desc");
        Assert.That(actualData, Is.EqualTo(new List<double> { 10.3, 6.3, 5.2, 0, -1 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1 asc");
        Assert.That(actualData, Is.EqualTo(new List<double> { -1, 0, 5.2, 6.3, 10.3 }));
    }

    [Test]
    public void SortBoolTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<bool> tableRows = [true, false, false, true];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<bool> actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1");
        Assert.That(actualData, Is.EqualTo(new List<bool> { true, true, false, false }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1 desc");
        Assert.That(actualData, Is.EqualTo(new List<bool> { true, true, false, false }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | sort by column1 asc");
        Assert.That(actualData, Is.EqualTo(new List<bool> { false, false, true, true }));
    }
}