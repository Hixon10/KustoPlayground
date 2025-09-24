namespace KustoPlayground.Core.Tests;

public class InOperatorTests
{
    [Test]
    [Description("in operator - simple match")]
    public void InOperator_SimpleMatchTwoValues()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "apple", "banana", "pear", "orange" };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | where col1 in ('apple', 'pear')");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "apple", "pear" }));
    }

    [Test]
    [Description("in operator - simple match")]
    public void InOperator_SimpleMatchOneValue()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "apple", "banana", "pear", "orange" };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | where col1 in ('apple')");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "apple" }));
    }

    [Test]
    [Description("!in operator - exclude values")]
    public void NotInOperator_ExcludesValues()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "apple", "banana", "pear", "orange" };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | where col1 !in ('apple', 'pear')");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "banana", "orange" }));
    }

    [Test]
    [Description("in~ operator - case-insensitive match")]
    public void InTildeOperator_CaseInsensitiveMatch()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "Apple", "banana", "Pear", "Orange" };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | where col1 in~ ('apple', 'pear')");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "Apple", "Pear" }));
    }

    [Test]
    [Description("!in~ operator - case-insensitive exclude")]
    public void NotInTildeOperator_CaseInsensitiveExclude()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "Apple", "banana", "Pear", "Orange" };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | where col1 !in~ ('apple', 'pear')");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "banana", "Orange" }));
    }

    [Test]
    [Description("in operator - with numeric column")]
    public void InOperator_WithNumbers()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<int> { 1, 2, 3, 4, 5 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(rows, columnName, "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            "table1 | where col1 in (2, 4)");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 2, 4 }));
    }

    [Test]
    [Description("in operator - filtered input before applying in")]
    public void InOperator_WithFilteredInput()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<int> { 1, 2, 3, 4, 5, 6 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(rows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // First filter input rows to values > 2, then apply in
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            "table1 | where col1 > 2 | where col1 in (table1 | where col1 % 2 == 0 | project col1)");

        // Rows > 2 are {3,4,5,6}, and even numbers from table are {2,4,6}
        // Intersection = {4,6}
        Assert.That(actualData, Is.EquivalentTo(new List<int> { 4, 6 }));
    }

    [Test]
    [Description("in operator with two-column table, filtering on one column and applying in")]
    public void InOperator_TwoColumnTable_WithFilter()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(int, string)>
        {
            (1, "apple"),
            (2, "banana"),
            (3, "pear"),
            (4, "orange"),
            (5, "apple")
        };

        Table table = TestUtils.GenerateTableWith2Columns(
            rows,
            columnName1: "col1",
            columnName2: "col2",
            tableName: "table1");

        kustoDatabase.AddTable(table);

        // First filter by col2 == "apple", then apply in on col1 using even numbers
        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            columnName1: "col1",
            columnName2: "col2",
            kustoDatabase,
            "table1 | where col2 == 'apple' | where col1 in (table1 | where col1 % 2 == 0 | project col1)");

        // Rows with col2 == "apple": (1,"apple"), (5,"apple")
        // Even numbers in table: (2,"banana"), (4,"orange")
        // None match -> empty result
        Assert.That(actualData, Is.Empty);
    }

    [Test]
    [Description("!in operator with two-column table, projecting one column for comparison")]
    public void NotInOperator_TwoColumnTable_WithProjection()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(int, string)>
        {
            (1, "apple"),
            (2, "banana"),
            (3, "pear"),
            (4, "orange"),
            (5, "apple")
        };

        Table table = TestUtils.GenerateTableWith2Columns(
            rows,
            columnName1: "col1",
            columnName2: "col2",
            tableName: "table1");

        kustoDatabase.AddTable(table);

        // Filter col2 == "apple", then exclude col1 values found in subquery (even numbers)
        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            columnName1: "col1",
            columnName2: "col2",
            kustoDatabase,
            "table1 | where col2 == 'apple' | where col1 !in (table1 | where col1 % 2 == 0 | project col1)");

        // Rows with col2 == "apple": (1,"apple"), (5,"apple")
        // Even numbers: {2,4}
        // Neither 1 nor 5 is in {2,4} -> keep both
        var expected = new List<(int, string)>
        {
            (1, "apple"),
            (5, "apple")
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("in~ operator with two-column table, case-insensitive string match")]
    public void InTildeOperator_TwoColumnTable()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(int, string)>
        {
            (1, "Apple"),
            (2, "Banana"),
            (3, "Pear"),
            (4, "Orange")
        };

        Table table = TestUtils.GenerateTableWith2Columns(
            rows,
            columnName1: "col1",
            columnName2: "col2",
            tableName: "table1");

        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            columnName1: "col1",
            columnName2: "col2",
            kustoDatabase,
            "table1 | where col2 in~ ('apple','pear')");

        var expected = new List<(int, string)>
        {
            (1, "Apple"),
            (3, "Pear")
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("!in operator - filtered input before applying not in")]
    public void NotInOperator_WithFilteredInput()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<int> { 1, 2, 3, 4, 5, 6 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(rows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // First filter input rows to values > 2, then exclude even numbers
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            "table1 | where col1 > 2 | where col1 !in (table1 | where col1 % 2 == 0 | project col1)");

        // Rows > 2 are {3,4,5,6}, evens are {2,4,6}, so after exclusion we keep {3,5}
        Assert.That(actualData, Is.EquivalentTo(new List<int> { 3, 5 }));
    }

    [Test]
    [Description("in operator with subquery - selects matching rows - double in")]
    public void InOperator_WithSubqueryWithIn()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "apple", "banana", "pear", "orange" };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Subquery returns only 'apple' and 'pear'
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | where col1 in (table1 | where col1 in ('apple','pear') | project col1)");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "apple", "pear" }));
    }

    [Test]
    [Description("in operator with subquery - selects matching rows")]
    public void InOperator_WithSubquery()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "apple", "banana", "pear", "orange" };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Subquery returns only 'apple' and 'pear'
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | where col1 in (table1 | where col1 contains 'an')");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "banana", "orange" }));
    }

    [Test]
    [Description("!in operator with subquery - excludes matching rows")]
    public void NotInOperator_WithSubquery()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "apple", "banana", "pear", "orange" };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Subquery returns 'apple' and 'pear', which are excluded
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | where col1 !in (table1 | where col1 in ('apple','pear') | project col1)");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "banana", "orange" }));
    }

    [Test]
    [Description("in~ operator with subquery - case-insensitive match")]
    public void InTildeOperator_WithSubquery()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "Apple", "banana", "Pear", "Orange" };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Subquery selects lowercase apple and pear, but in~ matches regardless of case
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | where col1 in (table1 | where col1 in~ ('apple','pear') | project col1)");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "Apple", "Pear" }));
    }

    [Test]
    [Description("!in~ operator with subquery - excludes case-insensitive matches")]
    public void NotInTildeOperator_WithSubquery()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "Apple", "banana", "Pear", "Orange" };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Subquery selects lowercase apple and pear, which are excluded (case-insensitive)
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | where col1 !in~ (table1 | where col1 in~ ('apple','pear') | project col1)");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "banana", "Orange" }));
    }

    [Test]
    [Description("in operator with numeric subquery - selects even numbers")]
    public void InOperator_WithNumericSubquery()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<int> { 1, 2, 3, 4, 5 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(rows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Subquery selects only even numbers (2, 4)
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            "table1 | where col1 in (table1 | where col1 % 2 == 0 | project col1)");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 2, 4 }));
    }

    [Test]
    [Description("!in operator with numeric subquery - excludes even numbers")]
    public void NotInOperator_WithNumericSubquery()
    {
        var kustoDatabase = new KustoDatabase();
        var rows = new List<long> { 1, 2, 3, 4, 5 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(rows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Subquery selects even numbers (2, 4), which are excluded
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            columnName,
            kustoDatabase,
            "table1 | where col1 !in (table1 | where col1 % 2 == 0 | project col1)");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 1, 3, 5 }));
    }

    ////
    [Test]
    [Description("in operator - Strings from a scalar list (case-sensitive)")]
    public void InOperator_StringScalarList_CaseSensitive()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "Apple", "apple", "Banana", "Cherry", "Date" };
        const string columnName = "fruit";
        var table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Act
        var query = $"table1 | where {columnName} in (\"Apple\", \"Cherry\")";
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            query);

        // Assert
        var expected = new List<string> { "Apple", "Cherry" };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("in operator - Integers from a scalar list")]
    public void InOperator_IntegerScalarList()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<int> { 10, 20, 30, 40, 50 };
        const string columnName = "id";
        var table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "inventory");
        kustoDatabase.AddTable(table);

        // Act
        var query = $"inventory | where {columnName} in (20, 40, 99)";
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            query);

        // Assert
        var expected = new List<int> { 20, 40 };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("in operator - Strings from a tabular subquery (case-sensitive)")]
    public void InOperator_TabularSubquery_CaseSensitive()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var mainTableRows = new List<string> { "Apple", "apple", "Banana", "Cherry" };
        var lookupTableRows = new List<string> { "Apple", "Cherry", "Durian" };
        var mainTable = TestUtils.GenerateTableWithColumn(mainTableRows, "fruit", "table1");
        var lookupTable = TestUtils.GenerateTableWithColumn(lookupTableRows, "fruit", "lookupTable");
        kustoDatabase.AddTable(mainTable);
        kustoDatabase.AddTable(lookupTable);

        // Act
        // The subquery `(lookupTable)` implicitly uses its first column.
        var query = "table1 | where fruit in (lookupTable)";
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "fruit",
            kustoDatabase,
            query);

        // Assert
        var expected = new List<string> { "Apple", "Cherry" };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("!in operator - Strings from a scalar list (case-sensitive)")]
    public void NotInOperator_StringScalarList_CaseSensitive()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "Apple", "apple", "Banana", "Cherry", "Date" };
        const string columnName = "fruit";
        var table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Act
        var query = $"table1 | where {columnName} !in (\"Apple\", \"Cherry\")";
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            query);

        // Assert
        var expected = new List<string> { "apple", "Banana", "Date" };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("!in operator - Strings from a tabular subquery (case-sensitive)")]
    public void NotInOperator_TabularSubquery_CaseSensitive()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var mainTableRows = new List<string> { "Apple", "apple", "Banana", "Cherry" };
        var lookupTableRows = new List<string> { "Apple", "Cherry", "Durian" };
        var mainTable = TestUtils.GenerateTableWithColumn(mainTableRows, "fruit", "table1");
        var lookupTable = TestUtils.GenerateTableWithColumn(lookupTableRows, "fruit", "lookupTable");
        kustoDatabase.AddTable(mainTable);
        kustoDatabase.AddTable(lookupTable);

        // Act
        var query = "table1 | where fruit !in (lookupTable)";
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "fruit",
            kustoDatabase,
            query);

        // Assert
        var expected = new List<string> { "apple", "Banana" };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("in~ operator - Strings from a scalar list (case-insensitive)")]
    public void InCaseInsensitiveOperator_StringScalarList()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "Apple", "apple", "Banana", "Cherry", "Date" };
        const string columnName = "fruit";
        var table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Act
        // Notice the list values have different casing than some of the table rows
        var query = $"table1 | where {columnName} in~ (\"apple\", \"CHERRY\")";
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            query);

        // Assert
        var expected = new List<string> { "Apple", "apple", "Cherry" };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("in~ operator - Strings from a tabular subquery (case-insensitive)")]
    public void InCaseInsensitiveOperator_TabularSubquery()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var mainTableRows = new List<string> { "Apple", "apple", "Banana", "Cherry" };
        var lookupTableRows = new List<string> { "APPLE", "cherry", "Durian" };
        var mainTable = TestUtils.GenerateTableWithColumn(mainTableRows, "fruit", "table1");
        var lookupTable = TestUtils.GenerateTableWithColumn(lookupTableRows, "fruit", "lookupTable");
        kustoDatabase.AddTable(mainTable);
        kustoDatabase.AddTable(lookupTable);

        // Act
        var query = "table1 | where fruit in~ (lookupTable)";
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "fruit",
            kustoDatabase,
            query);

        // Assert
        var expected = new List<string> { "Apple", "apple", "Cherry" };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("!in~ operator - Strings from a scalar list (case-insensitive)")]
    public void NotInCaseInsensitiveOperator_StringScalarList()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<string> { "Apple", "apple", "Banana", "Cherry", "Date" };
        const string columnName = "fruit";
        var table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        // Act
        var query = $"table1 | where {columnName} !in~ (\"apple\", \"CHERRY\")";
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            query);

        // Assert
        var expected = new List<string> { "Banana", "Date" };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("!in~ operator - Strings from a tabular subquery (case-insensitive)")]
    public void NotInCaseInsensitiveOperator_TabularSubquery()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var mainTableRows = new List<string> { "Apple", "apple", "Banana", "Cherry", "Date" };
        var lookupTableRows = new List<string> { "APPLE", "cherry", "Durian" };
        var mainTable = TestUtils.GenerateTableWithColumn(mainTableRows, "fruit", "table1");
        var lookupTable = TestUtils.GenerateTableWithColumn(lookupTableRows, "fruit", "lookupTable");
        kustoDatabase.AddTable(mainTable);
        kustoDatabase.AddTable(lookupTable);

        // Act
        var query = "table1 | where fruit !in~ (lookupTable)";
        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "fruit",
            kustoDatabase,
            query);

        // Assert
        var expected = new List<string> { "Banana", "Date" };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("in operator on a multi-column table, filtering on the string column")]
    public void InOperator_WithTwoColumnTable()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(int, string)>
        {
            (101, "Apple"),
            (102, "apple"), // Should be filtered out by case-sensitive 'in'
            (201, "Banana"),
            (301, "Cherry"),
            (401, "banana") // Should be filtered out
        };
        Table table = TestUtils.GenerateTableWith2Columns(rows,
            columnName1: "Id",
            columnName2: "Category",
            tableName: "Products");
        kustoDatabase.AddTable(table);

        // Act
        // The 'in' operator filters on the 'Category' column, but we retrieve both columns.
        var query = "Products | where Category in (\"Apple\", \"Cherry\")";

        List<(int, string)> actualData = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            columnName1: "Id",
            columnName2: "Category",
            kustoDatabase,
            query: query);

        // Assert
        var expected = new List<(int, string)>
        {
            (101, "Apple"),
            (301, "Cherry")
        };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("!in~ operator on a multi-column table (case-insensitive)")]
    public void NotInCaseInsensitive_WithTwoColumnTable()
    {
        // Arrange
        var kustoDatabase = new KustoDatabase();
        var rows = new List<(int, string)>
        {
            (101, "Apple"), // Will be filtered out
            (102, "apple"), // Will be filtered out
            (201, "Banana"), // Should remain
            (301, "CHERRY"), // Will be filtered out
            (401, "Date") // Should remain
        };
        Table table = TestUtils.GenerateTableWith2Columns(rows,
            columnName1: "Id",
            columnName2: "Category",
            tableName: "Products");
        kustoDatabase.AddTable(table);

        // Act
        // Filter out rows where Category is 'apple' or 'cherry' case-insensitively.
        var query = "Products | where Category !in~ (\"apple\", \"cherry\")";

        List<(int, string)> actualData = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            columnName1: "Id",
            columnName2: "Category",
            kustoDatabase,
            query: query);

        // Assert
        var expected = new List<(int, string)>
        {
            (201, "Banana"),
            (401, "Date")
        };
        Assert.That(actualData, Is.EquivalentTo(expected));
    }
}