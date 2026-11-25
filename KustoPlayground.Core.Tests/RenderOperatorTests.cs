namespace KustoPlayground.Core.Tests;

public class RenderOperatorTests
{
    [TestCase("let T=datatable(X:long,Y:real)[2,0.5, 1,real(null)]; T | sort by X asc", "linechart")]
    [TestCase("print T=datetime(2026-01-01), Y=long(7) | take 0", "timechart")]
    [TestCase("print X=1, Bag=dynamic({'Y':7}) | evaluate bag_unpack(Bag)", "linechart")]
    public void RenderAnnotatesTheSameRowsAndOrderedSchema(string query, string kind)
    {
        var db = new KustoDatabase();
        var plain = db.ExecuteQuery(query, includeSchema: true);
        var chart = db.ExecuteQuery($"{query} | render {kind}", includeSchema: true);
        Assert.That(plain.ExecutionErrors, Is.Null);
        Assert.That(chart.ExecutionErrors, Is.Null);
        Assert.That(plain.Visualization, Is.Null);
        Assert.That(chart.Visualization, Is.EqualTo(kind));
        Assert.That(chart.ResultRows, Is.EqualTo(plain.ResultRows));
        Assert.That(chart.Columns, Is.EqualTo(plain.Columns));
    }

    [Test]
    public void RenderUsesSyntaxAndDoesNotRequireASchemaRequest()
    {
        var result = new KustoDatabase().ExecuteQuery(
            "(print Note='| render piechart', Y=1 | render linechart); // | render barchart");
        Assert.That(result.ExecutionErrors, Is.Null);
        Assert.That(result.Visualization, Is.EqualTo("linechart"));
        Assert.That(result.Columns, Is.Null);
        Assert.That(result.ResultRows!.Single()["Note"], Is.EqualTo("| render piechart"));
    }

    [TestCase("print X=1 | render piechart", "piechart")]
    [TestCase("print X=1 | render linechart with (title='Example')", "options")]
    [TestCase("print X=1 | render linechart | take 0", "final")]
    [TestCase("let T=print X=1 | render timechart; print X=2", "final")]
    public void UnsupportedRenderDoesNotSilentlyReturnRows(string query, string reason)
    {
        var result = new KustoDatabase().ExecuteQuery(query);
        Assert.That(result.ExecutionErrors!.Select(e => e.Description), Has.Some.Contain(reason).IgnoreCase);
        Assert.That(result.ResultRows, Is.Null);
        Assert.That(result.Visualization, Is.Null);
    }
}
