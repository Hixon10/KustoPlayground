using System.Globalization;
using System.Text.Json;
using Kusto.Language;

namespace KustoPlayground.Core;

internal static partial class DynamicValues
{
    internal static object? Get(object? parent, object? key)
    {
        if (!TryGet(parent, key, out var value)) return null;
        return value is JsonElement element ? Read(element) : value;
    }

    private static bool TryGet(object? parent, object? key, out object? value)
    {
        switch (parent)
        {
            case Dictionary<string, object?> bag when key is string name:
                return bag.TryGetValue(name, out value);
            case object?[] items when TryIndex(key, items.Length, out int index):
                value = items[index];
                return true;
            case JsonElement { ValueKind: JsonValueKind.Object } obj when key is string name && obj.TryGetProperty(name, out var property):
                value = property;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Array } array when TryIndex(key, array.GetArrayLength(), out int index):
                value = array[index];
                return true;
            default:
                value = null;
                return false;
        }
    }

    private static bool TryIndex(object? key, int length, out int index)
    {
        long offset = key switch
        {
            int n => n,
            long n => n,
            byte or sbyte or short or ushort or uint => Convert.ToInt64(key, CultureInfo.InvariantCulture),
            ulong n when n < (ulong)length => (long)n,
            _ => length
        };
        if (offset < 0) offset += length;
        index = offset >= 0 && offset < length ? (int)offset : -1;
        return index >= 0;
    }

    private static bool TrySelect(string path, object? current, out object? selected)
    {
        selected = null;
        if (path.Length == 0 || path[0] != '$') return false;
        int i = 1;
        while (i < path.Length)
        {
            object? key;
            char op = path[i++];
            if (op == '.')
            {
                if (i == path.Length || !(path[i] == '_' || char.IsAsciiLetter(path[i]))) return false;
                int start = i++;
                while (i < path.Length && (path[i] == '_' || char.IsAsciiLetterOrDigit(path[i]))) i++;
                key = path[start..i];
            }
            else if (op != '[' || !TryBracket(path, ref i, out key))
            {
                return false;
            }
            if (!TryGet(current, key, out current)) return false;
        }
        selected = current;
        return true;
    }

    private static bool TryBracket(string path, ref int i, out object? key)
    {
        key = null;
        if (i == path.Length) return false;
        int start = i;
        if (path[i] is '\'' or '"')
        {
            char quote = path[i++];
            while (i < path.Length)
            {
                char c = path[i++];
                if (c == '\\')
                {
                    if (i == path.Length) return false;
                    char escaped = path[i++];
                    if (escaped == 'u')
                    {
                        if (path.Length - i < 4 || !ushort.TryParse(path.AsSpan(i, 4), NumberStyles.AllowHexSpecifier,
                                CultureInfo.InvariantCulture, out _)) return false;
                        i += 4;
                    }
                    else if (escaped is not ('\'' or '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't')) return false;
                }
                else if (c == quote)
                {
                    key = KustoFacts.GetStringLiteralValue(path[start..i]);
                    break;
                }
            }
        }
        else
        {
            while (i < path.Length && char.IsAsciiDigit(path[i])) i++;
            if (!int.TryParse(path.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out int index)) return false;
            key = index;
        }
        return key is not null && i < path.Length && path[i++] == ']';
    }
}
