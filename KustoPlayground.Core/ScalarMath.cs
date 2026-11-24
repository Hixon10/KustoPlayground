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
                if (op == SyntaxKind.DivideExpression) return span == TimeSpan.Zero ? null : duration / span;
            }
            if (right is DateTime instant && op == SyntaxKind.AddExpression) return instant + duration;
            if (CompareUtils.IsNumeric(right) && op == SyntaxKind.MultiplyExpression) return duration * Convert.ToDouble(right, Culture);
            if (CompareUtils.IsNumeric(right) && op == SyntaxKind.DivideExpression)
            {
                var divisor = Convert.ToDouble(right, Culture);
                return divisor == 0 ? null : duration / divisor;
            }
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

    internal static double? ToReal(object? value)
    {
        if (value is string text)
        {
            var span = text.AsSpan().Trim();
            if (span.Equals("inf", StringComparison.OrdinalIgnoreCase) || span.Equals("+inf", StringComparison.OrdinalIgnoreCase))
                return double.PositiveInfinity;
            if (span.Equals("-inf", StringComparison.OrdinalIgnoreCase)) return double.NegativeInfinity;
            return double.TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, Culture, out var real) ? real : null;
        }

        return value switch
        {
            DateTime date => (date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : date).Ticks,
            TimeSpan time => time.Ticks,
            bool flag => flag ? 1d : 0d,
            not null when CompareUtils.IsNumeric(value) => Convert.ToDouble(value, Culture),
            _ => null
        };
    }

    internal static object? Bin(object?[] args, TypeSymbol? resultType)
    {
        if (args.Length is not (2 or 3)) throw new ArgumentException("Expected a value, a bin size, and optionally an anchor.");
        if (Array.Exists(args, v => v is null)) return null;
        var input = args[0]!;
        var width = args[1]!;
        var anchor = args.Length == 3 ? args[2] : null;
        if (width is TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero) return null;
            if (input is DateTime dt && anchor is null or DateTime)
            {
                var ticks = FloorBin(dt.Ticks, duration.Ticks, (anchor as DateTime?)?.Ticks ?? 0);
                return ticks >= 0 && ticks <= DateTime.MaxValue.Ticks ? new DateTime((long)ticks, dt.Kind) : null;
            }
            if (input is TimeSpan ts && anchor is null or TimeSpan)
            {
                var ticks = FloorBin(ts.Ticks, duration.Ticks, (anchor as TimeSpan?)?.Ticks ?? 0);
                return ticks >= long.MinValue ? TimeSpan.FromTicks((long)ticks) : null;
            }
        }
        if (!CompareUtils.IsNumeric(input) || !CompareUtils.IsNumeric(width) || (anchor is not null && !CompareUtils.IsNumeric(anchor)))
            throw new ArgumentException("Expected numeric operands, or a datetime or timespan with a timespan bin size.");

        if (resultType == ScalarTypes.Real || input is float or double || width is float or double)
        {
            var size = Convert.ToDouble(width, Culture);
            if (size == 0 && args.Length == 2) return double.NaN;
            var start = Convert.ToDouble(anchor, Culture);
            return size > 0 ? start + size * Math.Floor((Convert.ToDouble(input, Culture) - start) / size) : null;
        }
        var step = Convert.ToDecimal(width, Culture);
        if (step <= 0) return null;
        try
        {
            var value = FloorBin(Convert.ToDecimal(input, Culture), step, Convert.ToDecimal(anchor, Culture));
            if (resultType == ScalarTypes.Decimal || input is decimal || width is decimal) return value;
            return value is >= long.MinValue and <= long.MaxValue ? (long)value : null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static decimal FloorBin(decimal value, decimal size, decimal anchor)
    {
        // A remainder avoids rounding a nearly integral quotient up to the next bin.
        var remainder = (value - anchor) % size;
        return value - (remainder < 0 ? remainder + size : remainder);
    }
}
