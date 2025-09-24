using System.Text.Json;

namespace KustoPlayground.Core.Tests;

public class KustoDatabaseTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void ExecutionResultJsonContextTest()
    {
        var executionResult = new ExecutionResult
        {
            ExecutionErrors =
            [
                new ExecutionError
                {
                    Code = nameof(ExecutionError.ErrorCodes.UnknownTable),
                    Description = "Error Description"
                },
                new ExecutionError
                {
                    Code = nameof(ExecutionError.ErrorCodes.InternalError)
                }
            ],
            ResultRows = new List<Dictionary<string, object?>>
            {
                new()
                {
                    { "column1", 3 },
                    { "column2", "string 2" }
                },
                new()
                {
                    { "column1", 4 }
                }
            }
        };

        string serialize = JsonSerializer.Serialize(executionResult);
        Assert.That(string.IsNullOrEmpty(serialize), Is.False);

        string serialize2 =
            JsonSerializer.Serialize(executionResult, ExecutionResultJsonContext.Default.ExecutionResult);
        Assert.That(string.IsNullOrEmpty(serialize2), Is.False);

        string json = """
                      {
                        "ResultRows": [
                          {
                            "column1": 3,
                            "column2": "string 2"
                          },
                          {
                            "column1": 4
                          }
                        ],
                        "ExecutionErrors": [
                          {
                            "Code": "UnknownTable",
                            "Description": "Error Description"
                          },
                          {
                            "Code": "InternalError"
                          }
                        ]
                      }        
                      """;

        ExecutionResult? deserialize = JsonSerializer.Deserialize<ExecutionResult>(json);
        AssertExecutionResult(deserialize);

        ExecutionResult? deserialize2 =
            JsonSerializer.Deserialize<ExecutionResult>(json, ExecutionResultJsonContext.Default.ExecutionResult);
        AssertExecutionResult(deserialize2);
        return;

        static void AssertExecutionResult(ExecutionResult? result)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ResultRows!, Has.Count.EqualTo(2));

            IReadOnlyDictionary<string, object?> row1;
            IReadOnlyDictionary<string, object?> row2;

            IReadOnlyDictionary<string, object?> firstRow = result.ResultRows[0];
            IReadOnlyDictionary<string, object?> lastRow = result.ResultRows[1];
            if (((JsonElement)firstRow["column1"]!).GetInt32() == 3)
            {
                row1 = firstRow;
                row2 = lastRow;
            }
            else
            {
                row1 = lastRow;
                row2 = firstRow;
            }

            Assert.That(((JsonElement)row1["column1"]!).GetInt32(), Is.EqualTo(3));
            Assert.That(((JsonElement)row1["column2"]!).GetString(), Is.EqualTo("string 2"));
            Assert.That(((JsonElement)row2["column1"]!).GetInt32(), Is.EqualTo(4));

            Assert.That(result.ExecutionErrors!, Has.Count.EqualTo(2));
            Assert.That(result.ExecutionErrors[0].Code, Is.EqualTo("UnknownTable"));
            Assert.That(result.ExecutionErrors[0].Description, Is.EqualTo("Error Description"));
            Assert.That(result.ExecutionErrors[1].Code, Is.EqualTo("InternalError"));
            Assert.That(result.ExecutionErrors[1].Description, Is.Null);
        }
    }

    [Test]
    public void ExecuteQueryWhenUnknownTableTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        string query = @"StormEvents
            | where State == 'FLORIDA' and DamageProperty > 10000
            | project StartTime, EventType, DamageProperty
            | take 10
        ";

        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ResultRows, Is.Null);
        Assert.That(results.ExecutionErrors!, Has.Count.EqualTo(1));
    }

    [Test]
    public void ExecuteQueryWhenEmptyResultWithoutErrorsTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        var startTimeCol = new Column<DateTime>("StartTime", isNullable: false);
        var stormEvents = new Table("StormEvents", [startTimeCol]);
        kustoDatabase.AddTable(stormEvents);

        string query = @"StormEvents
            | take 10
        ";

        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null);
        Assert.That(results.ResultRows!, Is.Empty);
    }

    [Test]
    public void EvaluateBinaryNumbersTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        string query = @"StormEvents
            | extend Elapsed = now() - StartTime
            | extend Elapsed3 = ago(4d) - 10d
            | extend Elapsed4 = 10d + now()
            | extend A = DamageProperty - 53
            | extend B = 42.2 - 53
            | extend C = -42.2 + 53
            | extend D = 10 * DamageProperty
            | extend E = DamageProperty / 0
            | extend F = 1d / 1s
            | extend result3 = 24 * 60 * time(00:01:00) / time(1s)
        ";

        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null);

        DateTime now = DateTime.UtcNow;

        foreach (IReadOnlyDictionary<string, object?> row in results.ResultRows!)
        {
            Assert.That((TimeSpan)row["Elapsed"]!,
                Is.EqualTo(now - (DateTime)row["StartTime"]!)
                    .Within(TimeSpan.FromMinutes(1)));

            var elapsed3 = (DateTime)row["Elapsed3"]!;
            var expectedElapsed3 = now.AddDays(-14);
            Assert.That(elapsed3, Is.EqualTo(expectedElapsed3).Within(TimeSpan.FromMinutes(1)));

            var elapsed4 = (DateTime)row["Elapsed4"]!;
            var expectedElapsed4 = now.Add(TimeSpan.FromDays(10));
            Assert.That(elapsed4, Is.EqualTo(expectedElapsed4).Within(TimeSpan.FromMinutes(1)));

            var a = (double)row["A"]!;
            var damageProperty = (int)row["DamageProperty"]!;
            Assert.That(a, Is.EqualTo(damageProperty - 53));

            var b = (double)row["B"]!;
            Assert.That(b, Is.EqualTo(42.2 - 53));

            var c = (double)row["C"]!;
            Assert.That(c, Is.EqualTo(-42.2 + 53));

            var d = (double)row["D"]!;
            Assert.That(d, Is.EqualTo(10 * damageProperty));

            Assert.That(row["E"], Is.Null);

            var f = (double)row["F"]!;
            Assert.That(f, Is.EqualTo(86400));

            var result3 = (double)row["result3"]!;
            Assert.That(result3, Is.EqualTo(86400));
        }
    }

    [Test]
    public void ExtendOperatorSmokeTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        // CopyState is new column, EventType we should override old value
        string query = @"StormEvents
            | extend CopyState = State, EventType = State, EncodedStr = base64_encode_tostring(State)
            | extend CopyState2 = State
        ";

        var results = kustoDatabase.ExecuteQuery(query);
        Assert.That(results.ExecutionErrors, Is.Null);

        Dictionary<string, string> expectedBase64Encode = new Dictionary<string, string>
        {
            { "FLORIDA", "RkxPUklEQQ==" },
            { "TEXAS", "VEVYQVM=" }
        };

        foreach (IReadOnlyDictionary<string, object?> row in results.ResultRows!)
        {
            string state = (string)row["State"]!;
            Assert.That((string)row["CopyState"]!, Is.EqualTo(state));
            Assert.That((string)row["EventType"]!, Is.EqualTo(state));
            Assert.That((string)row["EncodedStr"]!, Is.EqualTo(expectedBase64Encode[state]));

            Assert.That((string)row["CopyState2"]!, Is.EqualTo(state));
        }
    }

    [Test]
    public void ExecuteQueryWithSortTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var florida2025 = new Dictionary<string, object?>
        {
            { "EventType", "Hurricane" },
            { "StartTime", new DateTime(2025, 8, 23, 6, 20, 0) },
            { "State", "FLORIDA" }
        }.AsReadOnly();
        var florida2024 = new Dictionary<string, object?>
        {
            { "EventType", "Tornado" },
            { "StartTime", new DateTime(2024, 6, 1, 16, 50, 30) },
            { "State", "FLORIDA" }
        }.AsReadOnly();
        var texas2023 = new Dictionary<string, object?>
        {
            { "EventType", "Flood" },
            { "StartTime", new DateTime(2023, 3, 28, 10, 30, 0) },
            { "State", "TEXAS" }
        }.AsReadOnly();

        // execute query 1
        string query = @"StormEvents
            | where DamageProperty > 1
            | sort by State asc, StartTime desc
            | project StartTime, EventType, State
            | take 10
        ";
        var results = kustoDatabase.ExecuteQuery(query);
        List<IReadOnlyDictionary<string, object?>> expected = [florida2025, florida2024, texas2023];
        AssertResult(results, expected);

        // execute query 2
        query = @"StormEvents
            | where DamageProperty > 1
            | sort by State asc, StartTime asc
            | project StartTime, EventType, State
            | take 10
        ";
        results = kustoDatabase.ExecuteQuery(query);
        expected = [florida2024, florida2025, texas2023];
        AssertResult(results, expected);

        // execute query 3
        query = @"StormEvents
            | where DamageProperty > 1
            | sort by State desc, StartTime desc
            | project StartTime, EventType, State
            | take 10
        ";
        results = kustoDatabase.ExecuteQuery(query);
        expected = [texas2023, florida2025, florida2024];
        AssertResult(results, expected);

        // execute query 4
        query = @"StormEvents
            | where DamageProperty > 1
            | sort by State, StartTime
            | project StartTime, EventType, State
            | take 10
        ";
        results = kustoDatabase.ExecuteQuery(query);
        expected = [texas2023, florida2025, florida2024];
        AssertResult(results, expected);

        // execute query 5
        query = @"StormEvents
            | where DamageProperty > 1
            | sort by StartTime
            | project StartTime, EventType, State
            | take 10
        ";
        results = kustoDatabase.ExecuteQuery(query);
        expected = [florida2025, florida2024, texas2023];
        AssertResult(results, expected);

        // execute query 6
        query = @"StormEvents
            | where DamageProperty > 1
            | sort by StartTime
            | sort by StartTime asc
            | project StartTime, EventType, State
            | take 10
        ";
        results = kustoDatabase.ExecuteQuery(query);
        expected = [texas2023, florida2024, florida2025];
        AssertResult(results, expected);

        return;

        void AssertResult(ExecutionResult executionResult, List<IReadOnlyDictionary<string, object?>> list)
        {
            Assert.That(executionResult.ExecutionErrors, Is.Null);
            Assert.That(executionResult.ResultRows, Has.Count.EqualTo(list.Count));
            for (var index = 0; index < executionResult.ResultRows.Count; index++)
            {
                Assert.That(executionResult.ResultRows[index], Is.EquivalentTo(list[index]));
            }
        }
    }

    [Test]
    public void ExecuteQueryWithTableAndDataFromUiTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        string query = @"StormEvents
            | where State == 'FLORIDA' and DamageProperty > 10000
            | project StartTime, EventType, DamageProperty
            | take 10
        ";
        var results = kustoDatabase.ExecuteQuery(query);
        List<IReadOnlyDictionary<string, object?>> expected =
        [
            new Dictionary<string, object?>
            {
                { "DamageProperty", 20000 },
                { "StartTime", new DateTime(2025, 8, 23, 6, 20, 0) },
                { "EventType", "Hurricane" }
            }.AsReadOnly()
        ];

        Assert.That(results.ExecutionErrors, Is.Null);
        Assert.That(results.ResultRows, Has.Count.EqualTo(expected.Count));
        for (var index = 0; index < results.ResultRows.Count; index++)
        {
            Assert.That(results.ResultRows[index], Is.EquivalentTo(expected[index]));
        }
    }

    [Test]
    public void GetAllRowsForIntTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        HashSet<int> expectedData = [1, 2, 3, 4];
        Table table = TestUtils.GenerateTableWithColumn(expectedData, tableName: "table1");
        kustoDatabase.AddTable(table);

        List<int> actualData =
            TestUtils.ExecuteAndGetDataForOneColumn<int>(TestUtils.GetColumnName(table), kustoDatabase, table.Name);
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    public void GetAllRowsForLongTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        HashSet<long> expectedData = [1L, 2L, 3L, 4L];
        Table table = TestUtils.GenerateTableWithColumn(expectedData, tableName: "table1");
        kustoDatabase.AddTable(table);

        List<long> actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            TestUtils.GetColumnName(table), kustoDatabase, table.Name);
        Assert.That(actualData, Is.EquivalentTo(expectedData));
    }

    [Test]
    public void GetAllRowsWhenTwoTablesRegisteredTest()
    {
        KustoDatabase kustoDatabase = new KustoDatabase();

        HashSet<int> table1Data = [1, 2, 3, 4];
        Table table1 = TestUtils.GenerateTableWithColumn(table1Data, tableName: "table1");
        kustoDatabase.AddTable(table1);

        HashSet<int> table2Data = [4, 5, 6, 7];
        Table table2 = TestUtils.GenerateTableWithColumn(table2Data, tableName: "table2");
        kustoDatabase.AddTable(table2);

        List<int> actualData1 = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            TestUtils.GetColumnName(table1), kustoDatabase, table1.Name);
        Assert.That(actualData1, Is.EquivalentTo(table1Data));

        List<int> actualData2 = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            TestUtils.GetColumnName(table2), kustoDatabase, table2.Name);
        Assert.That(actualData2, Is.EquivalentTo(table2Data));
    }

    [Test]
    [Description("modulo operator - simple remainder")]
    public void ModuloOperator_SimpleRemainder()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<int> { 1, 2, 3, 4, 5, 6 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table1");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            "table1 | where col1 % 2 == 1");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 1, 3, 5 }));
    }

    [Test]
    [Description("modulo operator - divisible numbers")]
    public void ModuloOperator_DivisibleNumbers()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<int> { 5, 10, 15, 20, 25, 30 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table2");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            "table2 | where col1 % 5 == 0");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 5, 10, 15, 20, 25, 30 }));
    }

    [Test]
    [Description("modulo operator - negative numbers")]
    public void ModuloOperator_NegativeNumbers()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<int> { -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table3");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            columnName,
            kustoDatabase,
            "table3 | where col1 % 2 == 0");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { -4, -2, 0, 2, 4 }));
    }

    [Test]
    [Description("modulo operator - with larger divisor")]
    public void ModuloOperator_LargerDivisor()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<long> { 1, 5, 10, 12, 20, 25, 33 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table4");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            columnName,
            kustoDatabase,
            "table4 | where col1 % 10 == 2");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 12 }));
    }

    [Test]
    [Description("modulo operator - projection with extend")]
    public void ModuloOperator_ProjectionWithExtend()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<int> { 7, 8, 9, 10 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table5");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(
            "col2",
            kustoDatabase,
            "table5 | extend col2 = col1 % 3");

        Assert.That(actualData, Is.EquivalentTo(new List<double> { 1, 2, 0, 1 }));
    }

    [Test]
    [Description("modulo operator - double values")]
    public void ModuloOperator_DoubleValues()
    {
        var kustoDatabase = new KustoDatabase();
        var tableRows = new List<double> { 1.5, 2.0, 3.7, 4.2, 5.0 };
        const string columnName = "col1";
        Table table = TestUtils.GenerateTableWithColumn(tableRows, columnName, "table6");
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(
            columnName,
            kustoDatabase,
            "table6 | where col1 % 2 == 0");

        // expected: numbers divisible by 2 (2.0, 4.2 since remainder 0.2 ≠ 0, only 2.0 passes)
        Assert.That(actualData, Is.EquivalentTo(new List<double> { 2.0 }));
    }
}