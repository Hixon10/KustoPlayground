using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KustoPlayground.Core;

namespace KustoPlayground.Cli;

internal static class Command
{
    private const string Help = """
        Run KQL locally using the Kusto Playground engine.

        kustoplayground --help
        kustoplayground --execute --query <KQL> [--json <table JSON> | --csv <CSV text>]

        Help: -h, --help, -help         Execute: -e, --execute, -execute
        Pass each value as one argument. JSON uses the playground's Name, Columns,
        and Rows format. CSV needs a header and data; its table is named Input.
        Omit both inputs for print, datatable, or other self-contained queries.

        Example: kustoplayground -e --query "print Answer=6*7"
        Success: JSON on stdout (schema, then rows in results.items; duration in ms).
        Failure: JSON on stderr; exit 1 for query/output errors, 2 for invalid input.
        """;

    internal static int Run(string[] args, Stream output, Stream error)
    {
        if (args is ["-h" or "--help" or "-help"])
        {
            output.Write(Encoding.UTF8.GetBytes(Help + Environment.NewLine));
            return 0;
        }

        var start = Stopwatch.GetTimestamp();
        string? query = null, json = null, csv = null;
        int Fail(string message, int exitCode = 2)
        {
            JsonOutput.WriteError(error, message, start);
            return exitCode;
        }

        if (args.Length == 0 || args[0] is not ("-e" or "--execute" or "-execute"))
            return Fail("Expected an execute command or --help.");
        for (int i = 1; i < args.Length; i++)
        {
            string option = args[i];
            if (option is not ("--query" or "--json" or "--csv")) return Fail($"Unknown option: {option}.");
            if (++i == args.Length || args[i] is "--query" or "--json" or "--csv" || string.IsNullOrWhiteSpace(args[i]))
                return Fail($"Missing value for {option}.");
            switch (option)
            {
                case "--query" when query is null: query = args[i]; break;
                case "--json" when json is null: json = args[i]; break;
                case "--csv" when csv is null: csv = args[i]; break;
                default: return Fail($"Duplicate option: {option}.");
            }
        }
        if (query is null) return Fail("--query is required.");
        if (json is not null && csv is not null) return Fail("--json and --csv are mutually exclusive.");

        var database = new KustoDatabase();
        try
        {
            if (json is not null) database.AddTable(InputTable.FromJson(json));
            if (csv is not null) database.AddTable(InputTable.FromCsv(csv));
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
