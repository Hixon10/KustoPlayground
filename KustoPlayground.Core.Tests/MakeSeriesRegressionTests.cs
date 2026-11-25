namespace KustoPlayground.Core.Tests;

public class MakeSeriesRegressionTests
{
    [Test]
    public void ExplicitAliasesAndAxisAreReservedBeforeGeneratedNames()
    {
        var row = SummarizeOperatorOpusTestUtils.ExecuteAndGetSingleRow(new KustoDatabase(), """
            datatable(X:long)[2,4,4]
            | make-series take_any(X), X=count(), X1=sum(X) on X step 2
            | project Chosen=X2, Counts=X3, Sums=X1, Axis=X
            """);
        AstraTestUtils.AssertJsonValue(row["Chosen"], "[2,4]");
        AstraTestUtils.AssertJsonValue(row["Counts"], "[1,2]");
        AstraTestUtils.AssertJsonValue(row["Sums"], "[2,8]");
        AstraTestUtils.AssertJsonValue(row["Axis"], "[2,4]");
    }

    [TestCase("let size=1; datatable(X:long,size:long)[] | make-series N=count() on X step size | getschema")]
    [TestCase("datatable(X:long)[] | make-series N=count() on X step 0 | getschema")]
    [TestCase("datatable(X:long)[] | make-series N=percentiles(X,50,90) on X step 1 | getschema")]
    public void SchemaOnlyQueriesStillValidateTheSeries(string query) =>
        AstraTestUtils.AssertRejected(new KustoDatabase(), query);
}
