using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Kusto.Language;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

/// <summary>
/// Main interface to interact with a Kusto database.
/// </summary>
public partial class KustoDatabase
{
    private readonly ConcurrentDictionary<string, Table> _tables = new();

    /// <summary>
    /// Add a table to the current database.
    /// </summary>
    /// <param name="table">Table</param>
    public void AddTable(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        _tables[table.Name] = table;
    }

    /// <summary>
    /// Execute a query in the database.
    /// </summary>
    /// <param name="query">query</param>
    /// <returns>Rows, or execution errors.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Top-level handler, exceptions are returned.")]
    public ExecutionResult ExecuteQuery(string query)
    {
        try
        {
            ArgumentException.ThrowIfNullOrEmpty(query);
            var code = KustoCode.Parse(query);
            if (code.Syntax.GetFirstDescendant<SummarizeOperator>() is not null)
            {
                code = AnalyzeSummaries(code);
                var errors = code.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => new ExecutionError { Code = d.Code, Description = d.Message }).ToList();
                if (errors.Count != 0)
                {
                    return new ExecutionResult { ExecutionErrors = errors };
                }
            }

            return new ExecutionResult
            {
                ResultRows = ExecuteQueryInternal(code)
            };
        }
        catch (Exception ex)
        {
            return new ExecutionResult
            {
                ExecutionErrors = new List<ExecutionError>
                {
                    new()
                    {
                        Code = nameof(ExecutionError.ErrorCodes.InternalError),
                        Description = ex.Message
                    }
                }
            };
        }
    }

    private List<IReadOnlyDictionary<string, object?>> ExecuteQueryInternal(KustoCode code)
    {
        if (code.Syntax is not QueryBlock block)
        {
            throw new InvalidOperationException("Expected a QueryBlock at root.");
        }

        // Statements is a SyntaxList<SeparatedElement<Statement>>
        var firstStmt = block.Statements[0].Element;

        if (firstStmt is not ExpressionStatement exprStmt)
        {
            throw new InvalidOperationException("Expected ExpressionStatement.");
        }

        IEnumerable<Dictionary<string, object?>> executeExpression = ExecuteExpression(exprStmt.Expression);
        List<IReadOnlyDictionary<string, object?>> results = new List<IReadOnlyDictionary<string, object?>>();

        foreach (Dictionary<string, object?> row in executeExpression)
        {
            results.Add(row.AsReadOnly());
        }

        return results;
    }

    private IEnumerable<Dictionary<string, object?>> ExecuteExpression(Expression expr)
    {
        switch (expr)
        {
            case NameReference nameRef:
            {
                if (_tables.TryGetValue(nameRef.Name.SimpleName, out Table? table))
                {
                    return table.Rows.Select(row => new Dictionary<string, object?>(row._values));
                }

                throw new InvalidOperationException($"Unknown table: {nameRef.Name}");
            }
            case PipeExpression pipe:
            {
                var left = ExecuteExpression(pipe.Expression);
                return ApplyOperator(left, pipe.Operator);
            }
            case PrintOperator printOperator:
            {
                IEnumerable<Expression> exprs = printOperator.Expressions.Select(se => se.Element);
                Dictionary<string, object?> result = new Dictionary<string, object?>();

                int i = 0;

                foreach (Expression expression in exprs)
                {
                    string columnName;
                    Expression columnExpression;

                    switch (expression)
                    {
                        case SimpleNamedExpression simpleNamedExpression:
                            columnName = simpleNamedExpression.Name.SimpleName;
                            columnExpression = simpleNamedExpression.Expression;
                            break;

                        default:
                            columnName = $"print_{i}";
                            i++;
                            columnExpression = expression;
                            break;
                    }

                    object? columnValue = EvalOperand(columnExpression, new Dictionary<string, object?>());
                    result[columnName] = columnValue;
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

            default:
                throw new NotSupportedException($"Unsupported operator: {op.GetType().Name}");
        }
    }

    private static IEnumerable<Dictionary<string, object?>> ApplyDistinctOperator(
        IEnumerable<Dictionary<string, object?>> source,
        DistinctOperator distinctOperator)
    {
        // not sure, should we materialize source here,
        // or it is fine to iterate over source several times,
        // when we have several sort columns.
        List<Dictionary<string, object?>> sourceCopy = source.ToList();

        if (sourceCopy.Count == 0)
        {
            return sourceCopy;
        }

        // unwrap SeparatedElement<Expression> → Expression
        IEnumerable<Expression> exprs = distinctOperator.Expressions.Select(se => se.Element);

        // often NameReference, but can also be SimpleNamedExpression (alias = expr)
        List<string> columns = new List<string>();
        foreach (Expression e in exprs)
        {
            if (e is NameReference nr)
            {
                columns.Add(nr.Name.SimpleName);
                continue;
            }

            if (e is SimpleNamedExpression sne && sne.Expression is NameReference)
            {
                columns.Add(sne.Name.SimpleName);
                continue;
            }

            if (e is StarExpression)
            {
                // just add all columns of the first row
                columns.AddRange(sourceCopy[0].Keys);
                break;
            }

            throw new NotSupportedException($"Unsupported distinct expression: {e.GetType().Name}");
        }

        return sourceCopy
            .GroupBy(dict => string.Join("|", columns.Select(col => dict[col])))
            .Select(g => g.First());
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

        if (inExpression.Left is not NameReference leftName)
        {
            throw new NotSupportedException($"Unsupported In Left expression: {inExpression.Left.GetType().Name}");
        }

        string columnName = leftName.Name.SimpleName;

        if (!row.TryGetValue(columnName, out object? columnValue))
        {
            throw new ArgumentException($"row doesn't have a value for column: '{columnName}'");
        }

        bool foundValue = false;
        IEnumerable<Expression> exprs = inExpression.Right.Expressions.Select(se => se.Element);
        foreach (Expression expression in exprs)
        {
            if (expression is LiteralExpression literalExpression)
            {
                // 'table1 | where col1 in (2, 4)', it will be 2 and 4
                object literalValue = ParseLiteral(literalExpression);
                if (CompareUtils.AreEqual(columnValue, literalValue, comparisonType))
                {
                    foundValue = true;
                    break;
                }
            }
            else
            {
                /// TODO - add cache for execution result?
                // 'table1 | where col1 in (table1 | where col1 contains 'an')',
                foreach (Dictionary<string, object?> resultRow in ExecuteExpression(expression))
                {
                    if (!resultRow.TryGetValue(columnName, out object? value))
                    {
                        throw new ArgumentException($"nested in query doesn't return {columnName} column");
                    }

                    if (CompareUtils.AreEqual(columnValue, value, comparisonType))
                    {
                        foundValue = true;
                        break;
                    }
                }
            }
        }

        return inverseResult ? !foundValue : foundValue;
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
                return GetPropValue(row, nr.Name.SimpleName);
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
            case DynamicExpression dynamicLiteral:
                return DynamicValues.Parse(dynamicLiteral.LiteralValue);
            case BinaryExpression binary:
                return EvaluateBinary(binary, Evaluate);
            case BetweenExpression between:
                return EvaluateBetweenExpression(between, row);
            case InExpression membership:
                return EvaluateInExpression(membership, row);
            case FunctionCallExpression call when aggregate is not null && IsAggregate(call):
                return aggregate(call);
            case FunctionCallExpression call:
                return EvaluateFunction(call, call.ArgumentList.Expressions.Select(e => Evaluate(e.Element)).ToArray());
            default:
                throw new NotSupportedException($"Unsupported operand: {expr.GetType().Name}");
        }
    }

    private static object? EvaluateFunction(FunctionCallExpression call, object?[] args)
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
            "bin" or "bin_at" => ScalarMath.Bin(args, call.ResultType),
            "isnull" => args[0] is null,
            "isnotnull" => args[0] is not null,
            "not" => !(bool?)args[0],
            "tostring" => DynamicValues.ToText(args[0]),
            "toreal" or "todouble" => ScalarMath.ToReal(args[0]),
            "pack_array" => args,
            "parse_json" => DynamicValues.Parse(args[0]),
            "bag_pack" => DynamicValues.Pack(args),
            "dcount_hll" => args[0] is DistinctState state ? state.Count : null,
            _ => throw new NotSupportedException($"Function {functionName} not implemented.")
        };
    }

    private IEnumerable<Dictionary<string, object?>> ApplyProject(
        IEnumerable<Dictionary<string, object?>> source,
        ProjectOperator project)
    {
        var props = project.Expressions.Select(item => item.Element switch
        {
            NameReference name => (Alias: name.SimpleName, Expr: (Expression)name),
            SimpleNamedExpression named => (Alias: named.Name.SimpleName, Expr: named.Expression),
            _ => throw new NotSupportedException($"Unsupported project expression: {item.Element.Kind}")
        }).ToArray();

        return source.Select(row =>
        {
            var dict = new Dictionary<string, object?>();
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

            Comparer<object?> comparer = Comparer<object?>.Create(CompareUtils.Compare);

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

    private IEnumerable<Dictionary<string, object?>> ApplyExtend(
        IEnumerable<Dictionary<string, object?>> source,
        ExtendOperator extend)
    {
        foreach (Dictionary<string, object?> row in source)
        {
            foreach (Expression expression in extend.Expressions.Select(se => se.Element))
            {
                string columnName;
                Expression nameReference;
                if (expression is NameReference nr)
                {
                    columnName = nr.Name.SimpleName;
                    nameReference = nr;
                }
                else if (expression is SimpleNamedExpression sne)
                {
                    columnName = sne.Name.SimpleName;
                    nameReference = sne.Expression;
                }
                else
                {
                    throw new NotSupportedException($"Unsupported project expression: {expression.GetType().Name}");
                }

                object? newValue = EvalOperand(nameReference, row);
                row[columnName] = newValue;
            }

            yield return row;
        }
    }

    private static IEnumerable<Dictionary<string, object?>> ApplyTake(
        IEnumerable<Dictionary<string, object?>> source,
        TakeOperator take)
    {
        if (take.Expression is LiteralExpression lit)
        {
            var n = Convert.ToInt32(ParseLiteral(lit), CultureInfo.InvariantCulture);
            return source.Take(n);
        }

        throw new NotSupportedException("Take must be a literal integer.");
    }

    private static object? GetPropValue(Dictionary<string, object?> row, string name)
    {
        return row.GetValueOrDefault(name);
    }

    private static object ParseLiteral(LiteralExpression lit)
    {
        var text = lit.Token.Text;
        if (lit.Kind == SyntaxKind.StringLiteralExpression)
        {
            return text.Trim('\'', '"');
        }

        if (lit.Kind == SyntaxKind.LongLiteralExpression)
        {
            return long.Parse(text, CultureInfo.InvariantCulture);
        }

        if (lit.Kind == SyntaxKind.BooleanLiteralExpression)
        {
            return bool.Parse(text);
        }

        if (lit.Kind == SyntaxKind.IntLiteralExpression)
        {
            return int.Parse(text, CultureInfo.InvariantCulture);
        }

        if (lit.Kind == SyntaxKind.RealLiteralExpression)
        {
            return double.Parse(text, CultureInfo.InvariantCulture);
        }

        if (lit.Kind == SyntaxKind.DateTimeLiteralExpression)
        {
            if (lit.LiteralValue == null)
            {
                return DateTime.UtcNow;
            }

            return (DateTime)lit.LiteralValue;
        }

        if (lit.Kind == SyntaxKind.TimespanLiteralExpression)
        {
            return (TimeSpan)lit.LiteralValue;
        }

        return text;
    }
}