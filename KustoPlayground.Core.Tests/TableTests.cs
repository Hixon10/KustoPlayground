namespace KustoPlayground.Core.Tests;

public class TableTests
{
    [Test]
    public void SmokeTest()
    {
        var idCol = new Column<int>("Id", isNullable: false);
        var nameCol = new Column<string>("Name");
        var ageCol = new Column<int>("Age");

        var users = new Table("Users", new ColumnBase[] { idCol, nameCol, ageCol });

        users.AddRow(new Dictionary<string, object?>
        {
            ["Id"] = 1,
            ["Name"] = "Alice",
            ["Age"] = 30
        });

        users.AddRow(new Dictionary<string, object?>
        {
            ["Id"] = 2,
        });
        
        users.AddRow(new Dictionary<string, object?>
        {
            ["Id"] = 3,
            ["Age"] = null
        });
        
        // row0
        Row row0 = users.Rows[0];
        int id = row0.Get(idCol);
        Assert.That(id, Is.EqualTo(1));
            
        string? name = row0.Get(nameCol);
        Assert.That(name, Is.EqualTo("Alice"));
            
        int? age = row0.Get(ageCol);
        Assert.That(age, Is.EqualTo(30));
        
        // row1
        Row row1 = users.Rows[1];
        id = row1.Get(idCol);
        Assert.That(id, Is.EqualTo(2));
            
        name = row1.Get(nameCol);
        Assert.That(name, Is.Null);
            
        age = row1.Get(ageCol);
        Assert.That(age, Is.EqualTo(0));
        
        // row2
        Row row2 = users.Rows[2];
        id = row2.Get(idCol);
        Assert.That(id, Is.EqualTo(3));
            
        name = row2.Get(nameCol);
        Assert.That(name, Is.Null);
            
        age = row2.Get(ageCol);
        Assert.That(age, Is.EqualTo(0));
    }
}