namespace KustoPlayground.Core.Tests;

public class NeonStrcatAndTrimFunctionTests
{
    #region strcat() Tests

    [Test]
    [Description("strcat() - official doc example concatenating multiple string literals")]
    public void Strcat_OfficialDocBasicConcatenation()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "print str = strcat(\"hello\", \" \", \"world\")",
            columnName: "str");

        Assert.That(actual, Is.EqualTo("hello world"));
    }

    [Test]
    [Description("strcat() - official doc multi-line string with newline escapes")]
    public void Strcat_OfficialDocMultilineEscapeCharacters()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "print MultiLineString = strcat(\"Line 1\\n\", \"Line 2\\n\", \"Line 3\")",
            columnName: "MultiLineString");

        Assert.That(actual, Is.EqualTo("Line 1\nLine 2\nLine 3"));
    }

    [Test]
    [Description("strcat() - single argument returns string representation of that argument")]
    public void Strcat_SingleArgument_StringAndNonString()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print strcat('onlyone')"), Is.EqualTo("onlyone"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print strcat(42)"), Is.EqualTo("42"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print strcat('')"), Is.EqualTo(""));
    }

    [Test]
    [Description("strcat() - empty string arguments concatenate cleanly")]
    public void Strcat_EmptyStrings()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print strcat('', '')"), Is.EqualTo(""));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print strcat('', 'a', '', 'b', '')"), Is.EqualTo("ab"));
    }

    [Test]
    [Description("strcat() - forcibly converts non-string scalar arguments (int, long, real, bool)")]
    public void Strcat_MixedScalarTypes_AutoConvertedToString()
    {
        var db = new KustoDatabase();

        var actualNumeric = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "print strcat('count=', int(7), ', big=', long(3000000000), ', ratio=', real(2.5))");

        Assert.That(actualNumeric, Is.EqualTo("count=7, big=3000000000, ratio=2.5"));

        var actualBool = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "print strcat('ok=', true, ', fail=', false)");

        Assert.That(actualBool, Is.EqualTo("ok=true, fail=false").IgnoreCase);
    }

    [Test]
    [Description("strcat() - null scalar arguments are treated as empty string and do not nullify result")]
    public void Strcat_NullArguments_TreatedAsEmptyString()
    {
        var db = new KustoDatabase();

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print strcat('a', int(null), 'b')"),
            Is.EqualTo("ab"));
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print strcat(long(null), real(null), bool(null), datetime(null))"),
            Is.EqualTo(""));
    }

    [Test]
    [Description("strcat() - table columns combined with literals and nullable columns")]
    public void Strcat_TableColumnsAndNullableValues()
    {
        var db = new KustoDatabase();
        var table = NeonTestUtils.CreateTable(
            "Users",
            [
                new Column<int>("Id", isNullable: false),
                new Column<string>("First", isNullable: false),
                new Column<string>("Middle", isNullable: true),
                new Column<int>("Score", isNullable: true)
            ],
            [
                new() { ["Id"] = 1, ["First"] = "Ada", ["Middle"] = "L", ["Score"] = 99 },
                new() { ["Id"] = 2, ["First"] = "Alan", ["Middle"] = null, ["Score"] = null },
                new() { ["Id"] = 3, ["First"] = "Grace", ["Middle"] = "", ["Score"] = 100 }
            ]);
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            "Id",
            "Summary",
            db,
            "Users | extend Summary = strcat(First, '-', Middle, ':', Score) | sort by Id asc");

        Assert.That(actual, Is.EqualTo(new List<(int, string)>
        {
            (1, "Ada-L:99"),
            (2, "Alan-:"),
            (3, "Grace-:100")
        }));
    }

    [Test]
    [Description("strcat() - used in where filter and nested strcat calls")]
    public void Strcat_InWhereFilterAndNestedCalls()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(string, string)>
            {
                ("Jane", "Doe"),
                ("John", "Smith"),
                ("Ada", "Lovelace")
            },
            columnName1: "First",
            columnName2: "Last",
            tableName: "People");
        db.AddTable(table);

        var matched = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "First",
            db,
            "People | where strcat(First, ' ', Last) == 'Jane Doe' | project First");

        Assert.That(matched, Is.EquivalentTo(new List<string> { "Jane" }));

        var nested = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "print strcat(strcat('a', 'b'), '-', strcat('c', 'd'))");

        Assert.That(nested, Is.EqualTo("ab-cd"));
    }

    [Test]
    [Description("strcat() - maximum supported 64 arguments succeeds")]
    public void Strcat_Maximum64Arguments_Succeeds()
    {
        var db = new KustoDatabase();
        var args = Enumerable.Range(1, 64).Select(i => $"'{i}'");
        var query = $"print strcat({string.Join(", ", args)})";
        var expected = string.Concat(Enumerable.Range(1, 64).Select(i => i.ToString()));

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<string>(db, query);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    [Description("strcat() - unicode and special characters preserved")]
    public void Strcat_UnicodeAndSpecialCharacters()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "print strcat('café', ' -> ', '東京', ' [ok]')");

        Assert.That(actual, Is.EqualTo("café -> 東京 [ok]"));
    }

    [Test]
    [Description("strcat() - 0 arguments or more than 64 arguments returns execution error")]
    public void Strcat_InvalidArgumentCount_ReturnsError()
    {
        var db = new KustoDatabase();

        NeonTestUtils.ExecuteAndExpectError(db, "print strcat()");

        var tooManyArgs = Enumerable.Range(1, 65).Select(i => $"'{i}'");
        NeonTestUtils.ExecuteAndExpectError(db, $"print strcat({string.Join(", ", tooManyArgs)})");
    }

    #endregion

    #region trim() Tests

    [Test]
    [Description("trim() - official doc examples (substring, non-word chars, whitespace)")]
    public void Trim_OfficialDocExamples()
    {
        var db = new KustoDatabase();

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, @"print trim('--', @'--https://bing.com--')"),
            Is.EqualTo("https://bing.com"));

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, @"print trim(@'[^\w]+', '-  Te st1// $')"),
            Is.EqualTo("Te st1"));

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, @"print trim(@'\s+', @'    Hello, world!    ')"),
            Is.EqualTo("Hello, world!"));
    }

    [Test]
    [Description("trim() - official let-doc examples trimming digit '1'")]
    public void Trim_OfficialDigitExamples()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('1', '10')"), Is.EqualTo("0"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('1', '11')"), Is.EqualTo(""));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('1', '12')"), Is.EqualTo("2"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('1', '21')"), Is.EqualTo("2"));
    }

    [Test]
    [Description("trim() - trims leading-only, trailing-only, and preserves interior matches")]
    public void Trim_LeadingOnlyTrailingOnlyAndPreservesInterior()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "--only-leading"),
                (2, "only-trailing--"),
                (3, "--keep--middle--"),
                (4, "no--edges--here")
            },
            columnName1: "Id",
            columnName2: "Text",
            tableName: "Strings");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            "Id",
            "Trimmed",
            db,
            "Strings | extend Trimmed = trim('--', Text) | sort by Id asc");

        Assert.That(actual, Is.EqualTo(new List<(int, string)>
        {
            (1, "only-leading"),
            (2, "only-trailing"),
            (3, "keep--middle"),
            (4, "no--edges--here")
        }));
    }

    [Test]
    [Description("trim() - quantified regex patterns strip repeated prefixes and suffixes")]
    public void Trim_QuantifiedRegexPatterns()
    {
        var db = new KustoDatabase();

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('-+', '----hello----')"),
            Is.EqualTo("hello"));
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('(ab)+', 'ababhelloabab')"),
            Is.EqualTo("hello"));
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('[0-9]+', '12345abc67890')"),
            Is.EqualTo("abc"));
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, @"print trim(@'[\s,;/]+', ' ,;/ hello world ;/, ')"),
            Is.EqualTo("hello world"));
    }

    [Test]
    [Description("trim() - regex metacharacters, alternation, case sensitivity, and (?i) flag")]
    public void Trim_RegexMetacharactersAlternationAndCaseSensitivity()
    {
        var db = new KustoDatabase();

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, @"print trim(@'\.+', '...domain.com...')"),
            Is.EqualTo("domain.com"));

        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('(foo|bar)', 'fooMIDDLEbar')"),
            Is.EqualTo("MIDDLE"));

        // Case-sensitive by default
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('abc', 'ABChelloABC')"),
            Is.EqualTo("ABChelloABC"));

        // Case-insensitive with inline flag (?i)
        Assert.That(
            NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('(?i)abc', 'ABChelloabc')"),
            Is.EqualTo("hello"));
    }

    [Test]
    [Description("trim() - edge cases: empty source, empty regex, no match, and entire string matched")]
    public void Trim_EdgeCases_EmptySourceEmptyRegexAndFullMatch()
    {
        var db = new KustoDatabase();

        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, @"print trim(@'\s+', '')"), Is.EqualTo(""));
        NeonTestUtils.ExecuteAndExpectError(db, "print trim('', 'hello')");
        NeonTestUtils.ExecuteAndExpectError(db, "print trim('', '')");
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('xyz', 'hello')"), Is.EqualTo("hello"));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, @"print trim(@'\s+', '     ')"), Is.EqualTo(""));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('a+', 'aaaaa')"), Is.EqualTo(""));
        Assert.That(NeonTestUtils.ExecuteAndGetSingleValue<string>(db, "print trim('x', 'x')"), Is.EqualTo(""));
    }

    [Test]
    [Description("trim() - handles tabs, newlines, and carriage returns with whitespace regex")]
    public void Trim_WhitespaceTabsAndNewlines()
    {
        var db = new KustoDatabase();

        var actual = NeonTestUtils.ExecuteAndGetSingleValue<string>(
            db,
            "print trim(\"\\\\s+\", \"\\t\\r\\n  hello\\nworld  \\r\\n\\t\")");

        Assert.That(actual, Is.EqualTo("hello\nworld"));
    }

    [Test]
    [Description("trim() - works with let constant pattern, where filtering, and strcat() composition")]
    public void Trim_LetConstantPatternWhereFilterAndStrcatCombination()
    {
        var db = new KustoDatabase();
        var table = TestUtils.GenerateTableWith2Columns(
            new List<(int, string)>
            {
                (1, "---alpha---"),
                (2, "  --beta--  "),
                (3, "--gamma--")
            },
            columnName1: "Id",
            columnName2: "Raw",
            tableName: "Items");
        db.AddTable(table);

        var actual = TestUtils.ExecuteAndGetDataFor2Columns<int, string>(
            "Id",
            "Labeled",
            db,
            """
            let pat = @'[-\s]+';
            Items
            | extend Clean = trim(pat, Raw)
            | extend Labeled = strcat('[', Clean, ']')
            | sort by Id asc
            """);

        Assert.That(actual, Is.EqualTo(new List<(int, string)>
        {
            (1, "[alpha]"),
            (2, "[beta]"),
            (3, "[gamma]")
        }));

        var filtered = TestUtils.ExecuteAndGetDataForOneColumn<int>(
            "Id",
            db,
            @"Items | where trim(@'[-\s]+', Raw) == 'beta' | project Id");

        Assert.That(filtered, Is.EquivalentTo(new List<int> { 2 }));
    }

    [Test]
    [Description("trim() - invalid argument count or invalid regex pattern returns execution error")]
    public void Trim_InvalidArguments_ReturnsError()
    {
        var db = new KustoDatabase();

        NeonTestUtils.ExecuteAndExpectError(db, "print trim()");
        NeonTestUtils.ExecuteAndExpectError(db, "print trim('a')");
        NeonTestUtils.ExecuteAndExpectError(db, "print trim('a', 'aba', 'extra')");
        NeonTestUtils.ExecuteAndExpectError(db, "print trim('[unclosed', 'abc')");
    }

    #endregion
}
