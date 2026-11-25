using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kusto.Language.Symbols;

namespace KustoPlayground.Core;

internal static partial class DynamicValues
{
    // Snapshot incoming containers; a caller may dispose its JsonDocument or reuse a mutable input row.
    internal static object? FromStorage(object? value) => value switch
    {
        JsonElement element => Read(element),
        IReadOnlyDictionary<string, object?> bag => bag.ToDictionary(p => p.Key, p => FromStorage(p.Value), StringComparer.Ordinal),
        IList list => list.Cast<object?>().Select(FromStorage).ToArray(),
        _ => PackedScalar(value)
    };

    internal static object? PackedScalar(object? value) => value switch
    {
        byte or sbyte or short or ushort or int or uint => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        float number => (double)number,
        _ => value
    };

    internal static object InferString(string text)
    {
        if (text.Length >= 10 && text[4] == '-' && text[7] == '-' &&
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            return date;
        return Guid.TryParseExact(text, "D", out var guid) ? guid : text;
    }

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
            case double or float when !double.IsFinite(Convert.ToDouble(value, CultureInfo.InvariantCulture)):
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case double or float:
                // Preserve the real type (2.0) and Kusto's exponent spelling.
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

    internal static object? Extract(string? path, string? json, TypeSymbol? targetType)
    {
        object? selected = null;
        bool found = path is not null && json is not null && TryRead(json, path, out selected);
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
            result = Read(element, inferStrings: path is null);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or ArgumentException { InnerException: EncoderFallbackException })
        {
            // JsonDocument defers checking escaped surrogate pairs until a string is read.
            return false;
        }
    }

    internal static object? Read(JsonElement value) => Read(value, inferStrings: false);

    private static object? Read(JsonElement value, bool inferStrings) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out long n) => n,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => inferStrings ? InferString(value.GetString()!) : value.GetString(),
        JsonValueKind.Array => value.EnumerateArray().Select(e => Read(e, inferStrings)).ToArray(),
        JsonValueKind.Object => ReadBag(value, inferStrings),
        _ => throw new InvalidOperationException($"Unexpected JSON token: {value.ValueKind}")
    };

    private static Dictionary<string, object?> ReadBag(JsonElement value, bool inferStrings)
    {
        var bag = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) bag[property.Name] = Read(property.Value, inferStrings);
        return bag;
    }
}
