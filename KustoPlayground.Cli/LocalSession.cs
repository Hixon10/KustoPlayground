using System.Text.Json;
using KustoPlayground.Core;

namespace KustoPlayground.Cli;

internal readonly record struct CliReply(bool IsError, ReadOnlyMemory<byte> Json)
{
    internal void WriteLine(Stream stream)
    {
        stream.Write(Json.Span);
        stream.WriteByte((byte)'\n');
    }
}

internal sealed class LocalSession
{
    private readonly KustoDatabase database = new();
    private readonly HashSet<string> names = new(StringComparer.Ordinal);
    private readonly Lock registration = new();

    internal Task<CliReply> AddJsonAsync(string source, bool fromFile, long start, CancellationToken token = default) =>
        AddAsync(source, fromFile, InputTable.FromJson, start, token);

    internal Task<CliReply> AddCsvAsync(string tableName, string source, bool fromFile, long start, CancellationToken token = default) =>
        AddAsync(source, fromFile, data => InputTable.FromCsv(data, tableName), start, token);

    private async Task<CliReply> AddAsync(string source, bool fromFile, Func<string, Table> parse, long start, CancellationToken token)
    {
        try
        {
            var data = fromFile ? await File.ReadAllTextAsync(source, token).ConfigureAwait(false) : source;
            var table = parse(data);
            // The engine replaces matching names. Only publish fully built tables, once per name, across concurrent calls.
            lock (registration)
            {
                token.ThrowIfCancellationRequested();
                if (!names.Add(table.Name)) return JsonOutput.Error($"Duplicate table name: {table.Name}.", start);
                database.AddTable(table);
            }
            return JsonOutput.TableAdded(table.Name, start);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return JsonOutput.Error($"Cannot read input file: {ex.Message}", start);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or KeyNotFoundException or NotSupportedException or OverflowException)
        {
            return JsonOutput.Error($"Invalid table input: {ex.Message}", start);
        }
    }

    internal CliReply Execute(string query, long start)
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
