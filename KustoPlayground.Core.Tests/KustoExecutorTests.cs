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
        List<IReadOnlyDictionary<string, object?>> results = kustoExecutor.Execute(query);
        List<IReadOnlyDictionary<string, object?>> expected =
        [
            new Dictionary<string, object?>
            {
                { "DamageProperty", 20000 },
                { "StartTime", new DateTime(2025, 8, 23, 6, 20, 0) },
                { "EventType", "Hurricane" },
            }.AsReadOnly(),
        ];
        
        Assert.That(results, Has.Count.EqualTo(expected.Count));
        for (var index = 0; index < results.Count; index++)
        {
            Assert.That(results[index], Is.EquivalentTo(expected[index]));
        }
    }
}