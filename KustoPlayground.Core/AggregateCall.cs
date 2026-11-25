using System.Globalization;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed class AggregateCall
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private readonly string _name;
    private readonly FunctionCallExpression _call;
    private readonly Expression[] _args;
    private readonly Func<Expression, QueryRow, object?> _eval;
    private readonly int _predicate;
    private readonly int _limit;
    private readonly bool _weighted;
    private readonly decimal[]? _percentages;

    internal AggregateCall(FunctionCallExpression call, Func<Expression, QueryRow, object?> eval)
    {
        _call = call;
        _eval = eval;
        _args = call.ArgumentList.Expressions.Select(e => e.Element).ToArray();
        var spelling = call.Name.SimpleName;
        _name = spelling switch
        {
            "cnt" => "count", "any" => "take_any", "anyif" => "take_anyif",
            "argmax" => "arg_max", "argmin" => "arg_min", "makelist" => "make_list", "makeset" => "make_set",
            "make_dictionary" => "make_bag",
            _ => spelling
        };
        _predicate = _name switch
        {
            "countif" => 0,
            "covarianceif" or "covariancepif" => 2,
            _ when _name.EndsWith("if", StringComparison.Ordinal) => 1,
            _ => -1
        };
        if (_name is "arg_max" or "arg_min" && _args.Length < 2)
            throw new ArgumentException($"{_name} requires a comparison expression and at least one return column or *.");

        if (_name is "dcount" or "dcountif" or "hll")
        {
            var accuracyIndex = _name == "dcountif" ? 2 : 1;
            if (_args.Length > accuracyIndex && Convert.ToInt64(Constant(_args[accuracyIndex]), Culture) is < 0 or > 4)
                throw new ArgumentOutOfRangeException(nameof(call), "Distinct-count accuracy must be between 0 and 4.");
        }
        if (_name.StartsWith("percentile", StringComparison.Ordinal))
        {
            _weighted = _name is "percentilew" or "percentilesw" or "percentilesw_array";
            _percentages = _args.Skip(_weighted ? 2 : 1).Select(Constant)
                .SelectMany(value => value is object?[] items ? items : [value])
                .Select(p => p is not null && CompareUtils.IsNumeric(p) ? Convert.ToDecimal(p, Culture) :
                    throw new ArgumentException("Percentiles must be numeric constants.")).ToArray();
            if (_percentages.Any(p => p is < 0 or > 100))
                throw new ArgumentOutOfRangeException(nameof(call), "Percentiles must be between 0 and 100.");
        }
        if (_name.StartsWith("make_", StringComparison.Ordinal))
        {
            int limitIndex = _predicate < 0 ? 1 : 2;
            _limit = _args.Length > limitIndex ? Convert.ToInt32(Constant(_args[limitIndex]), Culture)
                : spelling is "makelist" or "makeset" ? 128 : 1_048_576;
            if (_limit is < 1 or > 1_048_576)
                throw new ArgumentOutOfRangeException(nameof(call), "Collection size must be between 1 and 1048576.");
        }
    }

    internal object?[] Evaluate(IReadOnlyList<QueryRow> group)
    {
        IEnumerable<QueryRow> rows = _predicate < 0 ? group : group.Where(r => Value(_predicate, r) is true);
        if (_name is "arg_max" or "arg_min" or "take_any" or "take_anyif") return SelectRow(rows);
        if (_percentages is not null) return Percentiles(rows, _percentages);

        var values = rows.Select(r => Value(0, r)).Where(v => v is not null).Select(v => v!);
        object? result = _name switch
        {
            "count" => (long)group.Count,
            "countif" => rows.LongCount(),
            "dcount" or "dcountif" or "count_distinct" or "count_distinctif" => values.Distinct(DynamicCollections.Comparer).LongCount(),
            "hll" => new DistinctState(values),
            "hll_merge" => DistinctState.Merge(values.Cast<DistinctState>()),
            "sum" or "sumif" or "avg" or "avgif" => NumericAggregates.Total(values, _args[0].ResultType, _name.StartsWith("avg", StringComparison.Ordinal)),
            "min" or "minif" or "max" or "maxif" => Extreme(values, _name.StartsWith("max", StringComparison.Ordinal)),
            "variance" or "variancep" or "varianceif" or "variancepif" or "stdev" or "stdevp" or "stdevif" or "stdevpif"
                or "covariance" or "covariancep" or "covarianceif" or "covariancepif" => Dispersion(rows),
            "make_list" or "make_list_if" or "make_list_with_nulls" or "make_set" or "make_set_if" => Collect(rows),
            "make_bag" or "make_bag_if" => MakeBag(values),
            "binary_all_and" or "binary_all_or" or "binary_all_xor" => ScalarFunctions.ConvertTo(Bitwise(values), _call.ResultType),
            _ => throw new NotSupportedException($"Aggregate {_name} is not implemented.")
        };
        return [result];
    }

    private object? Value(int argument, QueryRow row) => _eval(_args[argument], row);

    private object Constant(Expression arg) => arg.IsConstant && _eval(arg, new QueryRow()) is { } value
        ? value : throw new ArgumentException($"{_name} requires a non-null constant for {arg}.");

    private static int Compare(object x, object y) => x is string s && y is string t
        ? string.CompareOrdinal(s, t) : ((IComparable)x).CompareTo(y);

    private static string? EmptyValue(TypeSymbol type) => type == ScalarTypes.String ? string.Empty : null;

    private object? Extreme(IEnumerable<object> values, bool maximum)
    {
        object? best = null;
        foreach (var value in values)
        {
            if (value is "") continue;
            if (best is null || (maximum ? Compare(value, best) > 0 : Compare(value, best) < 0)) best = value;
        }
        return best ?? EmptyValue(_args[0].ResultType);
    }

    private object?[] SelectRow(IEnumerable<QueryRow> rows)
    {
        QueryRow? winner = null;
        object? best = null;
        bool extreme = _name is "arg_max" or "arg_min";
        foreach (var row in rows)
        {
            if (!extreme)
            {
                winner ??= row;
                if (_args[0] is StarExpression || (_name == "take_any" && _args.Length > 1)) break;
                if (Value(0, row) is not (null or "")) { winner = row; break; }
                continue;
            }
            var value = Value(0, row);
            if (winner is null || (value is not null && (best is null ||
                (_name == "arg_max" ? Compare(value, best) > 0 : Compare(value, best) < 0))))
            {
                winner = row;
                best = value;
            }
        }
        if (_call.RawResultType is not TupleSymbol tuple)
            return [winner is null ? EmptyValue(_call.ResultType) : Value(0, winner)];

        return tuple.Columns.Select(column =>
        {
            if (winner is null) return EmptyValue(column.Type);
            // Computed arguments belong to this call. Star expansion and forwarded columns instead
            // refer to the current input schema, possibly produced by an earlier pipeline operator.
            if (column.Source is Expression expression && _call.IsAncestorOf(expression)) return _eval(expression, winner);
            return winner[column.OriginalColumns.Count > 0 ? column.OriginalColumns[0].Name : column.Name];
        }).ToArray();
    }

    private object?[] Percentiles(IEnumerable<QueryRow> rows, decimal[] percentages)
    {
        var sorted = new List<(object Value, long Weight)>();
        decimal total = 0;
        foreach (var row in rows)
        {
            var value = Value(0, row);
            long weight = _weighted ? Convert.ToInt64(Value(1, row), Culture) : 1;
            if (value is null || weight <= 0) continue;
            sorted.Add((value, weight));
            total += weight;
        }
        sorted.Sort((a, b) => Compare(a.Value, b.Value));
        var results = new object?[percentages.Length];
        if (sorted.Count != 0)
        {
            int i = 0;
            decimal cumulative = sorted[0].Weight;
            foreach (var index in Enumerable.Range(0, percentages.Length).OrderBy(n => percentages[n]))
            {
                decimal rank = decimal.Ceiling(total * percentages[index] / 100);
                while (cumulative < rank) cumulative += sorted[++i].Weight;
                results[index] = sorted[i].Value switch
                {
                    int n => (long)n,
                    decimal d => (double)d,
                    var value => value
                };
            }
        }
        return _name.EndsWith("_array", StringComparison.Ordinal) ? [results] : results;
    }

    private double Dispersion(IEnumerable<QueryRow> rows)
    {
        bool covariance = _name.StartsWith("covariance", StringComparison.Ordinal);
        var moments = new NumericAggregates.Moments();
        foreach (var row in rows)
        {
            var x = Value(0, row);
            var y = covariance ? Value(1, row) : x;
            if (x is not null && y is not null) moments.Add(Convert.ToDouble(x, Culture), Convert.ToDouble(y, Culture));
        }
        bool population = _name is "stdevp" or "stdevpif" or "variancep" or "variancepif" or "covariancep" or "covariancepif";
        var result = moments.Covariance(population);
        return _name.StartsWith("stdev", StringComparison.Ordinal) ? Math.Sqrt(Math.Max(0, result)) : result;
    }

    private object?[] Collect(IEnumerable<QueryRow> rows)
    {
        var result = new List<object?>();
        var seen = _name.StartsWith("make_set", StringComparison.Ordinal) ? new HashSet<object?>(DynamicCollections.Comparer) : null;
        foreach (var row in rows)
        {
            var value = Value(0, row);
            if (value is object?[] array)
            {
                foreach (var item in array) { Add(item); if (result.Count == _limit) break; }
            }
            else Add(value);
            if (result.Count == _limit) break;
        }
        return result.ToArray();

        void Add(object? value)
        {
            value = DynamicValues.PackedScalar(value);
            if ((value is not null || _name == "make_list_with_nulls") && (seen is null || seen.Add(value))) result.Add(value);
        }
    }

    private Dictionary<string, object?> MakeBag(IEnumerable<object> values)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var bag in values.OfType<IReadOnlyDictionary<string, object?>>())
        {
            foreach (var (key, value) in bag)
            {
                result.TryAdd(key, value);
                if (result.Count == _limit) return result;
            }
        }
        return result;
    }

    private long? Bitwise(IEnumerable<object> values)
    {
        long? result = null;
        foreach (var value in values)
        {
            long n = Convert.ToInt64(value, Culture);
            result = result is null ? n : _name switch
            {
                "binary_all_and" => result.Value & n,
                "binary_all_or" => result.Value | n,
                _ => result.Value ^ n
            };
        }
        return result;
    }
}

// Exact, mergeable distinct state for in-memory queries; not the external ADX HLL wire format.
internal sealed class DistinctState(IEnumerable<object> values)
{
    private readonly HashSet<object> _values = new(values);
    internal long Count => _values.Count;
    internal static DistinctState Merge(IEnumerable<DistinctState> states) => new(states.SelectMany(s => s._values));
}
