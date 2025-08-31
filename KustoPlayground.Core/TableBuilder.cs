using System.Text.Json.Serialization;

namespace KustoPlayground.Core;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(TableDef))]
[JsonSerializable(typeof(ColumnDef))]
public partial class TableDefJsonContext : JsonSerializerContext;

public sealed class TableDef
{
    public required string Name { get; init; }
    public required IReadOnlyList<ColumnDef> Columns { get; init; }
    public required IReadOnlyList<Dictionary<string, object?>> Rows { get; init; }
}

public sealed class ColumnDef
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Nullable { get; set; }
}

internal static class TableBuilder
{
    internal static void ValidateTableDef(TableDef tableDef)
    {
        ArgumentNullException.ThrowIfNull(tableDef);

        if (string.IsNullOrEmpty(tableDef.Name))
        {
            throw new ArgumentException("empty table name");
        }
        
        if (tableDef.Rows == null || tableDef.Rows.Count == 0)
        {
            throw new ArgumentException("empty table rows");
        }

        if (tableDef.Columns == null || tableDef.Columns.Count == 0)
        {
            throw new ArgumentException("empty table columns");
        }
        
        foreach (ColumnDef columnDef in tableDef.Columns)
        {
            if (string.IsNullOrEmpty(columnDef.Name))
            { 
                throw new ArgumentException("empty column name");
            }
        }
    }
    
    internal static void AddRows(
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyList<ColumnBase> columns,
        Dictionary<string, Type> columnToType,
        Table table)
    {
        foreach (var row in rows)
        {
            var parsedRow = new Dictionary<string, object?>();
            foreach (var col in columns)
            {
                string? raw = row[col.Name]?.ToString();
                parsedRow[col.Name] = ParseValue(raw, columnToType[col.Name]);
            }

            table.AddRow(parsedRow);
        }
    }


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

        if (targetType == typeof(float) && float.TryParse(raw, out float f)) return f;
        if (targetType == typeof(byte) && byte.TryParse(raw, out byte by)) return by;
        if (targetType == typeof(sbyte) && sbyte.TryParse(raw, out sbyte sb)) return sb;
        if (targetType == typeof(short) && short.TryParse(raw, out short sh)) return sh;
        if (targetType == typeof(ushort) && ushort.TryParse(raw, out ushort us)) return us;
        if (targetType == typeof(uint) && uint.TryParse(raw, out uint ui)) return ui;
        if (targetType == typeof(char) && char.TryParse(raw, out char ch)) return ch;
        if (targetType == typeof(ulong) && ulong.TryParse(raw, out ulong ul)) return ul;
        if (targetType == typeof(DateTimeOffset) && DateTimeOffset.TryParse(raw, out DateTimeOffset dto)) return dto;
        if (targetType == typeof(TimeSpan) && TimeSpan.TryParse(raw, out TimeSpan ts)) return ts;

        return raw; // fallback
    }

    internal static ColumnBase Create(Type type, string name, bool isNullable)
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

        if (type == typeof(byte)) return new Column<byte>(name, isNullable);
        if (type == typeof(sbyte)) return new Column<sbyte>(name, isNullable);
        if (type == typeof(short)) return new Column<short>(name, isNullable);
        if (type == typeof(ushort)) return new Column<ushort>(name, isNullable);
        if (type == typeof(uint)) return new Column<uint>(name, isNullable);
        if (type == typeof(char)) return new Column<char>(name, isNullable);
        if (type == typeof(ulong)) return new Column<ulong>(name, isNullable);
        if (type == typeof(DateTimeOffset)) return new Column<DateTimeOffset>(name, isNullable);
        if (type == typeof(TimeSpan)) return new Column<TimeSpan>(name, isNullable);

        throw new NotSupportedException($"Unsupported column type: {type}");
    }
}