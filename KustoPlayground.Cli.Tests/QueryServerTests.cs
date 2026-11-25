using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KustoPlayground.Cli.Tests;

public class QueryServerTests
{
    [Test]
    public async Task SdkClientCanDiscoverQueryAndRecoverFromToolErrorsInTheSameSession()
    {
        var requests = new Pipe();
        var responses = new Pipe();
        using var errors = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var server = Command.RunAsync(["--csv", "City,Score\nOslo,3", "--mcp-server"],
            requests.Reader.AsStream(), responses.Writer.AsStream(), errors);
        try
        {
            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(requests.Writer.AsStream(), responses.Reader.AsStream()), cancellationToken: token);
            var tools = await client.ListToolsAsync(cancellationToken: token);
            Assert.That(tools.Select(tool => tool.Name), Is.EqualTo(new[] { "query" }));

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
        }
        finally
        {
            await requests.Writer.CompleteAsync();
            Assert.That(await server.WaitAsync(token), Is.Zero);
        }
        var logs = Encoding.UTF8.GetString(errors.ToArray());
        Assert.That(logs, Does.Contain("Starting").And.Contain("Query completed").And.Not.Contain("Oslo"));
    }
}
