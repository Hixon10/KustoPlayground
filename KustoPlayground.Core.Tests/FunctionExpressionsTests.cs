namespace KustoPlayground.Core.Tests;

public class FunctionExpressionsTests
{
    [Test]
    [Description("UrlEncode - Encode strings with special characters")]
    public void UrlEncode_StringColumn()
    {
        var kustoDatabase = new KustoDatabase();
        List<string> tableRows = new() { "hello world", "a+b=c", "email@test.com", "https://www.bing.com/hello world" };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName: columnName, tableName: "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | extend column1 = url_encode(column1)");

        var expectedData = new List<string>
        {
            "hello+world",
            "a%2bb%3dc",
            "email%40test.com",
            "https%3a%2f%2fwww.bing.com%2fhello+world"
        };

        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("UrlDecode - Decode encoded strings back to original")]
    public void UrlDecode_StringColumn()
    {
        var kustoDatabase = new KustoDatabase();
        List<string> tableRows = new()
            { "hello+world", "a%2bb%3dc", "email%40test.com", "https%3a%2f%2fwww.bing.com%2f" };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName: columnName, tableName: "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | extend column1 = url_decode(column1)");

        var expectedData = new List<string>
        {
            "hello world",
            "a+b=c",
            "email@test.com",
            "https://www.bing.com/"
        };

        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    [Description("UrlEncode/Decode - Roundtrip test")]
    public void UrlEncodeDecode_Roundtrip()
    {
        var kustoDatabase = new KustoDatabase();
        List<string> tableRows = new() { "spaces here", "a+b=c", "https://example.com/query?q=test value" };
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName: columnName, tableName: "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            columnName,
            kustoDatabase,
            "table1 | extend column1 = url_decode(url_encode(column1))");

        Assert.That(actualData, Is.EquivalentTo(tableRows));
    }

    [Test]
    public void Base64DecodeToStringSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        List<string> tableRows =
        [
            "cmVkIGNvbG9y",
            "Ymx1ZSBjb2xvcg=="
        ];
        const string columnName = "column1";
        Table table = TestUtils.GenerateTableWithColumn(
            tableRows, tableName: "table1", columnName: columnName);
        kustoDatabase.AddTable(table);

        var results = kustoDatabase.ExecuteQuery(
            "table1 | extend DecodedStr = base64_decode_tostring(column1)");
        Assert.That(results.ExecutionErrors, Is.Null);

        Dictionary<string, string> expectedBase64Decode = new Dictionary<string, string>()
        {
            { "cmVkIGNvbG9y", "red color" },
            { "Ymx1ZSBjb2xvcg==", "blue color" }
        };

        foreach (IReadOnlyDictionary<string, object?> row in results.ResultRows!)
        {
            string column1 = (string)row["column1"]!;
            Assert.That((string)row["DecodedStr"]!, Is.EqualTo(expectedBase64Decode[column1]));
        }
    }
}