using System.Text.Json;
using KustoPlayground.Core;
using Microsoft.VisualBasic.FileIO;

namespace KustoPlayground.Cli;

internal static class InputTable
{
    internal static Table FromJson(string json)
    {
        var table = JsonSerializer.Deserialize(json, TableDefJsonContext.Default.TableDef);
        if (table?.Columns is null || table.Rows is null || table.Columns.Any(c => c is null) || table.Rows.Any(r => r is null))
            throw new FormatException("Expected a table object with Name, Columns and Rows.");
        return TableBuilderFromJson.Build(table);
    }

    internal static Table FromCsv(string csv, string tableName)
    {
        using var reader = new StringReader(csv);
        using var parser = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        try
        {
            var headers = parser.ReadFields();
            if (headers is null || headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
                throw new FormatException("CSV requires distinct, non-empty column names.");
            var rows = new List<Dictionary<string, object?>>();
            while (parser.ReadFields() is { } fields)
            {
                if (fields.Length != headers.Length)
                    throw new FormatException($"CSV record {rows.Count + 2} has {fields.Length} fields; expected {headers.Length}.");
                var row = new Dictionary<string, object?>(headers.Length, StringComparer.Ordinal);
                for (int i = 0; i < fields.Length; i++) row.Add(headers[i], fields[i]);
                rows.Add(row);
            }
            return TableBuilderFromCsv.Build(new TableDef
            {
                Name = tableName, Columns = headers.Select(h => new ColumnDef { Name = h }).ToArray(), Rows = rows
            });
        }
        catch (MalformedLineException ex)
        {
            throw new FormatException($"Malformed CSV at line {ex.LineNumber}.", ex);
        }
    }
}
