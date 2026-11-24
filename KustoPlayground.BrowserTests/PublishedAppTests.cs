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
    public async Task SummarizePublishedTable()
    {
        await Page.GetByLabel("Option 2: CSV data as text:").FillAsync("City,Score\nOslo,2\nLima,5\nOslo,8");
        await Page.GetByLabel("Table name:").FillAsync("Scores");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Table" }).ClickAsync();
        await Expect(Page.GetByText("CSV table \"Scores\" added successfully.")).ToBeVisibleAsync();
        await AssertQuery("Scores | sort by Score asc | summarize Total=sum(Score), Values=make_list(Score) by City | sort by Total desc",
            "City=Oslo, Total=10, Values=[2,8]", "City=Lima, Total=5, Values=[5]");
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
