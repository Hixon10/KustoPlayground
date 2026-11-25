using System.Globalization;
using System.Numerics;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator
{
    private const int MaxSeriesLength = 1 << 20;

    private static string? ValidateSeries(MakeSeriesOperator op)
    {
        if (op.OnClause.Expression is not NameReference { ReferencedSymbol: ColumnSymbol, ResultType: ScalarSymbol type } ||
            !(type.IsNumeric || type == ScalarTypes.DateTime || type == ScalarTypes.TimeSpan))
            return "make-series requires a numeric, datetime or timespan column as its axis.";
        if (op.ByClause is { } by)
        {
            if (IsNonempty(op)) return "make-series kind=nonempty cannot be combined with by.";
            if (by.Expressions.Any(e => e.Element.ResultType is DynamicSymbol)) return "A make-series grouping key cannot be dynamic.";
        }
        foreach (var item in op.Aggregates)
        {
            var measure = item.Element;
            var resultType = UnwrapOutput(measure.Expression).ResultType;
            if (resultType is not ScalarSymbol { IsNumeric: true } && resultType != ScalarTypes.DateTime)
                return "A make-series expression must produce a single numeric or datetime value.";
            if (measure.DefaultExpression is { Expression: var fill } &&
                (fill.ResultType is not ScalarSymbol { IsNumeric: true } || !IsSeriesScalar(fill)))
                return "A make-series default must be a numeric constant.";
        }
        return null;
    }

    private static bool IsNonempty(MakeSeriesOperator op) =>
        op.Parameters.Any(p => p.Name.SimpleName == "kind" && p.Expression.LiteralValue is "nonempty");

    // IsConstant in Kusto.Language misses some parenthesized arithmetic and let expressions.
    // Only column references outside a nested toscalar depend on this input row.
    private static bool IsSeriesScalar(Expression expr) => expr.IsConstant ||
        expr.GetDescendantsOrSelf<NameReference>(n => n.ReferencedSymbol is ColumnSymbol)
            .All(n => n.GetFirstAncestor<ToScalarExpression>() is { } frozen && (frozen == expr || expr.IsAncestorOf(frozen)));

    private IEnumerable<QueryRow> ApplyMakeSeries(IEnumerable<QueryRow> source, MakeSeriesOperator op)
    {
        var axis = (NameReference)op.OnClause.Expression;
        var type = axis.ResultType;
        var keys = op.ByClause?.Expressions.Select(e => UnwrapOutput(e.Element)).ToArray() ?? [];
        var measures = op.Aggregates.Select(e => MakeMeasure(e.Element)).ToArray();
        var columns = ((TableSymbol)op.ResultType).Columns;
        var (from, to, step) = GetSeriesBounds(op);
        bool nonempty = IsNonempty(op);
        bool legacy = op.RangeClause is MakeSeriesInRangeClause;

        // Keep integer/decimal coordinates and time ticks exact; a double cannot distinguish
        // adjacent longs above 2^53 or adjacent datetime ticks in a contemporary year.
        return type == ScalarTypes.Real
            ? ExpandSeries<double>(source, axis, keys, columns, measures, nonempty, legacy, from, to, step,
                v => Convert.ToDouble(v, CultureInfo.InvariantCulture), v => v)
            : ExpandSeries<decimal>(source, axis, keys, columns, measures, nonempty, legacy, from, to, step,
                v => v switch { DateTime dt => dt.Ticks, TimeSpan ts => ts.Ticks, _ => Convert.ToDecimal(v, CultureInfo.InvariantCulture) },
                v => type == ScalarTypes.DateTime ? new DateTime((long)v, DateTimeKind.Utc) :
                    type == ScalarTypes.TimeSpan ? TimeSpan.FromTicks((long)v) : type == ScalarTypes.Decimal ? (object)v : (long)v);
    }

    private sealed record SeriesMeasure(object? Default, Func<IReadOnlyList<QueryRow>, object?> Reduce);

    private (object? From, object? To, object Step) GetSeriesBounds(MakeSeriesOperator op) => Cached(op, () =>
    {
        var (from, to, step) = SeriesBounds(op.RangeClause);
        object Value(Expression e) => IsSeriesScalar(e) ? EvalOperand(e, []) ??
            throw new ArgumentException("An explicit make-series range bound or step cannot be null.") :
            throw new ArgumentException("make-series bounds and step must be independent of the input row.");
        object? low = from is null ? null : Value(from), high = to is null ? null : Value(to);
        var width = Value(step);
        static bool Finite(object? v) => v is not double d || double.IsFinite(d);
        if (!ScalarMath.IsPositiveBinSize(width) || !Finite(width) || !Finite(low) || !Finite(high))
            throw new ArgumentException("make-series requires finite bounds and a positive finite step.");
        return (low, high, width);
    });

    private SeriesMeasure MakeMeasure(MakeSeriesExpression measure)
    {
        var output = UnwrapOutput(measure.Expression);
        var type = output.ResultType;
        var fill = measure.DefaultExpression?.Expression;
        object? AsElement(object? value) => DynamicValues.PackedScalar(ScalarFunctions.ConvertTo(value, type));
        var defaultValue = AsElement(fill is null ? 0L : EvalOperand(fill, []));
        var calls = output.GetDescendantsOrSelf<FunctionCallExpression>().Where(IsAggregate)
            .ToDictionary(call => call, call => new AggregateCall(call, (expr, row) => EvalOperand(expr, row)));
        var empty = new QueryRow();
        return new SeriesMeasure(defaultValue, rows =>
        {
            var value = EvalOperand(output, empty, call => calls[call].Evaluate(rows).Single());
            return value is null or double.NaN or float.NaN ? defaultValue : AsElement(value);
        });
    }

    private IEnumerable<QueryRow> ExpandSeries<T>(IEnumerable<QueryRow> source, NameReference axis, Expression[] keys,
        IReadOnlyList<ColumnSymbol> columns, SeriesMeasure[] measures, bool nonempty, bool legacy,
        object? from, object? to, object step, Func<object, T> coordinate, Func<T, object> axisValue)
        where T : struct, IFloatingPoint<T>
    {
        T? start = from is null ? null : coordinate(from), stop = to is null ? null : coordinate(to);
        T width = coordinate(step);
        if (stop < start || !legacy && start.HasValue && start == stop) yield break;

        T anchor = legacy ? T.Zero : start ?? stop ?? T.Zero;
        T Index(T value)
        {
            T offset = value - anchor;
            // Removing the remainder first avoids rounding an almost-integral decimal quotient upwards.
            if (typeof(T) == typeof(double)) return T.Floor(offset / width);
            T remainder = offset % width;
            return (offset - remainder) / width - (remainder < T.Zero ? T.One : T.Zero);
        }
        T? first = start is { } lo ? Index(lo) : null, last = null;
        if (stop is { } hi)
        {
            T bin = Index(hi);
            // Legacy range includes the stop bin. With only "to", ADX includes it only
            // when aligned to the zero-origin grid; otherwise the last bin precedes it.
            bool inclusive = legacy || start is null && hi % width == T.Zero;
            last = !inclusive && anchor + bin * width >= hi ? bin - T.One : bin;
        }

        var groups = new Dictionary<object?[], Dictionary<T, List<QueryRow>>>(GroupKeyComparer.Instance);
        foreach (var row in source)
        {
            if (row[axis.SimpleName] is not { } raw) continue;
            T value = coordinate(raw);
            if (!T.IsFinite(value)) continue;
            T index = Index(value);
            if (!T.IsFinite(index)) throw new ArgumentException("The make-series axis cannot represent this bin index.");
            if (index < first || index > last) continue;
            var key = keys.Length == 0 ? [] : Array.ConvertAll(keys, e => EvalOperand(e, row));
            if (!groups.TryGetValue(key, out var bins)) groups.Add(key, bins = []);
            if (!bins.TryGetValue(index, out var rows)) bins.Add(index, rows = []);
            rows.Add(row);
        }
        if (groups.Count == 0 && nonempty) groups.Add([], []);

        foreach (var (key, bins) in groups)
        {
            T? low = first, high = last;
            if (bins.Count != 0)
            {
                low ??= bins.Keys.Min();
                high ??= bins.Keys.Max();
            }
            T length = low is { } l && high is { } h ? h - l + T.One : T.Zero;
            if (!T.IsFinite(length) || length > T.CreateChecked(MaxSeriesLength))
                throw new ArgumentException($"make-series supports at most {MaxSeriesLength} bins per series.");
            int count = int.CreateChecked(length);
            var result = new QueryRow(columns.Count, StringComparer.Ordinal);
            for (int i = 0; i < key.Length; i++) result.Add(columns[i].Name, key[i]);
            for (int i = 0; i < measures.Length; i++)
            {
                var measure = measures[i];
                // A synthetic row with an uninferable bound contains one default but has no axis.
                var series = new object?[Math.Max(count, 1)];
                Array.Fill(series, measure.Default);
                foreach (var (index, rows) in bins)
                    series[int.CreateChecked(index - low!.Value)] = measure.Reduce(rows);
                result.Add(columns[key.Length + i].Name, series);
            }
            var coordinates = new object?[count];
            for (int i = 0; i < count; i++) coordinates[i] = axisValue(anchor + (low!.Value + T.CreateChecked(i)) * width);
            result.Add(columns[^1].Name, coordinates);
            yield return result;
        }
    }
}
