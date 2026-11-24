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
        await AssertQuery("print R=bin(4.5, 0.0) | project Bag=pack_all()", "Bag={\"R\":NaN}");
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
