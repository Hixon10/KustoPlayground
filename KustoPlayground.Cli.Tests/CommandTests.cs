using System.Text;
using System.Text.Json;

namespace KustoPlayground.Cli.Tests;

public class CommandTests
{
    private static (int Exit, string Output, string Error) Run(params string[] args)
    {
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        int exit = Command.Run(args, output, error);
        return (exit, Encoding.UTF8.GetString(output.ToArray()), Encoding.UTF8.GetString(error.ToArray()));
    }

    private static JsonElement Items(params string[] args)
    {
        var (exit, output, error) = Run(args);
        Assert.That(exit, Is.Zero, error);
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.That(root.GetProperty("status").GetInt32(), Is.EqualTo(200));
        Assert.That(root.GetProperty("duration").GetInt64(), Is.GreaterThanOrEqualTo(0));
        return root.GetProperty("results").GetProperty("items").Clone();
    }

    [TestCase("-h")]
    [TestCase("--help")]
    [TestCase("-help")]
    public void HelpExplainsBothInputFormats(string command)
    {
        var (exit, output, error) = Run(command);
        Assert.That(exit, Is.Zero);
        Assert.That(error, Is.Empty);
        Assert.That(output, Does.Contain("--query").And.Contain("--json").And.Contain("--csv").And.Contain("Input"));
    }

    [TestCase("-e")]
    [TestCase("--execute")]
    [TestCase("-execute")]
    public void StandaloneQueryReturnsSchemaAndOrderedTypedCells(string command)
    {
        var items = Items(command, "--query", "let x=40; print Z=x+2, A=long(null), Obj=dynamic({'tags':['a','b']});");
        Assert.That(items[0].EnumerateObject().Select(p => (p.Name, p.Value.GetString())),
            Is.EqualTo(new[] { ("Z", "long"), ("A", "long"), ("Obj", "dynamic") }));
        Assert.That(items[1][0].GetInt64(), Is.EqualTo(42));
        Assert.That(items[1][1].ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(items[1][2].GetProperty("tags")[1].GetString(), Is.EqualTo("b"));
    }

    [Test]
    public void EmptyImportedTableResultKeepsItsSchema()
    {
        const string input = """{"Name":"Scores","Columns":[{"Name":"N","Type":"int"}],"Rows":[{"N":3}]}""";
        var items = Items("-e", "--query", "Scores | where N > 5 | project Value=N", "--json", input);
        Assert.That(items.GetArrayLength(), Is.EqualTo(1));
        Assert.That(items[0].GetProperty("Value").GetString(), Is.EqualTo("int"));
    }

    [Test]
    public void SchemaDistinguishesTheSystemOrdinalFromAnOrdinaryLong()
    {
        var items = Items("-e", "--query",
            "let S=print X=1 | getschema; S | project N=ColumnOrdinal, ColumnOrdinal=long(42) | take 0");
        Assert.That(items[0].EnumerateObject().Select(p => (p.Name, p.Value.GetString())),
            Is.EqualTo(new[] { ("N", "int"), ("ColumnOrdinal", "long") }));
    }

    [TestCase("let F=(){B}; F()")]
    [TestCase("let F=(T:(*)){T}; F(B)")]
    [TestCase("let F=(T:(*)){T}; B | invoke F()")]
    public void InferredBagColumnsSurviveFunctionBoundaries(string invocation)
    {
        const string input = "let B=print P=dynamic({'tag':'blue','x':2}) | evaluate bag_unpack(P); ";
        var items = Items("-e", "--query", input + invocation);
        Assert.That(items[0].EnumerateObject().Select(p => (p.Name, p.Value.GetString())),
            Is.EqualTo(new[] { ("tag", "string"), ("x", "long") }));
        Assert.That(items[1][0].GetString(), Is.EqualTo("blue"));
        Assert.That(items[1][1].GetInt64(), Is.EqualTo(2));
    }

    [Test]
    public void CsvPreservesQuotedCommasQuotesAndMultilineFields()
    {
        const string csv = "City,Note,Score\r\n\"London, UK\",\"a\"\"b\r\nc\",12\r\nOslo,none,3";
        var items = Items("-e", "--csv", csv, "--query", "Input | where Score > 10 | project City, Note");
        Assert.That(items.GetArrayLength(), Is.EqualTo(2));
        Assert.That(items[1][0].GetString(), Is.EqualTo("London, UK"));
        Assert.That(items[1][1].GetString()!.ReplaceLineEndings("\n"), Is.EqualTo("a\"b\nc"));
    }

    [TestCase(new[] { "-e" }, "--query")]
    [TestCase(new[] { "-e", "--query", " " }, "--query")]
    [TestCase(new[] { "--help", "--execute" }, "--help")]
    [TestCase(new[] { "-e", "--unknown", "x" }, "--unknown")]
    [TestCase(new[] { "-e", "--query", "print 1", "--query", "print 2" }, "Duplicate")]
    [TestCase(new[] { "-e", "--query", "--csv", "x" }, "--query")]
    [TestCase(new[] { "-e", "--json", "{}", "--csv", "N\n2", "--query", "print 1" }, "mutually exclusive")]
    [TestCase(new[] { "-e", "--json", "{", "--query", "print 1" }, "Invalid table")]
    [TestCase(new[] { "-e", "--csv", "x,y\n1", "--query", "print 1" }, "fields")]
    [TestCase(new[] { "-e", "--csv", "x,x\n1,2", "--query", "print 1" }, "column names")]
    public void InvalidCommandsAndTableInputsProduceNoResult(string[] args, string message)
    {
        var (exit, output, error) = Run(args);
        Assert.That(exit, Is.EqualTo(2));
        Assert.That(output, Is.Empty);
        using var document = JsonDocument.Parse(error);
        Assert.That(document.RootElement.GetProperty("message").GetString(), Does.Contain(message));
    }

    [Test]
    public void QueryErrorsAreDifferentFromInvalidArguments()
    {
        var (exit, output, error) = Run("-e", "--query", "MissingTable | count");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(output, Is.Empty);
        using var document = JsonDocument.Parse(error);
        Assert.That(document.RootElement.GetProperty("message").GetString(), Does.Contain("MissingTable"));
    }
}
