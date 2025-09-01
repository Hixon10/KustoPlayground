using System.Text.Json.Serialization;

namespace KustoPlayground.Core;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(ExecutionResult))]
[JsonSerializable(typeof(ExecutionError))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(char))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(ulong))]
public partial class ExecutionResultJsonContext : JsonSerializerContext;

/// <summary>
/// Represents the result of a query execution.
/// </summary>
public sealed class ExecutionResult
{
    /// <summary>
    /// The collection of rows returned by the query.
    /// Can be null, if the execution fails.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>>? ResultRows { get; init; }

    /// <summary>
    /// The collection of errors encountered during execution.
    /// Can be null, if no errors occurred.
    /// </summary>
    public IReadOnlyList<ExecutionError>? ExecutionErrors { get; init; }
}

/// <summary>
/// Represents an error that occurred during query execution 
/// (for example, a parsing error).
/// </summary>
public sealed class ExecutionError
{
    public enum ErrorCodes
    {
        None,
        InternalError,
        UnknownTable,
    }

    public required string Code { get; init; }
    public string? Description { get; init; }
}