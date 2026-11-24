using System.Globalization;

namespace KustoPlayground.Core.Tests;

internal static class JoinNeonTestUtils
{
    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> ExecuteJoin(
        KustoDatabase database,
        string query) =>
        NullableNeonTestUtils.ExecuteAndAssertSuccess(database, query);

    internal static void AssertJoinError(KustoDatabase database, string query)
    {
        ExecutionResult result = database.ExecuteQuery(query);
        Assert.That(result, Is.Not.Null);
        Assert.That(
            result.ExecutionErrors,
            Is.Not.Null.And.Not.Empty,
            () => $"Expected execution error for join query '{query}', but query succeeded with {result.ResultRows?.Count ?? 0} rows.");
    }

    internal static void AssertExactColumnOrder(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        params string[] expectedColumns)
    {
        Assert.That(rows, Is.Not.Empty, "Cannot assert row key order on an empty result set; use getschema for empty schemas.");
        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            Assert.That(row.Keys.ToArray(), Is.EqualTo(expectedColumns));
        }
    }

    internal static List<(string ColumnName, int ColumnOrdinal, string DataType, string ColumnType)> ExecuteGetSchema(
        KustoDatabase database,
        string query)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(database, $"{query} | getschema");
        return rows
            .Select(row => (
                ColumnName: (string)row["ColumnName"]!,
                ColumnOrdinal: Convert.ToInt32(row["ColumnOrdinal"]!, CultureInfo.InvariantCulture),
                DataType: (string)row["DataType"]!,
                ColumnType: (string)row["ColumnType"]!))
            .OrderBy(col => col.ColumnOrdinal)
            .ToList();
    }

    internal static List<(T1, T2, T3, T4)> ExecuteAndGetNullable4Columns<T1, T2, T3, T4>(
        string column1,
        string column2,
        string column3,
        string column4,
        KustoDatabase database,
        string query)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(database, query);
        return rows.Select(row => (
            ReadCell<T1>(row, column1),
            ReadCell<T2>(row, column2),
            ReadCell<T3>(row, column3),
            ReadCell<T4>(row, column4))).ToList();
    }

    internal static List<(T1, T2, T3, T4, T5)> ExecuteAndGetNullable5Columns<T1, T2, T3, T4, T5>(
        string column1,
        string column2,
        string column3,
        string column4,
        string column5,
        KustoDatabase database,
        string query)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = ExecuteJoin(database, query);
        return rows.Select(row => (
            ReadCell<T1>(row, column1),
            ReadCell<T2>(row, column2),
            ReadCell<T3>(row, column3),
            ReadCell<T4>(row, column4),
            ReadCell<T5>(row, column5))).ToList();
    }

    internal static T ReadCell<T>(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        Assert.That(row, Does.ContainKey(columnName),
            () => $"Missing expected column '{columnName}'. Actual columns: [{string.Join(", ", row.Keys)}]");

        object? raw = row[columnName];
        if (raw is null)
        {
            if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) is null)
            {
                throw new AssertionException(
                    $"Column '{columnName}' was null, which cannot be cast to non-nullable '{typeof(T).Name}'.");
            }

            return default!;
        }

        if (raw is T typed)
        {
            return typed;
        }

        Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType == typeof(long))
        {
            return (T)(object)Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(int))
        {
            return (T)(object)Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(double))
        {
            return (T)(object)Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(decimal))
        {
            return (T)(object)Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
        }

        return (T)raw;
    }
}
