namespace KustoPlayground.Core;

public static class TableBuilderFromCsv
{
    public static Table Build(string tableName, List<string> headers, List<Dictionary<string, object?>> rows)
    {
        if (rows == null || rows.Count == 0)
            throw new ArgumentException("empty rows");

        if (headers == null || headers.Count == 0)
            throw new ArgumentException("empty headers");

        foreach (var row in rows)
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
            var values = rows.Select(r => r[header]?.ToString()).ToList();
            var columnType = InferColumnTypeFromStrings(values, out bool isNullable);
            columns.Add(ColumnFactory.Create(columnType, header, isNullable));
            columnToType[header] = columnType;
        }

        var table = new Table(tableName, columns);

        // parse and insert rows
        foreach (var row in rows)
        {
            var parsedRow = new Dictionary<string, object?>();
            foreach (var col in columns)
            {
                var raw = row[col.Name]?.ToString();
                parsedRow[col.Name] = ParseValue(raw, columnToType[col.Name]);
            }

            table.AddRow(parsedRow);
        }

        return table;
    }

    private static class ColumnFactory
    {
        public static ColumnBase Create(Type type, string name, bool isNullable)
        {
            if (type == typeof(int)) return new Column<int>(name, isNullable);
            if (type == typeof(long)) return new Column<long>(name, isNullable);
            if (type == typeof(float)) return new Column<float>(name, isNullable);
            if (type == typeof(double)) return new Column<double>(name, isNullable);
            if (type == typeof(decimal)) return new Column<decimal>(name, isNullable);
            if (type == typeof(bool)) return new Column<bool>(name, isNullable);
            if (type == typeof(DateTime)) return new Column<DateTime>(name, isNullable);
            if (type == typeof(Guid)) return new Column<Guid>(name, isNullable);
            if (type == typeof(string)) return new Column<string>(name, isNullable);

            throw new NotSupportedException($"Unsupported column type: {type}");
        }
    }

    // ---- Helpers ----

    private static Type InferColumnTypeFromStrings(List<string?> values, out bool isNullable)
    {
        isNullable = values.Any(v => string.IsNullOrEmpty(v));

        var nonNullValues = values.Where(v => !string.IsNullOrEmpty(v)).ToList();
        if (nonNullValues.Count == 0)
            return typeof(string);

        Type currentType = DetectType(nonNullValues[0]!);

        for (var index = 1; index < nonNullValues.Count; index++)
        {
            var val = nonNullValues[index];
            var detectedType = DetectType(val!);
            currentType = GetWiderType(currentType, detectedType);
            if (currentType == typeof(string))
                break;
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
        if (a == b) return a;

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

    private static object? ParseValue(string? raw, Type targetType)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        if (targetType == typeof(int) && int.TryParse(raw, out var i)) return i;
        if (targetType == typeof(long) && long.TryParse(raw, out var l)) return l;
        if (targetType == typeof(decimal) && decimal.TryParse(raw, out var d)) return d;
        if (targetType == typeof(double) && double.TryParse(raw, out var dbl)) return dbl;
        if (targetType == typeof(bool) && bool.TryParse(raw, out var b)) return b;
        if (targetType == typeof(DateTime) && DateTime.TryParse(raw, out var dt)) return dt;
        if (targetType == typeof(Guid) && Guid.TryParse(raw, out var g)) return g;

        return raw; // fallback
    }
}