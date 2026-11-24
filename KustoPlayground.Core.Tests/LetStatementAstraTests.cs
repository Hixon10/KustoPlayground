namespace KustoPlayground.Core.Tests;

public class LetStatementAstraTests
{
    [Test]
    public void Let_ChainedScalarsAllowEscapedNamesAndSemicolonsInsideStrings()
    {
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<long, string>("N", "Label", new KustoDatabase(),
            """
            let ['base number'] = 3; let n = ['base number'] + 4;
            let label = 'a;b';
            print N = n, Label = label
            """);

        Assert.That(rows, Is.EqualTo(new[] { (7L, "a;b") }));
    }

    [Test]
    public void Let_RowColumnWinsOverOuterScalarOfTheSameName()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(new long[] { 1, 2 }, "N", "Samples"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<long, long>("FromRow", "FromLet", database,
            """
            let N = 99;
            let Saved = N;
            Samples | project FromRow = N, FromLet = Saved
            """);

        Assert.That(rows, Is.EquivalentTo(new[] { (1L, 99L), (2L, 99L) }));
    }

    [Test]
    public void Let_TabularBindingCanBeUsedInTwoIndependentBranches()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWith2Columns<long, string>(
            [(1, "west"), (2, "east"), (3, "west"), (4, "west")], "Score", "Region", "Scores"));

        var values = TestUtils.ExecuteAndGetDataForOneColumn<long>("Score", database,
            """
            let regionName = 'west';
            let Regional = Scores | where Region == regionName;
            let TopScores = Regional | where Score >= 3;
            TopScores | union (Regional | where Score <= 3) | project Score
            """);

        Assert.That(values, Is.EquivalentTo(new long[] { 1, 3, 3, 4 }));
    }

    [Test]
    public void Let_FunctionLocalShadowAndDefaultArgumentDoNotChangeOuterScope()
    {
        var rows = TestUtils.ExecuteAndGetDataFor3Columns<string, string, string>("Defaulted", "Explicit", "Outside",
            new KustoDatabase(),
            """
            let Tag = 'outer';
            let Format = (s:string, mark:string = '!') {
                let Tag = 'inner';
                strcat(Tag, ':', s, mark)
            };
            print Defaulted = Format('a'), Explicit = Format('b', '?'), Outside = Tag
            """);

        Assert.That(rows, Is.EqualTo(new[] { ("inner:a!", "inner:b?", "outer") }));
    }

    [Test]
    public void Let_ScalarFunctionReadsAnOuterBindingAndAnotherFunction()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(new long[] { 0, 3 }, "N", "Samples"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<long, long>("N", "Value", database,
            """
            let step = 2;
            let AddStep = (x:long) { x + step };
            let Twice = (x:long) { AddStep(AddStep(x)) };
            Samples | extend Value = Twice(N)
            """);

        Assert.That(rows, Is.EquivalentTo(new[] { (0L, 4L), (3L, 7L) }));
    }

    [Test]
    public void Let_TabularParameterRequiresOnlyItsDeclaredColumns()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWith3Columns<string, long, bool>(
            [("a", 1, true), ("b", 2, true), ("c", 5, false)], "Label", "Score", "Extra", "Scores"));

        var values = TestUtils.ExecuteAndGetDataForOneColumn<string>("Label", database,
            """
            let AtLeast = (T:(Score:long, Label:string), minimum:long) {
                T | where Score >= minimum | project Label
            };
            AtLeast(Scores, 2)
            """);

        Assert.That(values, Is.EquivalentTo(new[] { "b", "c" }));
    }

    [Test]
    public void Let_WildcardTabularParameterCanBeInvokedOnAnEmptyTable()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(Array.Empty<string>(), "Label", "Labels"));
        var values = TestUtils.ExecuteAndGetDataForOneColumn<long>("Count", database,
            """
            let Size = (T:(*)) { T | count };
            Labels | invoke Size()
            """);

        Assert.That(values, Is.EqualTo(new[] { 0L }));
    }

    [Test]
    public void Let_WildcardUnionIncludesViewsButNotOrdinaryFunctions()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(new[] { "table" }, "Label", "SrcTable"));
        var values = TestUtils.ExecuteAndGetDataForOneColumn<string>("Label", database,
            """
            let SrcView = view () { print Label = 'view' };
            let SrcFunction = () { print Label = 'not a view' };
            let OtherView = view () { print Label = 'different prefix' };
            union Src* | project Label
            """);

        Assert.That(values, Is.EquivalentTo(new[] { "table", "view" }));
    }

    [Test]
    public void Let_BindsACalculationWhileToscalarFreezesItsValue()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(new long[] { 1, 2, 3 }, "Id", "Events"));

        var rows = TestUtils.ExecuteAndGetDataFor2Columns<Guid, Guid>("Stable", "Fresh", database,
            """
            let stable = toscalar(new_guid());
            let fresh = new_guid();
            Events | extend Stable = stable, Fresh = fresh
            """);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.Select(row => row.Item1), Is.All.EqualTo(rows[0].Item1));
        Assert.That(rows.Select(row => row.Item2), Is.Unique);
    }

    [Test]
    public void Let_MaterializedTabularBindingReusesTheSameRows()
    {
        var rows = TestUtils.ExecuteAndGetDataFor2Columns<Guid, Guid>("First", "Second", new KustoDatabase(),
            """
            let Snapshot = materialize(print Token = new_guid());
            print First = toscalar(Snapshot), Second = toscalar(Snapshot)
            """);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Item1, Is.Not.EqualTo(Guid.Empty));
        Assert.That(rows[0].Item2, Is.EqualTo(rows[0].Item1));
    }

    [Test]
    public void Let_NamesDoNotLeakIntoTheNextQueryOnTheSameDatabase()
    {
        var database = new KustoDatabase();
        var values = TestUtils.ExecuteAndGetDataForOneColumn<long>("Value", database,
            "let QueryLocal = 23; print Value = QueryLocal");
        Assert.That(values, Is.EqualTo(new[] { 23L }));

        AstraTestUtils.AssertRejected(database, "print Value = QueryLocal");
    }

    [TestCase("let n = 1; print Value = n", "let n = 1 print Value = n")]
    [TestCase("let b = 1; let a = b; print Value = a", "let a = b; let b = 1; print Value = a")]
    [TestCase("let F = () { let hidden = 1; hidden }; print Value = F()",
        "let F = () { let hidden = 1; hidden }; print Value = hidden")]
    [TestCase("let Data = print Score = 1, Other = 2; let F = (T:(Score:long)) { T | project Value = Score }; F(Data)",
        "let Data = print Score = 1, Other = 2; let F = (T:(Score:long)) { T | project Value = Other }; F(Data)")]
    public void Let_RejectsMissingTerminatorsAndOutOfScopeReferences(string valid, string invalid)
    {
        var database = new KustoDatabase();
        var values = TestUtils.ExecuteAndGetDataForOneColumn<long>("Value", database, valid);
        Assert.That(values, Is.EqualTo(new[] { 1L }));
        AstraTestUtils.AssertRejected(database, invalid);
    }

    [Test]
    public void Let_PipelinesComposeParseTrimConversionBinAndStrcat()
    {
        var database = new KustoDatabase();
        database.AddTable(TestUtils.GenerateTableWithColumn(
            new[] { "name=red;value= 12 ;", "name=red;value=14;", "name=blue;value=5;", "name=bad;value=x;", "noise" },
            "Line", "Messages"));

        var labels = TestUtils.ExecuteAndGetDataForOneColumn<string>("Label", database,
            """
            let width = 5;
            let ReadMetric = (s:string) { tolong(trim(@'\s+', s)) };
            let Parsed = Messages | parse Line with "name=" Sensor ";value=" Raw ";";
            Parsed
            | extend N = ReadMetric(Raw)
            | where N >= 0
            | extend Label = strcat(Sensor, ':', tostring(bin(N, width)))
            """);

        Assert.That(labels, Is.EquivalentTo(new[] { "red:10", "red:10", "blue:5" }));
    }
}
