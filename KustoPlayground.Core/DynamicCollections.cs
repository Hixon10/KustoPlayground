using System.Text.Json;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

internal static partial class DynamicCollections
{
    internal static IEqualityComparer<object?> Comparer { get; } = new DynamicComparer();

    internal static bool Supports(string name) => name is
        "array_length" or "arraylength" or "array_slice" or "array_concat" or "array_index_of" or "series_fill_linear"
        or "array_reverse" or "array_sort_asc" or "array_sort_desc" or "array_sum" or "array_iff" or "array_iif"
        or "set_union" or "set_intersect" or "set_difference" or "set_has_element"
        or "bag_pack" or "pack" or "pack_array" or "pack_all" or "bag_keys" or "bag_has_key" or "bag_merge" or "bag_set_key";

    internal static object? Evaluate(FunctionCallExpression call, object?[] args, Dictionary<string, object?> row)
    {
        string name = call.Name.SimpleName;
        if (name == "pack_array" && call.ArgumentList.Expressions.Count == 1
            && call.ArgumentList.Expressions[0].Element is StarExpression)
            return row.Values.Select(ToDynamic).ToArray();

        var (minimum, maximum) = name switch
        {
            "pack_all" => (0, 1),
            "array_length" or "arraylength" or "array_reverse" or "array_sum" or "bag_keys" => (1, 1),
            "set_has_element" or "bag_has_key" => (2, 2),
            "array_slice" or "array_iff" or "array_iif" or "bag_set_key" => (3, 3),
            "array_index_of" => (2, 5),
            "series_fill_linear" => (1, 4),
            "array_concat" or "array_sort_asc" or "array_sort_desc" or "pack_array" => (1, int.MaxValue),
            _ => (2, int.MaxValue)
        };
        if (args.Length < minimum || args.Length > maximum)
            throw new ArgumentException($"Invalid number of arguments for {name}.", nameof(args));
        if (name == "set_has_element" && call.ArgumentList.Expressions[1].Element.ResultType is DynamicSymbol)
            throw new ArgumentException("set_has_element requires a non-dynamic scalar search value.", nameof(args));

        return name switch
        {
            "array_length" or "arraylength" => AsArray(args[0])?.LongLength,
            "array_slice" => Slice(args),
            "array_concat" => Concat(args),
            "array_index_of" => IndexOf(args),
            "array_reverse" => AsArray(args[0])?.Reverse().ToArray(),
            "array_sort_asc" or "array_sort_desc" => Sort(args, name == "array_sort_desc"),
            "array_sum" => Sum(args[0]),
            "series_fill_linear" => FillLinear(args),
            "array_iff" or "array_iif" => Choose(args),
            "set_union" or "set_intersect" or "set_difference" => Set(name, args),
            "set_has_element" => HasElement(args[0], args[1]),
            "pack_array" => args.Select(ToDynamic).ToArray(),
            "bag_pack" or "pack" => Pack(call, args),
            "pack_all" => PackAll(call, args, row),
            "bag_keys" => AsBag(args[0])?.Keys.Cast<object?>().ToArray(),
            "bag_has_key" => HasKey(args[0], args[1] as string),
            "bag_merge" => Merge(args),
            "bag_set_key" => SetKey(args[0], args[1] as string, args[2]),
            _ => throw new NotSupportedException($"Unsupported dynamic collection function: {name}")
        };
    }

    private static object? Unwrap(object? value) => value is JsonElement element ? DynamicValues.Read(element) : value;

    private static object?[]? AsArray(object? value) => Unwrap(value) as object?[];

    private static Dictionary<string, object?>? AsBag(object? value) => Unwrap(value) as Dictionary<string, object?>;

    private static object? CanonicalScalar(object? value) => DynamicValues.PackedScalar(Unwrap(value)) switch
    {
        DateTimeOffset date => date.UtcDateTime,
        var other => other
    };

    private static object? ToDynamic(object? value) => CanonicalScalar(value) switch
    {
        object?[] array => array.Select(ToDynamic).ToArray(),
        Dictionary<string, object?> bag => bag.ToDictionary(pair => pair.Key, pair => ToDynamic(pair.Value), StringComparer.Ordinal),
        var scalar => scalar
    };

    private static long? Integer(object? value) => CanonicalScalar(value) is long number ? number : null;

    private static bool Matches(object? left, object? right)
    {
        left = CanonicalScalar(left);
        right = CanonicalScalar(right);
        // Membership promotes numeric scalars, unlike the type-sensitive identities used by set operations.
        return left is not null && right is not null && CompareUtils.IsNumeric(left) && CompareUtils.IsNumeric(right)
            ? CompareUtils.AreEqual(left, right, StringComparison.Ordinal) == true
            : Comparer.Equals(left, right);
    }

    private sealed class DynamicComparer : EqualityComparer<object?>
    {
        public override bool Equals(object? x, object? y)
        {
            x = CanonicalScalar(x);
            y = CanonicalScalar(y);
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x is object?[] leftArray && y is object?[] rightArray)
                return leftArray.AsSpan().SequenceEqual(rightArray, this);
            if (x is Dictionary<string, object?> leftBag && y is Dictionary<string, object?> rightBag)
            {
                if (leftBag.Count != rightBag.Count) return false;
                if (rightBag.Comparer != StringComparer.Ordinal && rightBag.Comparer != EqualityComparer<string>.Default)
                    rightBag = new Dictionary<string, object?>(rightBag, StringComparer.Ordinal);
                foreach (var (key, value) in leftBag)
                    if (!rightBag.TryGetValue(key, out var other) || !Equals(value, other)) return false;
                return true;
            }
            return x.GetType() == y.GetType() && x.Equals(y);
        }

        public override int GetHashCode(object? obj)
        {
            obj = CanonicalScalar(obj);
            if (obj is null) return 0;
            if (obj is object?[] array)
            {
                var hash = new HashCode();
                hash.Add(array.Length);
                foreach (var value in array) hash.Add(GetHashCode(value));
                return hash.ToHashCode();
            }
            if (obj is Dictionary<string, object?> bag)
            {
                int hash = 0;
                foreach (var (key, value) in bag)
                    hash = unchecked(hash + HashCode.Combine(StringComparer.Ordinal.GetHashCode(key), GetHashCode(value)));
                return HashCode.Combine(bag.Count, hash);
            }
            return HashCode.Combine(obj.GetType(), obj.GetHashCode());
        }
    }
}
