namespace KustoPlayground.Core.Tests;

public class QueryCompositionTests
{
    [Test]
    public void WildcardViewUnionBindsColumnsEvenWithoutStoredTables()
    {
        var result = TestUtils.ExecuteAndGetDataFor2Columns<string, string>("Kind", "Text", new KustoDatabase(),
            """
            let ['Log event'] = view () { print Kind = 'event' };
            let LogMessage = view () { print Text = 'detail' };
            let LogFunction = () { print Kind = 'not a view' };
            union Log* | project Kind, Text
            """);
        Assert.That(result, Is.EquivalentTo(new[] { ("event", ""), ("", "detail") }));
    }

    [Test]
    public void ScalarAndTabularLetValuesCanFilterParsedValues()
    {
        var ids = TestUtils.ExecuteAndGetDataForOneColumn<long>("N", new KustoDatabase(),
            """
            let Allowed = parse_json('[1,[2]]');
            let Selected = print Id=2;
            print Text='02', N=2
            | where tolong(Text) in (Allowed) and N in (Selected)
            """);
        Assert.That(ids, Is.EqualTo(new long[] { 2 }));
    }

    [Test]
    public void UnnamedExtendUsesTheColumnReportedByGetSchema()
    {
        const string query = "print N=1 | extend strcat('id:', N)";
        var db = new KustoDatabase();
        var schema = TestUtils.ExecuteAndGetDataForOneColumn<string>("ColumnName", db, query + " | getschema");
        Assert.That(schema, Has.Count.EqualTo(2));
        var value = TestUtils.ExecuteAndGetDataForOneColumn<string>(schema[1], db, query);
        Assert.That(value, Is.EqualTo(new[] { "id:1" }));
    }
}
