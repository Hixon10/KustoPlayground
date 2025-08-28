namespace KustoPlayground.Core;

public static class TableBuilderFromCsv
{
    public static Table Build(TableDef tableDef)
    {
        if (tableDef.Rows == null || tableDef.Rows.Count == 0)
        {
            throw new ArgumentException("empty rows");
        }

        // ignore nullable + type as of now,
        // we will deduct them for csv.
        /// TODO use type + nullable from ColumnDef?
        List<string> headers = tableDef.Columns.Select(c => c.Name).ToList();

        if (headers == null || headers.Count == 0)
        {
            throw new ArgumentException("empty headers");
        }

        foreach (var row in tableDef.Rows)
        {
            if (row.Count != headers.Count)
            {
                throw new ArgumentException(
                    $"unexpected number of columns: expectedColumnsCount={headers.Count}, " +
                    $"row={string.Join(", ", row.Select(kvp => $"{kvp.Key}:{kvp.Value}"))}");
            }
        }

        var columns = new List<ColumnBase>();
        var columnToType = new Dictionary<string, Type>();

        // infer schema
        foreach (var header in headers)
        {
            var values = tableDef.Rows.Select(r => r[header]?.ToString()).ToList();
            var columnType = InferColumnTypeFromStrings(values, out bool isNullable);
            columns.Add(TableBuilder.Create(columnType, header, isNullable));
            columnToType[header] = columnType;
        }

        var table = new Table(tableDef.Name, columns);

        // parse and insert rows
        TableBuilder.AddRows(tableDef.Rows, columns, columnToType, table);

        return table;
    }

    // ---- Helpers ----

    private static Type InferColumnTypeFromStrings(List<string?> values, out bool isNullable)
    {
        isNullable = values.Any(string.IsNullOrEmpty);

        var nonNullValues = values.Where(v => !string.IsNullOrEmpty(v)).ToList();
        if (nonNullValues.Count == 0)
        {
            return typeof(string);
        }

        Type currentType = DetectType(nonNullValues[0]!);

        for (var index = 1; index < nonNullValues.Count; index++)
        {
            string? val = nonNullValues[index];
            var detectedType = DetectType(val!);
            currentType = GetWiderType(currentType, detectedType);
            if (currentType == typeof(string))
            {
                break;
            }
        }

        return currentType;
    }

    private static Type DetectType(string val)
    {
        if (int.TryParse(val, out _)) return typeof(int);
        if (long.TryParse(val, out _)) return typeof(long);
        if (decimal.TryParse(val, out _)) return typeof(decimal);
        if (double.TryParse(val, out _)) return typeof(double);
        if (bool.TryParse(val, out _)) return typeof(bool);
        if (DateTime.TryParse(val, out _)) return typeof(DateTime);
        if (Guid.TryParse(val, out _)) return typeof(Guid);

        return typeof(string);
    }

    private static Type GetWiderType(Type a, Type b)
    {
        if (a == b)
        {
            return a;
        }

        if (IsNumeric(a) && IsNumeric(b))
        {
            if (a == typeof(decimal) || b == typeof(decimal)) return typeof(decimal);
            if (a == typeof(double) || b == typeof(double)) return typeof(double);
            if (a == typeof(float) || b == typeof(float)) return typeof(float);
            if (a == typeof(long) || b == typeof(long)) return typeof(long);
            return typeof(int);
        }

        // otherwise incompatible → string
        return typeof(string);
    }

    private static bool IsNumeric(Type t) =>
        t == typeof(int) || t == typeof(long) ||
        t == typeof(float) || t == typeof(double) || t == typeof(decimal);
}