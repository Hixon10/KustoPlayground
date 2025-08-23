using System.Reflection;
using Kusto.Language;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

public class StormEvent
{
    public DateTime StartTime { get; set; }
    public required string State { get; set; }
    public required string EventType { get; set; }
    public double DamageProperty { get; set; }
}

public class KustoExecutor
{
    private readonly Dictionary<string, object> _tables = new();

    public KustoExecutor()
    {
        var events = new List<StormEvent>
        {
            new StormEvent
                { StartTime = new DateTime(2025, 8, 23, 6, 20, 0), State = "FLORIDA", EventType = "Hurricane", DamageProperty = 20000 },
            new StormEvent
                { StartTime = new DateTime(2023, 3, 28, 10, 30, 0), State = "TEXAS", EventType = "Flood", DamageProperty = 5000 },
            new StormEvent
                { StartTime = new DateTime(2024, 6, 1, 16, 50, 30), State = "FLORIDA", EventType = "Tornado", DamageProperty = 5000 },
        };

        RegisterTable("StormEvents", events);
    }

    public void RegisterTable<T>(string name, IEnumerable<T> data)
    {
        _tables[name] = data;
    }


    public List<string> Execute(string query)
    {
        var code = KustoCode.Parse(query);

        if (code.Syntax is not QueryBlock block)
        {
            throw new InvalidOperationException("Expected a QueryBlock at root.");
        }

        //
        // Statements is a SyntaxList<SeparatedElement<Statement>>
        var firstStmt = block.Statements[0].Element;

        if (firstStmt is not ExpressionStatement exprStmt)
        {
            throw new InvalidOperationException("Expected ExpressionStatement.");
        }

// This is your query AST root
        var rootExpr = exprStmt.Expression;
        //

        IEnumerable<object> executeExpression = ExecuteExpression(rootExpr);
        List<string> results = new List<string>();

        foreach (var row in executeExpression)
        {
            string str = string.Join(", ",
                ((Dictionary<string, object>)row).Select(kv => $"{kv.Key}={kv.Value}"));
            results.Add(str);
        }

        ;

        return results;
    }

    private IEnumerable<object> ExecuteExpression(Expression expr)
    {
        switch (expr)
        {
            case NameReference nameRef:
                if (_tables.TryGetValue(nameRef.Name.SimpleName, out var table))
                {
                    return (IEnumerable<object>)table;
                }

                throw new InvalidOperationException($"Unknown table: {nameRef.Name}");

            case PipeExpression pipe:
                var left = ExecuteExpression(pipe.Expression);
                return ApplyOperator(left, pipe.Operator);

            default:
                throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}");
        }
    }

    private IEnumerable<object> ApplyOperator(IEnumerable<object> source, QueryOperator op)
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

    private IEnumerable<object> ApplyFilter(IEnumerable<object> source, FilterOperator filter)
    {
        bool Predicate(object row)
        {
            bool result = EvaluateCondition(filter.Condition, row);
            return result;
        }

        return source.Where(Predicate);
    }

    private bool EvaluateCondition(Expression expr, object row)
    {
        switch (expr)
        {
            case BinaryExpression be:
                return EvaluateBinary(be, row);

            case NameReference nameRef:
                // Interpret bare property as truthy/non-null
                return GetPropValue(row, nameRef.Name.SimpleName) != null;

            case LiteralExpression lit:
                return (bool)ParseLiteral(lit);

            default:
                throw new NotSupportedException($"Unsupported condition expression: {expr.GetType().Name}");
        }
    }

    private bool EvaluateBinary(BinaryExpression be, object row)
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
                return Equals(left, right);
            }

            case SyntaxKind.NotEqualExpression:
            {
                var left = EvalOperand(be.Left, row);
                var right = EvalOperand(be.Right, row);
                return !Equals(left, right);
            }

            case SyntaxKind.GreaterThanExpression:
            {
                var left = Convert.ToDouble(EvalOperand(be.Left, row));
                var right = Convert.ToDouble(EvalOperand(be.Right, row));
                return left > right;
            }

            case SyntaxKind.LessThanExpression:
            {
                var left = Convert.ToDouble(EvalOperand(be.Left, row));
                var right = Convert.ToDouble(EvalOperand(be.Right, row));
                return left < right;
            }

            default:
                throw new NotSupportedException($"Unsupported binary expression: {be.Kind}");
        }
    }

    private object? EvalOperand(Expression expr, object row)
    {
        switch (expr)
        {
            case NameReference nr:
                return GetPropValue(row, nr.Name.SimpleName);
            case LiteralExpression lit:
                return ParseLiteral(lit);
            case BinaryExpression be:
                return EvaluateCondition(be, row); // only if nested boolean expression
            default:
                throw new NotSupportedException($"Unsupported operand: {expr.GetType().Name}");
        }
    }

    private IEnumerable<object> ApplyProject(IEnumerable<object> source, ProjectOperator project)
    {
        // unwrap SeparatedElement<Expression> → Expression
        var exprs = project.Expressions.Select(se => se.Element).ToList();

        // often NameReference, but can also be SimpleNamedExpression (alias = expr)
        var props = exprs.Select(e =>
        {
            if (e is NameReference nr)
            {
                return (Alias: nr.Name.SimpleName, Expr: (Expression)nr);
            }

            if (e is SimpleNamedExpression sne && sne.Expression is NameReference inner)
            {
                return (Alias: sne.Name.SimpleName, Expr: (Expression)inner);
            }

            throw new NotSupportedException($"Unsupported project expression: {e.GetType().Name}");
        }).ToList();

        return source.Select(row =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var p in props)
            {
                if (p.Expr is NameReference nr)
                    dict[p.Alias] = GetPropValue(row, nr.Name.SimpleName);
            }

            return dict;
        });
    }

    private IEnumerable<object> ApplyTake(IEnumerable<object> source, TakeOperator take)
    {
        if (take.Expression is LiteralExpression lit)
        {
            var n = Convert.ToInt32(ParseLiteral(lit));
            return source.Take(n);
        }

        throw new NotSupportedException("Take must be a literal integer.");
    }

    private static object? GetPropValue(object obj, string name)
    {
        Type type = obj.GetType();
        PropertyInfo? propertyInfo = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        object? propValue = propertyInfo?.GetValue(obj);
        return propValue;
    }

    private static object ParseLiteral(LiteralExpression lit)
    {
        var text = lit.Token.Text;
        if (lit.Kind == SyntaxKind.StringLiteralExpression)
        {
            return text.Trim('\'', '"');
        }

        if (lit.Kind == SyntaxKind.IntLiteralExpression)
        {
            return int.Parse(text);
        }

        if (lit.Kind == SyntaxKind.RealLiteralExpression)
        {
            return double.Parse(text);
        }

        return text;
    }
}