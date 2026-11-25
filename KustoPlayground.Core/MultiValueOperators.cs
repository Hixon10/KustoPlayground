using System.Globalization;
using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator
{
    private sealed record MvColumn(string Name, Expression Expression, TypeSymbol? CastType);

    private IEnumerable<QueryRow> ApplyMvExpand(IEnumerable<QueryRow> source, MvExpandOperator op)
    {
        var columns = MvColumns(op.Expressions.Select(item => (item.Element.Expression, (ToTypeOfClause?)item.Element.ToTypeOf)),
            (TableSymbol)op.ResultType);
        var index = MvItemIndex(op.Parameters);
        var limit = MvRowLimit(op.RowLimitClause?.RowLimit);
        bool asArray = op.Parameters.FirstOrDefault(p => p.Name.SimpleName is "kind" or "bagexpansion")
            ?.Expression.LiteralValue is "array";
        return source.SelectMany(row => ExpandMvRow(row, columns, index, limit, asArray));
    }

    private IEnumerable<QueryRow> ApplyMvApply(IEnumerable<QueryRow> source, MvApplyOperator op)
    {
        var schema = (TableSymbol)op.ResultType;
        var columns = MvColumns(op.Expressions.Select(item => (item.Element.Expression, (ToTypeOfClause?)item.Element.ToTypeOf)), schema);
        var index = MvItemIndex(op.Parameters);
        var limit = MvRowLimit(op.RowLimitClause?.RowLimit);
        foreach (var row in source)
        {
            var expanded = ExpandMvRow(row, columns, index, limit, asArray: false);
            foreach (var subrow in ApplyMvSubquery(expanded, op.Subquery.Expression))
            {
                // The bound schema omits expanded columns that the subquery drops, but restores other input columns.
                var result = new QueryRow(schema.Columns.Count, StringComparer.Ordinal);
                foreach (var column in schema.Columns)
                {
                    result[column.Name] = subrow.TryGetValue(column.Name, out var value)
                        ? value : row.GetValueOrDefault(column.Name);
                }
                yield return result;
            }
        }
    }

    private IEnumerable<QueryRow> ApplyMvSubquery(IEnumerable<QueryRow> source, Expression expression) => expression switch
    {
        PipeExpression pipe => ApplyOperator(ApplyMvSubquery(source, pipe.Expression), pipe.Operator),
        ParenthesizedExpression parens => ApplyMvSubquery(source, parens.Expression),
        QueryOperator op => ApplyOperator(source, op),
        _ => throw new NotSupportedException($"Unsupported mv-apply subquery: {expression.Kind}")
    };

    private static MvColumn[] MvColumns(IEnumerable<(Expression Expression, ToTypeOfClause? Cast)> expressions,
        TableSymbol schema)
    {
        var columns = new List<MvColumn>();
        int unnamed = 1;
        foreach (var (expression, cast) in expressions)
        {
            var name = expression switch
            {
                SimpleNamedExpression named => named.Name.SimpleName,
                NameReference reference => reference.SimpleName,
                _ => schema.Columns.FirstOrDefault(c => c.Source == expression)?.Name
                    ?? KustoFacts.GetExpressionResultName(expression) ?? $"Column{unnamed++}"
            };
            columns.Add(new MvColumn(name, UnwrapOutput(expression), cast?.TypeOf.ReferencedSymbol as TypeSymbol));
        }
        return columns.ToArray();
    }

    private static string? MvItemIndex(SyntaxList<NamedParameter> parameters) =>
        (parameters.FirstOrDefault(p => p.Name.SimpleName == "with_itemindex")?.Expression as NameDeclaration)?.SimpleName;

    private int MvRowLimit(Expression? expression)
    {
        if (expression is null) return int.MaxValue;
        var limit = EvalOperand(expression, new QueryRow());
        if (limit is not (int or long)) throw new ArgumentException("The row limit must be an integer.", nameof(expression));
        return (int)Math.Clamp(Convert.ToInt64(limit, CultureInfo.InvariantCulture), 0, int.MaxValue);
    }

    private IEnumerable<QueryRow> ExpandMvRow(QueryRow row, MvColumn[] columns, string? index, int limit, bool asArray)
    {
        var values = columns.Select(column => MvElements(EvalOperand(column.Expression, row), asArray)).ToArray();
        int count = Math.Min(limit, values.Max(items => items.Length));
        for (int i = 0; i < count; i++)
        {
            var result = new QueryRow(row);
            for (int j = 0; j < columns.Length; j++)
            {
                var value = i < values[j].Length ? values[j][i] : null;
                result[columns[j].Name] = CastMvElement(value, columns[j].CastType);
            }
            if (index is not null) result[index] = (long)i;
            yield return result;
        }
    }

    private static object?[] MvElements(object? value, bool asArray) => value switch
    {
        object?[] array => array,
        QueryRow bag => bag.Select(pair => asArray
            ? (object?)new object?[] { pair.Key, pair.Value }
            : new QueryRow { [pair.Key] = pair.Value }).ToArray(),
        _ => [value]
    };

    private static object? CastMvElement(object? value, TypeSymbol? type)
    {
        if (type is null or DynamicSymbol) return value;
        if (type == ScalarTypes.String) return DynamicValues.ToText(value);
        if (value is null) return null;

        // Unlike tolong/tobool, expansion casts never parse strings or coerce bools to numbers.
        if (type == ScalarTypes.Int || type == ScalarTypes.Long || type == ScalarTypes.Real || type == ScalarTypes.Decimal)
            return CompareUtils.IsNumeric(value) ? ScalarFunctions.ConvertTo(value, type) : null;
        if (type == ScalarTypes.Bool)
            return value is bool or byte or sbyte or short or ushort or int or uint or long or ulong
                ? Convert.ToBoolean(value, CultureInfo.InvariantCulture) : null;
        if (type == ScalarTypes.DateTime)
            return value is DateTime or DateTimeOffset ? ScalarFunctions.ConvertTo(value, type) : null;
        if (type == ScalarTypes.TimeSpan) return value is TimeSpan or long ? ScalarFunctions.ConvertTo(value, type) : null;
        if (type == ScalarTypes.Guid) return value is Guid ? value : null;
        throw new NotSupportedException($"Unsupported mv expansion type: {type.Name}");
    }

}
