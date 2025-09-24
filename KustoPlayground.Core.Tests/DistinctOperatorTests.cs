namespace KustoPlayground.Core.Tests;

public class DistinctOperatorTests
{
    [Test]
    [Description("Distinct - Single int column")]
    public void Distinct_IntColumn()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> tableRows = new() { 1, 2, 2, 3, 3, 3, 4 };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            "table1 | distinct column1");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 1, 2, 3, 4 }));
    }


    [Test]
    [Description("Distinct - Single string column")]
    public void Distinct_StringColumn()
    {
        var kustoDatabase = new KustoDatabase();
        List<string> tableRows = new() { "apple", "banana", "apple", "pear", "pear" };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName: columnName, tableName: "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | distinct column1");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "apple", "banana", "pear" }));
    }

    [Test]
    [Description("Distinct - Two columns (int, string)")]
    public void Distinct_TwoColumns()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(int, string)>
        {
            (1, "a"), (1, "a"), (2, "b"), (2, "b"), (2, "c"), (3, "d")
        };
        Table table = TestUtils.GenerateTableWith2Columns(rows,
            columnName1: "col1", 
            columnName2: "col2",
            tableName: "table1");
        kustoDatabase.AddTable(table);

        List<(int, string)> actualData = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            columnName1: "col1", 
            columnName2: "col2",
            kustoDatabase,
            query: "table1 | distinct col1, col2");

        var expected = new List<(int, string)>
        {
            (1, "a"),
            (2, "b"),
            (2, "c"),
            (3, "d")
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Distinct - Three columns (int, double, string) with duplicates")]
    public void Distinct_ThreeColumns()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(int, double, string)>
        {
            (1, 1.1, "x"),
            (1, 1.1, "x"),
            (2, 2.2, "y"),
            (3, 3.3, "z"),
            (3, 3.3, "z")
        };
        Table table = TestUtils.GenerateTableWith3Columns(rows,
            "col1", "col2", "col3",
            "table1");
        kustoDatabase.AddTable(table);

        List<(int, double, string)> actualData = TestUtils.ExecuteAndGetDataFor3Columns<int, double, string>(
            "col1", "col2", "col3",
            kustoDatabase,
            "table1 | distinct col1, col2, col3");

        var expected = new List<(int, double, string)>
        {
            (1, 1.1, "x"),
            (2, 2.2, "y"),
            (3, 3.3, "z")
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Distinct - Four columns including DateTime")]
    public void Distinct_FourColumns()
    {
        var kustoDatabase = new KustoDatabase();
        DateTime d1 = new DateTime(2020, 1, 1);
        DateTime d2 = new DateTime(2020, 1, 2);
        var rows = new List<(int, long, double, DateTime)>
        {
            (1, 10L, 1.1, d1),
            (1, 10L, 1.1, d1),
            (2, 20L, 2.2, d2)
        };
        Table table = TestUtils.GenerateTableWith4Columns(
            rows,
            "col1", "col2", "col3", "col4",
            "table1");
        kustoDatabase.AddTable(table);

        List<(int, long, double, DateTime)> actualData =
            TestUtils.ExecuteAndGetDataFor4Columns<int, long, double, DateTime>(
                "col1", "col2", "col3", "col4",
                kustoDatabase,
                "table1 | distinct col1, col2, col3, col4");

        var expected = new List<(int, long, double, DateTime)>
        {
            (1, 10L, 1.1, d1),
            (2, 20L, 2.2, d2)
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Distinct - With where clause")]
    public void Distinct_WithWhere()
    {
        var kustoDatabase = new KustoDatabase();
        List<int> tableRows = new() { 1, 2, 2, 3, 3, 3, 4 };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows,
            columnName,
            "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            "table1 | where column1 > 2 | distinct column1");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 3, 4 }));
    }

    [Test]
    [Description("Distinct - All columns using *")]
    public void Distinct_AllColumnsWithAsterisk()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(int, string)>
        {
            (1, "a"),
            (1, "a"),
            (2, "b"),
            (2, "b")
        };
        Table table = TestUtils.GenerateTableWith2Columns(rows,
            "col1", "col2",
            "table1");
        kustoDatabase.AddTable(table);

        List<(int, string)> actualData = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            "col1", "col2",
            kustoDatabase,
            "table1 | distinct *");

        var expected = new List<(int, string)>
        {
            (1, "a"),
            (2, "b")
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Tests distinct on a single integer column with duplicate values.")]
    public void Distinct_OnSingleIntColumn_RemovesDuplicates()
    {
        var kustoDatabase = new KustoDatabase();
        var tableData = new List<int> { 1, 5, 2, 1, 8, 5, 1, 3 };
        Table table = TestUtils.GenerateTableWithColumn(tableData, "IntCol", "Numbers");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "Numbers | distinct IntCol");

        var expectedData = new List<int> { 1, 2, 3, 5, 8 };
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    //////////////////
    [Test]
    [Description("Tests distinct on a single string column, which should be case-sensitive.")]
    public void Distinct_OnSingleStringColumn_RemovesDuplicates()
    {
        var kustoDatabase = new KustoDatabase();
        var tableData = new List<string> { "apple", "Banana", "apple", "cherry", "Banana", "Apple" };
        Table table = TestUtils.GenerateTableWithColumn(tableData, "StringCol", "Fruits");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "Fruits | distinct StringCol");

        var expectedData = new List<string> { "apple", "Banana", "cherry", "Apple" };
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("Tests distinct on a single DateTime column.")]
    public void Distinct_OnSingleDateTimeColumn_RemovesDuplicates()
    {
        var dt1 = new DateTime(2025, 1, 15);
        var dt2 = new DateTime(2025, 3, 30);
        var dt3 = new DateTime(2025, 5, 20);

        var kustoDatabase = new KustoDatabase();
        var tableData = new List<DateTime> { dt1, dt2, dt1, dt3, dt2, dt1 };
        Table table = TestUtils.GenerateTableWithColumn(tableData, "DateCol", "Events");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<DateTime>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "Events | distinct DateCol");

        var expectedData = new List<DateTime> { dt1, dt2, dt3 };
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("Tests distinct on a table that is already unique.")]
    public void Distinct_OnAlreadyUniqueColumn_ReturnsSameData()
    {
        var kustoDatabase = new KustoDatabase();
        var tableData = new List<long> { 100L, 200L, 300L, 400L };
        Table table = TestUtils.GenerateTableWithColumn(tableData, "LongCol", "UniqueLogs");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "UniqueLogs | distinct LongCol");

        Assert.That(actualData, Is.EquivalentTo(tableData));
    }

    [Test]
    [Description("Tests distinct on an empty table, which should produce an empty result.")]
    public void Distinct_OnEmptyTable_ReturnsEmptyTable()
    {
        var kustoDatabase = new KustoDatabase();

        Table table = TestUtils.GenerateTableWithColumn(new List<int>(), "IntCol", "EmptyTable");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table),
            kustoDatabase,
            "EmptyTable | distinct IntCol");

        Assert.That(actualData, Is.Empty);
    }

    [Test]
    [Description("Tests distinct on two columns (int, string).")]
    public void Distinct_OnTwoColumns_RemovesDuplicatePairs()
    {
        var kustoDatabase = new KustoDatabase();

        var rows = new List<(int, string)>
        {
            (1, "A"), (2, "B"), (1, "A"), (1, "C"), (2, "B")
        };

        Table table = TestUtils.GenerateTableWith2Columns(rows,
            "Id", "Name",
            "MyTable");
        kustoDatabase.AddTable(table);

        List<(int, string)> actualData = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            "Id", "Name",
            kustoDatabase,
            "MyTable | distinct Id, Name");

        var expected = new List<(int, string)>
        {
            (1, "A"),
            (2, "B"),
            (1, "C")
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

     [Test]
    [Description("Tests distinct on three columns (int, double, DateTime).")]
    public void Distinct_OnThreeColumns_RemovesDuplicateTriplets()
    {
        var dt1 = new DateTime(2025, 1, 1);
        var dt2 = new DateTime(2025, 2, 2);
        
        var rows = new List<(int, double, DateTime)>
        {
            (1, 10.5, dt1),
            (2, 20.0, dt1),
            (1, 10.5, dt2),
            (1, 10.5, dt1), // Duplicate
            (2, 20.0, dt1)  // Duplicate
        };
        
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(rows, "Id", "Value", "Timestamp", "SensorReadings");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<int, double, DateTime>(
            "Id", "Value", "Timestamp",
            kustoDatabase,
            "SensorReadings | distinct Id, Value, Timestamp");

        var expected = new List<(int, double, DateTime)>
        {
            (1, 10.5, dt1),
            (2, 20.0, dt1),
            (1, 10.5, dt2)
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }
    
    [Test]
    [Description("Tests distinct on four columns of various types.")]
    public void Distinct_OnFourColumns_RemovesDuplicateQuadruplets()
    {
        var dt = new DateTime(2025, 9, 19);
        var rows = new List<(int, string, long, DateTime)>
        {
            (1, "X", 100L, dt),
            (1, "Y", 100L, dt),
            (2, "X", 200L, dt),
            (1, "X", 100L, dt), // Duplicate
            (1, "X", 300L, dt)
        };

        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith4Columns(rows, "ColA", "ColB", "ColC", "ColD", "WideTable");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor4Columns<int, string, long, DateTime>(
            "ColA", "ColB", "ColC", "ColD",
            kustoDatabase,
            "WideTable | distinct ColA, ColB, ColC, ColD");

        var expected = new List<(int, string, long, DateTime)>
        {
            (1, "X", 100L, dt),
            (1, "Y", 100L, dt),
            (2, "X", 200L, dt),
            (1, "X", 300L, dt)
        };
        
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Tests distinct with a wildcard (*) to denote all columns.")]
    public void Distinct_WithWildcard_RemovesDuplicateRows()
    {
        var rows = new List<(int, string, double)>
        {
            (1, "A", 1.1),
            (2, "B", 2.2),
            (1, "A", 1.1), // Duplicate row
            (1, "C", 3.3)
        };

        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(rows, "IntCol", "StringCol", "DoubleCol", "WildcardTest");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<int, string, double>(
            "IntCol", "StringCol", "DoubleCol",
            kustoDatabase,
            "WildcardTest | distinct *");

        var expected = new List<(int, string, double)>
        {
            (1, "A", 1.1),
            (2, "B", 2.2),
            (1, "C", 3.3)
        };
        
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("Tests distinct on two columns after a where clause has filtered the rows.")]
    public void Distinct_OnTwoColumns_AfterWhereClause()
    {
        var rows = new List<(string, string, int)>
        {
            ("G1", "S1", 100),
            ("G1", "S2", 50 ), // Will be filtered out
            ("G2", "S1", 150),
            ("G1", "S1", 200), // Duplicate (G1, S1) after filter
            ("G2", "S2", 300)
        };
        
        var kustoDatabase = new KustoDatabase();
        Table table = TestUtils.GenerateTableWith3Columns(rows, "Group", "SubGroup", "Value", "ComplexFilter");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "Group", "SubGroup",
            kustoDatabase,
            "ComplexFilter | where Value >= 100 | distinct Group, SubGroup");

        var expected = new List<(string, string)>
        {
            ("G1", "S1"),
            ("G2", "S1"),
            ("G2", "S2")
        };
        
        Assert.That(actualData, Is.EquivalentTo(expected));
    }
}