namespace KustoPlayground.Core.Tests;

public class KustoExecutorTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void ExecuteTest()
    {
        KustoExecutor kustoExecutor = new KustoExecutor();
        string query = @"StormEvents
            | where State == 'FLORIDA' and DamageProperty > 10000
            | project StartTime, EventType, DamageProperty
            | take 10
        ";
        List<string> results = kustoExecutor.Execute(query);
        List<string> expected =
        [
            "StartTime=8/23/2025 6:20:00 AM, EventType=Hurricane, DamageProperty=20000"
        ];
        Assert.That(results, Is.EqualTo(expected)); 
    }
}