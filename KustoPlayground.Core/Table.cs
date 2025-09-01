using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace KustoPlayground.Core;

internal abstract class ColumnBase
{
    internal string Name { get; }
    internal bool IsNullable { get; }

    protected ColumnBase(string name, bool isNullable)
    {
        Name = name;
        IsNullable = isNullable;
    }

    internal abstract void ValidateValue(object? value);

    internal abstract void SetValue(Row row, object? value);

    internal abstract Type GetColumnType();
}

internal class Column<T> : ColumnBase
{
    internal Column(string name, bool isNullable = true)
        : base(name, isNullable)
    {
    }

    internal override void ValidateValue(object? value)
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

    internal override void SetValue(Row row, object? value)
    {
        ValidateValue(value);
        row._values[Name] = value;
    }

    internal override Type GetColumnType() => typeof(T);
}

internal class Row
{
    internal readonly ConcurrentDictionary<string, object?> _values = new();
    internal ReadOnlyDictionary<string, ColumnBase> Schema { get; }

    internal Row(ReadOnlyDictionary<string, ColumnBase> schema)
    {
        Schema = schema;
    }

    internal T? Get<T>(Column<T> column)
    {
        return Get<T>(column.Name);
    }

    internal T? Get<T>(string columnName)
    {
        if (!Schema.TryGetValue(columnName, out ColumnBase? column))
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
    internal ReadOnlyDictionary<string, ColumnBase> Schema { get; }

    private ImmutableList<Row> _rows = ImmutableList<Row>.Empty;
    internal IReadOnlyList<Row> Rows => _rows;

    internal Table(string name, IEnumerable<ColumnBase> columns)
    {
        Name = name;
        Columns = columns.ToList();

        Schema = Columns.ToDictionary(
            k => k.Name,
            v => v).AsReadOnly();
    }

    public void AddRows(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var row in rows)
        {
            AddRow(row);
        }
    }

    public void AddRow(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var row = new Row(Schema);

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