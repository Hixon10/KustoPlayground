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

    internal static void WriteResult(Stream output, ExecutionResult result, long start)
    {
        // Only write stdout after all cells serialize, so an unsupported value cannot leave partial JSON.
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteNumber("status", 200);
        writer.WriteString("message", "Success");
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
        Finish(output, writer, buffer, start);
    }

    internal static void WriteError(Stream error, string message, long start)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteNumber("status", 400);
        writer.WriteString("message", message);
        Finish(error, writer, buffer, start);
    }

    private static void Finish(Stream stream, Utf8JsonWriter writer, ArrayBufferWriter<byte> buffer, long start)
    {
        writer.WriteNumber("duration", (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        writer.WriteEndObject();
        writer.Flush();
        stream.Write(buffer.WrittenSpan);
        stream.WriteByte((byte)'\n');
    }
}
