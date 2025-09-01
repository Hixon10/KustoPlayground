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
                ExecutionErrors =
                [
                    new ExecutionError
                    {
                        Code = nameof(ExecutionError.ErrorCodes.InternalError),
                        Description = ex.Message
                    }
                ]
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

            default:
                throw new NotSupportedException($"Unsupported operator: {op.GetType().Name}");
        }
    }

    private IEnumerable<Dictionary<string, object?>> ApplyFilter(
        IEnumerable<Dictionary<string, object?>> source,
        FilterOperator filter)
    {
        bool Predicate(Dictionary<string, object?> row)
        {
            bool result = EvaluateCondition(filter.Condition, row);
            return result;
        }

        return source.Where(Predicate);
    }

    private bool EvaluateCondition(Expression expr, Dictionary<string, object?> row)
    {
        switch (expr)
        {
            case BinaryExpression be:
                return EvaluateBinary(be, row);

            case NameReference nameRef:
            {
                object? propValue = GetPropValue(row, nameRef.Name.SimpleName);
                if (propValue is bool b)
                {
                    return b;
                }

                // Interpret bare property as truthy/non-null
                return propValue != null;
            }

            case LiteralExpression lit:
                return (bool)ParseLiteral(lit);

            default:
                throw new NotSupportedException($"Unsupported condition expression: {expr.GetType().Name}");
        }
    }

    private bool EvaluateBinary(BinaryExpression be, Dictionary<string, object?> row)
    {
        switch (be.Kind)
        {
            case SyntaxKind.AndExpression:
                return EvaluateCondition(be.Left, row) && EvaluateCondition(be.Right, row);

            case SyntaxKind.OrExpression:
                return EvaluateCondition(be.Left, row) || EvaluateCondition(be.Right, row);

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
            default:
                throw new NotSupportedException($"Unsupported operand: {expr.GetType().Name}");
        }
    }

    private static IEnumerable<Dictionary<string, object?>> ApplyProject(
        IEnumerable<Dictionary<string, object?>> source,
        ProjectOperator project)
    {
        // unwrap SeparatedElement<Expression> → Expression
        var exprs = project.Expressions.Select(se => se.Element);

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

        return text;
    }
}