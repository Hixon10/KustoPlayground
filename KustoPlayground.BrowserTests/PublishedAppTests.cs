using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace KustoPlayground.BrowserTests;

public class PublishedAppTests : PageTest
{
    private const string SitePath = "/KustoPlayground";
    private readonly List<string> browserErrors = [];
    private WebApplication? site;
    private string siteUrl = null!;

    [OneTimeSetUp]
    public async Task ServePublishedSite()
    {
        var webRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "artifacts", "site", "wwwroot"));
        if (!File.Exists(Path.Combine(webRoot, "index.html")))
        {
            throw new DirectoryNotFoundException(
                $"No published app at {webRoot}. Run: dotnet publish KustoPlayground.UI -c Release -o artifacts/site");
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { WebRootPath = webRoot });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        site = builder.Build();
        site.Map(SitePath, app =>
        {
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions { ServeUnknownFileTypes = true });
        });
        await site.StartAsync();
        siteUrl = $"{site.Urls.Single()}{SitePath}/";
    }

    [OneTimeTearDown]
    public async Task StopSite()
    {
        if (site is not null)
        {
            await site.StopAsync();
            await site.DisposeAsync();
        }
    }

    [SetUp]
    public async Task OpenPlayground()
    {
        browserErrors.Clear();
        Page.PageError += (_, error) => browserErrors.Add(error);
        Page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                browserErrors.Add(message.Text);
            }
        };
        await Page.GotoAsync(siteUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Add Kusto table" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [TearDown]
    public async Task CheckForRuntimeErrors()
    {
        Assert.That(browserErrors, Is.Empty);
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    [TestCase(false, TestName = "Import pasted CSV and query it")]
    [TestCase(true, TestName = "Upload a CSV file and query it")]
    public async Task ImportCsvAndQuery(bool upload)
    {
        const string csv = "City,Score\nOslo,2\nLima,5\nRome,8";
        if (upload)
        {
            await Page.GetByLabel("Option 1: CSV Data from .csv file:").SetInputFilesAsync(new FilePayload
            {
                Name = "scores.csv", MimeType = "text/csv", Buffer = Encoding.UTF8.GetBytes(csv)
            });
        }
        else
        {
            await Page.GetByLabel("Option 2: CSV data as text:").FillAsync(csv);
        }

        await Page.GetByLabel("Table name:").FillAsync("Scores");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Table" }).ClickAsync();
        await Expect(Page.GetByText("CSV table \"Scores\" added successfully.")).ToBeVisibleAsync();
        await AssertQuery("Scores | where Score >= 5 | project City", "City=Lima", "City=Rome");
    }

    [Test]
    public async Task ImportJsonAndQuery()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "JSON" }).ClickAsync();
        await Page.GetByLabel("JSON table representation:").FillAsync("""
            {
              "Name": "Inventory",
              "Columns": [{"Name": "Item", "Type": "string"}, {"Name": "Quantity", "Type": "int"}],
              "Rows": [{"Item": "Pen", "Quantity": 3}, {"Item": "Ink", "Quantity": 0}]
            }
            """);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Table" }).ClickAsync();
        await Expect(Page.GetByText("JSON table added successfully.")).ToBeVisibleAsync();
        await AssertQuery("Inventory | count", "Count=2");
        await AssertQuery("Inventory | where Quantity > 0 | project Item", "Item=Pen");
    }

    [Test]
    public async Task ExpandImportedDynamicValuesAndJoinInPublishedWasm()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "JSON" }).ClickAsync();
        await Page.GetByLabel("JSON table representation:").FillAsync("""
            {
              "Name": "Orders",
              "Columns": [{"Name":"OrderId","Type":"int"},{"Name":"Items","Type":"dynamic"}],
              "Rows": [
                {"OrderId":1,"Items":[{"sku":7,"units":2},{"sku":8,"units":1}]},
                {"OrderId":2,"Items":[{"sku":7,"units":3}]},
                {"OrderId":3,"Items":[]}
              ]
            }
            """);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Table" }).ClickAsync();
        await Expect(Page.GetByText("JSON table added successfully.")).ToBeVisibleAsync();
        await AssertQuery("""
            let Catalog = datatable(Sku:int, Item:string)[7, 'Pen', 8, 'Ink'];
            Orders | mv-expand Items
            | project Sku=tolong(Items.sku), Units=tolong(Items.units)
            | join kind=inner (Catalog) on Sku
            | summarize Sold=sum(Units) by Item | sort by Sold desc
            """, "Item=Pen, Sold=5", "Item=Ink, Sold=1");
    }

    [Test]
    public async Task QueryMissingValuesInPublishedWasm()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "JSON" }).ClickAsync();
        await Page.GetByLabel("JSON table representation:").FillAsync("""
            {
              "Name": "Samples",
              "Columns": [{"Name":"Id","Type":"int"}, {"Name":"Score","Type":"long","Nullable":true},
                          {"Name":"Tag","Type":"string","Nullable":true}],
              "Rows": [{"Id":1}, {"Id":2,"Score":0,"Tag":""}, {"Id":3,"Score":4,"Tag":"ok"}]
            }
            """);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Table" }).ClickAsync();
        await Expect(Page.GetByText("JSON table added successfully.")).ToBeVisibleAsync();
        await AssertQuery("Samples | where Score >= 0 | sort by Id asc | project Id", "Id=2", "Id=3");
        await AssertQuery("""
            let Missing = Samples | where isnull(Score) | project Score, MatchId=Id;
            Samples | join kind=leftouter (Missing) on Score | sort by Id asc
            | project Id, Tag=coalesce(Tag, 'untagged'), Matched=iff(isnotnull(MatchId), 'yes', 'no')
            """, "Id=1, Tag=untagged, Matched=yes", "Id=2, Tag=untagged, Matched=no", "Id=3, Tag=ok, Matched=no");
    }

    [Test]
    public async Task MakeSeriesFillsGapsInPublishedWasm()
    {
        await AssertQuery("""
            datatable(Hour:int, Units:long)[0,2, 0,3, 2,7]
            | make-series Units=sum(Units) default=long(null) on Hour from 0 to 4 step 1
            | project Bins=Hour, Totals=Units
            """, "Bins=[0,1,2,3], Totals=[5,null,7,null]");
    }

    [Test]
    public async Task LineChartConnectsTheTypedRowsAndClearsOnTheNextQuery()
    {
        await AssertQuery("datatable(X:long, Y:long)[2,3, 0,2, 1,5] | sort by X asc | render linechart",
            "X=0, Y=2", "X=1, Y=5", "X=2, Y=3");
        var (chart, points) = await ReadChart("linechart: Y by X", 3);
        Assert.That(points[0][0], Is.LessThan(points[2][0]));
        Assert.That(points[1][0], Is.EqualTo((points[0][0] + points[2][0]) / 2).Within(0.01));
        Assert.That(points[1][1], Is.LessThan(points[2][1]));
        Assert.That(points[2][1], Is.LessThan(points[0][1]));
        Assert.That(await chart.Locator("path.series").EvaluateAsync<double>("path => path.getTotalLength()"), Is.Positive);

        await AssertQuery("print X='not numeric', Y=7 | render linechart", "X=not numeric, Y=7");
        await Expect(chart).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("numeric X column");
        await AssertQuery("print Y=7", "Y=7");
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveCountAsync(0);
    }

    [Test]
    public async Task TimeChartUsesElapsedTimeAndBreaksAtMissingMeasurements()
    {
        await AssertQuery("""
            datatable(T:datetime, Y:long)[
                datetime(2026-01-01T00:00:00.0000000Z), 2, datetime(2026-01-01T00:00:00.0000001Z), 2,
                datetime(2026-01-01T00:00:00.0000002Z), long(null), datetime(2026-01-01T00:00:00.0000004Z), 2]
            | render timechart
            """, "T=2026-01-01T00:00:00.0000000Z, Y=2", "T=2026-01-01T00:00:00.0000001Z, Y=2",
            "T=2026-01-01T00:00:00.0000002Z, Y=", "T=2026-01-01T00:00:00.0000004Z, Y=2");
        var (chart, points) = await ReadChart("timechart: Y by T", 3);
        Assert.That(points[1][0] - points[0][0], Is.Positive);
        Assert.That(points[2][0] - points[0][0], Is.EqualTo(4 * (points[1][0] - points[0][0])).Within(0.01));
        Assert.That(points.Select(p => p[1]).Distinct().Count(), Is.EqualTo(1));
        var path = await chart.Locator("path.series").GetAttributeAsync("d");
        Assert.That(path!.Count(c => c == 'M'), Is.EqualTo(2), "Missing values must break the line.");

        await AssertQuery("print T=datetime(2026-01-01), Y=1 | take 0 | render timechart", "Empty result");
        await Expect(chart).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("no rows");
    }

    [Test]
    public async Task SummarizePublishedTable()
    {
        await Page.GetByLabel("Option 2: CSV data as text:").FillAsync("City,Score\nOslo,2\nLima,5\nOslo,8");
        await Page.GetByLabel("Table name:").FillAsync("Scores");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Table" }).ClickAsync();
        await Expect(Page.GetByText("CSV table \"Scores\" added successfully.")).ToBeVisibleAsync();
        await AssertQuery("Scores | sort by Score asc | summarize Total=sum(Score), Values=make_list(Score) by City | sort by Total desc",
            "City=Oslo, Total=10, Values=[2,8]", "City=Lima, Total=5, Values=[5]");
    }

    [Test]
    public async Task ParseAndInspectSchemaInPublishedWasm()
    {
        const string query = """
            let Bucket = (s:string) { bin(toint(trim(' ', s)), 5) };
            let Parsed = print Line='n= 14 ;' | parse Line with 'n=' Raw ';';
            Parsed | project Bucket=Bucket(Raw)
            | extend Bag=pack_all()
            | project Label=strcat('bucket=', extract_json('$.Bucket', tostring(Bag)))
            """;
        await AssertQuery(query, "Label=bucket=10");
        await AssertQuery(query + " | getschema | project ColumnName, ColumnType", "ColumnName=Label, ColumnType=string");
        await AssertQuery("print R=bin(4.5, 0.0) | project Bag=pack_all()", """Bag={"R":"NaN"}""");
    }

    private async Task<(ILocator Chart, double[][] Points)> ReadChart(string name, int count)
    {
        var chart = Page.GetByRole(AriaRole.Img, new() { Name = name, Exact = true });
        await Expect(chart).ToBeVisibleAsync();
        var points = chart.Locator("circle");
        await Expect(points).ToHaveCountAsync(count);
        return (chart, await points.EvaluateAllAsync<double[][]>("dots => dots.map(p => [p.cx.baseVal.value, p.cy.baseVal.value])"));
    }

    private async Task AssertQuery(string query, params string[] expectedRows)
    {
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "KQL query" }).FillAsync(query);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Process Kusto Query" }).ClickAsync();
        var rows = Page.GetByRole(AriaRole.List, new() { Name = "Query results" }).GetByRole(AriaRole.Listitem);
        await Expect(rows).ToHaveTextAsync(expectedRows);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Errors" })).ToHaveCountAsync(0);
    }
}
