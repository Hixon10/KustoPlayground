namespace KustoPlayground.Core.Tests;

public class WhereSmokeTests
{
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

    [Test]
    [Description("Weird test, but it replicates the original Kusto behaviour")]
    public void WhereStringNumbersSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<string> tableRows = ["1", "-2", "1", "3.1"];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<string> actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == \"orange\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string>()));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == \"1\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "1", "1" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == 1");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "1", "1" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 != \"1\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "-2", "3.1" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 != 1");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "-2", "3.1" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == \"3.1\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "3.1" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == 3.1");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "3.1" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 != \"3.1\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "1", "-2", "1" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 != 3.1");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "1", "-2", "1" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == \"-2\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "-2" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == -2");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "-2" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 != \"-2\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "1", "3.1", "1" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 != -2");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "1", "3.1", "1" }));
    }

    [Test]
    public void WhereStringTypeSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<string> tableRows = ["green", "red", "blue", "red"];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<string> actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == \"orange\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string>()));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == \"red\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "red", "red" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 != \"red\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "green", "blue" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == \"green\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "green" }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 != \"green\"");
        Assert.That(actualData, Is.EquivalentTo(new List<string> { "red", "blue", "red" }));
    }

    [Test]
    public void WhereIntTypeSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        HashSet<int> tableRows = [1, 2, 3, 4, 5, 6];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<int> actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 > 3");
        Assert.That(actualData, Is.EquivalentTo(new List<int> { 4, 5, 6 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 3");
        Assert.That(actualData, Is.EquivalentTo(new List<int> { 3, 4, 5, 6 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 == 3");
        Assert.That(actualData, Is.EquivalentTo(new List<int> { 3 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 != 3");
        Assert.That(actualData, Is.EquivalentTo(new List<int> { 1, 2, 4, 5, 6 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < 3");
        Assert.That(actualData, Is.EquivalentTo(new List<int> { 1, 2 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= 3");
        Assert.That(actualData, Is.EquivalentTo(new List<int> { 1, 2, 3 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < 7");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= 7");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > 0");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > +0");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= +10");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 0");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > -1");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= -1");
        Assert.That(actualData, Is.EquivalentTo(tableRows));
    }

    [Test]
    public void WhereLongTypeSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        HashSet<long> tableRows = [1L, 2L, 3L, 4L, 5L, 6L];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<long> actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 > 3");
        Assert.That(actualData, Is.EquivalentTo(new List<long> { 4L, 5L, 6L }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 3");
        Assert.That(actualData, Is.EquivalentTo(new List<long> { 3L, 4L, 5L, 6L }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 == 3");
        Assert.That(actualData, Is.EquivalentTo(new List<long> { 3L }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 != 3");
        Assert.That(actualData, Is.EquivalentTo(new List<long> { 1L, 2L, 4L, 5L, 6L }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < 3");
        Assert.That(actualData, Is.EquivalentTo(new List<long> { 1L, 2L }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= 3");
        Assert.That(actualData, Is.EquivalentTo(new List<long> { 1L, 2L, 3L }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < 7");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= 7");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > 0");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > +0");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= +10");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 0");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > -1");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= -1");
        Assert.That(actualData, Is.EquivalentTo(tableRows));
    }

    [Test]
    public void WhereDoubleTypeSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        HashSet<double> tableRows = [1.1D, 1.2D, 3D, 4D, 5.0D, 6];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<double> actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 > 3");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 4D, 5D, 6D }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 3");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 3D, 4D, 5D, 6D }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 == 3");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 3D }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 != 3");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 1.1D, 1.2D, 4D, 5D, 6D }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < 3");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 1.1D, 1.2D }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= 3");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 1.1D, 1.2D, 3D }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= 1.2");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 1.1D, 1.2D }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < 1.2");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 1.1D }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > 1.1");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 1.2D, 3D, 4D, 5.0D, 6 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 1.1");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 1.1D, 1.2D, 3D, 4D, 5.0D, 6 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 1.101");
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 1.2D, 3D, 4D, 5.0D, 6 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < +8.3");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= +8.3");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < 7");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= 7");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > 1");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 1");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 0");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= -0.42");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > -0.42");
        Assert.That(actualData, Is.EquivalentTo(tableRows));
    }

    [Test]
    public void WhereDecimalTypeSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        HashSet<decimal> tableRows = [1.1m, 1.2m, 3m, 4m, 5.0m, 6];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<decimal> actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 > 3");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 4m, 5m, 6m }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 3");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 3m, 4m, 5m, 6m }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 == 3");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 3m }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 != 3");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 1.1m, 1.2m, 4m, 5m, 6m }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < 3");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 1.1m, 1.2m }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= 3");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 1.1m, 1.2m, 3m }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= 1.2");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 1.1m, 1.2m }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < 1.2");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 1.1m }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > 1.1");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 1.2m, 3m, 4m, 5.0m, 6 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 1.1");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 1.1m, 1.2m, 3m, 4m, 5.0m, 6 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 1.101");
        Assert.That(actualData, Is.EquivalentTo(new List<decimal> { 1.2m, 3m, 4m, 5.0m, 6 }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < +8.3");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= +8.3");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < 7");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= 7");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > 1");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 1");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= 0");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= -0.42");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<decimal>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > -0.42");
        Assert.That(actualData, Is.EquivalentTo(tableRows));
    }

    [Test]
    public void WhereBoolTypeSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<bool> tableRows = [true, false, false, true, true];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        List<bool> actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table),
            kustoDatabase,
            "table1 | where column1 == false");
        Assert.That(actualData, Is.EquivalentTo(new List<bool> { false, false }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 == true");
        Assert.That(actualData, Is.EquivalentTo(new List<bool> { true, true, true }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1");
        Assert.That(actualData, Is.EquivalentTo(new List<bool> { true, true, true }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 != false");
        Assert.That(actualData, Is.EquivalentTo(new List<bool> { true, true, true }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 != true");
        Assert.That(actualData, Is.EquivalentTo(new List<bool> { false, false }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > true");
        Assert.That(actualData, Is.EquivalentTo(new List<bool>()));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < false");
        Assert.That(actualData, Is.EquivalentTo(new List<bool>()));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= false");
        Assert.That(actualData, Is.EquivalentTo(new List<bool> { false, false }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 > false");
        Assert.That(actualData, Is.EquivalentTo(new List<bool> { true, true, true }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 < true");
        Assert.That(actualData, Is.EquivalentTo(new List<bool> { false, false }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 <= true");
        Assert.That(actualData, Is.EquivalentTo(tableRows));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= true");
        Assert.That(actualData, Is.EquivalentTo(new List<bool> { true, true, true }));

        actualData = TestUtils.ExecuteAndGetDataForOneColumn<bool>(TestUtils.GetColumnNane(table), kustoDatabase,
            "table1 | where column1 >= false");
        Assert.That(actualData, Is.EquivalentTo(tableRows));
    }
}