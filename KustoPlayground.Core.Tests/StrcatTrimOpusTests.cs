namespace KustoPlayground.Core.Tests;

/// <summary>
/// strcat() - https://learn.microsoft.com/en-us/kusto/query/strcat-function
/// trim()   - https://learn.microsoft.com/en-us/kusto/query/trim-function
/// Expected values were verified against a real Azure Data Explorer cluster.
/// </summary>
public class StrcatTrimOpusTests
{
    [TestCase("strcat('hello', ' ', 'world')", "hello world")]
    [TestCase("strcat('x')", "x")]
    [TestCase("strcat('a', '', 'b')", "ab")]
    [TestCase("strcat('', '')", "")]
    [TestCase("strcat(strcat('a', 'b'), strcat('c'))", "abc")]
    [TestCase("strcat('café', '☕')", "café☕")]
    [TestCase(@"strcat('line1', '\n', 'line2')", "line1\nline2")]
    [TestCase(@"strcat(@'C:\temp', '\\', 'file.txt')", @"C:\temp\file.txt")]
    [TestCase("strcat(toupper('ab'), tolower('CD'))", "ABcd")]
    [Description("strcat - concatenates string arguments")]
    public void Strcat_Strings_Concatenates(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("strcat(1, 2)", "12")]
    [TestCase("strcat(toint(5), tolong(6))", "56")]
    [TestCase("strcat(-7, 'x')", "-7x")]
    [TestCase("strcat(1.5, 'x')", "1.5x")]
    [TestCase("strcat(-0.5, 2.25)", "-0.52.25")]
    [TestCase("strcat(1.0)", "1.0")]
    [TestCase("strcat(1e20)", "100000000000000000000.0")]
    [TestCase("strcat(decimal(1.50))", "1.5")]
    [TestCase("strcat(true, '|', false)", "True|False")]
    [TestCase("strcat('a', 1, 'b', 2.5, true)", "a1b2.5True")]
    [Description("strcat - numbers and bools are converted to strings the same way as tostring() does")]
    public void Strcat_NumbersAndBools_ConvertedToString(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("strcat(datetime(2020-01-02 03:04:05))", "2020-01-02T03:04:05.0000000Z")]
    [TestCase("strcat(datetime(2020-01-02 03:04:05.123))", "2020-01-02T03:04:05.1230000Z")]
    [TestCase("strcat('at ', datetime(2020-01-02))", "at 2020-01-02T00:00:00.0000000Z")]
    [TestCase("strcat(1h)", "01:00:00")]
    [TestCase("strcat(90m)", "01:30:00")]
    [TestCase("strcat(1d)", "1.00:00:00")]
    [TestCase("strcat(-1h)", "-01:00:00")]
    [TestCase("strcat(1ms)", "00:00:00.0010000")]
    [TestCase("strcat(time(1.02:03:04))", "1.02:03:04")]
    [TestCase("strcat(guid(74BE27DE-1E4E-49D9-B579-FE0B331D3642))", "74be27de-1e4e-49d9-b579-fe0b331d3642")]
    [Description("strcat - datetime is ISO 8601 with 7 fractional digits, timespan is [-][d.]hh:mm:ss[.fffffff], guid is lower case")]
    public void Strcat_DateTimeTimeSpanGuid_ConvertedToString(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("strcat('a', int(null), 'b')", "ab")]
    [TestCase("strcat(real(null))", "")]
    [TestCase("strcat('[', long(null), ']')", "[]")]
    [TestCase("strcat('[', bool(null), ']')", "[]")]
    [TestCase("strcat('[', datetime(null), ']')", "[]")]
    [TestCase("strcat('[', timespan(null), ']')", "[]")]
    [Description("strcat - null arguments are treated as empty strings, result is never null")]
    public void Strcat_NullArguments_TreatedAsEmptyStrings(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("""strcat('x', parse_json('{"a":1, "b":[1,2]}'))""", """x{"a":1,"b":[1,2]}""")]
    [TestCase("""strcat(parse_json('{"a":"x"}').a, '-', parse_json('{"n":5}').n)""", "x-5")]
    [TestCase("""strcat(dynamic(["a", "b"])[0], '!')""", "a!")]
    [Description("strcat - dynamic values are serialized as compact JSON, dynamic strings without quotes")]
    public void Strcat_DynamicValues_ConvertedToString(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [Test]
    [Description("strcat - result type is string")]
    public void Strcat_ResultType_IsString()
    {
        var schema = OpusTestUtils.ExecuteGetSchema(new KustoDatabase(), "print r = strcat(1, 2.5, true) | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("r", 0, "System.String", "string")
        }));
    }

    [Test]
    [Description("strcat - up to 64 arguments are supported")]
    public void Strcat_64Arguments_Concatenates()
    {
        var arguments = string.Join(", ", Enumerable.Repeat("'a'", 64));

        var actual = OpusTestUtils.EvaluateScalar($"strcat({arguments})");

        OpusTestUtils.AssertTypedValue(actual, new string('a', 64));
    }

    [TestCase(0)]
    [TestCase(65)]
    [Description("strcat - requires between 1 and 64 arguments")]
    public void Strcat_WrongNumberOfArguments_Fails(int argumentsCount)
    {
        var arguments = string.Join(", ", Enumerable.Repeat("'a'", argumentsCount));

        OpusTestUtils.AssertQueryFails(new KustoDatabase(), $"print r = strcat({arguments})");
    }

    [Test]
    [Description("strcat - concatenates string columns and literals")]
    public void Strcat_StringColumns_Concatenates()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Description",
            kustoDatabase,
            "StormEvents | project Description = strcat(EventType, ' in ', State)");

        Assert.That(actualData, Is.EquivalentTo(new List<string>
        {
            "Hurricane in FLORIDA",
            "Flood in TEXAS",
            "Tornado in FLORIDA"
        }));
    }

    [Test]
    [Description("strcat - int and datetime columns are converted to strings")]
    public void Strcat_IntAndDateTimeColumns_ConvertedToString()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "Damage",
            "When",
            kustoDatabase,
            "StormEvents | project Damage = strcat(State, ':', DamageProperty), When = strcat(EventType, '@', StartTime)");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string)>
        {
            ("FLORIDA:20000", "Hurricane@2025-08-23T06:20:00.0000000Z"),
            ("TEXAS:5000", "Flood@2023-03-28T10:30:00.0000000Z"),
            ("FLORIDA:5000", "Tornado@2024-06-01T16:50:30.0000000Z")
        }));
    }

    [Test]
    [Description("strcat - can be used inside where")]
    public void Strcat_InWhere_FiltersRows()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            "DamageProperty",
            kustoDatabase,
            "StormEvents | where strcat(State, '/', EventType) == 'FLORIDA/Tornado' | project DamageProperty");

        Assert.That(actualData, Is.EquivalentTo(new List<int> { 5000 }));
    }

    [Test]
    [Description("strcat - column created by extend can be filtered and counted")]
    public void Strcat_ExtendThenWhere_CountsRows()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Count",
            kustoDatabase,
            "StormEvents | extend Key = strcat(State, '-', DamageProperty) | where Key == 'FLORIDA-5000' or Key == 'TEXAS-5000' | count");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 2 }));
    }

    [Test]
    [Description("strcat - combined with bin and distinct")]
    public void Strcat_WithBinAndDistinct_ReturnsDistinctKeys()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Key",
            kustoDatabase,
            "StormEvents | project Key = strcat(State, '-', bin(DamageProperty, 10000)) | distinct Key");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "FLORIDA-20000", "FLORIDA-0", "TEXAS-0" }));
    }

    [Test]
    [Description("strcat - result can be passed to other string functions")]
    public void Strcat_InsideToLower_ReturnsLowerCase()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Name",
            kustoDatabase,
            "StormEvents | project Name = tolower(strcat(State, '_', EventType))");

        Assert.That(actualData, Is.EquivalentTo(new List<string>
        {
            "florida_hurricane",
            "texas_flood",
            "florida_tornado"
        }));
    }

    [Test]
    [Description("strcat - length of the concatenated string")]
    public void Strcat_InsideStrlen_ReturnsLength()
    {
        var actual = OpusTestUtils.EvaluateScalar("strlen(strcat('ab', 'cde'))");

        OpusTestUtils.AssertNumericValue(actual, 5);
    }

    [Test]
    [Description("strcat - null values of a nullable column become empty strings")]
    public void Strcat_NullableLongColumn_NullBecomesEmpty()
    {
        var table = OpusTestUtils.NewTable("Scores")
            .WithColumn<string>("Name")
            .WithColumn<long>("Score")
            .WithRow("a", 1L)
            .WithRow("b", null)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Text",
            kustoDatabase,
            "Scores | project Text = strcat(Name, '=', Score)");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "a=1", "b=" }));
    }

    [Test]
    [Description("strcat - real, bool, timespan and guid columns are formatted like tostring()")]
    public void Strcat_TypedColumns_FormattedLikeToString()
    {
        var table = OpusTestUtils.NewTable("Typed")
            .WithColumn<long>("Id")
            .WithColumn<double>("Real")
            .WithColumn<bool>("Flag")
            .WithColumn<TimeSpan>("Duration")
            .WithColumn<Guid>("Uid")
            .WithRow(1L, 1.0, true, TimeSpan.FromMinutes(90), Guid.Parse("74BE27DE-1E4E-49D9-B579-FE0B331D3642"))
            .WithRow(2L, -0.5, false, TimeSpan.FromDays(1), Guid.Empty)
            .Build();

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, string>(
            "Id",
            "Text",
            kustoDatabase,
            "Typed | project Id, Text = strcat(Real, '|', Flag, '|', Duration, '|', Uid)");

        Assert.That(actualData, Is.EquivalentTo(new List<(long, string)>
        {
            (1, "1.0|True|01:30:00|74be27de-1e4e-49d9-b579-fe0b331d3642"),
            (2, "-0.5|False|1.00:00:00|00000000-0000-0000-0000-000000000000")
        }));
    }

    [TestCase("trim(' ', '  abc  ')", " abc ")]
    [TestCase("trim(' ', ' a b ')", "a b")]
    [TestCase(@"trim(@'\s+', '\t\n hi \r\n')", "hi")]
    [TestCase(@"trim(@'\s+', '    Hello, world!    ')", "Hello, world!")]
    [TestCase("trim('--', '--https://bing.com--')", "https://bing.com")]
    [TestCase(@"trim(@'[^\w]+', '-  Te st1// $')", "Te st1")]
    [TestCase("trim('-', '-a-b-')", "a-b")]
    [TestCase("trim('z', 'abc')", "abc")]
    [TestCase("trim('x', '')", "")]
    [TestCase("trim('a', 'a')", "")]
    [TestCase("trim('ab', 'ab')", "")]
    [Description("trim - removes a leading and a trailing match of the regular expression")]
    public void Trim_BasicPatterns_RemovesLeadingAndTrailingMatch(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("trim('1', '11211')", "121")]
    [TestCase("trim('x', 'xxxx')", "xx")]
    [TestCase("trim('--', '---abc---')", "-abc-")]
    [TestCase(@"trim(@'\.', '..a.b..')", ".a.b.")]
    [TestCase("trim('aa', 'aaa')", "a")]
    [TestCase("trim('0', '1000')", "100")]
    [TestCase("trim('0', tostring(1000))", "100")]
    [TestCase(@"trim(@'[\[\]]', '[[v]]')", "[v]")]
    [TestCase("trim('[0-9]', '123abc456')", "23abc45")]
    [TestCase("trim('abc', 'abcabcXabc')", "abcX")]
    [TestCase("trim('aba', 'ababa')", "ba")]
    [Description("trim - only ONE match is removed at the start and ONE at the end (the pattern is not repeated)")]
    public void Trim_RepeatedMatches_RemovesOnlyOneMatchPerSide(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("trim('.', 'abc')", "b")]
    [TestCase("trim('a+', 'aaXaa')", "X")]
    [TestCase("trim('a*', 'aaXaa')", "X")]
    [TestCase("trim('x+', 'xxx')", "")]
    [TestCase("trim('^a', 'aaXaa')", "aXaa")]
    [TestCase("trim('a$', 'aaXaa')", "aaXa")]
    [TestCase("trim('a|b', 'abXba')", "bXb")]
    [TestCase("trim('ab|a', 'aXa')", "X")]
    [TestCase(@"trim(@'\d+', '2024-report-2025')", "-report-")]
    [TestCase("trim('é', 'éaé')", "a")]
    [Description("trim - regex features: any char, quantifiers, anchors, alternation, character classes, unicode")]
    public void Trim_RegexFeatures_AreSupported(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [TestCase("trim('a', 'Aba')", "Ab")]
    [TestCase("trim('(?i)a', 'Aba')", "b")]
    [Description("trim - matching is case-sensitive unless the (?i) flag is used")]
    public void Trim_CaseSensitivity_RespectsRegexFlags(string expression, string expected)
    {
        var actual = OpusTestUtils.EvaluateScalar(expression);

        OpusTestUtils.AssertTypedValue(actual, expected);
    }

    [Test]
    [Description("trim - tostring of a null value is an empty string and trimming it returns an empty string")]
    public void Trim_EmptyStringFromNull_ReturnsEmptyString()
    {
        var actual = OpusTestUtils.EvaluateScalar("trim('1', tostring(int(null)))");

        OpusTestUtils.AssertTypedValue(actual, "");
    }

    [Test]
    [Description("trim - result type is string")]
    public void Trim_ResultType_IsString()
    {
        var schema = OpusTestUtils.ExecuteGetSchema(new KustoDatabase(), "print r = trim('a', 'abc') | getschema");

        Assert.That(schema, Is.EqualTo(new List<(string, int, string, string)>
        {
            ("r", 0, "System.String", "string")
        }));
    }

    [Test]
    [Description("trim - documentation example: trim non-alphanumeric characters (range replaced by a table)")]
    public void Trim_DocExampleNonAlphanumeric_TrimsAllRows()
    {
        var table = TestUtils.GenerateTableWithColumn<long>([1, 2, 3, 4, 5], "x", "Numbers");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "str",
            "trimmed_str",
            kustoDatabase,
            """
            Numbers
            | project str = strcat("-  ", "Te st", x, @"// $")
            | extend trimmed_str = trim(@"[^\w]+", str)
            """);

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string)>
        {
            ("-  Te st1// $", "Te st1"),
            ("-  Te st2// $", "Te st2"),
            ("-  Te st3// $", "Te st3"),
            ("-  Te st4// $", "Te st4"),
            ("-  Te st5// $", "Te st5")
        }));
    }

    [Test]
    [Description("trim - trimming '1' from numbers 10..15 removes one '1' from each side")]
    public void Trim_OnesFromNumbers_RemovesOneMatchPerSide()
    {
        var table = TestUtils.GenerateTableWithColumn<long>([10, 11, 12, 13, 14, 15], "x", "Numbers");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            "str",
            "trimmed_str",
            kustoDatabase,
            "Numbers | project str = tostring(x) | extend trimmed_str = trim('1', str)");

        Assert.That(actualData, Is.EquivalentTo(new List<(string, string)>
        {
            ("10", "0"),
            ("11", ""),
            ("12", "2"),
            ("13", "3"),
            ("14", "4"),
            ("15", "5")
        }));
    }

    [Test]
    [Description("trim - can be used inside where to find values with surrounding whitespace")]
    public void Trim_InWhere_FindsPaddedValues()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["  a", "b  ", " c ", "d"], "Value", "Values");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "Count",
            kustoDatabase,
            @"Values | where trim(@'\s+', Value) != Value | count");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 3 }));
    }

    [Test]
    [Description("trim - trimmed values can be compared in where")]
    public void Trim_InWhereComparedToLiteral_FiltersRows()
    {
        var table = TestUtils.GenerateTableWithColumn<string>(["  a", "b  ", " c ", "d"], "Value", "Values");

        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(table);

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Value",
            kustoDatabase,
            @"Values | where trim(@'\s+', Value) == 'c'");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { " c " }));
    }

    [Test]
    [Description("trim - applied to the result of strcat")]
    public void Trim_OfStrcat_RemovesBrackets()
    {
        var actual = OpusTestUtils.EvaluateScalar(@"trim(@'[\[\]]', strcat('[', 'x', ']'))");

        OpusTestUtils.AssertTypedValue(actual, "x");
    }

    [Test]
    [Description("trim - alternation removes one leading and one trailing match from a column, combined with distinct")]
    public void Trim_StateColumnWithDistinct_RemovesLeadingAndTrailingMatch()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Trimmed",
            kustoDatabase,
            "StormEvents | project Trimmed = trim('F|A', State) | distinct Trimmed");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "LORID", "TEXAS" }));
    }

    [Test]
    [Description("trim - result of trim can be concatenated")]
    public void Trim_InsideStrcat_Concatenates()
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "Text",
            kustoDatabase,
            "StormEvents | where EventType == 'Flood' | project Text = strcat('<', trim('S', State), '>')");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "<TEXA>" }));
    }

    [TestCase("print r = trim('', 'abc')")]
    [TestCase("print r = trim('1', 121)")]
    [TestCase("StormEvents | extend r = trim(State, EventType)")]
    [Description("trim - empty regex, non-string source and non-literal regex are semantic errors")]
    public void Trim_InvalidArguments_Fails(string query)
    {
        var kustoDatabase = new KustoDatabase();
        kustoDatabase.AddTable(TestUtils.BuildTestTable());

        OpusTestUtils.AssertQueryFails(kustoDatabase, query);
    }
}
