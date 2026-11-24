using System.Globalization;
using Kusto.Language.Symbols;

namespace KustoPlayground.Core;

internal static class NumericAggregates
{
    internal static object? Total(IEnumerable<object> values, TypeSymbol type, bool average)
    {
        if (type == ScalarTypes.Real)
        {
            double sum = 0;
            long n = 0;
            foreach (var value in values) { sum += Convert.ToDouble(value, CultureInfo.InvariantCulture); n++; }
            return average ? sum / n : sum;
        }

        decimal total = 0;
        long count = 0;
        foreach (var value in values)
        {
            total += value switch
            {
                TimeSpan span => span.Ticks,
                DateTime date => date.Ticks,
                _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            };
            count++;
        }
        if (average && count == 0)
            return type == ScalarTypes.Decimal || type == ScalarTypes.TimeSpan || type == ScalarTypes.DateTime ? null : double.NaN;
        if (average) total /= count;
        if (type == ScalarTypes.TimeSpan) return TimeSpan.FromTicks((long)total);
        if (type == ScalarTypes.DateTime) return new DateTime((long)total, DateTimeKind.Utc);
        if (type == ScalarTypes.Decimal) return total;
        if (average) return (double)total;
        return (long)total;
    }

    // Welford's online co-moment avoids subtracting two nearly equal sums of squares.
    internal sealed class Moments
    {
        private long _count;
        private double _meanX, _meanY, _coMoment;

        internal void Add(double x, double y)
        {
            _count++;
            var delta = x - _meanX;
            _meanX += delta / _count;
            _meanY += (y - _meanY) / _count;
            _coMoment += delta * (y - _meanY);
        }

        internal double Covariance(bool population) => _count < 2 ? 0 : _coMoment / (population ? _count : _count - 1);
    }
}
