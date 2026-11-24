namespace KustoPlayground.Core.Tests;

public class DynamicSchemaIntegrationTests
{
    private const string Unpack = """
        let U = print Id=1, P=dynamic({"tag":"blue","n":7,"unused":false})
            | evaluate bag_unpack(P);
        U | project-away un* | extend Label=tag
        """;

    [Test]
    public void ProjectAwayKeepsOtherDiscoveredSlots()
    {
        var row = OpusTestUtils.ExecuteAndGetRows(new KustoDatabase(), Unpack).Single();
        Assert.That(row.Keys, Is.EquivalentTo(new[] { "Id", "tag", "n", "Label" }));
        Assert.That((row["n"], row["Label"]), Is.EqualTo((7L, "blue")));
    }

    [Test]
    public void EmptyProjectionRetainsTypesOfAliasedUnpackedSlots()
    {
        var columns = TestUtils.ExecuteAndGetDataFor2Columns<string, string>("ColumnName", "ColumnType",
            new KustoDatabase(), Unpack + " | take 0 | project Number=n, Label | getschema");
        Assert.That(columns, Is.EqualTo(new[] { ("Number", "long"), ("Label", "string") }));
    }
}
