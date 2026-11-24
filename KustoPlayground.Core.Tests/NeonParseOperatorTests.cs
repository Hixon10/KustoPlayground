namespace KustoPlayground.Core.Tests;

public class NeonParseOperatorTests
{
    #region kind=simple (default) Tests

    [Test]
    [Description("parse (default simple) - extracts string and typed columns with leading wildcard")]
    public void Parse_Simple_DefaultKind_BasicExtraction()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "Event: Login has event code: 200 and status: OK"),
                (2, "Event: Logout has event code: 401 and status: Denied"),
                (3, "Event: Timeout has event code: 504 and status: Failed")
            },
            columnName1: "Id",
            columnName2: "EventText",
            tableName: "TraceLogs");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "TraceLogs | parse EventText with * 'has event code: ' Code:int ' and status: ' Status:string | sort by Id asc");

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Code"], Is.EqualTo(200));
        Assert.That(rows[0]["Status"], Is.EqualTo("OK"));
        Assert.That(rows[1]["Code"], Is.EqualTo(401));
        Assert.That(rows[1]["Status"], Is.EqualTo("Denied"));
        Assert.That(rows[2]["Code"], Is.EqualTo(504));
        Assert.That(rows[2]["Status"], Is.EqualTo("Failed"));
    }

    [Test]
    [Description("parse (simple) - starts directly with column name without leading wildcard or literal")]
    public void Parse_Simple_StartsWithColumnName()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "host=web-01"),
                (2, "region=eastus"),
                (3, "env=prod=v2")
            },
            columnName1: "Id",
            columnName2: "Line",
            tableName: "KvPairs");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor3Columns<int, string, string>(
            "Id",
            "Key",
            "Val",
            db,
            "KvPairs | parse Line with Key '=' Val | sort by Id asc");

        Assert.That(actual, Is.EqualTo(new List<(int, string, string)>
        {
            (1, "host", "web-01"),
            (2, "region", "eastus"),
            (3, "env", "prod=v2")
        }));
    }

    [Test]
    [Description("parse (simple) - a literal may match after a prefix even without a leading *")]
    public void Parse_Simple_StartsWithLiteral_AllowsTextBeforeMatch()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "ERR:404:NotFound"),
                (2, "prefix ERR:500:ServerError")
            },
            columnName1: "Id",
            columnName2: "Msg",
            tableName: "Messages");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "Messages | parse Msg with 'ERR:' Code:int ':' Reason | sort by Id asc");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Code"], Is.EqualTo(404));
        Assert.That(rows[0]["Reason"], Is.EqualTo("NotFound"));

        Assert.That(rows[1]["Code"], Is.EqualTo(500));
        Assert.That(rows[1]["Reason"], Is.EqualTo("ServerError"));
    }

    [Test]
    [Description("parse (simple) - leading, middle, and trailing wildcards skip unwanted segments")]
    public void Parse_Simple_WildcardsAtStartMiddleAndEnd()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "[2025-01-01] user=alice role=admin latency=42ms extra=ignored"),
                (2, "[2025-01-02] user=bob role=guest latency=15ms tail")
            },
            columnName1: "Id",
            columnName2: "Log",
            tableName: "Audit");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor3Columns<int, string, int>(
            "Id",
            "User",
            "Latency",
            db,
            "Audit | parse Log with * 'user=' User ' ' * 'latency=' Latency:int 'ms' * | sort by Id asc");

        Assert.That(actual, Is.EqualTo(new List<(int, string, int)>
        {
            (1, "alice", 42),
            (2, "bob", 15)
        }));
    }

    [Test]
    [Description("parse (simple) - extracts all supported scalar column types")]
    public void Parse_Simple_AllSupportedScalarTypes()
    {
        var db = new KustoDatabase();
        var guid = Guid.Parse("74be27de-1e4e-49d9-b579-fe0b331d3642");
        var line = $"s=hello|i=42|l=3000000000|r=3.14|d=2.5|b=true|dt=2024-05-11 13:45:07|ts=01:02:03|g={guid}";
        var table = TestUtils.GenerateTableWithColumn(
            new List<string> { line },
            columnName: "Raw",
            tableName: "TypedLog");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "TypedLog | parse Raw with 's=' S:string '|i=' I:int '|l=' L:long '|r=' R:real '|d=' D:double '|b=' B:bool '|dt=' Dt:datetime '|ts=' Ts:timespan '|g=' G:guid");

        Assert.That(rows, Has.Count.EqualTo(1));
        var row = rows[0];
        Assert.That(row["S"], Is.EqualTo("hello"));
        Assert.That(row["I"], Is.EqualTo(42));
        Assert.That(row["L"], Is.EqualTo(3_000_000_000L));
        Assert.That((double)row["R"]!, Is.EqualTo(3.14).Within(1e-9));
        Assert.That((double)row["D"]!, Is.EqualTo(2.5).Within(1e-9));
        Assert.That(row["B"], Is.EqualTo(true));
        Assert.That(row["Dt"], Is.EqualTo(new DateTime(2024, 5, 11, 13, 45, 7, DateTimeKind.Utc)));
        Assert.That(row["Ts"], Is.EqualTo(new TimeSpan(1, 2, 3)));
        Assert.That(row["G"], Is.EqualTo(guid));
    }

    [Test]
    [Description("parse (simple) - unmatched rows are preserved with null typed columns and empty string columns")]
    public void Parse_Simple_UnmatchedRowsArePreservedNotFiltered()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "code=200 msg=ok"),
                (2, "completely unrelated line"),
                (3, ""),
                (4, "code=500 msg=fail")
            },
            columnName1: "Id",
            columnName2: "Text",
            tableName: "Logs");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "Logs | parse kind=simple Text with 'code=' Code:int ' msg=' Msg | sort by Id asc");

        Assert.That(rows, Has.Count.EqualTo(4));

        Assert.That(rows[0]["Code"], Is.EqualTo(200));
        Assert.That(rows[0]["Msg"], Is.EqualTo("ok"));

        Assert.That(rows[1]["Code"], Is.Null);
        Assert.That(string.IsNullOrEmpty((string?)rows[1]["Msg"]), Is.True);

        Assert.That(rows[2]["Code"], Is.Null);
        Assert.That(string.IsNullOrEmpty((string?)rows[2]["Msg"]), Is.True);

        Assert.That(rows[3]["Code"], Is.EqualTo(500));
        Assert.That(rows[3]["Msg"], Is.EqualTo("fail"));
    }

    [Test]
    [Description("parse (simple) - type conversion failure on one column fails all parsed columns in that row")]
    public void Parse_Simple_SingleTypeMismatchFailsWholeRow()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "a=10 b=20 c=ok"),
                (2, "a=10 b=notanint c=ok")
            },
            columnName1: "Id",
            columnName2: "Text",
            tableName: "StrictRows");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "StrictRows | parse kind=simple Text with 'a=' A:int ' b=' B:int ' c=' C | sort by Id asc");

        Assert.That(rows[0]["A"], Is.EqualTo(10));
        Assert.That(rows[0]["B"], Is.EqualTo(20));
        Assert.That(rows[0]["C"], Is.EqualTo("ok"));

        // In simple mode, failure to parse B:int causes A and C to also fail for row 2
        Assert.That(rows[1]["A"], Is.Null);
        Assert.That(rows[1]["B"], Is.Null);
        Assert.That(string.IsNullOrEmpty((string?)rows[1]["C"]), Is.True);
    }

    [Test]
    [Description("parse (simple) - preserves existing columns and overwrites column if same name is specified")]
    public void Parse_Simple_PreservesExistingAndOverwritesMatchingName()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith3Columns(
            new List<(int, string, string)>
            {
                (1, "keep-me", "tag=new-value")
            },
            columnName1: "Id",
            columnName2: "Tag",
            columnName3: "Raw",
            tableName: "OverwriteTest");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "OverwriteTest | parse Raw with 'tag=' Tag");

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Id"], Is.EqualTo(1));
        Assert.That(rows[0]["Raw"], Is.EqualTo("tag=new-value"));
        Assert.That(rows[0]["Tag"], Is.EqualTo("new-value"));
    }

    [Test]
    [Description("parse (simple) - accepts arbitrary scalar expression as input and chained parse operators")]
    public void Parse_Simple_ExpressionInputAndChainedParse()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith3Columns(
            new List<(int, string, string)>
            {
                (1, "us-east", "node=42;cpu=75.5")
            },
            columnName1: "Id",
            columnName2: "Region",
            columnName3: "Metrics",
            tableName: "Telemetry");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            """
            Telemetry
            | parse strcat(Region, '|', Metrics) with Geo '|' Body
            | parse Body with 'node=' NodeId:int ';cpu=' Cpu:real
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Geo"], Is.EqualTo("us-east"));
        Assert.That(rows[0]["NodeId"], Is.EqualTo(42));
        Assert.That((double)rows[0]["Cpu"]!, Is.EqualTo(75.5).Within(1e-9));
    }

    #endregion

    #region kind=relaxed Tests

    [Test]
    [Description("parse (kind=relaxed) - populates valid columns even when another typed column fails conversion")]
    public void Parse_Relaxed_PartialTypeConversionFailurePopulatesOtherColumns()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "a=10 b=20 c=ok"),
                (2, "a=10 b=notanint c=partial"),
                (3, "a=bad b=30 c=also_partial"),
                (4, "missing delimiters altogether")
            },
            columnName1: "Id",
            columnName2: "Text",
            tableName: "RelaxedRows");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            "RelaxedRows | parse kind=relaxed Text with 'a=' A:int ' b=' B:int ' c=' C | sort by Id asc");

        Assert.That(rows, Has.Count.EqualTo(4));

        Assert.That(rows[0]["A"], Is.EqualTo(10));
        Assert.That(rows[0]["B"], Is.EqualTo(20));
        Assert.That(rows[0]["C"], Is.EqualTo("ok"));

        // Row 2: B fails int conversion -> null, but A and C still populate
        Assert.That(rows[1]["A"], Is.EqualTo(10));
        Assert.That(rows[1]["B"], Is.Null);
        Assert.That(rows[1]["C"], Is.EqualTo("partial"));

        // Row 3: A fails int conversion -> null, but B and C still populate
        Assert.That(rows[2]["A"], Is.Null);
        Assert.That(rows[2]["B"], Is.EqualTo(30));
        Assert.That(rows[2]["C"], Is.EqualTo("also_partial"));

        // Row 4: required string delimiters do not match -> entire row fails
        Assert.That(rows[3]["A"], Is.Null);
        Assert.That(rows[3]["B"], Is.Null);
        Assert.That(string.IsNullOrEmpty((string?)rows[3]["C"]), Is.True);
    }

    #endregion

    #region kind=regex and flags Tests

    [Test]
    [Description("parse (kind=regex) - uses regular expression delimiters for flexible extraction")]
    public void Parse_Regex_DelimiterPatterns()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "user:alice   status=200"),
                (2, "user=bob\tstatus:404"),
                (3, "no match here")
            },
            columnName1: "Id",
            columnName2: "Line",
            tableName: "RegexLogs");
        db.AddTable(table);

        var rows = NeonTestUtils.ExecuteAndAssertNoErrors(
            db,
            @"RegexLogs | parse kind=regex Line with 'user[:=]' User @'\s+' 'status[:=]' Status:int | sort by Id asc");

        Assert.That(rows, Has.Count.EqualTo(3));
        // The default greedy capture leaves one of the three spaces for the delimiter.
        Assert.That(rows[0]["User"], Is.EqualTo("alice  "));
        Assert.That(rows[0]["Status"], Is.EqualTo(200));
        Assert.That(rows[1]["User"], Is.EqualTo("bob"));
        Assert.That(rows[1]["Status"], Is.EqualTo(404));
        Assert.That(rows[2]["Status"], Is.Null);
    }

    [Test]
    [Description("parse (kind=regex) - greedy default vs flags=U (ungreedy) matching")]
    public void Parse_Regex_GreedyDefaultVsUngreedyFlagU()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWithColumn(
            new List<string> { "<first>-<second>-<third>" },
            columnName: "Text",
            tableName: "Tags");
        db.AddTable(table);

        var greedy = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Captured",
            db,
            "Tags | parse kind=regex Text with '<' Captured '>'");
        Assert.That(greedy, Is.EqualTo(new List<string> { "first>-<second>-<third" }));

        var ungreedy = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Captured",
            db,
            "Tags | parse kind=regex flags=U Text with '<' Captured '>'");
        Assert.That(ungreedy, Is.EqualTo(new List<string> { "first" }));
    }

    [Test]
    [Description("parse (kind=regex flags=i) - case-insensitive regex delimiter matching")]
    public void Parse_Regex_FlagI_CaseInsensitive()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "ERROR: disk full"),
                (2, "Error: timeout"),
                (3, "error: connection reset")
            },
            columnName1: "Id",
            columnName2: "Line",
            tableName: "Errors");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            "Id",
            "Detail",
            db,
            @"Errors | parse kind=regex flags=i Line with @'error:\s+' Detail | sort by Id asc");

        Assert.That(actual, Is.EqualTo(new List<(int, string)>
        {
            (1, "disk full"),
            (2, "timeout"),
            (3, "connection reset")
        }));
    }

    [Test]
    [Description("parse (kind=regex flags=Ui) - combines ungreedy and case-insensitive flags")]
    public void Parse_Regex_CombinedFlagsUi()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWithColumn(
            new List<string> { "START:alpha:END START:beta:END" },
            columnName: "Text",
            tableName: "Blocks");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Token",
            db,
            "Blocks | parse kind=regex flags=Ui Text with 'start:' Token ':end'");

        Assert.That(actual, Is.EqualTo(new List<string> { "alpha" }));
    }

    [Test]
    [Description("parse (kind=regex flags=s and flags=ms) - dot matches newline and multiline anchors")]
    public void Parse_Regex_FlagsSAndMs_MultilineContent()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWithColumn(
            new List<string> { "BEGIN\nline1\nline2\nEND" },
            columnName: "Doc",
            tableName: "MultilineDocs");
        db.AddTable(table);

        var dotAll = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Body",
            db,
            "MultilineDocs | parse kind=regex flags=s Doc with 'BEGIN\\n' Body '\\nEND'");
        Assert.That(dotAll, Is.EqualTo(new List<string> { "line1\nline2" }));

        var table2 = TestUtils.GenerateTableWithColumn(
            new List<string> { "header\nKEY=42\nfooter" },
            columnName: "Doc",
            tableName: "ConfigDocs");
        db.AddTable(table2);

        var multilineAnchor = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            "Val",
            db,
            "ConfigDocs | parse kind=regex flags=ms Doc with * '^KEY=' Val:int '$'");
        Assert.That(multilineAnchor, Is.EqualTo(new List<int> { 42 }));
    }

    #endregion

    #region Pipeline & Error Cases

    [Test]
    [Description("parse - integrates with downstream where, extend, project, and sort")]
    public void Parse_DownstreamPipelineOperators()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWithColumn(
            new List<string>
            {
                "svc=auth ms=120",
                "svc=billing ms=45",
                "svc=search ms=310",
                "malformed"
            },
            columnName: "Line",
            tableName: "Requests");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<string, int>(
            "Service",
            "DurationMs",
            db,
            """
            Requests
            | parse Line with 'svc=' Service ' ms=' DurationMs:int
            | where DurationMs >= 100
            | sort by DurationMs asc
            | project Service, DurationMs
            """);

        Assert.That(actual, Is.EqualTo(new List<(string, int)>
        {
            ("auth", 120),
            ("search", 310)
        }));
    }

    [Test]
    [Description("parse - invalid pattern, unknown kind, unknown type, and invalid regex return errors")]
    public void Parse_InvalidSyntaxOrArguments_ReturnsError()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWithColumn(
            new List<string> { "hello world" },
            columnName: "Text",
            tableName: "T");
        db.AddTable(table);

        // Wildcard '*' immediately after a string column is disallowed in KQL
        NeonTestUtils.ExecuteAndExpectError(db, "T | parse Text with A * B");
        NeonTestUtils.ExecuteAndExpectError(db, "T | parse kind=invalid Text with A ' ' B");
        NeonTestUtils.ExecuteAndExpectError(db, "T | parse Text with A:unknown_type");
        NeonTestUtils.ExecuteAndExpectError(db, "T | parse kind=regex Text with '[unclosed' A");
    }

    #endregion
}
