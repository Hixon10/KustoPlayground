using System.Text.Json;
using System.Text.Json.Nodes;

namespace KustoPlayground.Core.Tests;

internal static class AstraTestUtils
{
    internal static T Print<T>(string expression)
    {
        var values = TestUtils.ExecuteAndGetDataForOneColumn<T>(
            "Value", new KustoDatabase(), $"print Value = {expression}");
        Assert.That(values, Has.Count.EqualTo(1), expression);
        return values[0];
    }

    internal static void AssertJsonValue(object? actual, string expectedJson)
    {
        var actualJson = JsonSerializer.SerializeToNode(actual);
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), actualJson), Is.True,
            $"Expected {expectedJson}, got {actualJson?.ToJsonString() ?? "null"}");
    }

    internal static void AssertRejected(KustoDatabase database, string query)
    {
        var result = database.ExecuteQuery(query);
        Assert.That(result.ExecutionErrors, Is.Not.Null.And.Not.Empty, query);
        Assert.That(result.ResultRows, Is.Null, query);
    }
}
