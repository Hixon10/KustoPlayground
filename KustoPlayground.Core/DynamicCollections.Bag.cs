using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

internal static partial class DynamicCollections
{
    private static Dictionary<string, object?> Pack(FunctionCallExpression call, object?[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("bag_pack expects key/value pairs.", nameof(args));
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (args[i] is not string key || call.ArgumentList.Expressions[i].Element.ResultType is DynamicSymbol)
                throw new ArgumentException("Bag keys must be strings.", nameof(args));
            if (key.Length > 0) result[key] = ToDynamic(args[i + 1]);
        }
        return result;
    }

    private static Dictionary<string, object?> PackAll(FunctionCallExpression call, object?[] args, Dictionary<string, object?> row)
    {
        bool ignoreEmpty = args switch
        {
            [] or [null] => false,
            [bool flag] => flag,
            _ => throw new ArgumentException("pack_all expects an optional bool argument.", nameof(args))
        };
        var properties = (call.ResultType as DynamicBagSymbol)?.Properties;
        var result = new Dictionary<string, object?>(row.Count, StringComparer.Ordinal);
        foreach (var (name, value) in row)
        {
            // An empty dynamic string is a value; only an empty string-typed column is omitted.
            bool emptyString = value is "" && properties?.FirstOrDefault(column => column.Name == name)?.Type is not DynamicSymbol;
            if (!ignoreEmpty || value is not null && !emptyString) result.Add(name, ToDynamic(value));
        }
        return result;
    }

    private static Dictionary<string, object?>? Merge(object?[] args)
    {
        Dictionary<string, object?>? result = null;
        foreach (var argument in args)
        {
            if (AsBag(argument) is not { } bag) continue;
            result ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, value) in bag) result.TryAdd(key, value);
        }
        return result;
    }

    private static bool HasKey(object? source, string? key)
    {
        if (AsBag(source) is null || !TryBagPath(key, out var path)) return false;
        object? current = source;
        foreach (var property in path)
            if (AsBag(current) is not { } bag || !bag.TryGetValue(property, out current)) return false;
        return true;
    }

    private static Dictionary<string, object?>? SetKey(object? source, string? key, object? value)
    {
        if (AsBag(source) is not { } bag || !TryBagPath(key, out var path)) return null;
        var result = new Dictionary<string, object?>(bag, StringComparer.Ordinal);
        var current = result;
        for (int i = 0; i < path.Length - 1; i++)
        {
            var nested = AsBag(current.GetValueOrDefault(path[i]));
            var copy = nested is null ? new Dictionary<string, object?>(StringComparer.Ordinal)
                : new Dictionary<string, object?>(nested, StringComparer.Ordinal);
            current[path[i]] = copy;
            current = copy;
        }
        current[path[^1]] = ToDynamic(value);
        return result;
    }

    private static bool TryBagPath(string? key, out string[] path)
    {
        path = [];
        if (key is null) return false;
        if (key.Length == 0 || key[0] != '$')
        {
            path = [key];
            return true;
        }
        var properties = new List<string>();
        int i = 1;
        while (i < key.Length)
        {
            char op = key[i++];
            if (op == '.')
            {
                if (i == key.Length || !(key[i] == '_' || char.IsAsciiLetter(key[i]))) return false;
                int start = i++;
                while (i < key.Length && (key[i] == '_' || char.IsAsciiLetterOrDigit(key[i]))) i++;
                properties.Add(key[start..i]);
            }
            else if (op == '[')
            {
                if (i == key.Length || key[i] is not ('\'' or '"')) return false;
                int start = i;
                char quote = key[i++];
                bool closed = false;
                while (i < key.Length)
                {
                    char c = key[i++];
                    if (c == '\\')
                    {
                        if (i == key.Length) return false;
                        i++;
                    }
                    else if (c == quote)
                    {
                        closed = true;
                        break;
                    }
                }
                if (!closed || i == key.Length || key[i] != ']') return false;
                properties.Add(KustoFacts.GetStringLiteralValue(key[start..i]));
                i++;
            }
            else return false;
        }
        path = properties.ToArray();
        return path.Length > 0;
    }
}
