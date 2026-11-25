using System.Text;
using System.Text.RegularExpressions;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using QueryRow = System.Collections.Generic.Dictionary<string, object?>;

namespace KustoPlayground.Core;

internal sealed class ParsePattern
{
    private readonly List<(string Name, ScalarSymbol Type, string Group)> _columns = [];
    private readonly Regex _regex;
    private readonly bool _relaxed;

    internal ParsePattern(ParseOperator op)
    {
        string? Parameter(string name) =>
            op.Parameters.FirstOrDefault(p => p.Name.SimpleName == name)?.Expression.LiteralValue as string;
        var kind = Parameter("kind") ?? "simple";
        _relaxed = kind == "relaxed";
        bool regex = kind == "regex";
        var flags = Parameter("flags") ?? string.Empty;
        bool ungreedy = flags.Contains('U', StringComparison.Ordinal);
        var options = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;
        if (!regex || flags.Contains('s', StringComparison.Ordinal)) options |= RegexOptions.Singleline;
        if (flags.Contains('i', StringComparison.Ordinal)) options |= RegexOptions.IgnoreCase;
        if (flags.Contains('m', StringComparison.Ordinal)) options |= RegexOptions.Multiline;
        if (flags.Any(c => c is not ('i' or 'm' or 's' or 'U'))) throw new ArgumentException($"Unknown parse flags: {flags}");
        var pattern = new StringBuilder();
        foreach (var part in op.Patterns)
        {
            switch (part)
            {
                case StarExpression:
                    pattern.Append(regex && !ungreedy ? ".*" : ".*?");
                    break;
                case Expression { IsLiteral: true, LiteralValue: string delimiter }:
                    pattern.Append(regex ? Ungreedy(delimiter, ungreedy) : Regex.Escape(delimiter));
                    break;
                case NameDeclaration name:
                    Capture(name.SimpleName, ScalarTypes.String);
                    break;
                case NameAndTypeDeclaration typed when typed.Type.ReferencedSymbol is ScalarSymbol type:
                    Capture(typed.Name.SimpleName, type);
                    break;
                default:
                    throw new ArgumentException($"Unsupported parse pattern element: {part}");
            }
        }
        if (_columns.Count == 0) throw new ArgumentException("parse requires at least one captured column.");
        // Simple and relaxed patterns can start anywhere but must consume the suffix; regex searches for a match.
        if (!regex) pattern.Append(@"\z");
        _regex = new Regex(pattern.ToString(), options, TimeSpan.FromSeconds(1));

        void Capture(string name, ScalarSymbol type)
        {
            string group = FormattableString.Invariant($"KustoCapture{_columns.Count}");
            _columns.Add((name, type, group));
            string any = regex && !ungreedy ? ".*" : ".*?";
            string capture = _relaxed ? any : type.Name switch
            {
                "long" or "int" => @"[+-]?[0-9]+",
                "real" or "decimal" => @"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?",
                "bool" => "(?i:true|false|1|0)",
                "guid" => @"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}",
                _ => any
            };
            pattern.Append("(?<").Append(group).Append('>').Append(capture).Append(')');
        }
    }

    internal QueryRow Apply(QueryRow row, object? source)
    {
        var match = _regex.Match(ScalarFunctions.ToText(source));
        var captures = new object?[_columns.Count];
        bool valid = match.Success;
        if (valid)
        {
            for (int i = 0; i < _columns.Count; i++)
            {
                var column = _columns[i];
                captures[i] = ScalarFunctions.ConvertTo(match.Groups[column.Group].Value, column.Type);
                if (!_relaxed && captures[i] is null) { valid = false; break; }
            }
        }
        var output = new QueryRow(row);
        for (int i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            output[column.Name] = valid ? captures[i] : column.Type == ScalarTypes.String ? string.Empty : null;
        }
        return output;
    }

    // .NET has no ungreedy flag: invert quantifiers, not escaped characters, character classes or group prefixes.
    private static string Ungreedy(string pattern, bool enabled)
    {
        if (!enabled) return pattern;
        var result = new StringBuilder();
        bool inClass = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            result.Append(c);
            if (c == '\\' && i + 1 < pattern.Length) { result.Append(pattern[++i]); continue; }
            if (c == '[') inClass = true;
            if (c == ']') inClass = false;
            if (inClass || c is not ('+' or '*' or '?' or '}') || (c == '?' && (i == 0 || pattern[i - 1] == '('))) continue;
            if (i + 1 < pattern.Length && pattern[i + 1] == '?') i++;
            else result.Append('?');
        }
        return result.ToString();
    }
}
