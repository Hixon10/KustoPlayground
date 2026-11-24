using System.Globalization;
using System.Text.Json;

namespace KustoPlayground.Core;

internal static class DynamicValues
{
    internal static string ToText(object? value) => value switch
    {
        object?[] items => JsonSerializer.Serialize(items, ExecutionResultJsonContext.Default.ObjectArray),
        Dictionary<string, object?> bag => JsonSerializer.Serialize(bag, ExecutionResultJsonContext.Default.DictionaryStringObject),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    internal static object? Parse(object? value)
    {
        if (value is not string text) return value;
        try
        {
            using var document = JsonDocument.Parse(text);
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            // Kusto parse_json preserves a string that is not valid JSON.
            return text;
        }
    }

    private static object? Read(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out long n) => n,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Array => value.EnumerateArray().Select(Read).ToArray(),
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Read(p.Value), StringComparer.Ordinal),
        _ => throw new InvalidOperationException($"Unexpected JSON token: {value.ValueKind}")
    };

    internal static Dictionary<string, object?> Pack(object?[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("bag_pack expects key/value pairs.");
        var bag = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
            bag[args[i] as string ?? throw new ArgumentException("Bag keys must be strings.")] = args[i + 1];
        return bag;
    }
}
