using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using KustoPlayground.Core;

namespace KustoPlayground.Cli;

internal static class JsonOutput
{
    private static readonly ExecutionResultJsonContext Json = new(new JsonSerializerOptions
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    });

    internal static CliReply Success(ExecutionResult result, long start)
    {
        // Only write stdout after all cells serialize, so an unsupported value cannot leave partial JSON.
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = Start(buffer, 200, "Success");
        writer.WriteStartObject("results");
        writer.WriteStartArray("items");
        writer.WriteStartObject();
        var columns = result.Columns!;
        foreach (var column in columns) writer.WriteString(column.Name, column.Type);
        writer.WriteEndObject();
        foreach (var row in result.ResultRows!)
        {
            writer.WriteStartArray();
            foreach (var column in columns) JsonSerializer.Serialize(writer, row[column.Name], Json.Object);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        return new CliReply(false, Finish(writer, buffer, start));
    }

    internal static CliReply TableAdded(string name, long start)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = Start(buffer, 200, "Success");
        writer.WriteStartObject("results");
        writer.WriteString("tableName", name);
        writer.WriteEndObject();
        return new CliReply(false, Finish(writer, buffer, start));
    }

    internal static CliReply Error(string message, long start)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = Start(buffer, 400, message);
        return new CliReply(true, Finish(writer, buffer, start));
    }

    private static Utf8JsonWriter Start(ArrayBufferWriter<byte> buffer, int status, string message)
    {
        var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteNumber("status", status);
        writer.WriteString("message", message);
        return writer;
    }

    private static ReadOnlyMemory<byte> Finish(Utf8JsonWriter writer, ArrayBufferWriter<byte> buffer, long start)
    {
        writer.WriteNumber("duration", (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }
}
