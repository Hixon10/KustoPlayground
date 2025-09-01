using System.Globalization;

namespace KustoPlayground.Core;

internal static class CompareUtils
{
    internal static bool AreEqual(object? left, object? right, StringComparison comparisonType)
    {
        if (left == null && right == null)
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            return Convert.ToDouble(left, CultureInfo.InvariantCulture) ==
                   Convert.ToDouble(right, CultureInfo.InvariantCulture);
        }

        if (IsNumeric(left) && right is string rightStr)
        {
            // if query tries to compare number, and "number2", we should allow it.
            if (double.TryParse(rightStr, CultureInfo.InvariantCulture, out double rightRes))
            {
                return Convert.ToDouble(left, CultureInfo.InvariantCulture) == rightRes;
            }
        }

        if (left is string leftString && IsNumeric(right))
        {
            // if query tries to compare "number", and number2, we should allow it.
            if (double.TryParse(leftString, CultureInfo.InvariantCulture, out double leftRes))
            {
                return leftRes == Convert.ToDouble(right, CultureInfo.InvariantCulture);
            }
        }

        if (left is string ls && right is string rs)
        {
            return string.Equals(ls, rs, comparisonType);
        }

        return left.Equals(right);
    }

    internal static int Compare(object? left, object? right)
    {
        if (left == null || right == null)
        {
            throw new InvalidOperationException("Cannot compare null values");
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            var dl = Convert.ToDouble(left, CultureInfo.InvariantCulture);
            var dr = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            return dl.CompareTo(dr);
        }

        if (left is string ls && right is string rs)
        {
            return string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase);
        }

        if (left is IComparable cl && right is IComparable cr && left.GetType() == right.GetType())
        {
            return cl.CompareTo(cr);
        }

        throw new NotSupportedException(
            $"Cannot compare values of types {left.GetType().Name} and {right.GetType().Name}");
    }

    private static bool IsNumeric(object value)
    {
        return value is sbyte or byte or short or ushort
            or int or uint or long or ulong
            or float or double or decimal;
    }
}