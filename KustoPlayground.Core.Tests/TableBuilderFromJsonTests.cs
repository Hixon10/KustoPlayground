using System.Text.Json;
using NUnit.Framework.Legacy;

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
                        { "Name": "EventType", "Type": "string", "Nullable": false },
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
                          "StartTime": "2025-08-24T12:00:00",
                          "State": "TEXAS",
                          "EventType": "Tornado",
                          "DamageProperty": 5000
                        }
                      ]
                    }
                    """;

        TableDef tableDef = JsonSerializer.Deserialize<TableDef>(json);
        SmokeTest(tableDef);
    }

    [Test]
    public void SmokeFromFileTest()
    {
        string filePath = Path.Combine("TestData", "table1.json");
        string json = File.ReadAllText(filePath);
        TableDef tableDef = JsonSerializer.Deserialize<TableDef>(json);
        SmokeTest(tableDef);
    }

    private void SmokeTest(TableDef tableDef)
    {
        Table table = TableBuilderFromJson.Build(tableDef);
        ClassicAssert.IsNotNull(table);
    }
}