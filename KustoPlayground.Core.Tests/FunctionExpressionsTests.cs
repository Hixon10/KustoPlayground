namespace KustoPlayground.Core.Tests;

public class FunctionExpressionsTests
{
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