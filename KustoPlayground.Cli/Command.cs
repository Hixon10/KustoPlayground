using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KustoPlayground.Core;

namespace KustoPlayground.Cli;

internal static class Command
{
    private const string Help = """
        Run KQL locally using the Kusto Playground engine.

        kustoplayground [--help]
        kustoplayground --execute --query <KQL> [--json <table JSON> | --csv <CSV text> | --json-file <path> | --csv-file <path>]

        Help: -h, --help, -help         Execute: -e, --execute, -execute
        Pass each value as one argument. File paths may be relative or absolute.
        Choose at most one input. JSON uses the playground's Name, Columns,
        and Rows format. CSV needs a header and data; its table is named Input.
        Omit the input for print, datatable, or other self-contained queries.

        Example: kustoplayground -e --query "print Answer=6*7"
        Success: JSON on stdout (schema, then rows in results.items; duration in ms).
        Failure: JSON on stderr; exit 1 for query/output errors, 2 for invalid input.
        """;

    internal static int Run(string[] args, Stream output, Stream error)
    {
        if (args is [] or ["-h" or "--help" or "-help"])
        {
            output.Write(Encoding.UTF8.GetBytes(Help + Environment.NewLine));
            return 0;
        }

        var start = Stopwatch.GetTimestamp();
        string? query = null;
        (string Option, string Value)? input = null;
        int Fail(string message, int exitCode = 2)
        {
            JsonOutput.WriteError(error, message, start);
            return exitCode;
        }

        if (args[0] is not ("-e" or "--execute" or "-execute"))
            return Fail("Expected an execute command or --help.");
        static bool IsOption(string value) => value is "--query" or "--json" or "--csv" or "--json-file" or "--csv-file";
        for (int i = 1; i < args.Length; i++)
        {
            string option = args[i];
            if (!IsOption(option)) return Fail($"Unknown option: {option}.");
            if (++i == args.Length || IsOption(args[i]) || string.IsNullOrWhiteSpace(args[i]))
                return Fail($"Missing value for {option}.");
            if (option == "--query")
            {
                if (query is not null) return Fail($"Duplicate option: {option}.");
                query = args[i];
            }
            else
            {
                if (input is { } prior)
                    return Fail(prior.Option == option ? $"Duplicate option: {option}." : $"{prior.Option} and {option} are mutually exclusive.");
                input = (option, args[i]);
            }
        }
        if (query is null) return Fail("--query is required.");

        var database = new KustoDatabase();
        try
        {
            if (input is { } source)
            {
                var data = source.Option is "--json-file" or "--csv-file" ? File.ReadAllText(source.Value) : source.Value;
                database.AddTable(source.Option is "--json" or "--json-file" ? InputTable.FromJson(data) : InputTable.FromCsv(data));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail($"Cannot read input file: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or KeyNotFoundException or NotSupportedException or OverflowException)
        {
            return Fail($"Invalid table input: {ex.Message}");
        }

        var result = database.ExecuteQuery(query, includeSchema: true);
        if (result.ExecutionErrors is { } errors)
            return Fail(string.Join("; ", errors.Select(e => $"{e.Code}: {e.Description}")), 1);
        try
        {
            JsonOutput.WriteResult(output, result, start);
            return 0;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return Fail($"Cannot represent this query result as JSON: {ex.Message}", 1);
        }
    }
}
