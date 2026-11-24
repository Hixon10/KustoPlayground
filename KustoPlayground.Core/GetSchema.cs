using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator
{
    private static IEnumerable<QueryRow> GetSchema(GetSchemaOperator op)
    {
        if (op.KindParameter is not null) throw new NotSupportedException("Only the tabular getschema output is supported.");
        if (op.Parent is not PipeExpression { Expression.ResultType: TableSymbol input })
            throw new ArgumentException("getschema requires tabular input.");

        // The binder appends parse captures, including overwrites; the row keeps the original ordinal.
        var columns = new Dictionary<string, ColumnSymbol>(StringComparer.Ordinal);
        foreach (var col in input.Columns) columns[col.Name] = col;
        int ordinal = 0;
        foreach (var column in columns.Values)
        {
            var type = SchemaType(column, ((TableSymbol)op.ResultType).Columns[1]);
            yield return new QueryRow
            {
                ["ColumnName"] = column.Name, ["ColumnOrdinal"] = ordinal++,
                ["DataType"] = type.Name switch
                {
                    "bool" => "System.SByte", "int" => "System.Int32", "long" => "System.Int64",
                    "real" => "System.Double", "decimal" => "System.Data.SqlTypes.SqlDecimal", "string" => "System.String",
                    "datetime" => "System.DateTime", "timespan" => "System.TimeSpan", "guid" => "System.Guid",
                    "dynamic" => "System.Object", _ => throw new NotSupportedException($"Unknown schema type: {type.Name}")
                },
                ["ColumnType"] = type.Name
            };
        }
    }

    private static TypeSymbol SchemaType(ColumnSymbol column, ColumnSymbol ordinalColumn)
    {
        // ADX returns an int ordinal; Kusto.Language currently models this particular system column as long.
        var origin = column;
        while (origin.Source is NameReference { ReferencedSymbol: ColumnSymbol source, Parent: not ParseOperator } &&
               origin.Type == source.Type && origin != source)
            origin = source;
        return origin == ordinalColumn ? ScalarTypes.Int : column.Type;
    }
}
