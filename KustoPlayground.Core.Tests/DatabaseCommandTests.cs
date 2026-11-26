namespace KustoPlayground.Core.Tests;

public class DatabaseCommandTests
{
    [Test]
    public void ShowDatabasesReturnsOneCurrentEphemeralDatabaseWithStableIdentity()
    {
        var db = new KustoDatabase();
        var result = db.ExecuteQuery(".show databases", includeSchema: true);
        Assert.That(result.ExecutionErrors, Is.Null);
        Assert.That(result.Columns!.Select(c => (c.Name, c.Type)), Is.EqualTo(new[]
        {
            ("DatabaseName", "string"), ("PersistentStorage", "string"), ("Version", "string"), ("IsCurrent", "bool"),
            ("DatabaseAccessMode", "string"), ("PrettyName", "string"), ("ReservedSlot1", "bool"), ("DatabaseId", "guid"),
            ("InTransitionTo", "string"), ("SuspensionState", "string")
        }));
        var row = result.ResultRows!.Single();
        Assert.That(row["DatabaseName"], Is.EqualTo("MyDatabase"));
        Assert.That(row["IsCurrent"], Is.True);
        Assert.That(row["DatabaseAccessMode"], Is.EqualTo("ReadWriteEphemeral"));
        Assert.That(row["PersistentStorage"], Is.EqualTo(""));
        Assert.That(row["DatabaseId"], Is.TypeOf<Guid>().And.Not.EqualTo(Guid.Empty));
        var projected = OpusTestUtils.ExecuteAndGetRows(db,
            ".show databases | where IsCurrent and isempty(SuspensionState) | project DatabaseId");
        Assert.That(projected.Single()["DatabaseId"], Is.EqualTo(row["DatabaseId"]));
        Assert.That(OpusTestUtils.ExecuteAndGetSingleValue("DatabaseId", new KustoDatabase(), ".show databases"),
            Is.Not.EqualTo(row["DatabaseId"]));
    }

    [Test]
    public void SchemaShowsTheDatabaseThenEveryTableAndDeclaredColumnEvenWithoutRows()
    {
        var db = new KustoDatabase();
        var empty = db.ExecuteQuery(".show database MyDatabase schema", includeSchema: true);
        Assert.That(empty.ExecutionErrors, Is.Null);
        Assert.That(empty.Columns!.Select(c => (c.Name, c.Type)), Is.EqualTo(new[]
        {
            ("DatabaseName", "string"), ("TableName", "string"), ("ColumnName", "string"), ("ColumnType", "string"),
            ("IsDefaultTable", "bool"), ("IsDefaultColumn", "bool"), ("PrettyName", "string"), ("Version", "string"),
            ("Folder", "string"), ("DocString", "string")
        }));
        var dbRow = empty.ResultRows!.Single();
        Assert.That(dbRow["DatabaseName"], Is.EqualTo("MyDatabase"));
        Assert.That(dbRow["TableName"], Is.EqualTo(""));

        db.AddTable(OpusTestUtils.NewTable("Z").WithColumn<long>("N").Build());
        db.AddTable(OpusTestUtils.NewTable("A").WithColumn<object>("Tags").WithColumn<bool>("Active").Build());
        var rows = OpusTestUtils.ExecuteAndGetRows(db, "// catalog\n.show database ['mydatabase'] schema;");
        Assert.That(rows.Select(r => (r["TableName"], r["ColumnName"], r["ColumnType"])), Is.EqualTo(new[]
        {
            ("", "", ""), ("A", "", ""), ("A", "Tags", "System.Object"), ("A", "Active", "System.SByte"),
            ("Z", "", ""), ("Z", "N", "System.Int64")
        }));
        Assert.That(rows.Select(r => r["DatabaseName"]), Is.All.EqualTo("MyDatabase"));
        Assert.That(rows[0]["Version"], Is.Not.EqualTo(dbRow["Version"]));
        Assert.That(rows[0]["Version"], Is.EqualTo(OpusTestUtils.ExecuteAndGetSingleValue("Version", db, ".show databases")));
        Assert.That(rows.Skip(1).Select(r => r["Version"]), Is.All.EqualTo(""));

        db.AddTable(OpusTestUtils.NewTable("A").WithColumn<DateTime>("Created").Build());
        Assert.That(OpusTestUtils.ExecuteAndGetValues("ColumnName", db,
            ".show database MyDatabase schema | where TableName == 'A' and isnotempty(ColumnName) | project ColumnName"),
            Is.EqualTo(new[] { "Created" }));
    }

    [Test]
    public void UnknownDatabaseIsNotResolvedAsATableOrAnAliasForTheCurrentDatabase()
    {
        var db = new KustoDatabase();
        db.AddTable(OpusTestUtils.NewTable("DemoDB").WithColumn<string>("Label").Build());
        var result = db.ExecuteQuery(".show database DemoDB schema | take 0");
        Assert.That(result.ResultRows, Is.Null);
        var error = result.ExecutionErrors!.Single();
        Assert.That(error.Code, Is.EqualTo("UnknownDatabase"));
        Assert.That(error.Description, Does.Contain("DemoDB").And.Contain("MyDatabase"));
    }

    [TestCase(".show databases details")]
    [TestCase(".show database MyDatabase schema as json")]
    [TestCase(".show database MyDatabase schema if_later_than 'v1.0' | getschema")]
    [TestCase(".show databases; print 1")]
    [TestCase(".show database MyDatabase nonsense")]
    [TestCase(".create database DemoDB")]
    public void OtherCommandsAndOptionsAreNotSilentlyRunAsCatalogReads(string command)
    {
        var result = new KustoDatabase().ExecuteQuery(command);
        Assert.That(result.ResultRows, Is.Null);
        Assert.That(result.ExecutionErrors, Is.Not.Empty);
    }
}
