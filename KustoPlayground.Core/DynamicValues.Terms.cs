namespace KustoPlayground.Core;

internal static partial class DynamicValues
{
    internal static bool HasTerm(object? value, string needle, bool caseSensitive)
    {
        return value switch
        {
            null => false,
            object?[] items => Array.Exists(items, item => HasTerm(item, needle, caseSensitive)),
            Dictionary<string, object?> bag => bag.Any(p => InText(p.Key) || HasTerm(p.Value, needle, caseSensitive)),
            _ => InText(ToText(value))
        };

        bool InText(string text)
        {
            if (needle.Length == 0) return true;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int start = 0;
            while (text.IndexOf(needle, start, comparison) is var index && index >= 0)
            {
                int end = index + needle.Length;
                if ((index == 0 || !char.IsAsciiLetterOrDigit(text[index - 1]) || !char.IsAsciiLetterOrDigit(needle[0])) &&
                    (end == text.Length || !char.IsAsciiLetterOrDigit(text[end]) || !char.IsAsciiLetterOrDigit(needle[^1])))
                    return true;
                start = index + 1;
            }
            return false;
        }
    }
}
