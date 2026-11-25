using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;

namespace KustoPlayground.Core;

internal static class ScalarFunctions
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    internal static object? Evaluate(FunctionCallExpression call, object?[] args)
    {
        var name = call.Name.SimpleName;
        switch (name)
        {
            case "bin" or "floor":
                if (args.Length != 2) throw new ArgumentException($"{name} requires exactly two arguments.");
                return ScalarMath.Bin(args, call.ResultType);
            case "strcat":
                if (args.Length is < 1 or > 64) throw new ArgumentException("strcat requires between 1 and 64 arguments.");
                if (args.Length == 1) return ArgumentText(call, 0, args[0]);
                var result = new StringBuilder();
                for (int i = 0; i < args.Length; i++) result.Append(ArgumentText(call, i, args[i]));
                return result.ToString();
            case "trim":
                if (args.Length != 2) throw new ArgumentException("trim requires a pattern and a source string.");
                if (args[0] is not string { Length: > 0 } pattern) throw new ArgumentException("trim requires a nonempty regular expression.");
                if (args[1] is not (null or string)) throw new ArgumentException("trim requires a string source.");
                return Regex.Replace((string?)args[1] ?? string.Empty, $@"\A(?:{pattern})|(?:{pattern})\z",
                    string.Empty, RegexOptions.CultureInvariant, RegexTimeout);
        }

        if (args.Length != 1) throw new ArgumentException($"{name} requires exactly one argument.");
        if (name == "tostring") return ArgumentText(call, 0, args[0]);
        if (name == "toint" && args[0] is string text && call.ArgumentList.Expressions[0].Element.ResultType is DynamicSymbol)
            return ToInteger<long>(text) is long integer ? unchecked((int)integer) : null;
        var type = name switch
        {
            "toint" => ScalarTypes.Int,
            "tolong" => ScalarTypes.Long,
            "toreal" or "todouble" => ScalarTypes.Real,
            "tobool" or "toboolean" => ScalarTypes.Bool,
            _ => throw new NotSupportedException($"Unsupported scalar function: {name}")
        };
        return ConvertTo(args[0], type);
    }

    private static string ArgumentText(FunctionCallExpression call, int index, object? value) =>
        call.ArgumentList.Expressions[index].Element.ResultType is DynamicSymbol ? DynamicValues.ToText(value) : ToText(value);

    internal static object? ConvertTo(object? value, TypeSymbol type)
    {
        if (type == ScalarTypes.String) return ToText(value);
        if (value is JsonElement json) value = DynamicValues.Read(json);
        if (value is null || type is DynamicSymbol) return value;
        if (type == ScalarTypes.Int) return ToInteger<int>(value);
        if (type == ScalarTypes.Long) return ToInteger<long>(value);
        if (type == ScalarTypes.Real) return ScalarMath.ToReal(value);
        if (type == ScalarTypes.Bool)
        {
            if (value is string text)
            {
                if (bool.TryParse(text, out var flag)) return flag;
                return long.TryParse(text, NumberStyles.Integer, Culture, out var number) ? number != 0 : null;
            }
            return value is bool || CompareUtils.IsNumeric(value) ? Convert.ToBoolean(value, Culture) : null;
        }
        if (type == ScalarTypes.Decimal)
        {
            if (value is string text)
                return decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, Culture, out var number) ? number : null;
            if (value is not bool && !CompareUtils.IsNumeric(value)) return null;
            try { return Convert.ToDecimal(value, Culture); }
            catch (OverflowException) { return null; }
        }
        if (type == ScalarTypes.DateTime)
        {
            if (value is long ticks) return ticks >= 0 && ticks <= DateTime.MaxValue.Ticks ? new DateTime(ticks, DateTimeKind.Utc) : null;
            if (value is DateTime date) return Utc(date);
            if (value is DateTimeOffset offset) return offset.UtcDateTime;
            return value is string text && DateTime.TryParse(text, Culture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date) ? date : null;
        }
        if (type == ScalarTypes.TimeSpan)
        {
            if (value is long ticks) return TimeSpan.FromTicks(ticks);
            if (value is TimeSpan span) return span;
            return value is string text && TimeSpan.TryParse(text, Culture, out span) ? span : null;
        }
        if (type == ScalarTypes.Guid)
        {
            if (value is Guid id) return id;
            return value is string text && Guid.TryParse(text, out id) ? id : null;
        }
        throw new NotSupportedException($"Unsupported scalar conversion to {type.Name}.");
    }

    private static T? ToInteger<T>(object value) where T : struct, IBinaryInteger<T>, IMinMaxValue<T>
    {
        switch (value)
        {
            case string text:
                // Decimal strings allow padding and a sign; hexadecimal strings must start at the prefix.
                return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? T.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, Culture, out var hex) ? hex : null
                    : T.TryParse(text, NumberStyles.Integer, Culture, out var integer) ? integer : null;
            case bool flag: return flag ? T.One : T.Zero;
            case byte or sbyte or short or ushort or int or uint or long:
                // Unlike an out-of-range string, narrowing an integer value wraps.
                return T.CreateTruncating(Convert.ToInt64(value, Culture));
            case ulong unsigned: return T.CreateTruncating(unsigned);
            case DateTime date: return T.CreateTruncating(Utc(date).Ticks);
            case TimeSpan span: return T.CreateTruncating(span.Ticks);
            case decimal number:
                var whole = decimal.Truncate(number);
                return whole >= decimal.CreateChecked(T.MinValue) && whole <= decimal.CreateChecked(T.MaxValue)
                    ? T.CreateChecked(whole) : null;
            case float or double:
                var real = Convert.ToDouble(value, Culture);
                if (!double.IsFinite(real)) return null;
                // Kusto returns the signed minimum for finite reals outside the target range.
                var lower = double.CreateChecked(T.MinValue);
                return real < lower || real >= -lower ? T.MinValue : T.CreateChecked(real);
            default: return null;
        }
    }

    internal static string ToText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        double number => FormatReal(number),
        float number => FormatReal(number),
        decimal number => number.ToString("0.############################", Culture),
        DateTime date => Utc(date).ToString("O", Culture),
        DateTimeOffset offset => offset.UtcDateTime.ToString("O", Culture),
        TimeSpan span => span.ToString("c", Culture),
        JsonElement or object?[] or Dictionary<string, object?> => DynamicValues.ToText(value),
        _ => Convert.ToString(value, Culture) ?? string.Empty
    };

    private static DateTime Utc(DateTime date) => date.Kind == DateTimeKind.Local
        ? date.ToUniversalTime() : DateTime.SpecifyKind(date, DateTimeKind.Utc);

    private static string FormatReal<T>(T number) where T : IFloatingPointIeee754<T>
    {
        if (T.IsNaN(number)) return "NaN";
        if (T.IsPositiveInfinity(number)) return "inf";
        if (T.IsNegativeInfinity(number)) return "-inf";

        // Keep the shortest round-trip digits, but use Kusto's fixed/scientific cutoffs and exponent spelling.
        var text = number.ToString("R", Culture);
        var marker = text.IndexOf('E', StringComparison.Ordinal);
        if (marker < 0) return text.Contains('.', StringComparison.Ordinal) ? text : text + ".0";
        var exponent = int.Parse(text.AsSpan(marker + 1), NumberStyles.AllowLeadingSign, Culture);
        var mantissa = text[..marker];
        if (exponent is < -6 or >= 21) return mantissa + "e" + exponent.ToString(Culture);

        var sign = number < T.Zero ? "-" : string.Empty;
        var digits = mantissa.TrimStart('-').Replace(".", string.Empty, StringComparison.Ordinal);
        if (exponent < 0) return sign + "0." + new string('0', -1 - exponent) + digits;
        var point = 1 + exponent;
        return sign + (point < digits.Length ? digits.Insert(point, ".") : digits.PadRight(point, '0') + ".0");
    }
}
