using System.Text.Json;
using static KustoPlayground.Core.Tests.NullableNeonTestUtils;

namespace KustoPlayground.Core.Tests;

/// <summary>
/// Nullable column creation, validation, and CSV/JSON ingestion via Table, Column&lt;T&gt;,
/// TableBuilderFromCsv, and TableBuilderFromJson, plus querying ingested nullable tables.
/// </summary>
public class NullableTableAndIngestionNeonTests
{
    #region Table and Column<T> nullability contracts

    [Test]
    [Description("Table.AddRow stores explicit null and omitted keys as null for all nullable column types")]
    public void Table_NullableColumns_AcceptExplicitNullAndOmittedKeys()
    {
        var table = new Table("AllNullable",
        [
            new Column<int>("I", isNullable: true),
            new Column<long>("L", isNullable: true),
            new Column<double>("D", isNullable: true),
            new Column<decimal>("M", isNullable: true),
            new Column<bool>("B", isNullable: true),
            new Column<DateTime>("Dt", isNullable: true),
            new Column<TimeSpan>("Ts", isNullable: true),
            new Column<Guid>("G", isNullable: true),
            new Column<string>("S", isNullable: true)
        ]);

        // Row 0: all explicit nulls
        table.AddRow(new Dictionary<string, object?>
        {
            ["I"] = null,
            ["L"] = null,
            ["D"] = null,
            ["M"] = null,
            ["B"] = null,
            ["Dt"] = null,
            ["Ts"] = null,
            ["G"] = null,
            ["S"] = null
        });

        // Row 1: all keys omitted
        table.AddRow(new Dictionary<string, object?>());

        Assert.That(table.Rows, Has.Count.EqualTo(2));
        foreach (Row row in table.Rows)
        {
            foreach (string colName in table.Schema.Keys)
            {
                Assert.That(row.Get<object?>(colName), Is.Null, $"Expected null in column '{colName}'.");
            }
        }

        // Querying via KustoDatabase sees null in every column
        var kustoDatabase = CreateDatabase(table);
        var rows = ExecuteAndAssertSuccess(
            kustoDatabase,
            "AllNullable | where isnull(I) and isnull(L) and isnull(D) and isnull(M) and isnull(B) and isnull(Dt) and isnull(Ts) and isnull(G)");
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    [Description("Table.AddRow rejects explicit null and omitted keys for non-nullable columns")]
    public void Table_NonNullableColumns_RejectNullAndOmittedKeys()
    {
        var table = new Table("Strict",
        [
            new Column<int>("Id", isNullable: false),
            new Column<string>("Name", isNullable: false)
        ]);

        Assert.That(
            () => table.AddRow(new Dictionary<string, object?> { ["Id"] = null, ["Name"] = "Alice" }),
            Throws.TypeOf<ArgumentNullException>());

        Assert.That(
            () => table.AddRow(new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = null }),
            Throws.TypeOf<ArgumentNullException>());

        Assert.That(
            () => table.AddRow(new Dictionary<string, object?> { ["Id"] = 1 }),
            Throws.TypeOf<ArgumentException>());
    }

    #endregion

    #region TableBuilderFromCsv nullable inference and querying

    [Test]
    [Description("TableBuilderFromCsv infers IsNullable=true and parses empty cells as null across int, long, decimal, double, bool, DateTime, and Guid columns")]
    public void TableBuilderFromCsv_EmptyCellsInferNullableAndParseAsNull()
    {
        var guid = new Guid("12345678-1234-1234-1234-123456789abc");
        var tableDef = new TableDef
        {
            Name = "CsvNulls",
            Columns =
            [
                new ColumnDef { Name = "Id" },
                new ColumnDef { Name = "Count" },
                new ColumnDef { Name = "Big" },
                new ColumnDef { Name = "Price" },
                new ColumnDef { Name = "Sci" },
                new ColumnDef { Name = "Flag" },
                new ColumnDef { Name = "When" },
                new ColumnDef { Name = "Uid" },
                new ColumnDef { Name = "AllEmpty" }
            ],
            Rows =
            [
                new Dictionary<string, object?>
                {
                    ["Id"] = "1",
                    ["Count"] = "42",
                    ["Big"] = "5000000000",
                    ["Price"] = "19.95",
                    ["Sci"] = "1.5e3",
                    ["Flag"] = "true",
                    ["When"] = "2024-06-15T10:00:00",
                    ["Uid"] = guid.ToString(),
                    ["AllEmpty"] = ""
                },
                new Dictionary<string, object?>
                {
                    ["Id"] = "2",
                    ["Count"] = "",
                    ["Big"] = "",
                    ["Price"] = "",
                    ["Sci"] = "",
                    ["Flag"] = "",
                    ["When"] = "",
                    ["Uid"] = "",
                    ["AllEmpty"] = ""
                },
                new Dictionary<string, object?>
                {
                    ["Id"] = "3",
                    ["Count"] = "10",
                    ["Big"] = "100",
                    ["Price"] = "5",
                    ["Sci"] = "2",
                    ["Flag"] = "false",
                    ["When"] = "2024-07-01T12:00:00",
                    ["Uid"] = guid.ToString(),
                    ["AllEmpty"] = ""
                }
            ]
        };

        Table table = TableBuilderFromCsv.Build(tableDef);

        Assert.That(table.Schema["Id"].IsNullable, Is.False);
        Assert.That(table.Schema["Id"].GetColumnType(), Is.EqualTo(typeof(int)));

        Assert.That(table.Schema["Count"].IsNullable, Is.True);
        Assert.That(table.Schema["Count"].GetColumnType(), Is.EqualTo(typeof(int)));

        Assert.That(table.Schema["Big"].IsNullable, Is.True);
        Assert.That(table.Schema["Big"].GetColumnType(), Is.EqualTo(typeof(long)));

        Assert.That(table.Schema["Price"].IsNullable, Is.True);
        Assert.That(table.Schema["Price"].GetColumnType(), Is.EqualTo(typeof(decimal)));

        Assert.That(table.Schema["Sci"].IsNullable, Is.True);
        Assert.That(table.Schema["Sci"].GetColumnType(), Is.EqualTo(typeof(double)));

        Assert.That(table.Schema["Flag"].IsNullable, Is.True);
        Assert.That(table.Schema["Flag"].GetColumnType(), Is.EqualTo(typeof(bool)));

        Assert.That(table.Schema["When"].IsNullable, Is.True);
        Assert.That(table.Schema["When"].GetColumnType(), Is.EqualTo(typeof(DateTime)));

        Assert.That(table.Schema["Uid"].IsNullable, Is.True);
        Assert.That(table.Schema["Uid"].GetColumnType(), Is.EqualTo(typeof(Guid)));

        Assert.That(table.Schema["AllEmpty"].IsNullable, Is.True);
        Assert.That(table.Schema["AllEmpty"].GetColumnType(), Is.EqualTo(typeof(string)));

        // Query the CSV-ingested table with null-aware KQL operators
        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "CsvNulls | where isnull(Count)"),
            Is.EqualTo(new int?[] { 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "CsvNulls | where Count != 42"),
            Is.EquivalentTo(new int?[] { 2, 3 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Count", kustoDatabase, "CsvNulls | sort by Count asc nulls last"),
            Is.EqualTo(new int?[] { 10, 42, null }));

        Assert.That(
            ExecuteAndGetNullableColumn<Guid?>("Uid", kustoDatabase, "CsvNulls | distinct Uid"),
            Is.EquivalentTo(new Guid?[] { guid, null }));
    }

    #endregion

    #region TableBuilderFromJson explicit nulls, omitted keys, and querying

    [Test]
    [Description("TableBuilderFromJson parses explicit JSON null values for nullable columns of all supported types and supports KQL queries")]
    public void TableBuilderFromJson_ExplicitJsonNulls_AndQuerying()
    {
        string json = """
                      {
                        "Name": "JsonNulls",
                        "Columns": [
                          { "Name": "Id", "Type": "int", "Nullable": false },
                          { "Name": "Score", "Type": "int", "Nullable": true },
                          { "Name": "Ratio", "Type": "double", "Nullable": true },
                          { "Name": "Active", "Type": "bool", "Nullable": true },
                          { "Name": "CreatedAt", "Type": "datetime", "Nullable": true },
                          { "Name": "Duration", "Type": "timespan", "Nullable": true },
                          { "Name": "TraceId", "Type": "guid", "Nullable": true },
                          { "Name": "Note", "Type": "string", "Nullable": true }
                        ],
                        "Rows": [
                          {
                            "Id": 1,
                            "Score": 100,
                            "Ratio": 0.75,
                            "Active": true,
                            "CreatedAt": "2024-06-01T08:00:00",
                            "Duration": "00:30:00",
                            "TraceId": "11111111-1111-1111-1111-111111111111",
                            "Note": "first"
                          },
                          {
                            "Id": 2,
                            "Score": null,
                            "Ratio": null,
                            "Active": null,
                            "CreatedAt": null,
                            "Duration": null,
                            "TraceId": null,
                            "Note": null
                          },
                          {
                            "Id": 3,
                            "Score": 50,
                            "Ratio": 0.25,
                            "Active": false,
                            "CreatedAt": "2024-06-10T08:00:00",
                            "Duration": "01:00:00",
                            "TraceId": "22222222-2222-2222-2222-222222222222",
                            "Note": "third"
                          }
                        ]
                      }
                      """;

        TableDef? tableDef = JsonSerializer.Deserialize<TableDef>(json, TableDefJsonContext.Default.TableDef);
        Assert.That(tableDef, Is.Not.Null);

        Table table = TableBuilderFromJson.Build(tableDef!);
        Assert.That(table.Rows, Has.Count.EqualTo(3));
        Assert.That(table.Rows[1].Get<object?>("Score"), Is.Null);
        Assert.That(table.Rows[1].Get<object?>("Ratio"), Is.Null);
        Assert.That(table.Rows[1].Get<object?>("Active"), Is.Null);
        Assert.That(table.Rows[1].Get<object?>("CreatedAt"), Is.Null);
        Assert.That(table.Rows[1].Get<object?>("Duration"), Is.Null);
        Assert.That(table.Rows[1].Get<object?>("TraceId"), Is.Null);
        Assert.That(table.Rows[1].Get<object?>("Note"), Is.Null);

        var kustoDatabase = CreateDatabase(table);

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "JsonNulls | where isnull(Score) and isnull(CreatedAt)"),
            Is.EqualTo(new int?[] { 2 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "JsonNulls | where Score != 100"),
            Is.EquivalentTo(new int?[] { 2, 3 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "JsonNulls | where Note !contains \"first\""),
            Is.EquivalentTo(new int?[] { 2, 3 }));

        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "JsonNulls | sort by Score desc nulls last"),
            Is.EqualTo(new int?[] { 1, 3, 2 }));
    }

    [Test]
    [Description("TableBuilderFromJson treats omitted JSON properties for Nullable=true columns as null")]
    public void TableBuilderFromJson_OmittedPropertiesForNullableColumns_DefaultToNull()
    {
        string json = """
                      {
                        "Name": "SparseJson",
                        "Columns": [
                          { "Name": "Id", "Type": "int", "Nullable": false },
                          { "Name": "Score", "Type": "int", "Nullable": true },
                          { "Name": "Tag", "Type": "string", "Nullable": true }
                        ],
                        "Rows": [
                          { "Id": 1, "Score": 10, "Tag": "ok" },
                          { "Id": 2 }
                        ]
                      }
                      """;

        TableDef? tableDef = JsonSerializer.Deserialize<TableDef>(json, TableDefJsonContext.Default.TableDef);
        Assert.That(tableDef, Is.Not.Null);

        Table table = TableBuilderFromJson.Build(tableDef!);
        Assert.That(table.Rows, Has.Count.EqualTo(2));
        Assert.That(table.Rows[1].Get<object?>("Score"), Is.Null);
        Assert.That(table.Rows[1].Get<object?>("Tag"), Is.Null);

        var kustoDatabase = CreateDatabase(table);
        Assert.That(
            ExecuteAndGetNullableColumn<int?>("Id", kustoDatabase, "SparseJson | where isnull(Score)"),
            Is.EqualTo(new int?[] { 2 }));
    }

    [Test]
    [Description("TableBuilderFromJson throws when a Nullable=false column receives JSON null")]
    public void TableBuilderFromJson_NullInNonNullableColumn_Throws()
    {
        string json = """
                      {
                        "Name": "InvalidJson",
                        "Columns": [
                          { "Name": "Id", "Type": "int", "Nullable": false }
                        ],
                        "Rows": [
                          { "Id": null }
                        ]
                      }
                      """;

        TableDef? tableDef = JsonSerializer.Deserialize<TableDef>(json, TableDefJsonContext.Default.TableDef);
        Assert.That(tableDef, Is.Not.Null);

        Assert.That(() => TableBuilderFromJson.Build(tableDef!), Throws.InstanceOf<ArgumentException>());
    }

    #endregion
}
