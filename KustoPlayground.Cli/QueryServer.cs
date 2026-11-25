using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KustoPlayground.Core;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KustoPlayground.Cli;

internal static class QueryServer
{
    private static readonly Tool QueryTool = new()
    {
        Name = "query",
        Description = "Execute KQL against the local, preloaded tables. Returns an ordered schema followed by row arrays.",
        InputSchema = JsonElement.Parse("""
            {"type":"object","properties":{"query":{"type":"string","minLength":1,"description":"The KQL query to execute."}},
             "required":["query"],"additionalProperties":false}
            """),
        Annotations = new ToolAnnotations { ReadOnlyHint = true, OpenWorldHint = false }
    };

    internal static async Task<int> RunAsync(KustoDatabase database, Stream input, Stream output, Stream error)
    {
        using var writer = new StreamWriter(error, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var log = TextWriter.Synchronized(writer);
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "kustoplayground", Version = typeof(QueryServer).Assembly.GetName().Version!.ToString() }
        };
        options.Handlers.ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [QueryTool] });
        options.Handlers.CallToolHandler = async (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (request.Params.Name != QueryTool.Name)
                throw new McpProtocolException("Unknown tool. The only available tool is query.", McpErrorCode.InvalidParams);
            var start = Stopwatch.GetTimestamp();
            var args = request.Params.Arguments;
            var reply = args is { Count: 1 } && args.TryGetValue("query", out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is { } kql && !string.IsNullOrWhiteSpace(kql)
                ? LocalQuery.Execute(database, kql, start)
                : JsonOutput.Error("Expected one non-empty string argument named 'query'.", start);
            await log.WriteLineAsync((reply.IsError ? "Query failed." : "Query completed.").AsMemory(), token).ConfigureAwait(false);
            return new CallToolResult
            {
                IsError = reply.IsError,
                StructuredContent = JsonElement.Parse(reply.Json.Span),
                Content = [new TextContentBlock { Text = Encoding.UTF8.GetString(reply.Json.Span) }]
            };
        };

        await log.WriteLineAsync("Starting Kusto Playground MCP server on stdio.").ConfigureAwait(false);
        var transport = new StreamServerTransport(input, output);
        await using var ownedTransport = transport.ConfigureAwait(false);
        var server = McpServer.Create(transport, options);
        await using var ownedServer = server.ConfigureAwait(false);
        // EOF drains the transport. Leave Ctrl+C to the OS: a blocked stdin read or running KQL need not observe cancellation.
        await server.RunAsync().ConfigureAwait(false);
        return 0;
    }
}
