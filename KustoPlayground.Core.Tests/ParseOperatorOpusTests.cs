namespace KustoPlayground.Core.Tests;

/// <summary>
/// parse operator - https://learn.microsoft.com/en-us/kusto/query/parse-operator
/// Expected values were verified against a real Azure Data Explorer cluster.
///
/// Summary of the verified semantics:
///  - simple (default): constants are case-sensitive, string columns are non-greedy, the match is NOT anchored
///    at the start but IS anchored at the end, typed columns only match values of their type (backtracking is used),
///    when the row doesn't match all extracted string columns are "" and typed columns are null.
///  - relaxed: like simple, but a typed column which can't be converted is null while other columns are still extracted.
///  - regex: string columns are greedy by default (flags=U makes them lazy), the match is not anchored.
/// </summary>
public class ParseOperatorOpusTests
{
    private const string NameAgePattern = "\"name=\" name \", age=\" age:long";

    [TestCase("")]
    [TestCase("kind=simple ")]
    [Description("parse simple mode - unmatched rows produce empty strings and nulls, anchored at the end, not anchored at the start")]
    public void Parse_SimpleMode_ExtractsNameAndAge(string kind)
    {
        var kustoDatabase = CreatePeopleDatabase();

        var actualData = ExecuteNameAgeQuery(
            kustoDatabase,
            $"People | parse {kind}Text with {NameAgePattern} | project Id, name, age");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string, long?)>
        {
            (1, "John", 30),
            (2, "", null),
            (3, "", null),
            (4, "", null),
            (5, "Bob", 41),
            (6, "", null),
            (7, "", 7),
            (8, "Joe", -8)
        }));
    }

    [Test]
    [Description("parse relaxed mode - typed column which can't be converted is null, other columns are still extracted")]
    public void Parse_RelaxedMode_ExtractsPartialMatches()
    {
        var kustoDatabase = CreatePeopleDatabase();

        var actualData = ExecuteNameAgeQuery(
            kustoDatabase,
            $"People | parse kind=relaxed Text with {NameAgePattern} | project Id, name, age");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string, long?)>
        {
            (1, "John", 30),
            (2, "", null),
            (3, "Ann", null),
            (4, "", null),
            (5, "Bob", 41),
            (6, "Eve", null),
            (7, "", 7),
            (8, "Joe", -8)
        }));
    }

    [Test]
    [Description("parse regex mode - pattern is translated to a regex which is not anchored (trailing text is ignored)")]
    public void Parse_RegexMode_ExtractsNameAndAge()
    {
        var kustoDatabase = CreatePeopleDatabase();

        var actualData = ExecuteNameAgeQuery(
            kustoDatabase,
            $"People | parse kind=regex Text with {NameAgePattern} | project Id, name, age");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string, long?)>
        {
            (1, "John", 30),
            (2, "", null),
            (3, "", null),
            (4, "", null),
            (5, "Bob", 41),
            (6, "Eve", 52),
            (7, "", 7),
            (8, "Joe", -8)
        }));
    }

    [Test]
    [Description("parse - extracted columns are appended to the end of the input columns with their declared types")]
    public void Parse_GetSchema_AppendsTypedColumns()
    {
        var kustoDatabase = CreatePeopleDatabase();

        var schema = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            $"People | parse Text with {NameAgePattern} | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Id", 0, "System.Int64", "long"),
            ("Text", 1, "System.String", "string"),
            ("name", 2, "System.String", "string"),
            ("age", 3, "System.Int64", "long")
        }));
    }

    [Test]
    [Description("parse - explicit string, int and real column types")]
    public void Parse_ExplicitColumnTypes_ConvertsValues()
    {
        var kustoDatabase = CreatePeopleDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            """
            People
            | where Id == 1
            | parse kind=simple Text with "name=" n1:string ", age=" a2:int
            | parse Text with "name=" n2 ", age=" a3:real
            | project n1, a2, n2, a3
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        OpusTestUtils.AssertTypedValue(rows[0]["n1"], "John");
        OpusTestUtils.AssertTypedValue(rows[0]["a2"], 30);
        OpusTestUtils.AssertTypedValue(rows[0]["n2"], "John");
        OpusTestUtils.AssertTypedValue(rows[0]["a3"], 30.0);
    }

    [Test]
    [Description("parse - int, real, datetime, timespan, bool and guid columns are extracted with their types")]
    public void Parse_AllScalarTypes_ConvertsValues()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(
            ["i=42;r=3.14;d=2016-02-17 08:40:01;t=01:30:00;b=true;g=74be27de-1e4e-49d9-b579-fe0b331d3642;end"],
            "Text",
            "Values");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            """
            Values
            | parse Text with "i=" i:int ";r=" r:real ";d=" d:datetime ";t=" t:timespan ";b=" b:bool ";g=" g:guid ";end"
            | project i, r, d, t, b, g
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        OpusTestUtils.AssertTypedValue(rows[0]["i"], 42);
        OpusTestUtils.AssertTypedValue(rows[0]["r"], 3.14);
        OpusTestUtils.AssertTypedValue(rows[0]["d"], OpusTestUtils.ParseDateTime("2016-02-17 08:40:01"));
        OpusTestUtils.AssertTypedValue(rows[0]["t"], TimeSpan.FromMinutes(90));
        OpusTestUtils.AssertTypedValue(rows[0]["b"], true);
        OpusTestUtils.AssertTypedValue(rows[0]["g"], Guid.Parse("74be27de-1e4e-49d9-b579-fe0b331d3642"));
    }

    [Test]
    [Description("parse - documentation example (simple mode) with leading/trailing wildcards, long and datetime (date) columns")]
    public void Parse_DocExampleTraces_ExtractsAllColumns()
    {
        var kustoDatabase = CreateTracesDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            """
            Traces
            | parse EventText with * "resourceName=" resourceName ", totalSlices=" totalSlices:long * "sliceNumber=" sliceNumber:long * "lockTime=" lockTime ", releaseTime=" releaseTime:date "," * "previousLockTime=" previousLockTime:date ")" *
            | project resourceName, totalSlices, sliceNumber, lockTime, releaseTime, previousLockTime
            """);

        var actualData = rows
            .Select(row => (
                (string)row["resourceName"]!,
                (long)row["totalSlices"]!,
                (long)row["sliceNumber"]!,
                (string)row["lockTime"]!,
                (DateTime)row["releaseTime"]!,
                (DateTime)row["previousLockTime"]!))
            .ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(string, long, long, string, DateTime, DateTime)>
        {
            ("PipelineScheduler", 27, 23, "02/17/2016 08:40:01", new DateTime(2016, 2, 17, 8, 40, 1), new DateTime(2016, 2, 17, 8, 39, 1)),
            ("PipelineScheduler", 27, 15, "02/17/2016 08:40:00", new DateTime(2016, 2, 17, 8, 40, 0), new DateTime(2016, 2, 17, 8, 39, 0)),
            ("PipelineScheduler", 27, 20, "02/17/2016 08:40:01", new DateTime(2016, 2, 17, 8, 40, 1), new DateTime(2016, 2, 17, 8, 39, 1)),
            ("PipelineScheduler", 27, 22, "02/17/2016 08:41:01", new DateTime(2016, 2, 17, 8, 41, 0), new DateTime(2016, 2, 17, 8, 40, 1)),
            ("PipelineScheduler", 27, 16, "02/17/2016 08:41:00", new DateTime(2016, 2, 17, 8, 41, 0), new DateTime(2016, 2, 17, 8, 40, 0))
        }));
    }

    [Test]
    [Description("parse - extracted column combined with distinct")]
    public void Parse_TracesWithDistinct_ReturnsSingleResource()
    {
        var kustoDatabase = CreateTracesDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "resourceName",
            kustoDatabase,
            """
            Traces
            | parse EventText with * "resourceName=" resourceName ", totalSlices=" *
            | distinct resourceName
            """);

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "PipelineScheduler" }));
    }

    [Test]
    [Description("parse - extracted long column can be used in where and sort")]
    public void Parse_TracesWithWhereAndSort_ReturnsOrderedSlices()
    {
        var kustoDatabase = CreateTracesDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "sliceNumber",
            kustoDatabase,
            """
            Traces
            | parse EventText with * "sliceNumber=" sliceNumber:long ", lockTime=" *
            | where sliceNumber > 15
            | sort by sliceNumber asc
            | project sliceNumber
            """);

        Assert.That(actualData, Is.EqualTo(new List<long> { 16, 20, 22, 23 }));
    }

    [TestCase("kind=regex flags=Ui", "PipelineScheduler")]
    [TestCase("kind=regex", "")]
    [Description("parse regex mode - flags=Ui makes the match case-insensitive and lazy")]
    public void Parse_RegexModeWithFlags_CaseInsensitiveMatch(string kind, string expected)
    {
        var kustoDatabase = CreateTracesDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "resourceName",
            kustoDatabase,
            $"""
             Traces
             | parse {kind} EventText with * "RESOURCENAME=" resourceName "," *
             | project resourceName
             """);

        Assert.That(actualData, Is.EquivalentTo(Enumerable.Repeat(expected, 5).ToList()));
    }

    [Test]
    [Description("parse regex mode - constants are regular expressions")]
    public void Parse_RegexModeWithRegexConstant_ExtractsInt()
    {
        var kustoDatabase = CreateTracesDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            "day",
            kustoDatabase,
            """
            Traces
            | parse kind=regex EventText with @"lockTime=\d+/" day:int "/" *
            | project day
            """);

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 17, 17, 17, 17, 17 }));
    }

    [TestCase("kind = regex", "k1=v1;k2", "v2")]
    [TestCase("kind=regex flags=U", "k1", "")]
    [Description("parse regex mode - string columns are greedy by default, flags=U makes them lazy")]
    public void Parse_RegexModeGreediness_DependsOnFlags(string kind, string expectedKey, string expectedRest)
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["k1=v1;k2=v2"], "Text", "Pairs");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "gKey",
            "gRest",
            kustoDatabase,
            $"""Pairs | parse {kind} Text with gKey "=" gRest | project gKey, gRest""");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string)> { (expectedKey, expectedRest) }));
    }

    [Test]
    [Description("parse simple mode - string columns are non-greedy, the last column takes the rest of the text")]
    public void Parse_SimpleModeNonGreedy_SplitsOnFirstSeparator()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["k1=v1;k2=v2"], "Text", "Pairs");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "sKey",
            "sRest",
            kustoDatabase,
            """Pairs | parse Text with sKey "=" sRest | project sKey, sRest""");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string)> { ("k1", "v1;k2=v2") }));
    }

    [Test]
    [Description("parse regex mode - trailing string column captures the rest of the text, the match can start anywhere")]
    public void Parse_RegexModeTrailingColumn_CapturesRest()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["xx name=Bob tail"], "Text", "Lines");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "rName",
            kustoDatabase,
            """Lines | parse kind=regex Text with "name=" rName | project rName""");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "Bob tail" }));
    }

    [Test]
    [Description("parse simple mode - several columns, each one stops at the first occurrence of the next constant")]
    public void Parse_SimpleModeMultipleColumns_SplitsOnFirstOccurrences()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["k=v=w|a,b,c"], "Text", "Lines");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor4Columns<string, string, string, string>(
            "k",
            "v",
            "x",
            "y",
            kustoDatabase,
            """Lines | parse Text with k "=" v "|" x "," y | project k, v, x, y""");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string, string, string)>
        {
            ("k", "v=w", "a", "b,c")
        }));
    }

    [Test]
    [Description("parse simple mode - trailing * accepts any suffix, leading * skips the shortest prefix")]
    public void Parse_SimpleModeWildcards_MatchPrefixAndSuffix()
    {
        var table = TestUtils.GenerateTableWith2Columns<long, string>(
            [(1L, "name=Bob, more"), (2L, "name=Bob"), (3L, "a=b=c")],
            "Id",
            "Text",
            "Lines");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            """
            Lines
            | parse Text with "name=" n1 "," *
            | parse Text with * "=" afterFirstEq
            | parse Text with beforeFirstEq "=" *
            | project Id, n1, afterFirstEq, beforeFirstEq
            """);

        var actualData = rows
            .Select(row => ((long)row["Id"]!, (string)row["n1"]!, (string)row["afterFirstEq"]!, (string)row["beforeFirstEq"]!))
            .ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string, string, string)>
        {
            (1, "Bob", "Bob, more", "name"),
            (2, "", "Bob", "name"),
            (3, "", "b=c", "a")
        }));
    }

    [Test]
    [Description("parse simple mode - pattern must match until the end of the text when it ends with a constant")]
    public void Parse_SimpleModeTrailingConstant_AnchoredAtEnd()
    {
        var table = TestUtils.GenerateTableWith2Columns<long, string>(
            [(1L, "name=Bob, more"), (2L, "name=Bob,")],
            "Id",
            "Text",
            "Lines");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, string>(
            "Id",
            "name",
            kustoDatabase,
            """Lines | parse Text with "name=" name "," | project Id, name""");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string)>
        {
            (1, ""),
            (2, "Bob")
        }));
    }

    [Test]
    [Description("parse simple mode - constants are case-sensitive, no match produces empty strings")]
    public void Parse_SimpleModeConstants_AreCaseSensitive()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["NAME=Bob, x"], "Text", "Lines");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "name",
            "rest",
            kustoDatabase,
            """Lines | parse Text with "name=" name "," rest | project name, rest""");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string)> { ("", "") }));
    }

    [Test]
    [Description("parse simple mode - typed column only matches values of its type, so the leading * backtracks to the next occurrence")]
    public void Parse_SimpleModeTypedColumnAfterWildcard_Backtracks()
    {
        var kustoDatabase = CreateXValuesDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            """
            XValues
            | parse Text with * "x=" v:long
            | parse Text with * "x=" w
            | project Id, v, w
            """);

        var actualData = rows.Select(row => ((long)row["Id"]!, (long?)row["v"], (string)row["w"]!)).ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long?, string)>
        {
            (1, 2, "1 x=2"),
            (2, 5, "5"),
            (3, null, "7 y"),
            (4, 8, "8")
        }));
    }

    [Test]
    [Description("parse relaxed mode - typed column is matched as text and converted, there is no backtracking")]
    public void Parse_RelaxedModeTypedColumnAfterWildcard_DoesNotBacktrack()
    {
        var kustoDatabase = CreateXValuesDatabase();

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            """
            XValues
            | where Id <= 2
            | parse kind=relaxed Text with * "x=" v:long
            | project Id, v
            """);

        var actualData = rows.Select(row => ((long)row["Id"]!, (long?)row["v"])).ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long?)>
        {
            (1, null),
            (2, 5)
        }));
    }

    [TestCase("", null, null)]
    [TestCase("kind=relaxed ", null, 2L)]
    [Description("parse - failed conversion of a typed column fails the whole row in simple mode, but not in relaxed mode")]
    public void Parse_FailedTypedConversion_DependsOnMode(string kind, long? expectedA, long? expectedB)
    {
        var table = TestUtils.GenerateTableWith2Columns<long, string>(
            [(1L, "a=x;b=2"), (2L, "a=1;b=2")],
            "Id",
            "Text",
            "Lines");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            $"""Lines | parse {kind}Text with "a=" a:long ";b=" b:long | project Id, a, b""");

        var actualData = rows.Select(row => ((long)row["Id"]!, (long?)row["a"], (long?)row["b"])).ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long?, long?)>
        {
            (1, expectedA, expectedB),
            (2, 1, 2)
        }));
    }

    [Test]
    [Description("parse - extracting into an existing (non-source) column overwrites it in place, unmatched rows get an empty string")]
    public void Parse_IntoExistingColumn_OverwritesValues()
    {
        var table = TestUtils.GenerateTableWith3Columns<long, string, string>(
            [(1L, "old1", "Key=Value"), (2L, "old2", "Other")],
            "Id",
            "Target",
            "Text",
            "Lines");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<long, string, string>(
            "Id",
            "Target",
            "Text",
            kustoDatabase,
            """Lines | parse Text with "Key=" Target""");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string, string)>
        {
            (1, "Value", "Key=Value"),
            (2, "", "Other")
        }));

        var schema = OpusTestUtils.ExecuteGetSchema(
            kustoDatabase,
            """Lines | parse Text with "Key=" Target | getschema""");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("Id", 0, "System.Int64", "long"),
            ("Target", 1, "System.String", "string"),
            ("Text", 2, "System.String", "string")
        }));
    }

    [Test]
    [Description("parse - the source column itself can be the target column")]
    public void Parse_IntoSourceColumn_ReplacesSource()
    {
        var table = TestUtils.GenerateTableWith2Columns<long, string>(
            [(1L, "Key=Value"), (2L, "Other")],
            "Id",
            "Text",
            "Lines");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, string>(
            "Id",
            "Text",
            kustoDatabase,
            """Lines | parse Text with "Key=" Text | project Id, Text""");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string)>
        {
            (1, "Value"),
            (2, "")
        }));
    }

    [Test]
    [Description("parse - non-string source column is converted to string before parsing")]
    public void Parse_LongSourceColumn_ParsedAsString()
    {
        var table = TestUtils.GenerateTableWithColumn<long>([12345, 12, 99], "n", "Numbers");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var rows = OpusTestUtils.ExecuteAndGetRows(
            kustoDatabase,
            """Numbers | parse n with "12" restNum:long | project n, restNum""");

        var actualData = rows.Select(row => ((long)row["n"]!, (long?)row["restNum"])).ToList();

        Assert.That(actualData, Is.EquivalentTo(new List<(long, long?)>
        {
            (12345, 345),
            (12, null),
            (99, null)
        }));
    }

    [Test]
    [Description("parse - datetime source column is parsed in its ISO 8601 string form")]
    public void Parse_DateTimeSourceColumn_ExtractsYearAndMonth()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor3Columns<string, int, int>(
            "EventType",
            "year",
            "month",
            kustoDatabase,
            """StormEvents | parse StartTime with year:int "-" month:int "-" * | project EventType, year, month""");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, int, int)>
        {
            ("Hurricane", 2025, 8),
            ("Flood", 2023, 3),
            ("Tornado", 2024, 6)
        }));
    }

    [Test]
    [Description("parse - source can be an expression")]
    public void Parse_ExpressionSource_ParsesExpressionResult()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["key=value", "KEY=Other", "nokey"], "Text", "Lines");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "Text",
            "upperVal",
            kustoDatabase,
            """Lines | parse toupper(Text) with "KEY=" upperVal | project Text, upperVal""");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string)>
        {
            ("key=value", "VALUE"),
            ("KEY=Other", "OTHER"),
            ("nokey", "")
        }));
    }

    [Test]
    [Description("parse - unmatched rows can be filtered out by checking the extracted string column")]
    public void Parse_WithWhere_FiltersUnmatchedRows()
    {
        var kustoDatabase = CreatePeopleDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "name",
            kustoDatabase,
            $"People | parse Text with {NameAgePattern} | where name != '' | project name");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "John", "Bob", "Joe" }));
    }

    [Test]
    [Description("parse - extracted columns combined with strcat")]
    public void Parse_WithStrcat_BuildsSentence()
    {
        var kustoDatabase = CreatePeopleDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Sentence",
            kustoDatabase,
            $"People | parse Text with {NameAgePattern} | where name != '' | project Sentence = strcat(name, ' is ', age)");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "John is 30", "Bob is 41", "Joe is -8" }));
    }

    [Test]
    [Description("parse - extracted long column can be used for sorting")]
    public void Parse_WithSortAndTake_ReturnsOldest()
    {
        var kustoDatabase = CreatePeopleDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "name",
            kustoDatabase,
            $"People | parse Text with {NameAgePattern} | where name != '' | sort by age desc | take 1 | project name");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "Bob" }));
    }

    [Test]
    [Description("parse - parsed values can be parsed again by a second parse operator")]
    public void Parse_Chained_ParsesExtractedValue()
    {
        var kustoDatabase = CreateTracesDatabase();

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, int>(
            "sliceNumber",
            "minute",
            kustoDatabase,
            """
            Traces
            | parse EventText with * "sliceNumber=" sliceNumber:long ", lockTime=" lockTime ", releaseTime=" *
            | parse lockTime with * ":" minute:int ":" *
            | project sliceNumber, minute
            """);

        Assert.That(actualData, Is.EquivalentTo(new List<(long, int)>
        {
            (23, 40),
            (15, 40),
            (20, 40),
            (22, 41),
            (16, 41)
        }));
    }

    [TestCase("""Lines | parse Text with * "=" *""")]
    [TestCase("""Lines | parse Text with "a=" a *""")]
    [TestCase("""Lines | parse NoSuchColumn with "a=" a""")]
    [Description("parse - pattern without columns, column followed by * and unknown source column are semantic errors")]
    public void Parse_InvalidPattern_Fails(string query)
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["a=1"], "Text", "Lines");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        OpusTestUtils.AssertQueryFails(kustoDatabase, query);
    }

    private static KustoDatabase CreatePeopleDatabase()
    {
        var table = TestUtils.GenerateTableWith2Columns<long, string>(
            [
                (1L, "name=John, age=30"),
                (2L, "garbage"),
                (3L, "name=Ann, age=abc"),
                (4L, ""),
                (5L, "xx name=Bob, age=41"),
                (6L, "name=Eve, age=52 tail"),
                (7L, "name=, age=7"),
                (8L, "name=Joe, age=-8")
            ],
            "Id",
            "Text",
            "People");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        return kustoDatabase;
    }

    private static List<(long, string, long?)> ExecuteNameAgeQuery(KustoDatabase kustoDatabase, string query)
    {
        return OpusTestUtils.ExecuteAndGetRows(kustoDatabase, query)
            .Select(row => ((long)row["Id"]!, (string)row["name"]!, (long?)row["age"]))
            .ToList();
    }

    private static KustoDatabase CreateXValuesDatabase()
    {
        var table = TestUtils.GenerateTableWith2Columns<long, string>(
            [(1L, "x=1 x=2"), (2L, "x=5"), (3L, "x=7 y"), (4L, "a x=8")],
            "Id",
            "Text",
            "XValues");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        return kustoDatabase;
    }

    private static KustoDatabase CreateTracesDatabase()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(
            [
                TraceEvent(23, "02/17/2016 08:40:01", "02/17/2016 08:40:01", "02/17/2016 08:39:01"),
                TraceEvent(15, "02/17/2016 08:40:00", "02/17/2016 08:40:00", "02/17/2016 08:39:00"),
                TraceEvent(20, "02/17/2016 08:40:01", "02/17/2016 08:40:01", "02/17/2016 08:39:01"),
                TraceEvent(22, "02/17/2016 08:41:01", "02/17/2016 08:41:00", "02/17/2016 08:40:01"),
                TraceEvent(16, "02/17/2016 08:41:00", "02/17/2016 08:41:00", "02/17/2016 08:40:00")
            ],
            "EventText",
            "Traces");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        return kustoDatabase;
    }

    private static string TraceEvent(int sliceNumber, string lockTime, string releaseTime, string previousLockTime)
    {
        return "Event: NotifySliceRelease (resourceName=PipelineScheduler, totalSlices=27, " +
               $"sliceNumber={sliceNumber}, lockTime={lockTime}, releaseTime={releaseTime}, previousLockTime={previousLockTime})";
    }
}
