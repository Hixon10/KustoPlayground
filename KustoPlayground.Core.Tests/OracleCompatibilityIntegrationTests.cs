namespace KustoPlayground.Core.Tests;

public class OracleCompatibilityIntegrationTests
{
    [TestCase("let width = -2; datatable(N:long)[] | project K=bin_at(N, width, 0)")]
    [TestCase("let occurrence = 1 - 1; datatable(A:dynamic)[] | project I=array_index_of(A, 'x', 0, -1, occurrence)")]
    public void InvalidConstantArgumentsFailBeforeReadingRows(string query)
    {
        OpusTestUtils.AssertQueryFails(new KustoDatabase(), query);
    }

    [Test]
    public void DatetimeBinUsesFractionalSecondsRatherThanTicks()
    {
        var result = OpusTestUtils.EvaluateScalar("bin(datetime(2024-01-01T12:34:11.87Z), 2.5)");
        Assert.That(result, Is.EqualTo(new DateTime(2024, 1, 1, 12, 34, 10, DateTimeKind.Utc)));
    }

    [Test]
    public void InferredCovarianceNameCanBeUsedDownstreamAndAliasedInArithmetic()
    {
        var row = OpusTestUtils.ExecuteAndGetRows(new KustoDatabase(), """
            datatable(X:long, Y:long)[1, 2, 3, 6]
            | summarize covariance(X,Y), Doubled=2 * covariancep(X,Y)
            | project Sample=covariance_X_Y, Doubled
            """).Single();
        Assert.That((row["Sample"], row["Doubled"]), Is.EqualTo((4d, 4d)));
    }
}
