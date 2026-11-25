using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KustoPlayground.Cli.Tests;

public class QueryServerTests
{
    private static async Task<string> WithServerAsync(Func<McpClient, CancellationToken, Task> run, params string[] inputs)
    {
        var requests = new Pipe();
        var responses = new Pipe();
        using var errors = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var server = Command.RunAsync(["--mcp-server", .. inputs],
            requests.Reader.AsStream(), responses.Writer.AsStream(), errors);
        try
        {
            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(requests.Writer.AsStream(), responses.Reader.AsStream()), cancellationToken: token);
            await run(client, token);
        }
        finally
        {
            await requests.Writer.CompleteAsync();
            Assert.That(await server.WaitAsync(token), Is.Zero);
        }
        return Encoding.UTF8.GetString(errors.ToArray());
    }

    [Test]
    public async Task SdkClientCanDiscoverToolsAndRecoverFromQueryErrorsInTheSameSession()
    {
        var logs = await WithServerAsync(async (client, token) =>
        {
            var tools = await client.ListToolsAsync(cancellationToken: token);
            Assert.That(tools.Select(tool => tool.Name), Is.EquivalentTo(new[] { "query", "add_table_from_json", "add_table_from_csv" }));

            async Task<CallToolResult> Call(object? argument) => await client.CallToolAsync("query",
                new Dictionary<string, object?> { ["query"] = argument }, cancellationToken: token);
            Assert.That((await Call(42)).IsError, Is.True);
            var badQuery = await Call("MissingData | count");
            Assert.That(badQuery.IsError, Is.True);
            Assert.That(badQuery.StructuredContent!.Value.GetProperty("message").GetString(), Does.Contain("MissingData"));

            var goodQuery = await Call("Input | project City, Twice=Score*2");
            Assert.That(goodQuery.IsError, Is.Not.True);
            var text = ((TextContentBlock)goodQuery.Content.Single()).Text;
            using var document = JsonDocument.Parse(text);
            Assert.That(JsonElement.DeepEquals(document.RootElement, goodQuery.StructuredContent!.Value), Is.True);
            var items = document.RootElement.GetProperty("results").GetProperty("items");
            Assert.That(items[1].GetRawText(), Is.EqualTo("""["Oslo",6]"""));
        }, "--csv", "City,Score\nOslo,3");
        Assert.That(logs, Does.Contain("Starting").And.Contain("Query completed").And.Not.Contain("Oslo"));
    }

    [TestCase("json", false)]
    [TestCase("json", true)]
    [TestCase("jsonPath", false)]
    [TestCase("csv", false)]
    [TestCase("csvPath", false)]
    public async Task LiveImportMakesTypedRowsAvailableForSubsequentQueries(string source, bool serializedJson)
    {
        const string definition = """{"Name":"Scores","Columns":[{"Name":"Points","Type":"int"}],"Rows":[{"Points":17}]}""";
        const string csv = "Points\n17";
        bool isCsv = source is "csv" or "csvPath";
        var args = new Dictionary<string, object?>();
        if (isCsv) args["tableName"] = "Scores";
        string? path = null;
        try
        {
            if (source is "csvPath" or "jsonPath")
            {
                path = Path.Combine(Path.GetTempPath(), $"live scores {Guid.NewGuid():N}");
                await File.WriteAllTextAsync(path, isCsv ? csv : definition);
                args[source] = path;
            }
            else if (isCsv) args[source] = csv;
            else args[source] = serializedJson ? definition : JsonElement.Parse(definition);

            var logs = await WithServerAsync(async (client, token) =>
            {
                var result = await client.CallToolAsync(isCsv ? "add_table_from_csv" : "add_table_from_json", args, cancellationToken: token);
                Assert.That(result.IsError, Is.Not.True);
                var receipt = result.StructuredContent!.Value;
                Assert.That(receipt.GetProperty("results").GetProperty("tableName").GetString(), Is.EqualTo("Scores"));
                Assert.That(JsonElement.DeepEquals(receipt, JsonElement.Parse(((TextContentBlock)result.Content.Single()).Text)), Is.True);
                if (path is not null) File.Delete(path);
                var query = await client.CallToolAsync("query", new Dictionary<string, object?> { ["query"] = "Scores | project Twice=Points*2" }, cancellationToken: token);
                Assert.That(query.StructuredContent!.Value.GetProperty("results").GetProperty("items")[1][0].GetInt64(), Is.EqualTo(34));
            });
            Assert.That(logs, Does.Contain("Table import completed").And.Not.Contain("Points"));
        }
        finally { if (path is not null) File.Delete(path); }
    }

    [TestCase("add_table_from_json", "{}")]
    [TestCase("add_table_from_json", """{"json":{},"jsonPath":"unused"}""")]
    [TestCase("add_table_from_json", """{"json":[]}""")]
    [TestCase("add_table_from_json", """{"jsonPath":" "}""")]
    [TestCase("add_table_from_json", """{"other":"value"}""")]
    [TestCase("add_table_from_csv", """{"csv":"N\n1"}""")]
    [TestCase("add_table_from_csv", """{"tableName":" ","csv":"N\n1"}""")]
    [TestCase("add_table_from_csv", """{"tableName":"T","csv":"N\n1","csvPath":"unused"}""")]
    [TestCase("add_table_from_csv", """{"tableName":"T","csvPath":42}""")]
    public async Task InvalidImportArgumentsAreToolErrors(string tool, string arguments)
    {
        await WithServerAsync(async (client, token) =>
        {
            var args = JsonElement.Parse(arguments).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
            var result = await client.CallToolAsync(tool, args, cancellationToken: token);
            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent!.Value.GetProperty("message").GetString(), Does.StartWith("Expected"));
        });
    }

    [Test]
    public async Task FailedImportsDoNotReserveANameOrPublishAPartialTable()
    {
        await WithServerAsync(async (client, token) =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var missing = await client.CallToolAsync("add_table_from_json", new Dictionary<string, object?> { ["jsonPath"] = path }, cancellationToken: token);
            Assert.That(missing.IsError, Is.True);
            Assert.That(missing.StructuredContent!.Value.GetProperty("message").GetString(), Does.Contain("Cannot read input file"));

            async Task<CallToolResult> Add(string json) => await client.CallToolAsync("add_table_from_json",
                new Dictionary<string, object?> { ["json"] = JsonElement.Parse(json) }, cancellationToken: token);
            var invalid = await Add("""{"Name":"T","Columns":[{"Name":"N","Type":"int"}],"Rows":[{"N":11},{}]}""");
            Assert.That(invalid.IsError, Is.True);
            Assert.That(invalid.StructuredContent!.Value.GetProperty("message").GetString(), Does.Contain("Invalid table input"));
            Assert.That((await Add("""{"Name":"T","Columns":[{"Name":"N","Type":"int"}],"Rows":[{"N":5}]}""")).IsError, Is.Not.True);
            var result = await client.CallToolAsync("query", new Dictionary<string, object?> { ["query"] = "T | summarize Total=sum(N)" }, cancellationToken: token);
            Assert.That(result.StructuredContent!.Value.GetProperty("results").GetProperty("items")[1][0].GetInt64(), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task ConcurrentAddsOfOneNameKeepOnlyTheSuccessfulCallersRows()
    {
        await WithServerAsync(async (client, token) =>
        {
            async Task<CallToolResult> Add(string csv) => await client.CallToolAsync("add_table_from_csv",
                new Dictionary<string, object?> { ["tableName"] = "Scores", ["csv"] = csv }, cancellationToken: token);
            var results = await Task.WhenAll(Add("N\n1"), Add("N\n2"));
            var failed = results.Single(r => r.IsError == true);
            Assert.That(failed.StructuredContent!.Value.GetProperty("message").GetString(), Does.Contain("Duplicate table name: Scores"));
            var result = await client.CallToolAsync("query", new Dictionary<string, object?> { ["query"] = "Scores | project N" }, cancellationToken: token);
            var items = result.StructuredContent!.Value.GetProperty("results").GetProperty("items");
            Assert.That(items.GetArrayLength(), Is.EqualTo(2));
            Assert.That(items[1][0].GetInt32(), Is.EqualTo(results[0].IsError == true ? 2 : 1));
        });
    }
}
