namespace KustoPlayground.Core.Tests;

public class WhereStringFunctionsTests
{
    [Test]
    public void WhereStringStartsWithSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<string> tableRows = ["green", "Red", "blue", "red"];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<string> actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 startswith \"abc\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string>()));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 startswith \"ed\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string>()));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 startswith \"bluee\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string>()));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 !startswith \"bluee\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "green", "Red", "blue", "red" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 startswith \"R\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "Red", "red" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 startswith \"re\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "Red", "red" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 !startswith \"R\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "green", "blue" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 !startswith \"r\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "green", "blue" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 startswith \"blue\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "blue" }));
    }

    [Test]
    public void WhereStringContainsSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<string> tableRows = ["green", "Red", "blue", "red"];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<string> actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 contains \"abc\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string>()));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 !contains \"abc\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "green", "Red", "blue", "red" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 contains \"red\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "Red", "red" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 !contains \"red\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "green", "blue" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 !contains \"re\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "blue" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 contains \"re\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "green", "Red", "red" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 contains \"e\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "green", "Red", "blue", "red" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 contains \"RE\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "green", "Red", "red" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 !contains \"RE\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "blue" }));
    }
}