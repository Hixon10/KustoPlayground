using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using KustoPlayground.Core;

namespace KustoPlayground.Cli;

internal static class Command
{
    private const string Help = """
        Run KQL locally using the Kusto Playground engine.

        kustoplayground [--help]
        kustoplayground [-e] --query <KQL> [table inputs...]
        kustoplayground [-e] --mcp-server [table inputs...]

        Help: -h, --help, -help         Execute: -e, --execute, -execute
        Choose exactly one of --query or --mcp-server. MCP serves the query tool
        over stdin/stdout until stdin closes or Ctrl+C; logs go to stderr.
        Repeat or mix --json <table JSON>, --csv <CSV text>, --json-path <path>
        and --csv-path <path>. --json-file and --csv-file are aliases.
        JSON uses the playground's Name, Columns, and Rows format. CSV needs a
        header; CSV tables are named Input, Input2, Input3, ... in argument order.
        Duplicate table names are errors. Paths may be absolute or relative.
        Omit table inputs for print, datatable, or other self-contained queries.

        Example: kustoplayground -e --query "print Answer=6*7"
        Example: kustoplayground --mcp-server --json-path products.json --csv-path orders.csv
        One-shot success: JSON on stdout (schema, then rows in results.items; duration in ms).
        One-shot failure: JSON on stderr; exit 1 for query/output errors, 2 for invalid input.
        """;

    internal static async Task<int> RunAsync(string[] args, Stream input, Stream output, Stream error)
    {
        if (args is [] or ["-h" or "--help" or "-help"])
        {
            output.Write(Encoding.UTF8.GetBytes(Help + Environment.NewLine));
            return 0;
        }

        var start = Stopwatch.GetTimestamp();
        string? query = null;
        bool serve = false;
        var inputs = new List<(string Option, string Value)>();
        int Fail(string message)
        {
            JsonOutput.Error(message, start).WriteLine(error);
            return 2;
        }

        static bool IsOption(string value) => value is "--query" or "--mcp-server" or "--json" or "--csv"
            or "--json-path" or "--csv-path" or "--json-file" or "--csv-file";
        for (int i = args[0] is "-e" or "--execute" or "-execute" ? 1 : 0; i < args.Length; i++)
        {
            string option = args[i];
            if (!IsOption(option)) return Fail($"Unknown option: {option}.");
            if (option == "--mcp-server")
            {
                if (serve) return Fail($"Duplicate option: {option}.");
                serve = true;
                continue;
            }
            if (++i == args.Length || IsOption(args[i]) || string.IsNullOrWhiteSpace(args[i]))
                return Fail($"Missing value for {option}.");
            if (option == "--query")
            {
                if (query is not null) return Fail($"Duplicate option: {option}.");
                query = args[i];
            }
            else inputs.Add((option, args[i]));
        }
        if ((serve, query) is (true, not null) or (false, null)) return Fail("Specify exactly one of --query or --mcp-server.");

        var database = new KustoDatabase();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int csvCount = 0;
        try
        {
            foreach (var (option, value) in inputs)
            {
                var data = option is "--json" or "--csv" ? value : await File.ReadAllTextAsync(value).ConfigureAwait(false);
                Table table;
                if (option is "--json" or "--json-path" or "--json-file") table = InputTable.FromJson(data);
                else
                {
                    csvCount++;
                    table = InputTable.FromCsv(data, csvCount == 1 ? "Input" : "Input" + csvCount.ToString(CultureInfo.InvariantCulture));
                }
                if (!names.Add(table.Name)) return Fail($"Duplicate table name: {table.Name}.");
                database.AddTable(table);
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

        if (query is null) return await QueryServer.RunAsync(database, input, output, error).ConfigureAwait(false);
        var reply = LocalQuery.Execute(database, query, start);
        reply.WriteLine(reply.IsError ? error : output);
        return reply.IsError ? 1 : 0;
    }
}
