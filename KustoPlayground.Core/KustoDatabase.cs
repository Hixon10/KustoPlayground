using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Kusto.Language;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

/// <summary>
/// Main interface to interact with a Kusto database.
/// </summary>
public class KustoDatabase
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
            return new ExecutionResult
            {
                ResultRows = ExecuteQueryInternal(query)
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

    private List<IReadOnlyDictionary<string, object?>> ExecuteQueryInternal(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("query is null or empty");
        }

        var code = KustoCode.Parse(query);

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
            object? result = EvaluateCondition(filter.Condition, row);
            return Convert.ToBoolean(result, CultureInfo.InvariantCulture);
        }

        return source.Where(Predicate);
    }

    private object? EvaluateCondition(Expression expr, Dictionary<string, object?> row)
    {
        return expr switch
        {
            BinaryExpression be => EvaluateBinary(be, row),
            NameReference nameRef => GetPropValue(row, nameRef.Name.SimpleName),
            LiteralExpression lit => ParseLiteral(lit),
            BetweenExpression between => EvaluateBetweenExpression(between, row),
            InExpression inExpression => EvaluateInExpression(inExpression, row),
            _ => throw new NotSupportedException($"Unsupported condition expression: {expr.GetType().Name}")
        };
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

    private object? EvaluateBinary(BinaryExpression be, Dictionary<string, object?> row)
    {
        switch (be.Kind)
        {
            case SyntaxKind.AndExpression:
                return Convert.ToBoolean(EvaluateCondition(be.Left, row), CultureInfo.InvariantCulture) &&
                       Convert.ToBoolean(EvaluateCondition(be.Right, row), CultureInfo.InvariantCulture);

            case SyntaxKind.OrExpression:
                return Convert.ToBoolean(EvaluateCondition(be.Left, row), CultureInfo.InvariantCulture) ||
                       Convert.ToBoolean(EvaluateCondition(be.Right, row), CultureInfo.InvariantCulture);

            case SyntaxKind.EqualExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return CompareUtils.AreEqual(left, right, StringComparison.Ordinal);
            }

            case SyntaxKind.EqualTildeExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return CompareUtils.AreEqual(left, right, StringComparison.OrdinalIgnoreCase);
            }

            case SyntaxKind.NotEqualExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return !CompareUtils.AreEqual(left, right, StringComparison.Ordinal);
            }

            case SyntaxKind.BangTildeExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return !CompareUtils.AreEqual(left, right, StringComparison.OrdinalIgnoreCase);
            }

            case SyntaxKind.GreaterThanExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return CompareUtils.Compare(left, right) > 0;
            }

            case SyntaxKind.GreaterThanOrEqualExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return CompareUtils.Compare(left, right) >= 0;
            }

            case SyntaxKind.LessThanExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return CompareUtils.Compare(left, right) < 0;
            }

            case SyntaxKind.LessThanOrEqualExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return CompareUtils.Compare(left, right) <= 0;
            }

            case SyntaxKind.ContainsExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return StringOperations.ContainsOperation(left, right);
            }

            case SyntaxKind.NotContainsExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return !StringOperations.ContainsOperation(left, right);
            }

            case SyntaxKind.StartsWithExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return StringOperations.StartsWithOperation(left, right);
            }

            case SyntaxKind.NotStartsWithExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return !StringOperations.StartsWithOperation(left, right);
            }

            case SyntaxKind.EndsWithExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return StringOperations.EndsWithOperation(left, right);
            }

            case SyntaxKind.NotEndsWithExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return !StringOperations.EndsWithOperation(left, right);
            }

            case SyntaxKind.MatchesRegexExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return StringOperations.MatchesRegexExpressionOperation(left, right);
            }

            case SyntaxKind.SubtractExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);

                if (left == null || right == null)
                {
                    return null;
                }

                if (left is DateTime leftDateTime1 && right is DateTime rightDateTime1)
                {
                    return leftDateTime1.Subtract(rightDateTime1);
                }

                if (left is DateTime leftDateTime2 && right is TimeSpan rightTimeSpan2)
                {
                    return leftDateTime2.Subtract(rightTimeSpan2);
                }

                if (CompareUtils.IsNumeric(left) && CompareUtils.IsNumeric(right))
                {
                    var dl = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                    var dr = Convert.ToDouble(right, CultureInfo.InvariantCulture);
                    return dl - dr;
                }

                throw new NotSupportedException(
                    $"Unsupported SubtractExpression: left={left.GetType()}, right={right.GetType()}");
            }

            case SyntaxKind.AddExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);

                if (left == null || right == null)
                {
                    return null;
                }

                if (left is DateTime && right is DateTime)
                {
                    // Not supported as of now
                    throw new NotSupportedException("Unsupported AddExpression: left=DateTime, right=DateTime");
                }

                if (left is DateTime leftDateTime2 && right is TimeSpan rightTimeSpan2)
                {
                    return leftDateTime2.Add(rightTimeSpan2);
                }

                if (left is TimeSpan leftTimeSpan3 && right is TimeSpan rightTimeSpan3)
                {
                    return leftTimeSpan3.Add(rightTimeSpan3);
                }

                if (left is TimeSpan leftTimeSpan4 && right is DateTime rightDateTim4)
                {
                    return rightDateTim4.Add(leftTimeSpan4);
                }

                if (CompareUtils.IsNumeric(left) && CompareUtils.IsNumeric(right))
                {
                    var dl = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                    var dr = Convert.ToDouble(right, CultureInfo.InvariantCulture);
                    return dl + dr;
                }

                throw new NotSupportedException(
                    $"Unsupported AddExpression: left={left.GetType()}, right={right.GetType()}");
            }

            case SyntaxKind.MultiplyExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);

                if (left == null || right == null)
                {
                    return null;
                }

                if (left is TimeSpan leftTimeSpan1 && CompareUtils.IsNumeric(right))
                {
                    var dr = Convert.ToDouble(right, CultureInfo.InvariantCulture);
                    return leftTimeSpan1.Multiply(dr);
                }

                if (CompareUtils.IsNumeric(left) && right is TimeSpan rightTimeSpan2)
                {
                    var dl = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                    return rightTimeSpan2.Multiply(dl);
                }

                if (CompareUtils.IsNumeric(left) && CompareUtils.IsNumeric(right))
                {
                    var dl = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                    var dr = Convert.ToDouble(right, CultureInfo.InvariantCulture);
                    return dl * dr;
                }

                throw new NotSupportedException(
                    $"Unsupported MultiplyExpression: left={left.GetType()}, right={right.GetType()}");
            }

            case SyntaxKind.DivideExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);

                if (left == null || right == null)
                {
                    return null;
                }

                if (left is TimeSpan leftTimeSpan1 && right is TimeSpan rightTimeSpan2)
                {
                    return leftTimeSpan1.Divide(rightTimeSpan2);
                }

                if (left is TimeSpan leftTimeSpan2 && CompareUtils.IsNumeric(right))
                {
                    var dr = Convert.ToDouble(right, CultureInfo.InvariantCulture);
                    return leftTimeSpan2.Divide(dr);
                }

                if (CompareUtils.IsNumeric(left) && CompareUtils.IsNumeric(right))
                {
                    var dl = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                    var dr = Convert.ToDouble(right, CultureInfo.InvariantCulture);
                    if (dr == 0.0)
                    {
                        return null;
                    }

                    return dl / dr;
                }

                throw new NotSupportedException(
                    $"Unsupported DivideExpression: left={left.GetType()}, right={right.GetType()}");
            }

            case SyntaxKind.ModuloExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);

                if (left == null || right == null)
                {
                    return null;
                }

                if (CompareUtils.IsNumeric(left) && CompareUtils.IsNumeric(right))
                {
                    var dl = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                    var dr = Convert.ToDouble(right, CultureInfo.InvariantCulture);
                    return dl % dr;
                }

                throw new NotSupportedException($"Unsupported modulo expression for non numeric types: " +
                                                $"{left.GetType()}, {right.GetType()}");
            }

            default:
                throw new NotSupportedException($"Unsupported binary expression: {be.Kind}");
        }
    }

    private object? EvalOperand(Expression expr, Dictionary<string, object?> row)
    {
        switch (expr)
        {
            case NameReference nr:
                return GetPropValue(row, nr.Name.SimpleName);
            case PrefixUnaryExpression pue:
                object operand = EvalOperand(pue.Expression, row) ??
                                 throw new ArgumentException($"operand is null for PrefixUnaryExpression: {pue.Kind}");
                switch (pue.Operator.Kind)
                {
                    case SyntaxKind.MinusToken:
                        if (operand is int i) return -i;
                        if (operand is long l) return -l;
                        if (operand is double d) return -d;
                        if (operand is float f) return -f;
                        if (operand is TimeSpan ts) return -ts;
                        throw new NotSupportedException($"Unary - not supported for {operand.GetType().Name}");

                    case SyntaxKind.PlusToken: // +x
                        return operand; // no-op

                    case SyntaxKind.BangToken: // !x
                        if (operand is bool b) return !b;
                        throw new NotSupportedException($"Unary ! not supported for {operand.GetType().Name}");

                    default:
                        throw new NotSupportedException($"Unsupported prefix operator: {pue.Operator.Kind}");
                }
            case LiteralExpression lit:
                return ParseLiteral(lit);
            case BinaryExpression be:
                return EvaluateCondition(be, row); // only if nested boolean expression
            case FunctionCallExpression fce:
                return EvaluateFunction(fce, row);
            default:
                throw new NotSupportedException($"Unsupported operand: {expr.GetType().Name}");
        }
    }

    private object? EvaluateFunction(FunctionCallExpression fce, Dictionary<string, object?> row)
    {
        object?[] args = fce.ArgumentList.Expressions
            .Select(expression => EvalOperand(expression.Element, row))
            .ToArray();
        string functionName = fce.Name.SimpleName;

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
            "now" => FunctionExpressions.Now(args),
            "ago" => FunctionExpressions.Ago(args),
            _ => throw new NotSupportedException($"Function {functionName} not implemented.")
        };
    }

    private static IEnumerable<Dictionary<string, object?>> ApplyProject(
        IEnumerable<Dictionary<string, object?>> source,
        ProjectOperator project)
    {
        // unwrap SeparatedElement<Expression> → Expression
        IEnumerable<Expression> exprs = project.Expressions.Select(se => se.Element);

        // often NameReference, but can also be SimpleNamedExpression (alias = expr)
        IEnumerable<(string Alias, NameReference Expr)> props = exprs.Select(e =>
        {
            if (e is NameReference nr)
            {
                return (Alias: nr.Name.SimpleName, Expr: nr);
            }

            if (e is SimpleNamedExpression sne && sne.Expression is NameReference inner)
            {
                return (Alias: sne.Name.SimpleName, Expr: inner);
            }

            throw new NotSupportedException($"Unsupported project expression: {e.GetType().Name}");
        });

        return source.Select(row =>
        {
            var dict = new Dictionary<string, object?>();
            foreach ((string Alias, NameReference? Expr) p in props)
            {
                if (p.Expr != null)
                {
                    dict[p.Alias] = GetPropValue(row, p.Expr.Name.SimpleName);
                }
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