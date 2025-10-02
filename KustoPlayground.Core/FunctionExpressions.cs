using System.Globalization;
using System.Web;

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
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes);
    }

    internal static string Base64DecodeToString(object?[] args)
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
        byte[] bytes = Convert.FromBase64String(input);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public static DateTime? ToDateTime(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("todatetime requires exactly 1 argument.");
        }

        if (args[0] is not string s)
        {
            return null;
        }

        if (DateTime.TryParse(s, out DateTime result))
        {
            return result;
        }

        return null;
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

        int days = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
        int hours = Convert.ToInt32(args[1], CultureInfo.InvariantCulture);
        int minutes = 0;
        int seconds = 0;

        if (args.Length >= 3)
        {
            minutes = Convert.ToInt32(args[2], CultureInfo.InvariantCulture);
        }

        if (args.Length >= 4)
        {
            seconds = Convert.ToInt32(args[3], CultureInfo.InvariantCulture);
        }

        return new TimeSpan(days, hours, minutes, seconds);
    }

    public static object? ToTimeSpan(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("totimespan requires exactly 1 argument.");
        }

        if (args[0] is not string s)
        {
            return null;
        }

        if (TimeSpan.TryParse(s, out TimeSpan result))
        {
            return result;
        }

        return null;
    }

    public static object? Now(object?[] args)
    {
        if (args.Length != 1 || args[0] is not TimeSpan ts)
        {
            return DateTime.UtcNow;
        }

        return DateTime.UtcNow.Add(ts);
    }

    public static object? Ago(object?[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("ago requires exactly 1 argument.");
        }

        if (args[0] is not TimeSpan ts)
        {
            throw new ArgumentException("ago requires TimeSpan argument.");
        }

        return DateTime.UtcNow.Subtract(ts);
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
}