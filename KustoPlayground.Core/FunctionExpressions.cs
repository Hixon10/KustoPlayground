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
}