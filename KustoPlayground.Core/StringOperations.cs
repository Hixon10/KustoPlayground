namespace KustoPlayground.Core;

internal static class StringOperations
{
    internal static bool ContainsOperation(object? left, object? right)
    {
        if (left is not string ls)
        {
            throw new NotSupportedException(
                "contains operation requires left operand to be a string, and it is not.");
        }

        if (right is not string rs)
        {
            throw new NotSupportedException(
                "contains operation requires right operand to be a string, and it is not.");
        }

        return ls.Contains(rs, StringComparison.OrdinalIgnoreCase);
    }
}