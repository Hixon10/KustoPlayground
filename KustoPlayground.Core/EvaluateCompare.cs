using System.Globalization;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

internal static class CompareUtils
{
    internal static bool? AreEqual(object? left, object? right, StringComparison comparisonType)
    {
        if (left is long a && right is long b) return a == b;
        if (left is decimal x && right is decimal y) return x == y;
        if (left == null && right == null)
        {
            return null;
        }

        if (left == null || right == null)
        {
            return false;
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            return !IsNaN(left) && !IsNaN(right) && CompareNumbers(left, right) == 0;
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
        if (left is long a && right is long b) return a.CompareTo(b);
        if (left is decimal x && right is decimal y) return x.CompareTo(y);
        if (left == null || right == null)
        {
            throw new InvalidOperationException("Cannot compare null values");
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            return CompareNumbers(left, right);
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

    private static int CompareNumbers(object left, object right) =>
        left is double or float || right is double or float
            ? Convert.ToDouble(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture))
            : Convert.ToDecimal(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));

    private static bool IsNaN(object? value) => value is double.NaN or float.NaN;

    internal static bool? Relational(SyntaxKind op, object? left, object? right)
    {
        if (left is null || right is null) return null;
        if (IsNaN(left) || IsNaN(right)) return false;
        var comparison = Compare(left, right);
        return op switch
        {
            SyntaxKind.LessThanExpression => comparison < 0,
            SyntaxKind.LessThanOrEqualExpression => comparison <= 0,
            SyntaxKind.GreaterThanExpression => comparison > 0,
            SyntaxKind.GreaterThanOrEqualExpression => comparison >= 0,
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };
    }

    internal static int SortComparison(object? left, object? right, bool descending, bool nullsFirst)
    {
        // NaN sorts next to null, independent of the direction used for ordinary values and infinities.
        static int Rank(object? value) => value is null ? 0 : IsNaN(value) ? 1 : 2;
        int a = Rank(left), b = Rank(right);
        if (a != b) return nullsFirst ? a.CompareTo(b) : b.CompareTo(a);
        return a != 2 ? 0 : descending ? Compare(right, left) : Compare(left, right);
    }

    internal static bool IsNumeric(object value)
    {
        return value is sbyte or byte or short or ushort
            or int or uint or long or ulong
            or float or double or decimal;
    }
}