using System.Collections.Immutable;
using System.Globalization;
using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator(IReadOnlyDictionary<string, Table> tables)
{
    private readonly IReadOnlyDictionary<string, Table> _tables = tables;
    private readonly ImmutableDictionary<string, Binding> _bindings = ImmutableDictionary<string, Binding>.Empty;
    private readonly Dictionary<Expression, object?> _cache = [];

    internal ExecutionResult Execute(string query)
    {
        var code = KustoCode.Parse(query);
        // Keep the standalone legacy operators' coercions; typed operators and let bodies use the binder.
        if (code.Syntax.GetFirstDescendant<LetStatement>() is not null ||
            code.Syntax.GetFirstDescendant<Expression>(NeedsBinding) is not null)
        {
            code = Analyze(code);
        }
        var diagnostics = code.HasSemantics ? code.GetDiagnostics() : code.GetSyntaxDiagnostics();
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => new ExecutionError { Code = d.Code, Description = d.Message }).ToList();
        if (errors.Count != 0) return new ExecutionResult { ExecutionErrors = errors };
        if (code.Syntax is not QueryBlock block || block.Statements.Count == 0 ||
            block.Statements[^1].Element is not ExpressionStatement final)
        {
            throw new ArgumentException("A query must end with a tabular expression.", nameof(query));
        }
        var scope = BindStatements(block.Statements.Take(block.Statements.Count - 1).Select(s => s.Element));
        return new ExecutionResult { ResultRows = scope.ExecuteExpression(final.Expression).Select(r => r.AsReadOnly()).ToList() };
    }

    private static bool NeedsBinding(Expression expression) => expression switch
    {
        SummarizeOperator or ParseOperator or GetSchemaOperator or ToScalarExpression or UnionOperator => true,
        FunctionCallExpression f => f.Name.SimpleName is "bin" or "floor" or "bin_at" or "strcat" or "trim"
            or "toint" or "tolong" or "toreal" or "todouble" or "tostring" or "tobool" or "toboolean"
            or "pack_all" or "parse_json" or "parsejson" or "todynamic" or "toobject" or "extract_json" or "extractjson",
        _ => false
    };

    private IEnumerable<Dictionary<string, object?>> ExecuteExpression(Expression expr)
    {
        switch (expr)
        {
            case NameReference nameRef:
                return ReadTable(nameRef.SimpleName, nameRef);
            case ParenthesizedExpression parens:
                return ExecuteExpression(parens.Expression);
            case FunctionCallExpression call:
                return CallTableFunction(call);
            case MaterializeExpression materialize:
                return Cached(materialize, () => ExecuteExpression(materialize.Expression).ToArray());
            case UnionOperator union:
                return ApplyUnion(null, union);
            case PipeExpression pipe:
            {
                if (pipe.Operator is GetSchemaOperator schema) return GetSchema(schema);
                var left = ExecuteExpression(pipe.Expression);
                return ApplyOperator(left, pipe.Operator);
            }
            case PrintOperator printOperator:
            {
                var result = new QueryRow();
                var input = new QueryRow();
                for (int i = 0; i < printOperator.Expressions.Count; i++)
                {
                    var expression = printOperator.Expressions[i].Element;
                    var name = expression is SimpleNamedExpression named ? named.Name.SimpleName : $"print_{i}";
                    result.Add(name, EvalOperand(UnwrapOutput(expression), input));
                }

                return [result];
            }
            default:
            {
                throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}");
            }
        }
    }

    private IEnumerable<Dictionary<string, object?>> ApplyOperator(
        IEnumerable<Dictionary<string, object?>> source,
        QueryOperator op)
    {
        switch (op)
        {
            case FilterOperator filter:
                return ApplyFilter(source, filter);

            case ProjectOperator project:
                return ApplyProject(source, project);

            case TakeOperator take:
                return ApplyTake(source, take);

            case ExtendOperator extend:
                return ApplyExtend(source, extend);

            case SortOperator sort:
                return ApplySort(source, sort);

            case CountOperator countOperator:
                return ApplyCountOperator(source);

            case DistinctOperator distinctOperator:
                return ApplyDistinctOperator(source, distinctOperator);

            case SummarizeOperator summarize:
                return ApplySummarize(source, summarize);

            case ParseOperator parse:
                var pattern = new ParsePattern(parse);
                return source.Select(row => pattern.Apply(row, EvalOperand(parse.Expression, row)));

            case InvokeOperator { Function: FunctionCallExpression call }:
                return CallTableFunction(call, source);

            case UnionOperator union:
                return ApplyUnion(source, union);

            default:
                throw new NotSupportedException($"Unsupported operator: {op.GetType().Name}");
        }
    }

    private IEnumerable<Dictionary<string, object?>> ApplyDistinctOperator(
        IEnumerable<Dictionary<string, object?>> source,
        DistinctOperator distinctOperator)
    {
        var expressions = distinctOperator.Expressions;
        var projected = expressions.Count == 1 && expressions[0].Element is StarExpression
            ? source : SelectColumns(source, expressions, extend: false);
        return projected.DistinctBy(row => row.Values.ToArray(), GroupKeyComparer.Instance);
    }

    private static List<Dictionary<string, object?>> ApplyCountOperator(
        IEnumerable<Dictionary<string, object?>> source)
    {
        long count = source.Count();
        return new List<Dictionary<string, object?>>
        {
            new()
            {
                { "Count", count }
            }
        };
    }

    private IEnumerable<Dictionary<string, object?>> ApplyFilter(
        IEnumerable<Dictionary<string, object?>> source,
        FilterOperator filter)
    {
        bool Predicate(Dictionary<string, object?> row)
        {
            object? result = EvalOperand(filter.Condition, row);
            return Convert.ToBoolean(result, CultureInfo.InvariantCulture);
        }

        return source.Where(Predicate);
    }

    private bool EvaluateInExpression(
        InExpression inExpression,
        Dictionary<string, object?> row)
    {
        (bool inverseResult, StringComparison comparisonType) = inExpression.Kind switch
        {
            SyntaxKind.InExpression => (false, StringComparison.Ordinal),
            SyntaxKind.InCsExpression => (false, StringComparison.OrdinalIgnoreCase),
            SyntaxKind.NotInExpression => (true, StringComparison.Ordinal),
            SyntaxKind.NotInCsExpression => (true, StringComparison.OrdinalIgnoreCase),
            _ => throw new NotSupportedException($"Unsupported In Kind: {inExpression.Kind}")
        };

        var left = EvalOperand(inExpression.Left, row);
        bool Matches(object? candidate) => candidate is object?[] items ? Array.Exists(items, Matches)
            : CompareUtils.AreEqual(left, candidate, comparisonType);
        foreach (var item in inExpression.Right.Expressions)
        {
            var expression = item.Element;
            if (expression.ResultType is TableSymbol || (expression.ResultType is null && expression is PipeExpression or NameReference))
            {
                if (ExecuteExpression(expression).Any(r => Matches(r.Values.FirstOrDefault()))) return !inverseResult;
            }
            else if (Matches(EvalOperand(expression, row))) return !inverseResult;
        }
        return inverseResult;
    }

    private bool EvaluateBetweenExpression(BetweenExpression between, Dictionary<string, object?> row)
    {
        object? left = EvalOperand(between.Right.First, row);
        object? rowValue = EvalOperand(between.Left, row);
        object? right = EvalOperand(between.Right.Second, row);

        if (left is DateTime ldt && right is TimeSpan rsp)
        {
            // to support (datetime(2007-07-27) .. 3d)
            right = ldt.Add(rsp);
        }

        if (between.Kind == SyntaxKind.BetweenExpression)
        {
            // (rowValue >= left and rowValue <= right)
            return CompareUtils.Compare(left, rowValue) <= 0 &&
                   CompareUtils.Compare(rowValue, right) <= 0;
        }

        if (between.Kind == SyntaxKind.NotBetweenExpression)
        {
            // (rowValue < left or rowValue > right)
            return CompareUtils.Compare(rowValue, left) < 0 ||
                   CompareUtils.Compare(right, rowValue) < 0;
        }

        throw new NotSupportedException($"Unsupported Between kind: {between.Kind}");
    }

    private static object? EvaluateBinary(BinaryExpression expression, Func<Expression, object?> evaluate)
    {
        var left = evaluate(expression.Left);
        if (expression.Kind == SyntaxKind.AndExpression && left is false) return false;
        if (expression.Kind == SyntaxKind.OrExpression && left is true) return true;
        var right = evaluate(expression.Right);
        return expression.Kind switch
        {
            SyntaxKind.AndExpression => (bool?)left & (bool?)right,
            SyntaxKind.OrExpression => (bool?)left | (bool?)right,
            SyntaxKind.EqualExpression => CompareUtils.AreEqual(left, right, StringComparison.Ordinal),
            SyntaxKind.EqualTildeExpression => CompareUtils.AreEqual(left, right, StringComparison.OrdinalIgnoreCase),
            SyntaxKind.NotEqualExpression => !CompareUtils.AreEqual(left, right, StringComparison.Ordinal),
            SyntaxKind.BangTildeExpression => !CompareUtils.AreEqual(left, right, StringComparison.OrdinalIgnoreCase),
            SyntaxKind.ContainsExpression => StringOperations.ContainsOperation(left, right),
            SyntaxKind.NotContainsExpression => !StringOperations.ContainsOperation(left, right),
            SyntaxKind.StartsWithExpression => StringOperations.StartsWithOperation(left, right),
            SyntaxKind.NotStartsWithExpression => !StringOperations.StartsWithOperation(left, right),
            SyntaxKind.EndsWithExpression => StringOperations.EndsWithOperation(left, right),
            SyntaxKind.NotEndsWithExpression => !StringOperations.EndsWithOperation(left, right),
            SyntaxKind.MatchesRegexExpression => StringOperations.MatchesRegexExpressionOperation(left, right),
            _ when left is null || right is null => null,
            SyntaxKind.GreaterThanExpression => CompareUtils.Compare(left, right) > 0,
            SyntaxKind.GreaterThanOrEqualExpression => CompareUtils.Compare(left, right) >= 0,
            SyntaxKind.LessThanExpression => CompareUtils.Compare(left, right) < 0,
            SyntaxKind.LessThanOrEqualExpression => CompareUtils.Compare(left, right) <= 0,
            _ => ScalarMath.Arithmetic(expression, left, right)
        };
    }

    private object? EvalOperand(Expression expr, Dictionary<string, object?> row,
        Func<FunctionCallExpression, object?>? aggregate = null)
    {
        object? Evaluate(Expression child) => EvalOperand(child, row, aggregate);
        switch (expr)
        {
            case NameReference nr:
                if (row.TryGetValue(nr.SimpleName, out var value)) return value;
                return _bindings.TryGetValue(nr.SimpleName, out var binding) && binding is ValueBinding scalar
                    ? scalar.Evaluate() : throw new ArgumentException($"Unknown scalar: {nr.SimpleName}");
            case SimpleNamedExpression named:
                return Evaluate(named.Expression);
            case ParenthesizedExpression parens:
                return Evaluate(parens.Expression);
            case PrefixUnaryExpression unary:
                var operand = Evaluate(unary.Expression);
                return (unary.Operator.Kind, operand) switch
                {
                    (_, null) => null,
                    (SyntaxKind.PlusToken, _) => operand,
                    (SyntaxKind.MinusToken, int i) => -i,
                    (SyntaxKind.MinusToken, long l) => -l,
                    (SyntaxKind.MinusToken, double d) => -d,
                    (SyntaxKind.MinusToken, float f) => -f,
                    (SyntaxKind.MinusToken, decimal d) => -d,
                    (SyntaxKind.MinusToken, TimeSpan span) => -span,
                    (SyntaxKind.BangToken, bool flag) => !flag,
                    _ => throw new NotSupportedException($"Unsupported unary expression: {unary.Kind}")
                };
            case LiteralExpression lit:
                return ParseLiteral(lit);
            case CompoundStringLiteralExpression compound:
                return compound.LiteralValue;
            case DynamicExpression dynamicLiteral:
                return Evaluate(dynamicLiteral.Expression);
            case JsonObjectExpression json:
                var bag = new QueryRow();
                foreach (var pair in json.Pairs)
                    bag[pair.Element.Name.ValueText] = Evaluate(pair.Element.Value);
                return bag;
            case JsonArrayExpression array:
                return array.Values.Select(item => Evaluate(item.Element)).ToArray();
            case PathExpression { Selector: NameReference property } path:
                return DynamicValues.Get(Evaluate(path.Expression), property.SimpleName);
            case PathExpression { Selector: BracketedExpression index } path:
                return DynamicValues.Get(Evaluate(path.Expression), Evaluate(index.Expression));
            case ElementExpression element:
                return DynamicValues.Get(Evaluate(element.Expression), Evaluate(element.Selector));
            case BracketedExpression bracket:
                return Evaluate(bracket.Expression);
            case TypeOfLiteralExpression type:
                return type.ReferencedSymbol as TypeSymbol ?? throw new ArgumentException($"Unknown type: {type}");
            case ToScalarExpression frozen:
                return Cached(frozen, () => frozen.Expression.ResultType is TableSymbol
                    ? ExecuteExpression(frozen.Expression).FirstOrDefault()?.Values.FirstOrDefault()
                    : EvalOperand(frozen.Expression, new QueryRow()));
            case BinaryExpression binary:
                return EvaluateBinary(binary, Evaluate);
            case BetweenExpression between:
                return EvaluateBetweenExpression(between, row);
            case InExpression membership:
                return EvaluateInExpression(membership, row);
            case FunctionCallExpression call when aggregate is not null && IsAggregate(call):
                return aggregate(call);
            case FunctionCallExpression call when _bindings.GetValueOrDefault(call.Name.SimpleName) is FunctionBinding udf:
                var (scope, body) = EnterFunction(udf, call, call.ArgumentList.Expressions.Select(e => e.Element), Evaluate);
                return scope.EvalOperand(body, new QueryRow());
            case FunctionCallExpression call:
                return EvaluateFunction(call, call.ArgumentList.Expressions.Select(e => Evaluate(e.Element)).ToArray(), row);
            default:
                throw new NotSupportedException($"Unsupported operand: {expr.GetType().Name}");
        }
    }

    private static object? EvaluateFunction(FunctionCallExpression call, object?[] args, QueryRow row)
    {
        string functionName = call.Name.SimpleName;
        return functionName switch
        {
            "base64_encode_tostring" => FunctionExpressions.Base64EncodeToString(args),
            "base64_decode_tostring" => FunctionExpressions.Base64DecodeToString(args),
            "url_encode" => FunctionExpressions.UrlEncode(args),
            "url_decode" => FunctionExpressions.UrlDecode(args),
            "toupper" => FunctionExpressions.ToUpper(args),
            "tolower" => FunctionExpressions.ToLower(args),
            "strlen" when call.ResultType == Kusto.Language.Symbols.ScalarTypes.Long => (long)FunctionExpressions.StrLen(args),
            "strlen" => FunctionExpressions.StrLen(args),
            "todatetime" => FunctionExpressions.ToDateTime(args),
            "make_timespan" => FunctionExpressions.MakeTimeSpan(args),
            "totimespan" => FunctionExpressions.ToTimeSpan(args),
            "now" => FunctionExpressions.Now(args),
            "ago" => FunctionExpressions.Ago(args),
            "bin_at" => ScalarMath.Bin(args, call.ResultType),
            "bin" or "floor" or "strcat" or "trim" or "toint" or "tolong" or "toreal" or "todouble"
                or "tostring" or "tobool" or "toboolean" => ScalarFunctions.Evaluate(call, args),
            "isnull" => args[0] is null,
            "isnotnull" => args[0] is not null,
            "not" => !(bool?)args[0],
            "pack_array" => args,
            "parse_json" or "todynamic" or "parsejson" or "toobject" =>
                call.ArgumentList.Expressions[0].Element.ResultType is DynamicSymbol ? args[0] : DynamicValues.Parse(args[0]),
            "extract_json" or "extractjson" => DynamicValues.Extract((string?)args[0], args[1], args.Length == 3 ? (TypeSymbol?)args[2] : null),
            "pack_all" => DynamicValues.PackAll(row, args),
            "gettype" => args[0] switch
            {
                null => "null",
                QueryRow => "dictionary",
                object?[] => "array",
                var v => ColumnType(v.GetType()).Name
            },
            "new_guid" => Guid.NewGuid(),
            "bag_pack" => DynamicValues.Pack(args),
            "dcount_hll" => args[0] is DistinctState state ? state.Count : null,
            _ => throw new NotSupportedException($"Function {functionName} not implemented.")
        };
    }

    private IEnumerable<QueryRow> ApplyProject(IEnumerable<QueryRow> source, ProjectOperator project) =>
        SelectColumns(source, project.Expressions, extend: false);

    private IEnumerable<QueryRow> SelectColumns(IEnumerable<QueryRow> source,
        SyntaxList<SeparatedElement<Expression>> expressions, bool extend)
    {
        var props = expressions.Select((item, index) => item.Element switch
        {
            NameReference name => (Alias: name.SimpleName, Expr: (Expression)name),
            SimpleNamedExpression named => (Alias: named.Name.SimpleName, Expr: named.Expression),
            _ when extend && expressions.Parent is ExtendOperator { ResultType: TableSymbol outputs } =>
                (Alias: outputs.Columns.FirstOrDefault(c => c.Source == item.Element)?.Name ??
                    throw new ArgumentException($"Cannot determine the output column for {item.Element}"), Expr: item.Element),
            _ when !extend && expressions.Parent is Expression { ResultType: TableSymbol schema } =>
                (Alias: schema.Columns[index].Name, Expr: item.Element),
            _ => throw new NotSupportedException($"Unsupported column expression: {item.Element.Kind}")
        }).ToArray();

        return source.Select(row =>
        {
            // Sibling expressions, including pack_all(), see the incoming row, not each other's output.
            var dict = extend ? new QueryRow(row) : new QueryRow();
            foreach (var (alias, expression) in props)
            {
                dict[alias] = EvalOperand(expression, row);
            }

            return dict;
        });
    }

    private static IEnumerable<Dictionary<string, object?>> ApplySort(
        IEnumerable<Dictionary<string, object?>> source,
        SortOperator sort)
    {
        if (sort.Expressions.Count == 0)
        {
            return source; // nothing to sort by
        }

        // not sure, should we materialize source here,
        // or it is fine to iterate over source several times,
        // when we have several sort columns.
        List<Dictionary<string, object?>> sourceCopy = source.ToList();

        IOrderedEnumerable<Dictionary<string, object?>>? ordered = null;

        foreach (var exprElement in sort.Expressions)
        {
            Expression? expr = exprElement.Element;

            bool descending = true;
            if (expr is OrderedExpression oexp)
            {
                descending = oexp.Ordering.AscOrDescKeyword.Kind == SyntaxKind.DescKeyword;
            }

            Func<Dictionary<string, object?>, object?> keySelector = row =>
            {
                if (expr is NameReference nameRef)
                {
                    return row.GetValueOrDefault(nameRef.Name.SimpleName);
                }

                if (expr is SimpleNamedExpression sne && sne.Expression is NameReference)
                {
                    return row.GetValueOrDefault(sne.Name.SimpleName);
                }

                if (expr is OrderedExpression oe && oe.Expression is NameReference orderedInner)
                {
                    return row.GetValueOrDefault(orderedInner.Name.SimpleName);
                }

                throw new NotSupportedException($"Expression {expr} not supported in sort.");
            };

            Comparer<object?> comparer = Comparer<object?>.Create((a, b) =>
                a is null ? (b is null ? 0 : -1) : b is null ? 1 : CompareUtils.Compare(a, b));

            if (ordered == null)
            {
                ordered = descending
                    ? sourceCopy.OrderByDescending(keySelector, comparer)
                    : sourceCopy.OrderBy(keySelector, comparer);
            }
            else
            {
                ordered = descending
                    ? ordered.ThenByDescending(keySelector, comparer)
                    : ordered.ThenBy(keySelector, comparer);
            }
        }

        return ordered ?? source;
    }

    private IEnumerable<QueryRow> ApplyExtend(IEnumerable<QueryRow> source, ExtendOperator extend) =>
        SelectColumns(source, extend.Expressions, extend: true);

    private IEnumerable<QueryRow> ApplyTake(IEnumerable<QueryRow> source, TakeOperator take)
    {
        var limit = EvalOperand(take.Expression, new QueryRow());
        if (limit is not (int or long)) throw new ArgumentException("take expects an integer.");
        return source.Take((int)Math.Clamp(Convert.ToInt64(limit, CultureInfo.InvariantCulture), 0, int.MaxValue));
    }

    private static object? ParseLiteral(LiteralExpression lit) => lit.Kind switch
    {
        SyntaxKind.NullLiteralExpression => null,
        SyntaxKind.DateTimeLiteralExpression when lit.Token.Text is "datetime()" or "datetime(now)" => DateTime.UtcNow,
        SyntaxKind.RealLiteralExpression when lit.Token.Text is "real(+inf)" or "real(inf)" => double.PositiveInfinity,
        SyntaxKind.RealLiteralExpression when lit.Token.Text == "real(-inf)" => double.NegativeInfinity,
        _ => lit.LiteralValue
    };
}