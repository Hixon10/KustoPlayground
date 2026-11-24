namespace KustoPlayground.Core.Tests;

public class NeonGetSchemaOperatorTests
{
    [Test]
    [Description("getschema - returns exact 4 columns (ColumnName, ColumnOrdinal, DataType, ColumnType) for all scalar types")]
    public void GetSchema_AllScalarTypes_ReturnsExactMappingsAndOrdinals()
    {
        var db = new KustoDatabase();
        var table = NeonTestUtils.CreateTable(
            "AllTypes",
            [
                new Column<int>("IntCol", isNullable: false),
                new Column<long>("LongCol", isNullable: false),
                new Column<double>("RealCol", isNullable: false),
                new Column<string>("StrCol", isNullable: false),
                new Column<bool>("BoolCol", isNullable: false),
                new Column<DateTime>("DtCol", isNullable: false),
                new Column<TimeSpan>("TsCol", isNullable: false),
                new Column<Guid>("GuidCol", isNullable: false)
            ],
            [
                new()
                {
                    ["IntCol"] = 1,
                    ["LongCol"] = 2L,
                    ["RealCol"] = 3.5,
                    ["StrCol"] = "abc",
                    ["BoolCol"] = true,
                    ["DtCol"] = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    ["TsCol"] = TimeSpan.FromMinutes(5),
                    ["GuidCol"] = Guid.NewGuid()
                }
            ]);
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(db, "AllTypes | getschema");

        Assert.That(rows, Has.Count.EqualTo(8));
        Assert.That(rows[0].Keys, Is.EquivalentTo(new[] { "ColumnName", "ColumnOrdinal", "DataType", "ColumnType" }));

        var actual = rows
            .Select(r => ((string)r["ColumnName"]!, (int)r["ColumnOrdinal"]!, (string)r["DataType"]!, (string)r["ColumnType"]!))
            .OrderBy(t => t.Item2)
            .ToList();

        var expected = new List<(string, int, string, string)>
        {
            ("IntCol", 0, "System.Int32", "int"),
            ("LongCol", 1, "System.Int64", "long"),
            ("RealCol", 2, "System.Double", "real"),
            ("StrCol", 3, "System.String", "string"),
            ("BoolCol", 4, "System.SByte", "bool"),
            ("DtCol", 5, "System.DateTime", "datetime"),
            ("TsCol", 6, "System.TimeSpan", "timespan"),
            ("GuidCol", 7, "System.Guid", "guid")
        };

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    [Description("getschema - dynamic column from parse_json() reports System.Object and dynamic")]
    public void GetSchema_DynamicColumn_ReportsSystemObjectAndDynamic()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWithColumn(
            new List<string> { """{"a":1}""" },
            columnName: "Raw",
            tableName: "JsonTable");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "JsonTable | extend Parsed = parse_json(Raw) | getschema | sort by ColumnOrdinal asc");

        Assert.That(actual, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Raw", 0, "System.String", "string"),
            ("Parsed", 1, "System.Object", "dynamic")
        }));
    }

    [Test]
    [Description("getschema - nullable columns report underlying non-nullable System.* DataType and KQL ColumnType")]
    public void GetSchema_NullableColumns_PreserveUnderlyingTypes()
    {
        var db = new KustoDatabase();
        var table = NeonTestUtils.CreateTable(
            "NullableTable",
            [
                new Column<int>("MaybeInt", isNullable: true),
                new Column<double>("MaybeReal", isNullable: true),
                new Column<bool>("MaybeBool", isNullable: true)
            ],
            [
                new() { ["MaybeInt"] = null, ["MaybeReal"] = null, ["MaybeBool"] = null }
            ]);
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "NullableTable | getschema | sort by ColumnOrdinal asc");

        Assert.That(actual, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("MaybeInt", 0, "System.Int32", "int"),
            ("MaybeReal", 1, "System.Double", "real"),
            ("MaybeBool", 2, "System.SByte", "bool")
        }));
    }

    [Test]
    [Description("getschema - empty table (0 rows) and empty where filter still return full schema")]
    public void GetSchema_EmptyTableAndEmptyWhereResult_StillReturnsSchema()
    {
        var db = new KustoDatabase();
        var emptyTable = new Table("EmptyTable", [
            new Column<string>("Name", isNullable: false),
            new Column<int>("Age", isNullable: false)
        ]);
        db.AddTable(emptyTable);

        var fromEmpty = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "EmptyTable | getschema | sort by ColumnOrdinal asc");

        Assert.That(fromEmpty, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Name", 0, "System.String", "string"),
            ("Age", 1, "System.Int32", "int")
        }));

        var populated = TestUtils.GenerateTableWith2Columns(
            new List<(string, int)> { ("Alice", 30) },
            columnName1: "Name",
            columnName2: "Age",
            tableName: "People");
        db.AddTable(populated);

        var fromFilteredEmpty = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "People | where Age > 1000 | getschema | sort by ColumnOrdinal asc");

        Assert.That(fromFilteredEmpty, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Name", 0, "System.String", "string"),
            ("Age", 1, "System.Int32", "int")
        }));
    }

    [Test]
    [Description("getschema - reflects project column selection, reordering, and renaming")]
    public void GetSchema_AfterProject_UpdatesNamesAndOrdinals()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var actual = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "StormEvents | project Damage = DamageProperty, State, StartTime | getschema | sort by ColumnOrdinal asc");

        Assert.That(actual, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Damage", 0, "System.Int32", "int"),
            ("State", 1, "System.String", "string"),
            ("StartTime", 2, "System.DateTime", "datetime")
        }));
    }

    [Test]
    [Description("getschema - reflects extend adding new columns and changing an existing column's type")]
    public void GetSchema_AfterExtend_IncludesNewColumnsAndTypeChanges()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)> { (10, "ok") },
            columnName1: "Val",
            columnName2: "Tag",
            tableName: "Items");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "Items | extend Val = tostring(Val), Flag = true | getschema | sort by ColumnOrdinal asc");

        Assert.That(actual, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Val", 0, "System.String", "string"),
            ("Tag", 1, "System.String", "string"),
            ("Flag", 2, "System.SByte", "bool")
        }));
    }

    [Test]
    [Description("getschema - after count, distinct, parse, and print operators")]
    public void GetSchema_AfterCountDistinctParseAndPrint()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var afterCount = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "StormEvents | count | getschema");
        Assert.That(afterCount, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Count", 0, "System.Int64", "long")
        }));

        var afterDistinct = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "StormEvents | distinct State, DamageProperty | getschema | sort by ColumnOrdinal asc");
        Assert.That(afterDistinct, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("State", 0, "System.String", "string"),
            ("DamageProperty", 1, "System.Int32", "int")
        }));

        var afterPrint = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "print a = int(1), b = 'hello', c = true, d = 1.5 | getschema | sort by ColumnOrdinal asc");
        Assert.That(afterPrint, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("a", 0, "System.Int32", "int"),
            ("b", 1, "System.String", "string"),
            ("c", 2, "System.SByte", "bool"),
            ("d", 3, "System.Double", "real")
        }));

        var logTable = TestUtils.GenerateTableWithColumn(
            new List<string> { "code=200 ok=true" },
            columnName: "Line",
            tableName: "Logs");
        db.AddTable(logTable);

        var afterParse = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "Logs | parse Line with 'code=' Code:int ' ok=' Ok:bool | getschema | sort by ColumnOrdinal asc");
        Assert.That(afterParse, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Line", 0, "System.String", "string"),
            ("Code", 1, "System.Int32", "int"),
            ("Ok", 2, "System.SByte", "bool")
        }));
    }

    [Test]
    [Description("getschema - output is a regular table that can be filtered, counted, sorted, and piped into getschema again")]
    public void GetSchema_PipedIntoFurtherOperatorsAndMetaGetSchema()
    {
        var db = new KustoDatabase();
        db.AddTable(TestUtils.BuildTestTable());

        var stringCols = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "ColumnName",
            db,
            "StormEvents | getschema | where ColumnType == 'string' | project ColumnName");
        Assert.That(stringCols, Is.EquivalentTo(new List<string> { "State", "EventType" }));

        var colCount = NeonTestUtils.ExecuteAndGetSingleValue<long>(
            db,
            "StormEvents | getschema | count",
            "Count");
        Assert.That(colCount, Is.EqualTo(4L));

        var lastTwo = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "ColumnName",
            db,
            "StormEvents | getschema | sort by ColumnOrdinal desc | take 2 | project ColumnName");
        Assert.That(lastTwo, Is.EqualTo(new List<string> { "DamageProperty", "EventType" }));

        // Meta-schema: getschema | getschema describes the 4 schema columns themselves
        var metaSchema = TestUtils.ExecuteAndGetDataFor4Columns<string, int, string, string>(
            "ColumnName",
            "ColumnOrdinal",
            "DataType",
            "ColumnType",
            db,
            "StormEvents | getschema | getschema | sort by ColumnOrdinal asc");
        Assert.That(metaSchema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("ColumnName", 0, "System.String", "string"),
            ("ColumnOrdinal", 1, "System.Int32", "int"),
            ("DataType", 2, "System.String", "string"),
            ("ColumnType", 3, "System.String", "string")
        }));
    }
}
