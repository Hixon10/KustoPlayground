using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KustoPlayground.Cli;

internal static class QueryServer
{
    private const string QueryName = "query";
    private const string AddJsonName = "add_table_from_json";
    private const string AddCsvName = "add_table_from_csv";

    private static readonly Tool QueryTool = new()
    {
        Name = QueryName,
        Description = "Execute KQL against the tables in this local session. Returns an ordered schema followed by row arrays.",
        InputSchema = JsonElement.Parse("""
            {"type":"object","properties":{"query":{"type":"string","minLength":1,"description":"The KQL query to execute."}},
             "required":["query"],"additionalProperties":false}
            """),
        Annotations = new ToolAnnotations { ReadOnlyHint = true, OpenWorldHint = false }
    };

    private static readonly Tool AddJsonTool = new()
    {
        Name = AddJsonName,
        Description = "Add a new table to this session using its JSON Name. Supply either json or jsonPath. Existing tables are never replaced.",
        InputSchema = JsonElement.Parse("""
            {"type":"object","properties":{
              "json":{"description":"Table definition as an object or serialized JSON text.","anyOf":[
                {"type":"object","required":["Name","Columns","Rows"],"properties":{
                  "Name":{"type":"string","minLength":1},
                  "Columns":{"type":"array","minItems":1,"items":{"type":"object","required":["Name","Type"],"properties":{
                    "Name":{"type":"string"},"Type":{"type":"string","description":"For example: string, int, long, double or dynamic."},"Nullable":{"type":"boolean"}}}},
                  "Rows":{"type":"array","minItems":1,"items":{"type":"object","description":"Values keyed by column name."}}}},
                {"type":"string","minLength":1}]},
              "jsonPath":{"type":"string","minLength":1,"description":"JSON file on the server, absolute or relative to its working directory."}},
             "minProperties":1,"maxProperties":1,"additionalProperties":false}
            """),
        Annotations = new ToolAnnotations { ReadOnlyHint = false, DestructiveHint = false }
    };

    private static readonly Tool AddCsvTool = new()
    {
        Name = AddCsvName,
        Description = "Add a named table to this session from header-based CSV. Supply tableName and either csv or csvPath. Existing tables are never replaced.",
        InputSchema = JsonElement.Parse("""
            {"type":"object","properties":{
              "tableName":{"type":"string","minLength":1,"description":"Name to use in subsequent KQL queries."},
              "csv":{"type":"string","minLength":1,"description":"CSV text with a header and data rows."},
              "csvPath":{"type":"string","minLength":1,"description":"CSV file on the server, absolute or relative to its working directory."}},
             "required":["tableName"],"minProperties":2,"maxProperties":2,"additionalProperties":false}
            """),
        Annotations = new ToolAnnotations { ReadOnlyHint = false, DestructiveHint = false }
    };

    internal static async Task<int> RunAsync(LocalSession session, Stream input, Stream output, Stream error)
    {
        using var writer = new StreamWriter(error, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var log = TextWriter.Synchronized(writer);
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "kustoplayground", Version = typeof(QueryServer).Assembly.GetName().Version!.ToString() }
        };
        options.Handlers.ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [QueryTool, AddJsonTool, AddCsvTool] });
        options.Handlers.CallToolHandler = async (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            var reply = await CallAsync(session, request.Params, token).ConfigureAwait(false);
            var operation = request.Params.Name == QueryName ? "Query" : "Table import";
            await log.WriteLineAsync($"{operation} {(reply.IsError ? "failed" : "completed")}.".AsMemory(), token).ConfigureAwait(false);
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

    private static async ValueTask<CliReply> CallAsync(LocalSession session, CallToolRequestParams call, CancellationToken token)
    {
        var start = Stopwatch.GetTimestamp();
        var args = call.Arguments;
        string? Text(string key) => args is not null && args.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;

        switch (call.Name)
        {
            case QueryName:
                return args is { Count: 1 } && Text("query") is { } kql ? session.Execute(kql, start) :
                    JsonOutput.Error("Expected one non-empty string argument named 'query'.", start);
            case AddJsonName:
                if (args is { Count: 1 })
                {
                    if (Text("jsonPath") is { } path)
                        return await session.AddJsonAsync(path, fromFile: true, start, token).ConfigureAwait(false);
                    var data = args.TryGetValue("json", out var json) && json.ValueKind == JsonValueKind.Object ? json.GetRawText() : Text("json");
                    if (data is not null) return await session.AddJsonAsync(data, fromFile: false, start, token).ConfigureAwait(false);
                }
                return JsonOutput.Error("Expected exactly one of 'json' (a table object or JSON text) or 'jsonPath' (a non-empty path).", start);
            case AddCsvName:
                if (args is { Count: 2 } && Text("tableName") is { } name)
                {
                    if (Text("csvPath") is { } path) return await session.AddCsvAsync(name, path, fromFile: true, start, token).ConfigureAwait(false);
                    if (Text("csv") is { } csv) return await session.AddCsvAsync(name, csv, fromFile: false, start, token).ConfigureAwait(false);
                }
                return JsonOutput.Error("Expected a non-empty 'tableName' and exactly one of 'csv' or 'csvPath' (non-empty strings).", start);
            default:
                throw new McpProtocolException("Unknown tool. Use tools/list for the available tools.", McpErrorCode.InvalidParams);
        }
    }
}
