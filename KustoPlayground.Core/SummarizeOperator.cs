using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator
{
    private static readonly GlobalState QueryGlobals = CreateQueryGlobals();

    private static GlobalState CreateQueryGlobals()
    {
        // The service accepts the documented count(column) form; it counts rows, not non-null values.
        // The language package still declares the obsolete optional argument as bool.
        var globals = GlobalState.Default;
        var count = new FunctionSymbol("count", ScalarTypes.Long, new Parameter("value", ParameterTypeKind.Scalar, minOccurring: 0))
            .WithResultNameKind(ResultNameKind.PrefixAndFirstArgument).WithResultNamePrefix("count");
        var unpack = globals.GetPlugIn("bag_unpack").Signatures[0];
        var unpackSignature = new Signature(unpack.CustomReturnType, Tabularity.Tabular,
            [.. unpack.Parameters, new Parameter("columnsConflict", ScalarTypes.String, minOccurring: 0),
                new Parameter("ignoredProperties", ScalarTypes.Dynamic, minOccurring: 0)])
            .WithLayout((signature, args, parameters) =>
            {
                for (int i = 0; i < args.Count; i++)
                    parameters.Add(args[i] is SimpleNamedExpression named ? signature.GetParameter(named.Name.SimpleName)
                        : i < signature.Parameters.Count ? signature.Parameters[i] : Signature.UnknownParameter);
            });
        var bagUnpack = new FunctionSymbol("bag_unpack", unpackSignature);
        var aggregates = globals.Aggregates.Select(f => f.Name switch
        {
            "count" => count,
            "cnt" => f.WithResultNamePrefix("count"),
            "sum" or "sumif" or "avg" or "avgif" => AddSignature(f, new Signature(
                f.Name.StartsWith("avg", StringComparison.Ordinal) ? ScalarTypes.Real : ScalarTypes.Long,
                [new Parameter("expr", ScalarTypes.Bool), .. f.Signatures[0].Parameters.Skip(1)])),
            "covariance" or "covariancep" or "covarianceif" or "covariancepif" =>
                new FunctionSymbol(f.Name, context =>
                {
                    // A single-field tuple supplies the projected name but still reduces to a scalar in arithmetic.
                    var names = context.Arguments.Take(2).Select(a => KustoFacts.GetExpressionResultName(a, "", context.RowScope));
                    return new TupleSymbol(new ColumnSymbol(f.Name + "_" + string.Join("_", names), ScalarTypes.Real));
                }, Tabularity.Scalar, f.Signatures[0].Parameters),
            _ => f
        }).ToArray();
        var functions = globals.Functions.Select(f => f.Name is "bin" or "floor"
            ? AddSignature(f, new Signature(ScalarTypes.DateTime, new Parameter("value", ScalarTypes.DateTime),
                new Parameter("roundTo", ParameterTypeKind.Number))) : f).ToArray();
        return globals.WithFunctions(functions).WithAggregates(aggregates)
            .WithPlugIns(globals.PlugIns.Select(f => f.Name == "bag_unpack" ? bagUnpack : f).ToArray());
    }

    private static FunctionSymbol AddSignature(FunctionSymbol f, Signature signature) =>
        new FunctionSymbol(f.Name, f.Signatures.Append(signature), f.Description)
            .WithResultNameKind(f.ResultNameKind).WithResultNamePrefix(f.ResultNamePrefix).WithIsConstantFoldable(f.IsConstantFoldable);

    private KustoCode Analyze(KustoCode code)
    {
        var tables = _tables.Values.Select(t => new TableSymbol(t.Name, t.Schema.Values.Select(c =>
            new ColumnSymbol(c.Name, ColumnType(c.GetColumnType())))));
        var bound = NormalizeJoinConditions(code).Analyze(QueryGlobals.WithDatabase(new DatabaseSymbol("Playground", tables)));
        return bound.Syntax.GetFirstDescendant<UnionOperator>() is not null ? BindViewUnions(bound) : bound;
    }

    private static ScalarSymbol ColumnType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(TimeSpan)) return ScalarTypes.TimeSpan;
        if (type == typeof(Guid)) return ScalarTypes.Guid;
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => ScalarTypes.Bool,
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 => ScalarTypes.Int,
            TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 => ScalarTypes.Long,
            TypeCode.Double or TypeCode.Single => ScalarTypes.Real,
            TypeCode.Decimal => ScalarTypes.Decimal,
            TypeCode.DateTime => ScalarTypes.DateTime,
            TypeCode.Char or TypeCode.String => ScalarTypes.String,
            _ => ScalarTypes.Dynamic
        };
    }

    private static bool IsAggregate(FunctionCallExpression call) =>
        call.ReferencedSymbol is FunctionSymbol function && QueryGlobals.IsAggregateFunction(function);

    private static Expression UnwrapOutput(Expression expression) => expression switch
    {
        SimpleNamedExpression named => named.Expression,
        CompoundNamedExpression tuple => tuple.Expression,
        _ => expression
    };

    private IEnumerable<QueryRow> ApplySummarize(IEnumerable<QueryRow> source, SummarizeOperator op)
    {
        var keys = op.ByClause?.Expressions.Select(e => UnwrapOutput(e.Element)).ToArray() ?? [];
        var outputs = op.Aggregates.Select(e => UnwrapOutput(e.Element)).ToArray();
        if (keys.Length == 0 && outputs.Length == 0)
            throw new ArgumentException("summarize requires at least one aggregation or group key.");

        var columns = ((TableSymbol)op.ResultType).Columns;
        var calls = op.Aggregates.GetDescendants<FunctionCallExpression>().Where(IsAggregate)
            .ToDictionary(call => call, call => new AggregateCall(call, EvalArgument));
        object? EvalArgument(Expression expr, QueryRow row) => EvalOperand(expr, row);

        QueryRow Emit(object?[] key, IEnumerable<QueryRow> group)
        {
            var result = new QueryRow(columns.Count, StringComparer.Ordinal);
            int position = 0;
            void Append(object? value) => result.Add(columns[position++].Name, value);
            foreach (var value in key) Append(value);
            var rows = group as IReadOnlyList<QueryRow> ?? group.ToArray();
            object? ScalarAggregate(FunctionCallExpression f) => calls[f].Evaluate(rows).Single();
            foreach (var output in outputs)
            {
                if (output is FunctionCallExpression call && calls.TryGetValue(call, out var aggregate))
                {
                    foreach (var value in aggregate.Evaluate(rows)) Append(value);
                }
                else
                {
                    Append(EvalOperand(output, result, ScalarAggregate));
                }
            }
            return result;
        }

        if (keys.Length == 0) return [Emit([], source)];
        return source.GroupBy(row => Array.ConvertAll(keys, e => EvalOperand(e, row)), GroupKeyComparer.Instance)
            .Select(g => Emit(g.Key, g));
    }

    private sealed class GroupKeyComparer : IEqualityComparer<object?[]>
    {
        internal static readonly GroupKeyComparer Instance = new();
        public bool Equals(object?[]? x, object?[]? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.AsSpan().SequenceEqual(y));

        public int GetHashCode(object?[] obj)
        {
            var hash = new HashCode();
            foreach (var component in obj) hash.Add(component);
            return hash.ToHashCode();
        }
    }
}
