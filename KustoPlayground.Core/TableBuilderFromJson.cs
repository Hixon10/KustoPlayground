namespace KustoPlayground.Core;

public static class TableBuilderFromJson
{
    private static readonly IReadOnlyDictionary<string, Type> _map
        = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
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
        };
    
    public static Table Build(TableDef tableDef)
    {
        ArgumentNullException.ThrowIfNull(tableDef);

        if (tableDef.Rows == null || tableDef.Rows.Count == 0)
        {
            throw new ArgumentException("empty rows");
        }

        if (tableDef.Columns == null || tableDef.Columns.Count == 0)
        {
            throw new ArgumentException("empty headers");
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