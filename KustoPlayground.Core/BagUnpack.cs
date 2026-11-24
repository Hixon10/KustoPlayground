using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator
{
    private readonly Dictionary<Expression, IReadOnlyList<ColumnSymbol>> _discoveredSchemas = [];

    private IEnumerable<QueryRow> UnpackBag(IEnumerable<QueryRow> source, EvaluateOperator op)
    {
        var call = op.FunctionCall;
        if (call.Name.SimpleName != "bag_unpack") throw new NotSupportedException($"Unsupported plugin: {call.Name.SimpleName}.");
        var args = call.ArgumentList.Expressions;
        if (args.Count == 0 || args[0].Element is not NameReference bag || op.Parent is not PipeExpression pipe)
            throw new ArgumentException("bag_unpack requires a dynamic column.");
        var prefix = string.Empty;
        var conflict = "error";
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Count; i++)
        {
            var arg = args[i].Element;
            var value = EvalOperand(UnwrapOutput(arg), new QueryRow());
            var option = arg is SimpleNamedExpression named ? named.Name.SimpleName :
                i switch { 1 => "column_prefix", 2 => "columnsConflict", 3 => "ignoredProperties", _ => "" };
            switch (option, value)
            {
                case ("column_prefix", string text): prefix = text; break;
                case ("columnsConflict", "error" or "replace_source" or "keep_source"): conflict = (string)value; break;
                case ("ignoredProperties", object?[] items):
                    foreach (var item in items) ignored.Add(item as string ?? throw new ArgumentException("Expected a string property name."));
                    break;
                default: throw new ArgumentException($"Invalid bag_unpack option: {option}.");
            }
        }

        var input = ColumnsOf(pipe.Expression).Where(c => c.Name != bag.SimpleName).ToDictionary(c => c.Name, StringComparer.Ordinal);
        var output = new Dictionary<string, ColumnSymbol>(StringComparer.Ordinal);
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        if (op.Schema is null || op.Schema.Schema.AsteriskToken is not null)
            foreach (var c in input.Values) output.Add(c.Name, c);

        void Slot(string key, TypeSymbol type)
        {
            var name = prefix + key;
            if (input.ContainsKey(name))
            {
                if (conflict == "error") throw new ArgumentException($"bag_unpack column '{name}' already exists.");
                if (conflict == "keep_source") return;
            }
            slots[name] = key;
            output[name] = new ColumnSymbol(name, type);
        }

        if (op.Schema is not null)
        {
            foreach (var item in op.Schema.Schema.Columns)
            {
                var name = item.Element.Name.SimpleName;
                if (input.TryGetValue(name, out var existing)) output[name] = existing;
                else if (name.StartsWith(prefix, StringComparison.Ordinal))
                    Slot(name[prefix.Length..], ((TableSymbol)op.ResultType).GetColumn(name).Type);
                else throw new ArgumentException($"Output column '{name}' does not have the requested prefix.");
            }
        }
        else
        {
            // Without an explicit schema every row contributes to slot discovery, including the last one.
            var rows = source.ToArray();
            source = rows;
            var types = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
            foreach (var row in rows)
                if (row[bag.SimpleName] is QueryRow properties)
                    foreach (var (key, value) in properties)
                    {
                        if (value is null || ignored.Contains(key)) continue;
                        var type = ColumnType(value.GetType());
                        types[key] = types.TryGetValue(key, out var prior) && prior != type ? ScalarTypes.Dynamic : type;
                    }
            foreach (var (key, type) in types) Slot(key, type);
        }
        _discoveredSchemas[op] = output.Values.ToArray();
        return source.Select(row =>
        {
            var result = new QueryRow(output.Count, StringComparer.Ordinal);
            var properties = row[bag.SimpleName] as QueryRow;
            foreach (var (name, column) in output)
            {
                result[name] = slots.TryGetValue(name, out var key)
                    ? ScalarFunctions.ConvertTo(ignored.Contains(key) ? null : properties?.GetValueOrDefault(key), column.Type)
                    : row[name];
            }
            return result;
        });
    }

    private IReadOnlyList<ColumnSymbol> ColumnsOf(Expression expression)
    {
        var table = (TableSymbol)expression.ResultType;
        var declared = table.Columns;
        if (expression is NameReference { ReferencedSymbol: VariableSymbol { Source: Expression source } } && table.IsOpen)
            return ColumnsOf(source);
        if (expression is ParenthesizedExpression parens) return ColumnsOf(parens.Expression);
        if (expression is MaterializeExpression materialize) return ColumnsOf(materialize.Expression);
        if (expression is not PipeExpression pipe) return declared;
        if (_discoveredSchemas.TryGetValue(pipe.Operator, out var actual)) return actual;
        if (!table.IsOpen) return declared;

        var input = ColumnsOf(pipe.Expression);
        if (pipe.Operator is FilterOperator or TakeOperator or SortOperator or TopOperator) return input;
        if (pipe.Operator is ProjectAwayOperator away)
        {
            var names = away.Expressions.Select(e => (NameReference)e.Element).ToArray();
            return input.Where(c => !names.Any(n => IsWildcard(n) ? KustoFacts.Matches(n.SimpleName, c.Name, false)
                : n.SimpleName == c.Name)).ToArray();
        }
        if (pipe.Operator is DistinctOperator { Expressions: [{ Element: StarExpression }] }) return input;

        var columns = input.ToDictionary(c => c.Name, StringComparer.Ordinal);
        // The binder marks references to discovered slots as unknown. Follow column aliases
        // back to their input slot instead of guessing its type from a surviving result row.
        ColumnSymbol Resolve(ColumnSymbol c) => c.Type == ScalarTypes.Unknown &&
            columns.TryGetValue(c.Source is NameReference n ? n.SimpleName : c.Name, out var original)
                ? c.WithType(original.Type) : c;
        if (pipe.Operator is not ExtendOperator) return declared.Select(Resolve).ToArray();

        var extended = new Dictionary<string, ColumnSymbol>(columns, StringComparer.Ordinal);
        foreach (var column in declared)
            if (column.Source is not null || columns.ContainsKey(column.Name)) extended[column.Name] = Resolve(column);
        return extended.Values.ToArray();
    }
}
