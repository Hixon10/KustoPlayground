namespace KustoPlayground.Core.Tests;

public class TableBuilderFromCsvTests
{
    private string CsvText { get; set; } = @"id,name,city,HasJob,StartTime
1,Alice,""New York"",true,2007-09-20T21:57:00Z
2,Bob Joe,""Chicago"",true,2007-12-20T07:50:00Z
3,Charlie,""San Francisco"",false,2007-12-30T16:00:00Z
4,Diana,""Boston"",true,2007-09-29T08:11:00Z
";

    [Test]
    public void SmokeTest()
    {
        var lines = CsvText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToList();

        var headers = lines[0].Split(',').Select(h => h.Trim()).ToList();

        var rows = lines.Skip(1)
            .Select(line => line.Split(',')
                .Select((val, idx) => new KeyValuePair<string, object?>(headers[idx], val.Trim()))
                .ToDictionary(kv => kv.Key, kv => kv.Value))
            .ToList();

        Table newTable = TableBuilderFromCsv.Build(new TableDef()
        {
            Name = "MyTable",
            Columns = headers.Select(header => new ColumnDef { Name = header }).ToList(),
            Rows = rows,
        });

        int[] expectedIds = [1, 2, 3, 4];
        string[] expectedNames = ["Alice", "Bob Joe", "Charlie", "Diana"];
        string[] expectedCities = ["\"New York\"", "\"Chicago\"", "\"San Francisco\"", "\"Boston\""];
        bool[] expectedHasJob = [true, true, false, true];
        DateTime[] expectedStartTime =
        [
            DateTime.Parse("2007-09-20T21:57:00Z"),
            DateTime.Parse("2007-12-20T07:50:00Z"),
            DateTime.Parse("2007-12-30T16:00:00Z"),
            DateTime.Parse("2007-09-29T08:11:00Z"),
        ];

        for (var index = 0; index < newTable.Rows.Count; index++)
        {
            var row = newTable.Rows[index];
            Assert.That(row.Schema["id"].Name, Is.EqualTo("id"));
            Assert.That(row.Schema["id"].IsNullable, Is.EqualTo(false));
            Assert.That(row.Schema["id"].GetColumnType(), Is.EqualTo(typeof(int)));
            Assert.That(row.Get<int>("id"), Is.EqualTo(expectedIds[index]));

            Assert.That(row.Schema["name"].Name, Is.EqualTo("name"));
            Assert.That(row.Schema["name"].IsNullable, Is.EqualTo(false));
            Assert.That(row.Schema["name"].GetColumnType(), Is.EqualTo(typeof(string)));
            Assert.That(row.Get<string>("name"), Is.EqualTo(expectedNames[index]));

            Assert.That(row.Schema["city"].Name, Is.EqualTo("city"));
            Assert.That(row.Schema["city"].IsNullable, Is.EqualTo(false));
            Assert.That(row.Schema["city"].GetColumnType(), Is.EqualTo(typeof(string)));
            Assert.That(row.Get<string>("city"), Is.EqualTo(expectedCities[index]));

            Assert.That(row.Schema["HasJob"].Name, Is.EqualTo("HasJob"));
            Assert.That(row.Schema["HasJob"].IsNullable, Is.EqualTo(false));
            Assert.That(row.Schema["HasJob"].GetColumnType(), Is.EqualTo(typeof(bool)));
            Assert.That(row.Get<bool>("HasJob"), Is.EqualTo(expectedHasJob[index]));

            Assert.That(row.Schema["StartTime"].Name, Is.EqualTo("StartTime"));
            Assert.That(row.Schema["StartTime"].IsNullable, Is.EqualTo(false));
            Assert.That(row.Schema["StartTime"].GetColumnType(), Is.EqualTo(typeof(DateTime)));
            Assert.That(row.Get<DateTime>("StartTime"), Is.EqualTo(expectedStartTime[index]));
        }
    }
}