using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using Kusto.Language;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(ExecutionResult))]
[JsonSerializable(typeof(ExecutionError))]
public partial class ExecutionResultJsonContext : JsonSerializerContext;

/// <summary>
/// Represents the result of a query execution.
/// </summary>
public sealed class ExecutionResult
{
    /// <summary>
    /// The collection of rows returned by the query.
    /// Can be null, if the execution fails.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>>? ResultRows { get; init; }

    /// <summary>
    /// The collection of errors encountered during execution.
    /// Can be null, if no errors occurred.
    /// </summary>
    public IReadOnlyList<ExecutionError>? ExecutionErrors { get; init; }
}

/// <summary>
/// Represents an error that occurred during query execution 
/// (for example, a parsing error).
/// </summary>
public sealed class ExecutionError
{
    public enum ErrorCodes
    {
        None,
        InternalError,
        UnknownTable,
    }

    public required string Code { get; init; }
    public string? Description { get; init; }
}

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
                return AreEqual(left, right);
            }

            case SyntaxKind.NotEqualExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return !AreEqual(left, right);
            }

            case SyntaxKind.GreaterThanExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return Compare(left, right) > 0;
            }

            case SyntaxKind.GreaterThanOrEqualExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return Compare(left, right) >= 0;
            }

            case SyntaxKind.LessThanExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return Compare(left, right) < 0;
            }

            case SyntaxKind.LessThanOrEqualExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return Compare(left, right) <= 0;
            }

            default:
                throw new NotSupportedException($"Unsupported binary expression: {be.Kind}");
        }
    }

    private static bool AreEqual(object? left, object? right)
    {
        if (left == null && right == null)
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            return Convert.ToDouble(left, CultureInfo.InvariantCulture) ==
                   Convert.ToDouble(right, CultureInfo.InvariantCulture);
        }

        if (IsNumeric(left) && right is string rightStr)
        {
            // if query tries to compare number, and "number2", we should allow it.
            if (double.TryParse(rightStr, CultureInfo.InvariantCulture, out double rightRes))
            {
                return Convert.ToDouble(left, CultureInfo.InvariantCulture) == rightRes;
            }
        }
        
        if (left is string leftString && IsNumeric(right))
        {
            // if query tries to compare "number", and number2, we should allow it.
            if (double.TryParse(leftString, CultureInfo.InvariantCulture, out double leftRes))
            {
                return leftRes == Convert.ToDouble(right, CultureInfo.InvariantCulture);
            }
        }
        
        if (left is string ls && right is string rs)
        {
            return string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase);
        }

        return left.Equals(right);
    }

    private static int Compare(object? left, object? right)
    {
        if (left == null || right == null)
        {
            throw new InvalidOperationException("Cannot compare null values");
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            var dl = Convert.ToDouble(left, CultureInfo.InvariantCulture);
            var dr = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            return dl.CompareTo(dr);
        }

        if (left is string ls && right is string rs)
        {
            return string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase);
        }

        if (left is IComparable cl && right is IComparable cr && left.GetType() == right.GetType())
        {
            return cl.CompareTo(cr);
        }

        throw new NotSupportedException(
            $"Cannot compare values of types {left.GetType().Name} and {right.GetType().Name}");
    }

    private static bool IsNumeric(object value)
    {
        return value is sbyte or byte or short or ushort
            or int or uint or long or ulong
            or float or double or decimal;
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