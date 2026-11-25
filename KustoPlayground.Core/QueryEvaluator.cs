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
    private readonly DateTime _queryTime = DateTime.UtcNow;
    private readonly ImmutableDictionary<string, Binding> _bindings = ImmutableDictionary<string, Binding>.Empty;
    private readonly Dictionary<Expression, object?> _cache = [];

    internal ExecutionResult Execute(string query, bool includeSchema)
    {
        var code = Analyze(KustoCode.Parse(query));
        var errors = code.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && !IsServiceCompatible(code, d))
            .Select(d => new ExecutionError { Code = d.Code, Description = d.Message }).ToList();
        if (errors.Count == 0) errors.AddRange(ValidateServiceSemantics(code));
        if (errors.Count != 0) return new ExecutionResult { ExecutionErrors = errors };
        if (code.Syntax is not QueryBlock block || block.Statements.Count == 0 ||
            block.Statements[^1].Element is not ExpressionStatement final)
        {
            throw new ArgumentException("A query must end with a tabular expression.", nameof(query));
        }
        var scope = BindStatements(block.Statements.Take(block.Statements.Count - 1).Select(s => s.Element));
        scope.ValidateConstantArguments(final.Expression);
        var rows = scope.ExecuteExpression(final.Expression).Select(r => r.AsReadOnly()).ToList();
        return new ExecutionResult
        {
            ResultRows = rows,
            Columns = includeSchema ? scope.DescribeColumns(final.Expression,
                block.GetFirstDescendant<GetSchemaOperator>(op => op.KindParameter is null)).ToArray() : null
        };
    }

    private static bool IsServiceCompatible(KustoCode code, Diagnostic diagnostic)
    {
        if (diagnostic.Code is not ("KS005" or "KS180" or "KS141" or "KS178" or "KS147" or "KS234" or "KS235") || !diagnostic.HasLocation) return false;
        var node = code.Syntax.GetNodeAt(diagnostic.Start, diagnostic.Length);
        if (diagnostic.Code is "KS141" or "KS178" && IsIntegralSeriesBound(node, diagnostic)) return true;
        if (diagnostic.Code == "KS005")
            return node is LiteralExpression { Kind: SyntaxKind.TokenLiteralExpression,
                Parent: NamedParameter { Name.SimpleName: "kind", Parent.Parent: ParseOperator } };
        if (diagnostic.Code == "KS180")
            return node.GetFirstAncestor<NamedParameter>() is { Name.SimpleName: "kind", Parent.Parent: JoinOperator };
        // Literal dynamic shapes are known to the binder; the service handles a missing slot
        // or an unsuitable collection at runtime just like a stored dynamic column.
        if (diagnostic.Code is "KS234" or "KS235")
            return node is Expression { ResultType: DynamicSymbol } && node.GetFirstAncestor<FunctionCallExpression>() is { } call
                && DynamicCollections.Supports(call.Name.SimpleName) && call.ArgumentList.Expressions.Any(a => a.Element == node);
        var bracket = node.GetFirstAncestorOrSelf<BracketedExpression>();
        return bracket?.Parent is ElementExpression { Expression.ResultType: DynamicSymbol } access && access.Selector == bracket &&
            (diagnostic.Code == "KS147" ? node == bracket : node == bracket.Expression);
    }

    private IEnumerable<Dictionary<string, object?>> ExecuteExpression(Expression expr)
    {
        switch (expr)
        {
            case DataTableExpression data:
                return ReadDataTable(data);
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
                if (pipe.Operator is GetSchemaOperator schema)
                {
                    if (pipe.Expression.ResultType is TableSymbol { IsOpen: true })
                        _ = ExecuteExpression(pipe.Expression);
                    return GetSchema(schema);
                }
                var left = ExecuteExpression(pipe.Expression);
                return ApplyOperator(left, pipe.Operator);
            }
            case PrintOperator printOperator:
                return SelectColumns([new QueryRow()], printOperator.Expressions, extend: false);
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

            case TopOperator top:
                return TakeRows(OrderRows(source, [top.ByExpression]), top.Expression);

            case ProjectAwayOperator away:
                var retained = ColumnsOf((PipeExpression)away.Parent!);
                return source.Select(row => retained.ToDictionary(c => c.Name, c => row[c.Name], StringComparer.Ordinal));

            case CountOperator countOperator:
                return ApplyCountOperator(source);

            case DistinctOperator distinctOperator:
                return ApplyDistinctOperator(source, distinctOperator);

            case SummarizeOperator summarize:
                return ApplySummarize(source, summarize);

            case MakeSeriesOperator series:
                return ApplyMakeSeries(source, series);

            case ParseOperator parse:
                var pattern = new ParsePattern(parse);
                return source.Select(row => pattern.Apply(row, EvalOperand(parse.Expression, row)));

            case InvokeOperator { Function: FunctionCallExpression call }:
                return CallTableFunction(call, source);

            case UnionOperator union:
                return ApplyUnion(source, union);

            case JoinOperator join:
                return ApplyJoin(source, join);

            case EvaluateOperator plugin:
                return UnpackBag(source, plugin);

            case MvExpandOperator expand:
                return ApplyMvExpand(source, expand);

            case MvApplyOperator apply:
                return ApplyMvApply(source, apply);

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

    private IEnumerable<QueryRow> ApplyFilter(IEnumerable<QueryRow> source, FilterOperator filter) =>
        source.Where(row => EvalOperand(filter.Condition, row) switch
        {
            bool keep => keep,
            null => false,
            _ => throw new ArgumentException("where requires a boolean predicate.")
        });

    private bool? EvaluateInExpression(
        InExpression inExpression,
        QueryRow row, Func<Expression, object?> evaluate)
    {
        (bool inverseResult, StringComparison comparisonType) = inExpression.Kind switch
        {
            SyntaxKind.InExpression => (false, StringComparison.Ordinal),
            SyntaxKind.InCsExpression => (false, StringComparison.OrdinalIgnoreCase),
            SyntaxKind.NotInExpression => (true, StringComparison.Ordinal),
            SyntaxKind.NotInCsExpression => (true, StringComparison.OrdinalIgnoreCase),
            _ => throw new NotSupportedException($"Unsupported In Kind: {inExpression.Kind}")
        };

        var left = evaluate(inExpression.Left);
        bool? Matches(object? candidate)
        {
            if (candidate is not object?[] array) return CompareUtils.AreEqual(left, candidate, comparisonType);
            bool? found = false;
            foreach (var value in array)
            {
                found |= Matches(value);
                if (found is true) break;
            }
            return found;
        }
        bool? match = false;
        foreach (var item in inExpression.Right.Expressions)
        {
            var expression = UnwrapParens(item.Element);
            if (expression.ResultType is TableSymbol)
            {
                // Tabular membership tests set inclusion: a null subquery value is never a match.
                var candidates = Cached(expression, () => ExecuteExpression(expression).Select(r => r.Values.FirstOrDefault()).ToArray());
                if (left is not null && Array.Exists(candidates, v => Matches(v) is true))
                    return !inverseResult;
            }
            else
            {
                var candidate = evaluate(expression);
                if (expression.ResultType is DynamicSymbol)
                {
                    if (candidate is QueryRow)
                        throw new ArgumentException("in expects a constant dynamic array, not a row-dependent value or a property bag.");
                    if (inExpression.Left.ResultType is DynamicSymbol && expression.ResultType is DynamicArraySymbol array &&
                        array.ElementType != ScalarTypes.String && array.ElementType is not DynamicSymbol)
                        throw new ArgumentException("Cast the dynamic left operand before testing membership in a non-string array.");
                }
                match |= Matches(candidate);
                if (match is true) return !inverseResult;
            }
        }
        return inverseResult ? !match : match;
    }

    private static Expression UnwrapParens(Expression expression)
    {
        while (expression is ParenthesizedExpression parens) expression = parens.Expression;
        return expression;
    }

    private static bool? EvaluateBetweenExpression(BetweenExpression between, Func<Expression, object?> evaluate)
    {
        object? left = evaluate(between.Right.First);
        object? rowValue = evaluate(between.Left);
        object? right = evaluate(between.Right.Second);

        if (right is TimeSpan rsp && (left is DateTime || rowValue is DateTime))
        {
            right = (left as DateTime?)?.Add(rsp);
        }

        if (between.Kind == SyntaxKind.BetweenExpression)
        {
            // (rowValue >= left and rowValue <= right)
            return CompareUtils.Relational(SyntaxKind.GreaterThanOrEqualExpression, rowValue, left) &
                   CompareUtils.Relational(SyntaxKind.LessThanOrEqualExpression, rowValue, right);
        }

        if (between.Kind == SyntaxKind.NotBetweenExpression)
        {
            // (rowValue < left or rowValue > right)
            return CompareUtils.Relational(SyntaxKind.LessThanExpression, rowValue, left) |
                   CompareUtils.Relational(SyntaxKind.GreaterThanExpression, rowValue, right);
        }

        throw new NotSupportedException($"Unsupported Between kind: {between.Kind}");
    }

    private static object? EvaluateBinary(BinaryExpression expression, Func<Expression, object?> evaluate)
    {
        var left = evaluate(expression.Left);
        if (expression.Kind == SyntaxKind.AndExpression && left is false) return false;
        if (expression.Kind == SyntaxKind.OrExpression && left is true) return true;
        var right = evaluate(expression.Right);
        bool dynamicLeft = expression.Left.ResultType is DynamicSymbol;
        bool dynamicRight = expression.Right.ResultType is DynamicSymbol;
        if (dynamicLeft || dynamicRight)
        {
            if (expression.Kind is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression
                or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression)
            {
                if (dynamicLeft && dynamicRight) throw new ArgumentException("Cast at least one dynamic arithmetic operand to a scalar.");
                if (dynamicLeft) left = ScalarFunctions.ConvertTo(left, expression.Right.ResultType);
                else right = ScalarFunctions.ConvertTo(right, expression.Left.ResultType);
            }
            else if (expression.Kind is SyntaxKind.GreaterThanExpression or SyntaxKind.LessThanExpression or
                SyntaxKind.GreaterThanOrEqualExpression or SyntaxKind.LessThanOrEqualExpression)
            {
                // Compare numeric payloads without narrowing; strings must first be parsed as the other operand's type.
                if (dynamicLeft && !dynamicRight && (left is null || !CompareUtils.IsNumeric(left) || right is not null && !CompareUtils.IsNumeric(right)))
                    left = ScalarFunctions.ConvertTo(left, expression.Right.ResultType);
                if (dynamicRight && !dynamicLeft && (right is null || !CompareUtils.IsNumeric(right) || left is not null && !CompareUtils.IsNumeric(left)))
                    right = ScalarFunctions.ConvertTo(right, expression.Left.ResultType);
            }
            else if (expression.Kind is SyntaxKind.ContainsExpression or SyntaxKind.NotContainsExpression or SyntaxKind.StartsWithExpression
                or SyntaxKind.NotStartsWithExpression or SyntaxKind.EndsWithExpression or SyntaxKind.NotEndsWithExpression or SyntaxKind.MatchesRegexExpression)
            {
                if (dynamicLeft) left = DynamicValues.ToText(left);
                if (dynamicRight) right = DynamicValues.ToText(right);
            }
        }
        return expression.Kind switch
        {
            SyntaxKind.AndExpression => (bool?)left & (bool?)right,
            SyntaxKind.OrExpression => (bool?)left | (bool?)right,
            SyntaxKind.EqualExpression => CompareUtils.AreEqual(left, right, StringComparison.Ordinal),
            SyntaxKind.EqualTildeExpression => CompareUtils.AreEqual(left, right, StringComparison.OrdinalIgnoreCase),
            SyntaxKind.NotEqualExpression => !CompareUtils.AreEqual(left, right, StringComparison.Ordinal),
            SyntaxKind.BangTildeExpression => !CompareUtils.AreEqual(left, right, StringComparison.OrdinalIgnoreCase),
            SyntaxKind.HasExpression or SyntaxKind.HasCsExpression or SyntaxKind.NotHasExpression or SyntaxKind.NotHasCsExpression =>
                DynamicValues.HasTerm(left, DynamicValues.ToText(right), expression.Kind is SyntaxKind.HasCsExpression or SyntaxKind.NotHasCsExpression)
                != (expression.Kind is SyntaxKind.NotHasExpression or SyntaxKind.NotHasCsExpression),
            SyntaxKind.ContainsExpression => StringOperations.ContainsOperation(left, right),
            SyntaxKind.NotContainsExpression => !StringOperations.ContainsOperation(left, right),
            SyntaxKind.StartsWithExpression => StringOperations.StartsWithOperation(left, right),
            SyntaxKind.NotStartsWithExpression => !StringOperations.StartsWithOperation(left, right),
            SyntaxKind.EndsWithExpression => StringOperations.EndsWithOperation(left, right),
            SyntaxKind.NotEndsWithExpression => !StringOperations.EndsWithOperation(left, right),
            SyntaxKind.MatchesRegexExpression => StringOperations.MatchesRegexExpressionOperation(left, right),
            _ when left is null || right is null => null,
            SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression or
                SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression =>
                CompareUtils.Relational(expression.Kind, left, right),
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
                return Cached(dynamicLiteral, () => EvalDynamicLiteral(dynamicLiteral.Expression));
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
                return EvaluateBetweenExpression(between, Evaluate);
            case InExpression membership:
                return EvaluateInExpression(membership, row, Evaluate);
            case HasAnyExpression any:
                return HasTerms(Evaluate(any.Left), any.Right, Evaluate, all: false);
            case HasAllExpression all:
                return HasTerms(Evaluate(all.Left), all.Right, Evaluate, all: true);
            case FunctionCallExpression call when aggregate is not null && IsAggregate(call):
                return aggregate(call);
            case FunctionCallExpression call when _bindings.GetValueOrDefault(call.Name.SimpleName) is FunctionBinding udf:
                var (scope, body) = EnterFunction(udf, call, call.ArgumentList.Expressions.Select(e => e.Element), Evaluate);
                return scope.EvalOperand(body, new QueryRow());
            case FunctionCallExpression call when call.Name.SimpleName is "coalesce" or "iff" or "iif" or "case":
                return EvaluateConditional(call, Evaluate);
            case FunctionCallExpression call when DynamicCollections.Supports(call.Name.SimpleName):
                var args = call.ArgumentList.Expressions.Select(e => e.Element is StarExpression ? null : Evaluate(e.Element)).ToArray();
                return DynamicCollections.Evaluate(call, args, row);
            case FunctionCallExpression call:
                return EvaluateFunction(call, call.ArgumentList.Expressions.Select(e => Evaluate(e.Element)).ToArray());
            default:
                throw new NotSupportedException($"Unsupported operand: {expr.GetType().Name}");
        }
    }

    private static object? EvaluateConditional(FunctionCallExpression call, Func<Expression, object?> evaluate)
    {
        var args = call.ArgumentList.Expressions;
        object? Result(object? value) => call.ResultType is DynamicSymbol ? value : ScalarFunctions.ConvertTo(value, call.ResultType);
        if (call.Name.SimpleName == "coalesce")
        {
            foreach (var arg in args)
            {
                var value = evaluate(arg.Element);
                if (value is not null && !(call.ResultType == ScalarTypes.String && value is "")) return Result(value);
            }
            return Result(null);
        }
        for (int i = 0; i < args.Count - 1; i += 2)
            if (evaluate(args[i].Element) is true) return Result(evaluate(args[i + 1].Element));
        return Result(evaluate(args[^1].Element));
    }

    private object? EvaluateFunction(FunctionCallExpression call, object?[] args)
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
            "strlen" => FunctionExpressions.StrLen(args),
            "todatetime" => FunctionExpressions.ToDateTime(args),
            "make_timespan" => FunctionExpressions.MakeTimeSpan(args),
            "totimespan" => FunctionExpressions.ToTimeSpan(args),
            "toguid" => ScalarFunctions.ConvertTo(args[0], ScalarTypes.Guid),
            "now" or "ago" => FunctionExpressions.Clock(functionName, args, _queryTime),
            "bin_at" => ScalarMath.Bin(args, call.ResultType),
            "bin" or "floor" or "strcat" or "trim" or "toint" or "tolong" or "toreal" or "todouble"
                or "tostring" or "tobool" or "toboolean" => ScalarFunctions.Evaluate(call, args),
            "isnull" => args[0] is null,
            "isnotnull" => args[0] is not null,
            "isempty" when args.Length == 1 => args[0] is null or "",
            "isnotempty" when args.Length == 1 => args[0] is not (null or ""),
            "not" => !(bool?)args[0],
            "parse_json" or "todynamic" or "parsejson" or "toobject" =>
                call.ArgumentList.Expressions[0].Element.ResultType is DynamicSymbol ? args[0] : DynamicValues.Parse(args[0]),
            "extract_json" or "extractjson" => DynamicValues.Extract((string?)args[0], (string?)args[1], args.Length == 3 ? (TypeSymbol?)args[2] : null),
            "gettype" => args[0] switch
            {
                null => "null",
                QueryRow => "dictionary",
                object?[] => "array",
                float or double when call.ArgumentList.Expressions[0].Element.ResultType is DynamicSymbol => "double",
                var v => ColumnType(v.GetType()).Name
            },
            "new_guid" => Guid.NewGuid(),
            "dcount_hll" => args[0] is DistinctState state ? state.Count : null,
            _ => throw new NotSupportedException($"Function {functionName} not implemented.")
        };
    }

    private IEnumerable<QueryRow> ReadDataTable(DataTableExpression data)
    {
        var columns = ((TableSymbol)data.ResultType).Columns;
        if (columns.Count == 0 || data.Values.Count % columns.Count != 0)
            throw new ArgumentException("datatable values must contain a whole number of rows.");
        var empty = new QueryRow();
        for (int i = 0; i < data.Values.Count;)
        {
            var row = new QueryRow();
            foreach (var column in columns)
                row.Add(column.Name, ScalarFunctions.ConvertTo(EvalOperand(data.Values[i++].Element, empty), column.Type));
            yield return row;
        }
    }

    private object? EvalDynamicLiteral(Expression literal) => literal switch
    {
        JsonArrayExpression array => array.Values.Select(item => EvalDynamicLiteral(item.Element)).ToArray(),
        JsonObjectExpression bag => bag.Pairs.Aggregate(new QueryRow(), (result, pair) =>
        {
            result[pair.Element.Name.ValueText] = EvalDynamicLiteral(pair.Element.Value);
            return result;
        }),
        LiteralExpression leaf => ParseLiteral(leaf) switch
        {
            string text => DynamicValues.InferString(text),
            TimeSpan span => span.Ticks,
            var scalar => DynamicValues.PackedScalar(scalar)
        },
        _ => EvalOperand(literal, new QueryRow())
    };

    private bool HasTerms(object? value, ExpressionList list, Func<Expression, object?> evaluate, bool all)
    {
        bool Match(object? item) => item is object?[] items
            ? all ? Array.TrueForAll(items, Match) : Array.Exists(items, Match)
            : item is not null && DynamicValues.HasTerm(value, DynamicValues.ToText(item), caseSensitive: false);
        foreach (var term in list.Expressions)
        {
            var expr = UnwrapParens(term.Element);
            bool hit = expr.ResultType is TableSymbol
                ? Match(Cached(expr, () => ExecuteExpression(expr).Select(r => r.Values.FirstOrDefault()).ToArray()))
                : Match(evaluate(expr));
            if (hit != all) return hit;
        }
        return all;
    }

    private IEnumerable<QueryRow> ApplyProject(IEnumerable<QueryRow> source, ProjectOperator project) =>
        SelectColumns(source, project.Expressions, extend: false);

    private IEnumerable<QueryRow> SelectColumns(IEnumerable<QueryRow> source,
        SyntaxList<SeparatedElement<Expression>> expressions, bool extend)
    {
        var op = (QueryOperator)expressions.Parent!;
        var schema = (TableSymbol)op.ResultType;
        int ordinal = 0;
        var props = expressions.Select(item =>
        {
            var value = UnwrapOutput(item.Element);
            if (value.RawResultType is TupleSymbol tuple)
            {
                // A named print projects only the first tuple field unless multiple names were supplied.
                int count = (op, item.Element) switch
                {
                    (PrintOperator, SimpleNamedExpression) => 1,
                    (PrintOperator, CompoundNamedExpression named) => named.Names.Names.Count,
                    _ => tuple.Columns.Count
                };
                var names = tuple.Columns.Take(count).Select(c => extend
                    ? schema.Columns.First(o => o.Source == c.Source).Name
                    : schema.Columns[ordinal++].Name).ToArray();
                return (Names: names, Expr: value, Tuple: value.ResultType is TupleSymbol);
            }
            string alias = item.Element switch
            {
                _ when !extend => schema.Columns[ordinal].Name,
                NameReference name => name.SimpleName,
                SimpleNamedExpression named => named.Name.SimpleName,
                _ => schema.Columns.FirstOrDefault(c => c.Source == item.Element)?.Name ??
                    throw new ArgumentException($"Cannot determine the output column for {item.Element}.")
            };
            ordinal++;
            return (Names: (string[])[alias], Expr: value, Tuple: false);
        }).ToArray();

        return source.Select(row =>
        {
            // Sibling expressions, including pack_all(), see the incoming row, not each other's output.
            var dict = extend ? new QueryRow(row) : new QueryRow();
            foreach (var (names, expression, tuple) in props)
            {
                var value = EvalOperand(expression, row);
                if (!tuple) dict[names[0]] = value;
                else
                {
                    var fields = (object?[])value!;
                    for (int i = 0; i < names.Length; i++) dict[names[i]] = fields[i];
                }
            }

            return dict;
        });
    }

    private IEnumerable<QueryRow> ApplySort(IEnumerable<QueryRow> source, SortOperator sort) =>
        OrderRows(source, sort.Expressions.Select(item => item.Element));

    private IEnumerable<QueryRow> OrderRows(IEnumerable<QueryRow> source, IEnumerable<Expression> keys)
    {
        IOrderedEnumerable<QueryRow>? ordered = null;
        foreach (var expression in keys)
        {
            var key = expression as OrderedExpression;
            var order = key?.Ordering;
            bool descending = order?.AscOrDescKeyword?.Kind != SyntaxKind.AscKeyword;
            bool nullsFirst = order?.NullsClause is { } nulls ? nulls.FirstOrLastKeyword.Kind == SyntaxKind.FirstKeyword : !descending;
            var comparer = Comparer<object?>.Create((a, b) => CompareUtils.SortComparison(a, b, descending, nullsFirst));
            object? SelectKey(QueryRow row) => EvalOperand(key?.Expression ?? expression, row);
            ordered = ordered is null ? source.OrderBy(SelectKey, comparer) : ordered.ThenBy(SelectKey, comparer);
        }
        return ordered ?? source;
    }

    private IEnumerable<QueryRow> ApplyExtend(IEnumerable<QueryRow> source, ExtendOperator extend) =>
        SelectColumns(source, extend.Expressions, extend: true);

    private IEnumerable<QueryRow> ApplyTake(IEnumerable<QueryRow> source, TakeOperator take) => TakeRows(source, take.Expression);

    private IEnumerable<QueryRow> TakeRows(IEnumerable<QueryRow> source, Expression count)
    {
        var limit = EvalOperand(count, new QueryRow());
        if (limit is not (int or long)) throw new ArgumentException("take expects an integer.");
        return source.Take((int)Math.Clamp(Convert.ToInt64(limit, CultureInfo.InvariantCulture), 0, int.MaxValue));
    }

    private object? ParseLiteral(LiteralExpression lit) => lit.Kind switch
    {
        SyntaxKind.NullLiteralExpression => null,
        SyntaxKind.DateTimeLiteralExpression when lit.Token.ValueText == "now" => _queryTime,
        SyntaxKind.DateTimeLiteralExpression => ScalarFunctions.ConvertTo(lit.LiteralValue, ScalarTypes.DateTime),
        SyntaxKind.RealLiteralExpression when lit.Token.Text is "real(+inf)" or "real(inf)" => double.PositiveInfinity,
        SyntaxKind.RealLiteralExpression when lit.Token.Text == "real(-inf)" => double.NegativeInfinity,
        _ => lit.LiteralValue
    };
}