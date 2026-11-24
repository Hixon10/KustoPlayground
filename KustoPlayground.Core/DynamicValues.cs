using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kusto.Language.Symbols;

namespace KustoPlayground.Core;

internal static partial class DynamicValues
{
    internal static string ToText(object? value) => value switch
    {
        object?[] or Dictionary<string, object?> => ToJson(value),
        JsonElement element => ToText(Read(element)),
        bool flag => flag ? "true" : "false",
        _ => ScalarFunctions.ToText(value)
    };

    private static string ToJson(object value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            Write(writer, value);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case Dictionary<string, object?> bag:
                writer.WriteStartObject();
                foreach (var (name, item) in bag)
                {
                    writer.WritePropertyName(name);
                    Write(writer, item);
                }
                writer.WriteEndObject();
                break;
            case object?[] items:
                writer.WriteStartArray();
                foreach (var item in items) Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonElement element:
                Write(writer, Read(element));
                break;
            case double or float:
                // Kusto preserves the real type (2.0) and permits bare NaN, inf and -inf.
                writer.WriteRawValue(ScalarFunctions.ToText(value), skipInputValidation: true);
                break;
            case decimal or DateTime or DateTimeOffset or TimeSpan or Guid:
                writer.WriteStringValue(ScalarFunctions.ToText(value));
                break;
            default:
                JsonSerializer.Serialize(writer, value, ExecutionResultJsonContext.Default.Object);
                break;
        }
    }

    internal static object? Parse(object? value) => value switch
    {
        "" => null,
        string text => TryRead(text, null, out var result) ? result : text,
        _ => value
    };

    internal static object? Extract(string? path, object? json, TypeSymbol? targetType)
    {
        object? selected = null;
        bool found = path is not null && (json is string text
            ? TryRead(text, path, out selected)
            : json is not null && TrySelect(path, json, out selected));
        if (selected is JsonElement element) selected = Read(element);
        if (targetType is null || targetType == ScalarTypes.String)
        {
            if (!found) return string.Empty;
            return selected is null ? "null" : ToText(selected);
        }
        return found ? ScalarFunctions.ConvertTo(selected, targetType) : null;
    }

    private static bool TryRead(string text, string? path, out object? result)
    {
        result = null;
        if (text.Length == 0) return false;
        try
        {
            using var document = JsonDocument.Parse(text);
            var element = document.RootElement;
            if (path is not null)
            {
                if (!TrySelect(path, element, out var selected)) return false;
                element = (JsonElement)selected!;
            }
            result = Read(element);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or ArgumentException { InnerException: EncoderFallbackException })
        {
            // JsonDocument defers checking escaped surrogate pairs until a string is read.
            return false;
        }
    }

    internal static object? Read(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out long n) => n,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Array => value.EnumerateArray().Select(Read).ToArray(),
        JsonValueKind.Object => ReadBag(value),
        _ => throw new InvalidOperationException($"Unexpected JSON token: {value.ValueKind}")
    };

    private static Dictionary<string, object?> ReadBag(JsonElement value)
    {
        var bag = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) bag[property.Name] = Read(property.Value);
        return bag;
    }

    internal static Dictionary<string, object?> PackAll(Dictionary<string, object?> row, object?[] args)
    {
        bool ignoreEmpty = args switch
        {
            [] or [null] => false,
            [bool flag] => flag,
            _ => throw new ArgumentException("pack_all expects an optional bool argument.", nameof(args))
        };
        var bag = new Dictionary<string, object?>(row.Count, StringComparer.Ordinal);
        foreach (var (name, value) in row)
        {
            if (!ignoreEmpty || value is not (null or "")) bag.Add(name, value);
        }
        return bag;
    }

    internal static Dictionary<string, object?> Pack(object?[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("bag_pack expects key/value pairs.");
        var bag = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
            bag[args[i] as string ?? throw new ArgumentException("Bag keys must be strings.")] = args[i + 1];
        return bag;
    }
}
