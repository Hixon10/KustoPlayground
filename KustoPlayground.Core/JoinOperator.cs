using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator
{
    private enum JoinFlavor { InnerUnique, Inner, LeftOuter, RightOuter, FullOuter, LeftSemi, RightSemi, LeftAnti, RightAnti }

    private sealed class JoinBucket
    {
        internal List<QueryRow> Rows { get; } = [];
        internal bool Matched { get; set; }
    }

    private IEnumerable<QueryRow> ApplyJoin(IEnumerable<QueryRow> source, JoinOperator op)
    {
        if (op.Parent is not PipeExpression { Expression.ResultType: TableSymbol leftSchema } ||
            op.Expression.ResultType is not TableSymbol rightSchema || op.ResultType is not TableSymbol outputSchema)
            throw new ArgumentException("join requires bound tabular inputs.");
        if (op.ConditionClause is not JoinOnClause on)
            throw new NotSupportedException("join requires an on clause with equality keys.");

        var kind = op.Parameters.FirstOrDefault(p => p.Name.SimpleName == "kind")?.Expression.LiteralValue switch
        {
            null or "innerunique" => JoinFlavor.InnerUnique,
            "inner" => JoinFlavor.Inner,
            "leftouter" => JoinFlavor.LeftOuter,
            "rightouter" => JoinFlavor.RightOuter,
            "fullouter" => JoinFlavor.FullOuter,
            "leftsemi" => JoinFlavor.LeftSemi,
            "rightsemi" => JoinFlavor.RightSemi,
            "leftanti" or "leftantisemi" or "anti" => JoinFlavor.LeftAnti,
            "rightanti" or "rightantisemi" => JoinFlavor.RightAnti,
            var other => throw new NotSupportedException($"Unsupported join kind: {other}")
        };
        var leftKeys = new List<NameReference>();
        var rightKeys = new List<NameReference>();
        foreach (var item in on.Expressions) AddJoinKeys(item.Element, leftKeys, rightKeys);
        var rightRows = ExecuteExpression(op.Expression);
        bool leftOnly = kind is JoinFlavor.LeftSemi or JoinFlavor.LeftAnti;
        bool rightOnly = kind is JoinFlavor.RightSemi or JoinFlavor.RightAnti;

        QueryRow Emit(QueryRow? left, QueryRow? right)
        {
            var result = new QueryRow(outputSchema.Columns.Count, StringComparer.Ordinal);
            int position = 0;
            void Append(TableSymbol schema, QueryRow? row)
            {
                foreach (var column in schema.Columns)
                {
                    // Copy query values verbatim. Only a wholly absent side gets typed padding.
                    var value = row is not null ? row[column.Name] : column.Type == ScalarTypes.String ? string.Empty : null;
                    result.Add(outputSchema.Columns[position++].Name, value);
                }
            }
            if (!rightOnly) Append(leftSchema, left);
            if (!leftOnly) Append(rightSchema, right);
            return result;
        }

        if (leftOnly || rightOnly)
        {
            var buildKeys = leftOnly ? rightKeys : leftKeys;
            var probeKeys = leftOnly ? leftKeys : rightKeys;
            var keys = (leftOnly ? rightRows : source).Select(row => ReadJoinKey(row, buildKeys))
                .ToHashSet(GroupKeyComparer.Instance);
            bool keepMatch = kind is JoinFlavor.LeftSemi or JoinFlavor.RightSemi;
            foreach (var row in leftOnly ? source : rightRows)
            {
                if (keys.Contains(ReadJoinKey(row, probeKeys)) == keepMatch)
                    yield return leftOnly ? Emit(row, null) : Emit(null, row);
            }
            yield break;
        }

        var index = new Dictionary<object?[], JoinBucket>(GroupKeyComparer.Instance);
        foreach (var right in rightRows)
        {
            var key = ReadJoinKey(right, rightKeys);
            if (!index.TryGetValue(key, out var bucket)) index.Add(key, bucket = new JoinBucket());
            bucket.Rows.Add(right);
        }

        foreach (var left in source)
        {
            if (index.TryGetValue(ReadJoinKey(left, leftKeys), out var bucket))
            {
                if (kind == JoinFlavor.InnerUnique && bucket.Matched) continue;
                bucket.Matched = true;
                foreach (var right in bucket.Rows) yield return Emit(left, right);
            }
            else if (kind is JoinFlavor.LeftOuter or JoinFlavor.FullOuter)
            {
                yield return Emit(left, null);
            }
        }

        if (kind is JoinFlavor.RightOuter or JoinFlavor.FullOuter)
        {
            foreach (var bucket in index.Values)
            {
                if (bucket.Matched) continue;
                foreach (var right in bucket.Rows) yield return Emit(null, right);
            }
        }
    }

    private static void AddJoinKeys(Expression expression, List<NameReference> left, List<NameReference> right)
    {
        switch (UnwrapParens(expression))
        {
            case NameReference common:
                left.Add(common);
                right.Add(common);
                break;
            case BinaryExpression { Kind: SyntaxKind.EqualExpression } equal:
                left.Add(QualifiedJoinColumn(equal.Left, "$left"));
                right.Add(QualifiedJoinColumn(equal.Right, "$right"));
                break;
            case BinaryExpression { Kind: SyntaxKind.AndExpression } both:
                AddJoinKeys(both.Left, left, right);
                AddJoinKeys(both.Right, left, right);
                break;
            default:
                throw new NotSupportedException($"Unsupported join key: {expression}");
        }
    }

    private static NameReference QualifiedJoinColumn(Expression expression, string side) =>
        UnwrapParens(expression) is PathExpression { Expression: NameReference table, Selector: NameReference column } &&
        table.SimpleName == side ? column : throw new NotSupportedException($"Expected {side}.<column> in join condition.");

    private static object?[] ReadJoinKey(QueryRow row, List<NameReference> columns)
    {
        var key = new object?[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var value = row[column.SimpleName];
            key[i] = value switch
            {
                null when column.ResultType == ScalarTypes.String => string.Empty,
                // Explicit equality keys allow int on one side and long on the other.
                byte or sbyte or short or ushort or int or uint or ulong => ScalarFunctions.ConvertTo(value, ScalarTypes.Long),
                float number => (double)number,
                char character => character.ToString(),
                _ => value
            };
        }
        return key;
    }
}
