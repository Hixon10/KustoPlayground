using System.Text;
using System.Text.Json;

namespace KustoPlayground.Cli.Tests;

public class CommandTests
{
    private static (int Exit, string Output, string Error) Run(params string[] args)
    {
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        int exit = Command.RunAsync(args, Stream.Null, output, error).GetAwaiter().GetResult();
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
        Assert.That(output, Does.Contain("--query").And.Contain("--json-file").And.Contain("--csv-file").And.Contain("Input"));
    }

    [Test]
    public void NoArgumentsBehaveExactlyLikeHelp() => Assert.That(Run(), Is.EqualTo(Run("--help")));

    [Test]
    public void StandaloneQueryReturnsSchemaAndOrderedTypedCells()
    {
        var items = Items("--query", "let x=40; print Z=x+2, A=long(null), Obj=dynamic({'tags':['a','b']});");
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
        var items = Items("--query", "Scores | where N > 5 | project Value=N", "--json", input);
        Assert.That(items.GetArrayLength(), Is.EqualTo(1));
        Assert.That(items[0].GetProperty("Value").GetString(), Is.EqualTo("int"));
    }

    [Test]
    public void SchemaDistinguishesTheSystemOrdinalFromAnOrdinaryLong()
    {
        var items = Items("--query",
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
        var items = Items("--query", input + invocation);
        Assert.That(items[0].EnumerateObject().Select(p => (p.Name, p.Value.GetString())),
            Is.EqualTo(new[] { ("tag", "string"), ("x", "long") }));
        Assert.That(items[1][0].GetString(), Is.EqualTo("blue"));
        Assert.That(items[1][1].GetInt64(), Is.EqualTo(2));
    }

    [Test]
    public void CsvPreservesQuotedCommasQuotesAndMultilineFields()
    {
        const string csv = "City,Note,Score\r\n\"London, UK\",\"a\"\"b\r\nc\",12\r\nOslo,none,3";
        var items = Items("--csv", csv, "--query", "Input | where Score > 10 | project City, Note");
        Assert.That(items.GetArrayLength(), Is.EqualTo(2));
        Assert.That(items[1][0].GetString(), Is.EqualTo("London, UK"));
        Assert.That(items[1][1].GetString()!.ReplaceLineEndings("\n"), Is.EqualTo("a\"b\nc"));
    }

    [TestCase("--json-file", """{"Name":"Scores","Columns":[{"Name":"N","Type":"int"}],"Rows":[{"N":3}]}""", "Scores")]
    [TestCase("--csv-file", "N\r\n3", "Input")]
    [TestCase("--json-path", """{"Name":"Scores","Columns":[{"Name":"N","Type":"int"}],"Rows":[{"N":3}]}""", "Scores")]
    [TestCase("--csv-path", "N\r\n3", "Input")]
    public void FileInputsProduceTheSameTypedResultAsInlineInputs(string option, string data, string table)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cli input {Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(path, data, Encoding.UTF8);
            var query = $"{table} | project Twice=N*2";
            var file = Items(option, path, "--query", query);
            Assert.That(file.GetRawText(), Is.EqualTo(Items("--query", query, option[..^5], data).GetRawText()));
        }
        finally { File.Delete(path); }
    }

    [TestCase("--json-file")]
    [TestCase("--csv-file")]
    [TestCase("--json-path")]
    [TestCase("--csv-path")]
    public void MissingFileIsAnInputError(string option)
    {
        var name = Guid.NewGuid().ToString("N");
        var (exit, output, error) = Run(option, Path.Combine(Path.GetTempPath(), name), "--query", "print 1");
        Assert.That(exit, Is.EqualTo(2));
        Assert.That(output, Is.Empty);
        using var document = JsonDocument.Parse(error);
        Assert.That(document.RootElement.GetProperty("message").GetString(), Does.Contain(name));
    }

    [Test]
    public void RepeatedMixedInputsCanAllParticipateInOneQuery()
    {
        const string product = """{"Name":"Products","Columns":[{"Name":"Sku","Type":"string"}],"Rows":[{"Sku":"tea"}]}""";
        const string labels = """{"Name":"Labels","Columns":[{"Name":"Sku","Type":"string"},{"Name":"Label","Type":"string"}],"Rows":[{"Sku":"tea","Label":"Green tea"}]}""";
        var items = Items("--json", product, "--csv", "Sku,Price\ntea,7", "--json", labels, "--csv", "Sku,Units\ntea,3",
            "--query", "Products | join kind=inner (Input) on Sku | join kind=inner (Labels) on Sku | join kind=inner (Input2) on Sku | project Label, Total=Price*Units");
        Assert.That(items.GetArrayLength(), Is.EqualTo(2));
        Assert.That(items[1][0].GetString(), Is.EqualTo("Green tea"));
        Assert.That(items[1][1].GetInt64(), Is.EqualTo(21));
    }

    [Test]
    public void RepeatedFileInputsKeepJsonNamesAndNumberOnlyTheCsvInputs()
    {
        var dir = Directory.CreateTempSubdirectory("kusto-cli-tables-");
        var first = Path.Combine(dir.FullName, "one.json");
        var second = Path.Combine(dir.FullName, "two.json");
        var csv = Path.Combine(dir.FullName, "values.csv");
        try
        {
            File.WriteAllText(first, """{"Name":"First","Columns":[{"Name":"N","Type":"int"}],"Rows":[{"N":1}]}""");
            File.WriteAllText(second, """{"Name":"Second","Columns":[{"Name":"N","Type":"int"}],"Rows":[{"N":2}]}""");
            File.WriteAllText(csv, "N\n3");
            var items = Items("--json-path", first, "--csv-path", csv, "--json-path", second, "--csv-path", csv,
                "--csv", "N\n4", "--query", "union First, Input, Second, Input2, Input3 | summarize Total=sum(N)");
            Assert.That(items[1][0].GetInt64(), Is.EqualTo(13));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            File.Delete(csv);
            dir.Delete();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DuplicateNamesNeverOverwriteTables(bool jsonFirst)
    {
        const string data = """{"Name":"Input","Columns":[{"Name":"N","Type":"int"}],"Rows":[{"N":9}]}""";
        string[] sources = jsonFirst ? ["--json", data, "--csv", "N\n2"] : ["--csv", "N\n2", "--json", data];
        var (exit, output, error) = Run(["--mcp-server", .. sources]);
        Assert.That(exit, Is.EqualTo(2));
        Assert.That(output, Is.Empty);
        Assert.That(error, Does.Contain("Duplicate table name: Input"));
    }

    [TestCase(new[] { "-e", "--query", "print 1" }, "Unknown option: -e")]
    [TestCase(new[] { "--execute", "--query", "print 1" }, "Unknown option: --execute")]
    [TestCase(new[] { "-execute", "--query", "print 1" }, "Unknown option: -execute")]
    [TestCase(new[] { "--query", " " }, "--query")]
    [TestCase(new[] { "--help", "--execute" }, "--help")]
    [TestCase(new[] { "--unknown", "x" }, "--unknown")]
    [TestCase(new[] { "--query", "print 1", "--query", "print 2" }, "Duplicate")]
    [TestCase(new[] { "--query", "--csv", "x" }, "--query")]
    [TestCase(new[] { "--mcp-server", "--query", "print 1", "--json-path", "missing.json" }, "exactly one")]
    [TestCase(new[] { "--query", "print 1", "--mcp-server" }, "exactly one")]
    [TestCase(new[] { "--csv-path", "missing.csv" }, "exactly one")]
    [TestCase(new[] { "--mcp-server", "--mcp-server" }, "Duplicate")]
    [TestCase(new[] { "--csv-path", "--mcp-server" }, "Missing value for --csv-path")]
    [TestCase(new[] { "--json-file", "--query", "print 1" }, "Missing value for --json-file")]
    [TestCase(new[] { "--json", "{", "--query", "print 1" }, "Invalid table")]
    [TestCase(new[] { "--csv", "x,y\n1", "--query", "print 1" }, "fields")]
    [TestCase(new[] { "--csv", "x,x\n1,2", "--query", "print 1" }, "column names")]
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
        var (exit, output, error) = Run("--query", "MissingTable | count");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(output, Is.Empty);
        using var document = JsonDocument.Parse(error);
        Assert.That(document.RootElement.GetProperty("message").GetString(), Does.Contain("MissingTable"));
    }
}
