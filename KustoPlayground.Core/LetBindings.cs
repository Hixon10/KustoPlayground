using System.Collections.Immutable;
using System.Text;
using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed partial class QueryEvaluator
{
    private abstract record Binding;
    private sealed record ValueBinding(Func<object?> Evaluate) : Binding;
    private sealed record TableBinding(Func<IEnumerable<QueryRow>> Evaluate, Func<IReadOnlyList<ColumnSymbol>>? Columns) : Binding;
    private sealed record FunctionBinding(FunctionDeclaration Declaration, QueryEvaluator Closure) : Binding;

    private QueryEvaluator(QueryEvaluator parent, ImmutableDictionary<string, Binding> bindings, bool functionCall = false)
        : this(parent._tables)
    {
        _bindings = bindings;
        _queryTime = parent._queryTime;
        if (!functionCall)
        {
            _cache = parent._cache;
            _discoveredSchemas = parent._discoveredSchemas;
        }
    }

    private T Cached<T>(Expression expression, Func<T> evaluate)
    {
        if (_cache.TryGetValue(expression, out var cached)) return (T)cached!;
        var value = evaluate();
        _cache.Add(expression, value);
        return value;
    }

    private QueryEvaluator BindStatements(IEnumerable<Statement> statements)
    {
        var scope = this;
        foreach (var statement in statements)
        {
            if (statement is not LetStatement let)
                throw new NotSupportedException($"Expected let before the result, found {statement.Kind}.");
            // let names an expression in its declaration scope, not a value in the caller's row.
            var closure = scope;
            if (let.Expression is not FunctionDeclaration) closure.ValidateConstantArguments(let.Expression);
            Binding binding = let.Expression switch
            {
                FunctionDeclaration declaration => new FunctionBinding(declaration, closure),
                { ResultType: TableSymbol } table => closure.BindTable(table),
                var expr => new ValueBinding(() => closure.EvalOperand(expr, new QueryRow()))
            };
            scope = new QueryEvaluator(scope, scope._bindings.SetItem(let.Name.SimpleName, binding));
        }
        return scope;
    }

    private IEnumerable<QueryRow> ReadTable(string name, Expression reference)
    {
        if (_bindings.TryGetValue(name, out var binding))
        {
            if (binding is TableBinding table) return table.Evaluate();
            if (binding is FunctionBinding udf) return CallTableFunction(udf, reference, []);
            throw new ArgumentException($"{name} is not a table.");
        }
        if (!_tables.TryGetValue(name, out var stored)) throw new ArgumentException($"Unknown table: {name}");
        // Storage distinguishes a missing cell for validation and import. KQL strings have no null value.
        return stored.Rows.Select(row => stored.Schema.Values.ToDictionary(c => c.Name,
            c => row._values[c.Name] ?? (c.GetColumnType() == typeof(string) ? string.Empty : null), StringComparer.Ordinal));
    }

    private IEnumerable<QueryRow> CallTableFunction(FunctionCallExpression call, IEnumerable<QueryRow>? piped = null)
    {
        if (_bindings.GetValueOrDefault(call.Name.SimpleName) is not FunctionBinding udf)
            throw new NotSupportedException($"Unknown tabular function: {call.Name.SimpleName}");
        return CallTableFunction(udf, call, call.ArgumentList.Expressions.Select(e => e.Element), piped);
    }

    private TableBinding BindTable(Expression value) => new(() => ExecuteExpression(value), () => ColumnsOf(value));

    private IEnumerable<QueryRow> CallTableFunction(FunctionBinding udf, Expression reference,
        IEnumerable<Expression> arguments, IEnumerable<QueryRow>? piped = null)
    {
        var (scope, body) = EnterFunction(udf, reference, arguments, e => EvalOperand(e, new QueryRow()), piped);
        var rows = scope.ExecuteExpression(body);
        // Inferred bag slots belong to this invocation's argument bindings, not the declaration.
        if (reference.ResultType is TableSymbol { IsOpen: true })
            _discoveredSchemas[reference.Parent is InvokeOperator invoke ? invoke : reference] = scope.ColumnsOf(body);
        return rows;
    }

    private (QueryEvaluator Scope, Expression Body) EnterFunction(FunctionBinding udf, Expression reference,
        IEnumerable<Expression> arguments, Func<Expression, object?> evaluate, IEnumerable<QueryRow>? piped = null)
    {
        var parameters = udf.Declaration.Parameters.Parameters.Select(p => p.Element).ToArray();
        var bindings = udf.Closure._bindings.ToBuilder();
        var supplied = new bool[parameters.Length];
        int next = 0;
        if (piped is not null)
        {
            if (parameters.Length == 0) throw new ArgumentException("invoke requires a tabular first parameter.");
            bindings[parameters[0].NameAndType.Name.SimpleName] = new TableBinding(() => piped,
                reference.Parent is InvokeOperator { Parent: PipeExpression pipe } ? () => ColumnsOf(pipe.Expression) : null);
            supplied[next++] = true;
        }
        foreach (var argument in arguments)
        {
            int i = argument is SimpleNamedExpression named
                ? Array.FindIndex(parameters, p => p.NameAndType.Name.SimpleName == named.Name.SimpleName) : next;
            if ((uint)i >= (uint)parameters.Length || supplied[i]) throw new ArgumentException($"Invalid argument: {argument}");
            var value = UnwrapOutput(argument);
            var declaration = parameters[i].NameAndType;
            bindings[declaration.Name.SimpleName] = declaration.Type is PrimitiveTypeExpression
                ? BindValue(evaluate(value), declaration.Type)
                : BindTable(value);
            supplied[i] = true;
            while (next < supplied.Length && supplied[next]) next++;
        }
        for (int i = 0; i < parameters.Length; i++)
        {
            if (supplied[i]) continue;
            var p = parameters[i];
            var value = p.DefaultValue?.Value ?? throw new ArgumentException($"Missing argument: {p.NameAndType.Name.SimpleName}");
            bindings[p.NameAndType.Name.SimpleName] = BindValue(udf.Closure.EvalOperand(value, new QueryRow()), p.NameAndType.Type);
        }
        // The binder specializes tabular/wildcard parameters to the supplied table at each call site.
        var body = reference.GetCalledFunctionBody() as FunctionBody ?? udf.Declaration.Body;
        var frame = new QueryEvaluator(udf.Closure, bindings.ToImmutable(), functionCall: true);
        var scope = frame.BindStatements(body.Statements.Select(s => s.Element));
        scope.ValidateConstantArguments(body.Expression);
        return (scope, body.Expression);
    }

    private static ValueBinding BindValue(object? value, TypeExpression declaration)
    {
        if (declaration.ReferencedSymbol is ScalarSymbol type && type is not DynamicSymbol && value is not null)
            value = ScalarFunctions.ConvertTo(value, type);
        return new ValueBinding(() => value);
    }

    private static IReadOnlyList<Symbol> UnionMembers(NameReference wildcard) => wildcard.ReferencedSymbol switch
    {
        GroupSymbol group => group.Members,
        TableSymbol or FunctionSymbol => [wildcard.ReferencedSymbol],
        _ => []
    };

    private static bool IsWildcard(NameReference name) => name.Name is WildcardedName or BracketedWildcardedName;

    private static KustoCode BindViewUnions(KustoCode code)
    {
        // The binder resolves wildcard views by name, but omits their columns from the union schema.
        // Bind explicit calls to the resolved views before any downstream operators use that schema.
        var matches = code.Syntax.GetDescendants<UnionOperator>().SelectMany(u => u.Expressions)
            .Select(item => item.Element).OfType<NameReference>().Where(IsWildcard)
            .Select(name => (Reference: name, Members: UnionMembers(name)))
            .Where(item => item.Members.Any(s => s is FunctionSymbol { IsView: true }))
            .OrderByDescending(item => item.Reference.TextStart);
        StringBuilder? expanded = null;
        foreach (var (reference, members) in matches)
        {
            expanded ??= new StringBuilder(code.Text);
            var names = members.Select(s => s is FunctionSymbol
                ? KustoFacts.GetBracketedName(s.Name) + "()" : KustoFacts.GetBracketedName(s.Name));
            expanded.Remove(reference.TextStart, reference.Width).Insert(reference.TextStart, string.Join(", ", names));
        }
        return expanded is null ? code : KustoCode.ParseAndAnalyze(expanded.ToString(), code.Globals);
    }

    private IEnumerable<QueryRow> ApplyUnion(IEnumerable<QueryRow>? left, UnionOperator union)
    {
        if (union.Parameters.Count != 0) throw new NotSupportedException("union parameters are not supported.");
        var inputs = new List<IEnumerable<QueryRow>>();
        if (left is not null) inputs.Add(left);
        foreach (var item in union.Expressions)
        {
            if (item.Element is NameReference wildcard && IsWildcard(wildcard))
            {
                foreach (var member in UnionMembers(wildcard)) inputs.Add(ReadTable(member.Name, wildcard));
            }
            else inputs.Add(ExecuteExpression(item.Element));
        }
        var columns = ((TableSymbol)union.ResultType).Columns;
        return inputs.SelectMany(rows => rows.Select(row =>
        {
            var result = new QueryRow();
            foreach (var column in columns)
            {
                if (!row.TryGetValue(column.Name, out var value))
                {
                    if (column.OriginalColumns is [var original, ..] && row.TryGetValue(original.Name, out var candidate) &&
                        candidate is not null && ColumnType(candidate.GetType()) == column.Type)
                        value = candidate;
                }
                result[column.Name] = value ?? (column.Type == ScalarTypes.String ? string.Empty : null);
            }
            return result;
        }));
    }
}
