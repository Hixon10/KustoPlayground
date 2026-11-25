using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator
{
    // These service rules are stricter than the signatures in Kusto.Language. Check the
    // bound expressions, not individual rows: an invalid query still fails on an empty table.
    private static IEnumerable<ExecutionError> ValidateServiceSemantics(KustoCode code)
    {
        foreach (var expression in code.Syntax.GetDescendants<Expression>())
        {
            string? message = expression switch
            {
                LiteralExpression { Kind: SyntaxKind.DateTimeLiteralExpression, Token.ValueText: "" } =>
                    "An empty datetime literal is invalid; use now() or datetime(null).",
                BinaryExpression { Kind: SyntaxKind.EqualExpression or SyntaxKind.NotEqualExpression,
                    Left.ResultType: DynamicSymbol, Right.ResultType: DynamicSymbol } =>
                    "Cannot compare two dynamic values without an explicit cast.",
                InExpression membership when membership.Right.Expressions.Any(e => e.Element.ResultType is not TableSymbol && !e.Element.IsConstant) =>
                    "The scalar values on the right of in must be constants, not row columns.",
                MakeSeriesOperator series => ValidateSeries(series),
                FunctionCallExpression { ReferencedSymbol: FunctionSymbol f } call
                    when QueryGlobals.IsBuiltInFunction(f) || QueryGlobals.IsAggregateFunction(f) => ValidateCall(call),
                _ => null
            };
            if (message is not null) yield return new ExecutionError { Code = "SemanticError", Description = message };
        }
    }

    private static string? ValidateCall(FunctionCallExpression call)
    {
        var args = call.ArgumentList.Expressions;
        var name = call.Name.SimpleName;
        switch (name)
        {
            case "extract_json" or "extractjson" when args[1].Element.ResultType is DynamicSymbol:
                return $"{name} requires a string data source; convert dynamic values with tostring().";
            case "dcount" or "dcountif" when args[0].Element.ResultType is DynamicSymbol:
                return $"{name} requires a scalar argument; cast the dynamic value first.";
            case "array_index_of" when args[1].Element.ResultType is DynamicSymbol || args[1].Element.ResultType == ScalarTypes.Bool:
                return "array_index_of requires a numeric, datetime, timespan, string or guid search value.";
            case "case" or "coalesce" or "iff" or "iif":
                var types = args.Where((_, i) => name switch
                {
                    "coalesce" => true,
                    "case" => i % 2 == 1 || i == args.Count - 1,
                    _ => i > 0
                }).Select(a => a.Element.ResultType).ToHashSet();
                if (types.Contains(ScalarTypes.Real) && (types.Contains(ScalarTypes.Int) || types.Contains(ScalarTypes.Long)))
                    return $"{name} requires compatible result types; cast integer and real branches to the same type.";
                break;
        }
        return null;
    }

    // Validate constant values in their lexical scope (including let aliases), before enumerating rows.
    private void ValidateConstantArguments(Expression expression)
    {
        var declaration = expression.GetFirstAncestor<FunctionDeclaration>();
        foreach (var series in expression.GetDescendantsOrSelf<MakeSeriesOperator>(s =>
            s.GetFirstAncestor<FunctionDeclaration>() == declaration))
            _ = GetSeriesBounds(series);
        foreach (var call in expression.GetDescendantsOrSelf<FunctionCallExpression>(f =>
            (f.Name.SimpleName is "bin_at" or "array_index_of") && f.ReferencedSymbol is FunctionSymbol fn && QueryGlobals.IsBuiltInFunction(fn)))
        {
            var args = call.ArgumentList.Expressions;
            object? Constant(int i) => args[i].Element.IsConstant ? EvalOperand(args[i].Element, []) :
                throw new ArgumentException($"{call.Name.SimpleName} requires a constant for argument {i + 1}.");
            if (call.Name.SimpleName == "bin_at")
            {
                if (!ScalarMath.IsPositiveBinSize(Constant(1)) || Constant(2) is null)
                    throw new ArgumentException("bin_at requires a positive bin size and a non-null fixed point.");
            }
            else if (args.Count == 5 && Constant(4) is not (int and > 0 or long and > 0))
                throw new ArgumentException("array_index_of requires a positive occurrence.");
        }
    }
}
