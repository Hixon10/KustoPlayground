using System.Globalization;
using System.Numerics;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

internal static class ScalarMath
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    internal static object? Arithmetic(BinaryExpression expr, object left, object right)
    {
        var op = expr.Kind;
        if (left is DateTime time)
        {
            if (right is DateTime other && op == SyntaxKind.SubtractExpression) return time - other;
            if (right is TimeSpan span && op == SyntaxKind.AddExpression) return time + span;
            if (right is TimeSpan delta && op == SyntaxKind.SubtractExpression) return time - delta;
        }
        if (left is TimeSpan duration)
        {
            if (right is TimeSpan span)
            {
                if (op == SyntaxKind.AddExpression) return duration + span;
                if (op == SyntaxKind.SubtractExpression) return duration - span;
                if (op == SyntaxKind.DivideExpression) return duration / span;
            }
            if (right is DateTime instant && op == SyntaxKind.AddExpression) return instant + duration;
            if (CompareUtils.IsNumeric(right) && op == SyntaxKind.MultiplyExpression) return duration * Convert.ToDouble(right, Culture);
            if (CompareUtils.IsNumeric(right) && op == SyntaxKind.DivideExpression) return duration / Convert.ToDouble(right, Culture);
        }
        if (right is TimeSpan factor && CompareUtils.IsNumeric(left) && op == SyntaxKind.MultiplyExpression)
            return factor * Convert.ToDouble(left, Culture);
        if (!CompareUtils.IsNumeric(left) || !CompareUtils.IsNumeric(right))
            throw new NotSupportedException($"Unsupported {op}: {left.GetType().Name}, {right.GetType().Name}");

        // Bound expressions preserve Kusto integer/decimal precision. Unbound legacy queries used real arithmetic.
        if (expr.ResultType == ScalarTypes.Long) return Calculate(op, Convert.ToInt64(left, Culture), Convert.ToInt64(right, Culture));
        if (expr.ResultType == ScalarTypes.Int) return Calculate(op, Convert.ToInt32(left, Culture), Convert.ToInt32(right, Culture));
        if (expr.ResultType == ScalarTypes.Decimal) return Calculate(op, Convert.ToDecimal(left, Culture), Convert.ToDecimal(right, Culture));
        return Calculate(op, Convert.ToDouble(left, Culture), Convert.ToDouble(right, Culture));
    }

    private static T? Calculate<T>(SyntaxKind op, T left, T right) where T : struct, INumber<T> => op switch
    {
        SyntaxKind.AddExpression => left + right,
        SyntaxKind.SubtractExpression => left - right,
        SyntaxKind.MultiplyExpression => left * right,
        SyntaxKind.DivideExpression => right == T.Zero ? null : left / right,
        SyntaxKind.ModuloExpression => right == T.Zero ? null : left % right,
        _ => throw new NotSupportedException($"Unsupported numeric operator: {op}")
    };

    internal static double? ToReal(object? value) =>
        double.TryParse(Convert.ToString(value, Culture), NumberStyles.Float, Culture, out var number) ? number : null;

    internal static object? Bin(object?[] args, TypeSymbol? resultType)
    {
        if (args.Length is not (2 or 3)) throw new ArgumentException("Expected a value, a bin size, and optionally an anchor.");
        if (Array.Exists(args, v => v is null)) return null;
        var anchor = args.Length == 3 ? args[2] : null;
        if (args[1] is TimeSpan width)
        {
            if (width <= TimeSpan.Zero) return null;
            if (args[0] is DateTime dt)
                return new DateTime(checked((long)FloorBin(dt.Ticks, width.Ticks, (anchor as DateTime?)?.Ticks ?? 0)), dt.Kind);
            if (args[0] is TimeSpan ts)
                return TimeSpan.FromTicks(checked((long)FloorBin(ts.Ticks, width.Ticks, (anchor as TimeSpan?)?.Ticks ?? 0)));
        }
        if (resultType == ScalarTypes.Real || args[0] is float or double || args[1] is float or double)
        {
            var size = Convert.ToDouble(args[1], Culture);
            var start = Convert.ToDouble(anchor, Culture);
            return size > 0 ? start + size * Math.Floor((Convert.ToDouble(args[0], Culture) - start) / size) : null;
        }
        var step = Convert.ToDecimal(args[1], Culture);
        if (step <= 0) return null;
        var value = FloorBin(Convert.ToDecimal(args[0], Culture), step, Convert.ToDecimal(anchor, Culture));
        if (resultType == ScalarTypes.Decimal) return value;
        return checked((long)value);
    }

    private static decimal FloorBin(decimal value, decimal size, decimal anchor) =>
        anchor + size * decimal.Floor((value - anchor) / size);
}
