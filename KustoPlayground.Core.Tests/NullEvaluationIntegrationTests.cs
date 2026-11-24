namespace KustoPlayground.Core.Tests;

public class NullEvaluationIntegrationTests
{
    [TestCase("coalesce(int(null), int(7), real(9))")]
    [TestCase("iff(bool(null), real(9), int(7))")]
    [TestCase("case(bool(null), real(9), true, int(7), int(0))")]
    public void SelectedBranchUsesTheBoundResultType(string expression)
    {
        OpusTestUtils.AssertTypedValue(OpusTestUtils.EvaluateScalar(expression), 7d);
    }

    [Test]
    public void MembershipCanConsumeANullableAggregate()
    {
        var result = OpusTestUtils.ExecuteAndGetSingleValue("R", new KustoDatabase(),
            "print X=long(null) | summarize R=min(X) in (1, long(null))");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void QueryClockIsSharedWithLetFunctions()
    {
        var result = OpusTestUtils.ExecuteAndGetSingleValue("R", new KustoDatabase(),
            "let clock = () { ago(0s) }; print R=now() - clock()");
        Assert.That(result, Is.EqualTo(TimeSpan.Zero));
    }
}
