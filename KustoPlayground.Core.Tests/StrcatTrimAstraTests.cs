namespace KustoPlayground.Core.Tests;

public class StrcatTrimAstraTests
{
    [TestCase("strcat('only')", "only")]
    [TestCase("strcat('', '')", "")]
    [TestCase("strcat('left', 'right')", "leftright")]
    [TestCase("strcat('a', '', ' ', 'b', 'c')", "a bc")]
    [TestCase("strcat('x', -12, ':', 0.25)", "x-12:0.25")]
    [TestCase("strcat(long(null), 'x', dynamic(null))", "x")]
    [TestCase("strcat(long(null), real(null))", "")]
    [TestCase("strcat('a', strcat('b', 'c'), 'd')", "abcd")]
    public void Strcat_PreservesArgumentOrderWithoutASeparator(string expression, string expected)
    {
        Assert.That(AstraTestUtils.Print<string>(expression), Is.EqualTo(expected));
    }

    [Test]
    public void Strcat_Accepts64ArgumentsButNot65()
    {
        var arguments = string.Join(", ", Enumerable.Repeat("'x'", 64));
        Assert.That(AstraTestUtils.Print<string>($"strcat({arguments})"), Is.EqualTo(new string('x', 64)));
        AstraTestUtils.AssertRejected(new KustoDatabase(), $"print Result = strcat({arguments}, 'x')");
    }

    [Test]
    public void Strcat_CombinesColumnsWithoutNormalizingText()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWith2Columns<string, long>(
            [("e\u0301", 7), ("", 0), ("a\nb", -2)], "Label", "Id", "Labels"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<long, string>("Id", "Text", database,
            "Labels | extend Text = strcat(Label, ':', Id, ':', Label)");

        Assert.That(rows, Is.EquivalentTo(new[] { (7L, "e\u0301:7:e\u0301"), (0L, ":0:"), (-2L, "a\nb:-2:a\nb") }));
    }

    [TestCase(@"\s+", "\t\u03b1  \u03b2\r\n", "\u03b1  \u03b2")]
    [TestCase(@"\s+", "", "")]
    [TestCase(@"\s+", "\t  ", "")]
    [TestCase(@"\s+", "  left", "left")]
    [TestCase(@"\s+", "right  ", "right")]
    [TestCase(@"[0-9]+", "123a45b67", "a45b")]
    [TestCase(@"[0-9]+", "a12b", "a12b")]
    [TestCase(@"(?:ab)+", "ababbaab", "ba")]
    [TestCase("ab", "ababXabab", "abXab")]
    [TestCase(@"\.+", "..a.b...", "a.b")]
    public void Trim_MatchesRegexOnlyAtTheTwoEnds(string pattern, string source, string expected)
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(new[] { source }, "Text", "Labels"));

        var values = TestUtils.ExecuteAndGetDataForOneColumn<string>("Trimmed", database,
            $"Labels | extend Trimmed = trim(@'{pattern}', Text)");

        Assert.That(values, Is.EqualTo(new[] { expected }));
    }

    [TestCase("strcat('a', 'b')", "ab", "strcat()")]
    [TestCase("trim('x', 'xyx')", "y", "trim('x')")]
    public void StringFunctions_RejectMissingArguments(string valid, string expected, string missingArgument)
    {
        Assert.That(AstraTestUtils.Print<string>(valid), Is.EqualTo(expected));
        AstraTestUtils.AssertRejected(new KustoDatabase(), $"print Value = {missingArgument}");
    }
}
