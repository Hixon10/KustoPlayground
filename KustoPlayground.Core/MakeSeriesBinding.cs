using System.Globalization;
using System.Text;
using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator
{
    // Kusto.Language projects unnamed single-column tuple aggregates as scalars, and gives
    // the aggregate precedence over the axis name. Bind those cases with explicit aliases
    // so downstream indexing, getschema, and let bindings see the same schema as execution.
    private static KustoCode BindSeriesNames(KustoCode code)
    {
        var edits = new List<(int Start, int Length, string Text)>();
        foreach (var op in code.Syntax.GetDescendants<MakeSeriesOperator>())
        {
            if (op.OnClause.Expression is not NameReference axis || op.ResultType is not TableSymbol table) continue;
            var groups = op.ByClause?.Expressions.Count ?? 0;
            var aliases = op.Aggregates.Select(a => a.Element.Expression).OfType<SimpleNamedExpression>()
                .Select(a => a.Name.SimpleName).ToArray();
            if (aliases.Distinct(StringComparer.Ordinal).Count() != aliases.Length ||
                table.Columns.Take(groups).Any(c => c.Name == axis.SimpleName || aliases.Contains(c.Name, StringComparer.Ordinal)))
                continue; // Preserve diagnostics for actual duplicate declarations.
            var input = (op.Parent as PipeExpression)?.Expression.ResultType as TableSymbol;
            var names = op.Aggregates.Select(a =>
            {
                var expr = a.Element.Expression;
                if (expr is SimpleNamedExpression alias) return alias.Name.SimpleName;
                if (expr.RawResultType is TupleSymbol { Columns: [var field] }) return field.Name;
                return KustoFacts.GetExpressionResultName(expr, null, input);
            }).ToArray();
            var reserved = table.Columns.Take(groups).Select(c => c.Name).Append(axis.SimpleName)
                .Concat(aliases.Where(n => n != axis.SimpleName)).ToHashSet(StringComparer.Ordinal);
            var used = new HashSet<string>(reserved, StringComparer.Ordinal);
            foreach (var name in names.OfType<string>()) reserved.Add(name);
            for (int i = 0; i < op.Aggregates.Count; i++)
            {
                var expr = op.Aggregates[i].Element.Expression;
                var named = expr as SimpleNamedExpression;
                if (names[i] is not { } name || named is not null && name != axis.SimpleName) continue;
                if (!used.Add(name))
                {
                    int suffix = 1;
                    string candidate;
                    do { candidate = name + (suffix++).ToString(CultureInfo.InvariantCulture); }
                    while (reserved.Contains(candidate) || !used.Add(candidate));
                    name = candidate;
                }
                if (named is null && expr.RawResultType is not TupleSymbol &&
                    table.Columns.Count == groups + op.Aggregates.Count + 1 && table.Columns[groups + i].Name == name) continue;
                var quoted = KustoFacts.GetBracketedName(name);
                edits.Add(named is null ? (expr.TextStart, 0, quoted + "=") : (named.Name.TextStart, named.Name.Width, quoted));
            }
        }
        if (edits.Count == 0) return code;
        var text = new StringBuilder(code.Text);
        foreach (var (start, length, replacement) in edits.OrderByDescending(e => e.Start))
            text.Remove(start, length).Insert(start, replacement);
        return KustoCode.ParseAndAnalyze(text.ToString(), code.Globals);
    }

    private static (Expression? From, Expression? To, Expression Step) SeriesBounds(MakeSeriesRangeClause range) => range switch
    {
        MakeSeriesFromToStepClause main => (main.MakeSeriesFromClause?.Expression, main.MakeSeriesToClause?.Expression,
            main.MakeSeriesStepClause.Expression),
        MakeSeriesInRangeClause { Arguments.Expressions: [var start, var stop, var step] } =>
            (start.Element, stop.Element, step.Element),
        _ => throw new ArgumentException("make-series requires a step, or in range(start, stop, step).")
    };

    private static bool IsIntegralSeriesBound(SyntaxNode node, Diagnostic diagnostic)
    {
        if (node.GetFirstAncestorOrSelf<MakeSeriesRangeClause>() is not { Parent: MakeSeriesOperator op } range ||
            op.OnClause.Expression.ResultType != ScalarTypes.Int || range.ContainsSyntaxDiagnostics) return false;
        var (from, to, step) = SeriesBounds(range);
        return Matches(from) || Matches(to) || Matches(step);

        bool Matches(Expression? e) => e?.ResultType is ScalarSymbol { IsInteger: true } &&
            e.TextStart == diagnostic.Start && e.Width == diagnostic.Length;
    }
}
