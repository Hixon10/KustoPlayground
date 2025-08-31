using System.Collections.ObjectModel;

namespace KustoPlayground.Core;

public static class TableBuilderFromJson
{
    internal static readonly ReadOnlyDictionary<string, Type> _map
        = new(new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["bool"] = typeof(bool),
            ["byte"] = typeof(byte),
            ["sbyte"] = typeof(sbyte),
            ["short"] = typeof(short),
            ["int16"] = typeof(short),
            ["ushort"] = typeof(ushort),
            ["uint16"] = typeof(ushort),
            ["int"] = typeof(int),
            ["int32"] = typeof(int),
            ["uint"] = typeof(uint),
            ["uint32"] = typeof(uint),
            ["long"] = typeof(long),
            ["int64"] = typeof(long),
            ["ulong"] = typeof(ulong),
            ["uint64"] = typeof(ulong),
            ["float"] = typeof(float),
            ["double"] = typeof(double),
            ["decimal"] = typeof(decimal),
            ["string"] = typeof(string),
            ["char"] = typeof(char),
            ["datetime"] = typeof(DateTime),
            ["datetimeoffset"] = typeof(DateTimeOffset),
            ["timespan"] = typeof(TimeSpan),
            ["guid"] = typeof(Guid),
        });

    public static Table Build(TableDef tableDef)
    {
        ArgumentNullException.ThrowIfNull(tableDef);
        TableBuilder.ValidateTableDef(tableDef);

        foreach (ColumnDef columnDef in tableDef.Columns)
        {
            if (string.IsNullOrEmpty(columnDef.Type))
            { 
                throw new ArgumentException($"column '{columnDef.Name}' has empty Type.");
            }
        }
        
        var columns = new List<ColumnBase>();
        var columnToType = new Dictionary<string, Type>();

        foreach (ColumnDef columnDef in tableDef.Columns)
        {
            Type type = _map[columnDef.Type];
            ColumnBase column = TableBuilder.Create(type, columnDef.Name, columnDef.Nullable);
            columns.Add(column);
            columnToType[columnDef.Name] = column.GetColumnType();
        }

        var table = new Table(tableDef.Name, columns);

        TableBuilder.AddRows(tableDef.Rows, columns, columnToType, table);

        return table;
    }
}