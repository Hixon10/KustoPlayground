using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace KustoPlayground.Core;

public abstract class ColumnBase
{
    public string Name { get; }
    public bool IsNullable { get; }

    protected ColumnBase(string name, bool isNullable)
    {
        Name = name;
        IsNullable = isNullable;
    }

    public abstract void ValidateValue(object? value);

    public abstract void SetValue(Row row, object? value);
    
    public abstract Type GetColumnType();
}

public class Column<T> : ColumnBase
{
    public Column(string name, bool isNullable = true)
        : base(name, isNullable)
    {
    }

    public override void ValidateValue(object? value)
    {
        if (value is null)
        {
            if (!IsNullable)
            {
                throw new ArgumentNullException($"Column '{Name}' cannot be null.");
            }
            return;
        }

        if (value is not T)
        {
            throw new ArgumentException(
                $"Value '{value}' is not of type {typeof(T).Name} for column '{Name}'."
            );
        }
    }

    public override void SetValue(Row row, object? value)
    {
        ValidateValue(value);
        row._values[Name] = value;
    }
    
    public override Type GetColumnType() => typeof(T);
}

public class Row
{
    internal readonly ConcurrentDictionary<string, object?> _values = new();
    private readonly ConcurrentDictionary<string, ColumnBase> _columns = new();

    public Row(IEnumerable<ColumnBase> columns)
    {
        foreach (ColumnBase column in columns)
        {
            _columns[column.Name] = column;
        }
    }

    public T? Get<T>(Column<T> column)
    {
        if (_values.TryGetValue(column.Name, out object? columnValue) && 
            columnValue != null)
        {
            return (T?)columnValue;
        }

        return default;
    }
    
    public T? Get<T>(string columnName)
    {
        if (!_columns.TryGetValue(columnName, out ColumnBase? column))
        {
            throw new KeyNotFoundException($"Column '{columnName}' does not exist.");
        }

        if (!_values.TryGetValue(columnName, out var columnValue) || columnValue == null)
        {
            return default;
        }

        if (!typeof(T).IsAssignableFrom(column.GetColumnType()))
        {
            throw new InvalidCastException(
                $"Column '{columnName}' is of type {column.GetColumnType().Name}, not {typeof(T).Name}."
            );
        }

        return (T)columnValue;
    }
}

public class Table
{
    public string Name { get; }
    private IReadOnlyList<ColumnBase> Columns { get; }
    
    private ImmutableList<Row> _rows = ImmutableList<Row>.Empty;
    public IReadOnlyList<Row> Rows => _rows;

    public Table(string name, IEnumerable<ColumnBase> columns)
    {
        Name = name;
        Columns = columns.ToList();
    }

    public void AddRow(Dictionary<string, object?> values)
    {
        var row = new Row(Columns);

        foreach (var column in Columns)
        {
            if (!values.TryGetValue(column.Name, out object? value))
            {
                if (!column.IsNullable)
                {
                    throw new ArgumentException(
                        $"Missing required value for column '{column.Name}'."
                    );
                }
                value = null;
            }

            column.SetValue(row, value);
        }

        ImmutableInterlocked.Update(ref _rows, static (list, r) => list.Add(r), row);
    }
}