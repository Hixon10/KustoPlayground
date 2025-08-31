using System.Text.Json;

namespace KustoPlayground.Core.Tests;

public class TableBuilderFromJsonTests
{
    [Test]
    public void SmokeFromVariableTest()
    {
        string json = """
                      {
                        "Name": "StormEvents",
                        "Columns": [
                          { "Name": "StartTime", "Type": "DateTime", "Nullable": false },
                          { "Name": "State", "Type": "string", "Nullable": false },
                          { "Name": "EventType", "Type": "string", "Nullable": true },
                          { "Name": "DamageProperty", "Type": "int", "Nullable": false }
                        ],
                        "Rows": [
                          {
                            "StartTime": "2025-08-23T06:20:00",
                            "State": "FLORIDA",
                            "EventType": "Hurricane",
                            "DamageProperty": 20000
                          },
                          {
                            "StartTime": "08/28/2025 09:49 AM",
                            "State": "TEXAS",
                            "EventType": "Tornado",
                            "DamageProperty": 5000
                          }
                        ]
                      }
                      """;

        TableDef? tableDef = JsonSerializer.Deserialize<TableDef>(json);
        SmokeTest(tableDef);
    }
    
    [Test]
    public void SmokeFromVariableWithJsonSerializerContextTest()
    {
        string json = """
                      {
                        "Name": "StormEvents",
                        "Columns": [
                          { "Name": "StartTime", "Type": "DateTime", "Nullable": false },
                          { "Name": "State", "Type": "string", "Nullable": false },
                          { "Name": "EventType", "Type": "string", "Nullable": true },
                          { "Name": "DamageProperty", "Type": "int", "Nullable": false }
                        ],
                        "Rows": [
                          {
                            "StartTime": "2025-08-23T06:20:00",
                            "State": "FLORIDA",
                            "EventType": "Hurricane",
                            "DamageProperty": 20000
                          },
                          {
                            "StartTime": "08/28/2025 09:49 AM",
                            "State": "TEXAS",
                            "EventType": "Tornado",
                            "DamageProperty": 5000
                          }
                        ]
                      }
                      """;

        TableDef? tableDef = JsonSerializer.Deserialize<TableDef>(json, TableDefJsonContext.Default.TableDef);
        SmokeTest(tableDef);
    }

    [Test]
    public void SmokeFromFileTest()
    {
        string filePath = Path.Combine("TestData", "table1.json");
        string json = File.ReadAllText(filePath);
        TableDef? tableDef = JsonSerializer.Deserialize<TableDef>(json);
        SmokeTest(tableDef);
    }

    private static void SmokeTest(TableDef? tableDef)
    {
        Assert.That(tableDef, Is.Not.Null);
        Table table = TableBuilderFromJson.Build(tableDef);
        Assert.That(table, Is.Not.Null);

        Assert.That(table.Name, Is.EqualTo("StormEvents"));

        foreach (Row row in table.Rows)
        {
            Assert.That(row.Schema, Has.Count.EqualTo(4));

            Assert.That(row.Schema["StartTime"].Name, Is.EqualTo("StartTime"));
            Assert.That(row.Schema["StartTime"].GetColumnType(), Is.EqualTo(typeof(DateTime)));
            Assert.That(row.Schema["StartTime"].IsNullable, Is.False);

            Assert.That(row.Schema["State"].Name, Is.EqualTo("State"));
            Assert.That(row.Schema["State"].GetColumnType(), Is.EqualTo(typeof(string)));
            Assert.That(row.Schema["State"].IsNullable, Is.False);

            Assert.That(row.Schema["EventType"].Name, Is.EqualTo("EventType"));
            Assert.That(row.Schema["EventType"].GetColumnType(), Is.EqualTo(typeof(string)));
            Assert.That(row.Schema["EventType"].IsNullable, Is.True);

            Assert.That(row.Schema["DamageProperty"].Name, Is.EqualTo("DamageProperty"));
            Assert.That(row.Schema["DamageProperty"].GetColumnType(), Is.EqualTo(typeof(int)));
            Assert.That(row.Schema["DamageProperty"].IsNullable, Is.False);
        }

        Assert.That(table.Rows[0].Get<DateTime>("StartTime"), Is.EqualTo(DateTime.Parse("2025-08-23T06:20:00")));
        Assert.That(table.Rows[0].Get<string>("State"), Is.EqualTo("FLORIDA"));
        Assert.That(table.Rows[0].Get<string>("EventType"), Is.EqualTo("Hurricane"));
        Assert.That(table.Rows[0].Get<int>("DamageProperty"), Is.EqualTo(20000));

        Assert.That(table.Rows[1].Get<DateTime>("StartTime"), Is.EqualTo(DateTime.Parse("08/28/2025 09:49 AM")));
        Assert.That(table.Rows[1].Get<string>("State"), Is.EqualTo("TEXAS"));
        Assert.That(table.Rows[1].Get<string>("EventType"), Is.EqualTo("Tornado"));
        Assert.That(table.Rows[1].Get<int>("DamageProperty"), Is.EqualTo(5000));
    }

    [Test]
    public void CanUseAllTypesTest()
    {
        var columns = new List<ColumnDef>();
        foreach (var kv in TableBuilderFromJson._map)
        {
            columns.Add(new ColumnDef
            {
                Name = kv.Key,
                Type = kv.Key,
                Nullable = kv.Value.IsClass || Nullable.GetUnderlyingType(kv.Value) != null
            });
        }

        var row = new Dictionary<string, object?>();
        foreach (var kv in TableBuilderFromJson._map)
        {
            row[kv.Key] = GetSampleValue(kv.Value);
        }

        var tableDef = new TableDef
        {
            Name = "AllTypesTable",
            Columns = columns,
            Rows = new List<Dictionary<string, object?>> { row }
        };

        var table = TableBuilderFromJson.Build(tableDef);
        Assert.That(table, Is.Not.Null);

        Assert.That(tableDef.Columns.Count, Is.EqualTo(TableBuilderFromJson._map.Count));
        Assert.That(tableDef.Rows.Count, Is.EqualTo(1));

        Dictionary<string, object?> actualRow = tableDef.Rows[0];
        foreach (var column in tableDef.Columns)
        {
            Assert.That(actualRow.ContainsKey(column.Name), Is.True, $"Missing column '{column.Name}' in row");

            var value = actualRow[column.Name];
            Assert.That(value, Is.Not.Null);

            Type expectedType = TableBuilderFromJson._map[column.Name];
            Assert.That(value.GetType(), Is.EqualTo(expectedType));
            Assert.That(value, Is.EqualTo(GetSampleValue(expectedType)));
        }
    }

    private static object GetSampleValue(Type type) => type switch
    {
        var t when t == typeof(bool) => true,
        var t when t == typeof(byte) => (byte)123,
        var t when t == typeof(sbyte) => (sbyte)-12,
        var t when t == typeof(short) => (short)-123,
        var t when t == typeof(ushort) => (ushort)123,
        var t when t == typeof(int) => 123,
        var t when t == typeof(uint) => 123u,
        var t when t == typeof(long) => 123L,
        var t when t == typeof(ulong) => 123UL,
        var t when t == typeof(float) => 123.45f,
        var t when t == typeof(double) => 123.45,
        var t when t == typeof(decimal) => 123.45m,
        var t when t == typeof(string) => "test",
        var t when t == typeof(char) => 'A',
        var t when t == typeof(DateTime) => new DateTime(2025, 8, 23, 6, 20, 0),
        var t when t == typeof(DateTimeOffset) => new DateTimeOffset(2023, 01, 01, 0, 0, 0, TimeSpan.Zero),
        var t when t == typeof(TimeSpan) => TimeSpan.FromMinutes(5),
        var t when t == typeof(Guid) => new Guid("12345678-1234-1234-1234-123456789abc"),
        _ => throw new NotSupportedException($"No sample value for {type}")
    };
}