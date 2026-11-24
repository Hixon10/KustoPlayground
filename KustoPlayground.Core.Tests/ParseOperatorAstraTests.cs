namespace KustoPlayground.Core.Tests;

public class ParseOperatorAstraTests
{
    [Test]
    public void Parse_AppendsDefaultStringAndTypedCapturesToOriginalColumns()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWith2Columns<long, string>(
            [(1, "prefix name=red fox;qty=-12; tail"), (2, "name=blue;qty=0;")], "Id", "Message", "Logs"));

        var rows = TestUtils.ExecuteAndGetDataFor4Columns<long, string, string, long>(
            "Id", "Message", "Label", "Qty", database,
            """Logs | parse Message with * "name=" Label ";qty=" Qty:long ";" *""");

        Assert.That(rows, Is.EquivalentTo(new[]
        {
            (1L, "prefix name=red fox;qty=-12; tail", "red fox", -12L), (2L, "name=blue;qty=0;", "blue", 0L)
        }));
    }

    [TestCase("", null, null)]
    [TestCase("kind=simple", null, null)]
    [TestCase("kind=relaxed", 8L, 3L)]
    public void Parse_ConversionFailureAndMissingDelimiterHaveDifferentEffects(string mode, long? partialA, long? partialC)
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWith2Columns<long, string>(
            [(1, "a=8;b=2;c=3;"), (2, "a=8;b=bad;c=3;"), (3, "a=8;b=2;"), (4, "")],
            "Id", "Message", "Logs"));

        var rows = TestUtils.ExecuteAndGetDataFor4Columns<long, long?, long?, long?>("Id", "A", "B", "C", database,
            $"""Logs | parse {mode} Message with "a=" A:long ";b=" B:long ";c=" C:long ";" """);

        Assert.That(rows, Is.EquivalentTo(new (long, long?, long?, long?)[]
        {
            (1, 8, 2, 3), (2, partialA, null, partialC), (3, null, null, null), (4, null, null, null)
        }));
    }

    [Test]
    public void Parse_SimpleDelimitersAreLiteralAndCaseSensitive()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(
            new[] { "n=12.end", "n=12Xend", "N=12.end" }, "Message", "Logs"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, long?>("Message", "N", database,
            """Logs | parse Message with "n=" N:long ".end" """);

        Assert.That(rows, Is.EquivalentTo(new (string, long?)[]
        {
            ("n=12.end", 12), ("n=12Xend", null), ("N=12.end", null)
        }));
    }

    [Test]
    public void Parse_PatternMayStartAndEndWithStringCaptures()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(
            new[] { "west|a|b", "east|c", "north|" }, "Message", "Logs"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, string>("Region", "Suffix", database,
            """Logs | parse Message with Region "|" Suffix""");

        Assert.That(rows, Is.EquivalentTo(new[] { ("west", "a|b"), ("east", "c"), ("north", "") }));
    }

    [Test]
    public void Parse_RegexSupportsCaseInsensitiveAndVariableWidthDelimiters()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(
            new[] { "id:   17;", "ID:\t-4;", "Ix: 2;" }, "Message", "Logs"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<string, long?>("Message", "N", database,
            """Logs | parse kind=regex flags=i Message with @"id:\s*" N:long ";" """);

        Assert.That(rows, Is.EquivalentTo(new (string, long?)[] { ("id:   17;", 17), ("ID:\t-4;", -4), ("Ix: 2;", null) }));
    }

    [TestCase("", null)]
    [TestCase("flags=s", 7L)]
    public void Parse_RegexDotOnlyCrossesNewlineInSingleLineMode(string flags, long? expected)
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(new[] { "head\nid=7;" }, "Message", "Logs"));

        var values = TestUtils.ExecuteAndGetDataForOneColumn<long?>("N", database,
            $"""Logs | parse kind=regex {flags} Message with @"head.*id=" N:long ";" """);

        Assert.That(values, Is.EqualTo(new[] { expected }));
    }

    [Test]
    public void Parse_ProducesDatetimeTimespanAndRealCaptures()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(
            new[] { "at=2024-02-29T12:34:56Z;elapsed=00:00:02.500;ratio=0.25;" }, "Message", "Logs"));

        var rows = TestUtils.ExecuteAndGetDataFor3Columns<DateTime, TimeSpan, double>("At", "Elapsed", "Ratio", database,
            """Logs | parse Message with "at=" At:datetime ";elapsed=" Elapsed:timespan ";ratio=" Ratio:real ";" """);

        var at = new DateTime(2024, 2, 29, 12, 34, 56, DateTimeKind.Utc);
        Assert.That(rows, Is.EqualTo(new[] { (at, TimeSpan.FromMilliseconds(2500), 0.25) }));
    }

    [Test]
    public void Parse_ConvertsANonStringSourceToText()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(new long[] { -12, 0, 7 }, "N", "Numbers"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<long, string>("N", "Digits", database,
            "Numbers | parse N with Digits:string");

        Assert.That(rows, Is.EquivalentTo(new[] { (-12L, "-12"), (0L, "0"), (7L, "7") }));
    }
}
