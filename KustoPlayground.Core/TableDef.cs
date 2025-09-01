using System.Text.Json.Serialization;

namespace KustoPlayground.Core;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(TableDef))]
[JsonSerializable(typeof(ColumnDef))]
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