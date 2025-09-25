namespace KustoPlayground.Core.Tests;

public class PrintOperatorTests
{
    [Test]
    [Description("print operator - single integer")]
    public void PrintOperator_SingleInteger()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<long>(
            "x",
            kustoDatabase,
            "print x=42");

        Assert.That(actualData, Is.EquivalentTo(new List<long> { 42 }));
    }

    [Test]
    [Description("print operator - single string")]
    public void PrintOperator_SingleString()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "msg",
            kustoDatabase,
            "print msg='hello world'");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "hello world" }));
    }

    [Test]
    [Description("print operator - multiple columns")]
    public void PrintOperator_MultipleColumns()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<long, string>(
            columnName1: "a",
            columnName2: "b",
            kustoDatabase,
            "print a=1, b='test'");

        var expected = new List<(long, string)>
        {
            (1, "test")
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("print operator - evaluates expression")]
    public void PrintOperator_ExpressionEvaluation()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(
            "sum",
            kustoDatabase,
            "print sum=2+3");

        Assert.That(actualData, Is.EquivalentTo(new List<double> { 5 }));
    }

    [Test]
    [Description("print operator - default column name and alias")]
    public void PrintOperator_DefaultColumnNameAndAlias()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<double, string>(
            columnName1: "print_0",
            columnName2: "x",
            kustoDatabase,
            "print 0+1+2+3+4+5, x='Wow!'");

        var expected = new List<(double, string)>
        {
            (15, "Wow!")
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("print operator - trailing semicolon is allowed")]
    public void PrintOperator_WithTrailingSemicolon()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<double>(
            "print_0",
            kustoDatabase,
            "print 1+2+3;");

        Assert.That(actualData, Is.EquivalentTo(new List<double> { 6 }));
    }

    [Test]
    [Description("print operator - string literal and base64_encode_tostring")]
    public void PrintOperator_StringAndBase64Encode()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            columnName1: "print_0",
            columnName2: "print_1",
            kustoDatabase,
            "print \"hello\", base64_encode_tostring(\"hello\")");

        var expected = new List<(string, string)>
        {
            ("hello", "aGVsbG8=")
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("print operator - string literal and base64_encode_tostring - 2")]
    public void PrintOperator_StringAndBase64Encode2()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataFor2Columns<string, string>(
            columnName1: "msg",
            columnName2: "encoded",
            kustoDatabase,
            "print msg='hello', encoded=base64_encode_tostring('hello')");

        var expected = new List<(string, string)>
        {
            ("hello", "aGVsbG8=")
        };

        Assert.That(actualData, Is.EquivalentTo(expected));
    }

    [Test]
    [Description("print operator - base64_encode_tostring without alias")]
    public void PrintOperator_Base64Encode_Anonymous()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "print_0",
            kustoDatabase,
            "print base64_encode_tostring('hello')");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "aGVsbG8=" }));
    }

    [Test]
    [Description("print operator - base64_encode_tostring without alias with ;")]
    public void PrintOperator_Base64Encode_Anonymous2()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "print_0",
            kustoDatabase,
            "print base64_encode_tostring('hello');");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "aGVsbG8=" }));
    }

    [Test]
    [Description("print operator - base64_encode_tostring without alias - 3")]
    public void PrintOperator_Base64Encode_Anonymous3()
    {
        var kustoDatabase = new KustoDatabase();

        var actualData = TestUtils.ExecuteAndGetDataForOneColumn<string>(
            "print_0",
            kustoDatabase,
            "print base64_encode_tostring(\"hello\")");

        Assert.That(actualData, Is.EquivalentTo(new List<string> { "aGVsbG8=" }));
    }
}