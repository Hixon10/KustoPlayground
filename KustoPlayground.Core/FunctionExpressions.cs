using System.Globalization;
using System.Text;
using System.Web;
using Kusto.Language.Symbols;

namespace KustoPlayground.Core;

internal static class FunctionExpressions
{
    internal static string Base64EncodeToString(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("base64_encode_tostring requires exactly 1 argument.");
        }

        if (args[0] == null)
        {
            return string.Empty;
        }

        string input = args[0]!.ToString() ?? string.Empty;
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes);
    }

    internal static string Base64DecodeToString(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("base64_decode_tostring requires exactly 1 argument.");
        }

        if (args[0] == null)
        {
            return string.Empty;
        }

        string input = args[0]!.ToString() ?? string.Empty;
        var buffer = new byte[System.Buffers.Text.Base64.GetMaxDecodedFromUtf8Length(input.Length)];
        return Convert.TryFromBase64String(input, buffer, out int length)
            ? Encoding.UTF8.GetString(buffer, 0, length) : string.Empty;
    }

    public static DateTime? ToDateTime(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("todatetime requires exactly 1 argument.");
        }

        return (DateTime?)ScalarFunctions.ConvertTo(args[0], ScalarTypes.DateTime);
    }

    public static TimeSpan? MakeTimeSpan(object?[] args)
    {
        if (args.Length < 2)
        {
            throw new ArgumentException("make_timespan requires at least 2 arguments.");
        }

        if (args.Length > 4)
        {
            throw new ArgumentException("make_timespan requires at most 4 arguments.");
        }

        if (Array.Exists(args, v => v is null)) return null;
        int start = args.Length == 4 ? 1 : 0;
        int days = start == 1 ? Convert.ToInt32(args[0], CultureInfo.InvariantCulture) : 0;
        int hours = Convert.ToInt32(args[start], CultureInfo.InvariantCulture);
        int minutes = Convert.ToInt32(args[start + 1], CultureInfo.InvariantCulture);
        double seconds = args.Length > 2 ? Convert.ToDouble(args[^1], CultureInfo.InvariantCulture) : 0;
        return new TimeSpan(days, hours, minutes, 0) + TimeSpan.FromSeconds(seconds);
    }

    public static object? ToTimeSpan(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("totimespan requires exactly 1 argument.");
        }

        return ScalarFunctions.ConvertTo(args[0], ScalarTypes.TimeSpan);
    }

    internal static DateTime? Clock(string name, object?[] args, DateTime queryTime)
    {
        return args switch
        {
            [] when name == "now" => queryTime,
            [null] => null,
            [TimeSpan offset] => name == "ago" ? queryTime - offset : queryTime + offset,
            _ => throw new ArgumentException($"{name} expects a timespan offset.")
        };
    }

    public static string UrlEncode(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("url_encode requires exactly 1 argument.");
        }

        if (args[0] == null)
        {
            return string.Empty;
        }

        string input = args[0]!.ToString() ?? string.Empty;
        return HttpUtility.UrlEncode(input);
    }

    public static string UrlDecode(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("url_decode requires exactly 1 argument.");
        }

        if (args[0] == null)
        {
            return string.Empty;
        }

        string input = args[0]!.ToString() ?? string.Empty;
        return HttpUtility.UrlDecode(input);
    }

    public static string ToUpper(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("toupper requires exactly 1 argument.");
        }

        if (args[0] == null)
        {
            return string.Empty;
        }

        string input = args[0]!.ToString() ?? string.Empty;
        return input.ToUpper(CultureInfo.InvariantCulture);
    }

    public static string ToLower(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("tolower requires exactly 1 argument.");
        }

        if (args[0] == null)
        {
            return string.Empty;
        }

        string input = args[0]!.ToString() ?? string.Empty;
#pragma warning disable CA1308
        return input.ToLower(CultureInfo.InvariantCulture);
#pragma warning restore CA1308
    }

    public static int StrLen(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("strlen requires exactly 1 argument.");
        }

        if (args[0] == null)
        {
            return 0;
        }

        string input = args[0]!.ToString() ?? string.Empty;
        return input.Length;
    }
}