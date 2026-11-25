using System.Text.Json;
using KustoPlayground.Core;

namespace KustoPlayground.Cli;

internal readonly record struct QueryReply(bool IsError, ReadOnlyMemory<byte> Json)
{
    internal void WriteLine(Stream stream)
    {
        stream.Write(Json.Span);
        stream.WriteByte((byte)'\n');
    }
}

internal static class LocalQuery
{
    internal static QueryReply Execute(KustoDatabase database, string query, long start)
    {
        var result = database.ExecuteQuery(query, includeSchema: true);
        if (result.ExecutionErrors is { } errors)
            return JsonOutput.Error(string.Join("; ", errors.Select(e => $"{e.Code}: {e.Description}")), start);
        try
        {
            return JsonOutput.Success(result, start);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return JsonOutput.Error($"Cannot represent this query result as JSON: {ex.Message}", start);
        }
    }
}
