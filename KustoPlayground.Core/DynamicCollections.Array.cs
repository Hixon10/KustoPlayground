using System.Globalization;

namespace KustoPlayground.Core;

internal static partial class DynamicCollections
{
    private static object?[]? Slice(object?[] args)
    {
        var source = AsArray(args[0]);
        if (source is null || Integer(args[1]) is not long start || Integer(args[2]) is not long end) return null;
        if (start < 0) start += source.Length;
        if (end < 0) end += source.Length;
        start = Math.Max(0, start);
        end = Math.Min(source.Length - 1L, end);
        return start > end ? [] : source[(int)start..((int)end + 1)];
    }

    private static object?[] Concat(object?[] args)
    {
        var result = new List<object?>();
        foreach (var argument in args)
            if (AsArray(argument) is { } source) result.AddRange(source);
        return result.ToArray();
    }

    private static long? IndexOf(object?[] args)
    {
        if (Unwrap(args[0]) is null) return null;
        if (AsArray(args[0]) is not { } source) return -1;
        long? start = args.Length > 2 ? Integer(args[2]) : 0;
        long? length = args.Length > 3 ? Integer(args[3]) : -1;
        long? occurrence = args.Length > 4 ? Integer(args[4]) : 1;
        if (start is null || length is null or < -1 || occurrence is null) return null;
        long first = start.Value < 0 ? Math.Max(0, source.LongLength + start.Value) : start.Value;
        if (first >= source.Length) return -1;
        long count = length == -1 ? source.Length - first : Math.Min(source.Length - first, length.Value);
        long remaining = occurrence.Value;
        for (long i = first; i < first + count; i++)
            if (Matches(source[i], args[1]) && --remaining == 0) return i;
        return -1;
    }

    private static object? Sort(object?[] args, bool descending)
    {
        bool hasNullPlacement = args.Length > 1 && args[^1] is bool;
        bool nullsLast = !hasNullPlacement || args[^1] is true;
        int count = args.Length - (hasNullPlacement ? 1 : 0);
        var result = new object?[count];
        if (AsArray(args[0]) is { } keys)
        {
            int[] permutation = Enumerable.Range(0, keys.Length).ToArray();
            Array.Sort(permutation, (left, right) =>
            {
                int comparison = CompareSortKeys(keys[left], keys[right], descending, nullsLast);
                return comparison != 0 ? comparison : left.CompareTo(right);
            });
            for (int i = 0; i < count; i++)
                if (AsArray(args[i]) is { } values && values.Length == keys.Length)
                    result[i] = permutation.Select(index => values[index]).ToArray();
        }
        return count == 1 ? result[0] : result;
    }

    private static int CompareSortKeys(object? left, object? right, bool descending, bool nullsLast)
    {
        left = CanonicalScalar(left);
        right = CanonicalScalar(right);
        if (left is null || right is null)
        {
            int comparison = (left is null).CompareTo(right is null);
            return nullsLast ? comparison : -comparison;
        }
        return descending ? CompareNonNullKeys(right, left) : CompareNonNullKeys(left, right);
    }

    private static int CompareNonNullKeys(object left, object right)
    {
        static int Rank(object value) => value switch
        {
            bool or DateTime or TimeSpan => 0,
            string => 1,
            Guid => 2,
            _ => CompareUtils.IsNumeric(value) ? 0 : 3
        };
        static object NumericKey(object value) => value switch
        {
            bool flag => flag ? 1L : 0L,
            DateTime date => date.Ticks,
            TimeSpan span => span.Ticks,
            _ => value
        };

        int leftRank = Rank(left), rightRank = Rank(right);
        if (leftRank != rightRank) return leftRank.CompareTo(rightRank);
        return leftRank switch
        {
            0 => CompareUtils.Compare(NumericKey(left), NumericKey(right)),
            1 => StringComparer.Ordinal.Compare((string)left, (string)right),
            2 => ((Guid)left).CompareTo((Guid)right),
            _ => StringComparer.Ordinal.Compare(DynamicValues.ToText(left), DynamicValues.ToText(right))
        };
    }

    private static double? Sum(object? value)
    {
        if (AsArray(value) is not { } source) return null;
        double? total = null;
        foreach (var item in source)
        {
            var number = Unwrap(item);
            if (number is not null && (number is bool || CompareUtils.IsNumeric(number)))
                total = (total ?? 0) + Convert.ToDouble(number, CultureInfo.InvariantCulture);
        }
        return total;
    }

    private static object?[]? Choose(object?[] args)
    {
        if (AsArray(args[0]) is not { } conditions) return null;
        var result = new object?[conditions.Length];
        for (int i = 0; i < conditions.Length; i++)
        {
            var condition = Unwrap(conditions[i]);
            bool? flag = condition switch
            {
                bool boolean => boolean,
                not null when CompareUtils.IsNumeric(condition) => Convert.ToDouble(condition, CultureInfo.InvariantCulture) != 0,
                _ => null
            };
            if (flag is null) continue;
            var branch = args[flag.Value ? 1 : 2];
            result[i] = AsArray(branch) is { } values
                ? i < values.Length ? values[i] : null
                : ToDynamic(branch);
        }
        return result;
    }

    private static object?[]? Set(string name, object?[] args)
    {
        if (name == "set_union" && Array.TrueForAll(args, value => Unwrap(value) is null)
            || name == "set_intersect" && Array.Exists(args, value => Unwrap(value) is null)
            || name == "set_difference" && Unwrap(args[0]) is null) return null;

        var result = new HashSet<object?>(Comparer);
        for (int i = 0; i < args.Length; i++)
        {
            var values = (AsArray(args[i]) ?? []).Select(Unwrap).Where(value => value is not null);
            if (i == 0 || name == "set_union") result.UnionWith(values);
            else if (name == "set_intersect") result.IntersectWith(values);
            else result.ExceptWith(values);
        }
        return result.ToArray();
    }

    private static bool? HasElement(object? array, object? value) => Unwrap(array) is null ? null
        : AsArray(array) is { } source && Array.Exists(source, item => Matches(item, value));
}
