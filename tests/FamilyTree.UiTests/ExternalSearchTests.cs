using System.Text.RegularExpressions;
using FamilyTree.UiTests.Support;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace FamilyTree.UiTests;

[TestClass]
public sealed class ExternalSearchTests : PageTest
{
    private RunningApp _app = null!;
    private HttpClient _noRedirectClient = null!;

    [TestInitialize]
    public void StartApp()
    {
        _app = new RunningApp();
        _noRedirectClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    }

    [TestCleanup]
    public void StopApp()
    {
        _noRedirectClient.Dispose();
        _app.Dispose();
    }

    private async Task<string> CreatePersonAsync()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FirstName"] = "John",
            ["LastName"] = "Murphy",
            ["Sex"] = "Male",
            ["BornOn"] = "1880",
            ["BornPlace"] = "Ballina, County Mayo",
        });
        var response = await _noRedirectClient.PostAsync($"{_app.BaseUrl}/people", content);
        return response.Headers.Location!.ToString().Trim('/').Split('/')[1];
    }

    private const string SourceName = "Irish census (National Archives of Ireland)";

    private ILocator Section => Page.Locator("details.external-search");

    private ILocator SectionToggle => Page.Locator("details.external-search > summary");

    [TestMethod]
    public async Task ExternalSearch_IsCollapsedByDefault_AndExpandsToShowTheSources()
    {
        var id = await CreatePersonAsync();

        await Page.GotoAsync($"{_app.BaseUrl}/people/{id}");

        await Expect(SectionToggle).ToContainTextAsync("Search external sources");
        await Expect(Section).Not.ToHaveAttributeAsync("open", new Regex(".*"));
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = SourceName })).Not.ToBeVisibleAsync();

        await SectionToggle.ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = SourceName })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task DetailsPage_KeepsPersonDetailsOnceExternalSourcesHaveLoaded()
    {
        var id = await CreatePersonAsync();

        await Page.GotoAsync($"{_app.BaseUrl}/people/{id}");
        await SectionToggle.ClickAsync();

        // The sources list loads lazily into the collapsible section...
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = SourceName })).ToBeVisibleAsync();

        // ...and must not replace the rest of the page.
        await Expect(Page.Locator("h1")).ToHaveTextAsync("John Murphy");
        await Expect(Page.Locator("dl.facts").First).ToContainTextAsync("Ballina, County Mayo");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Notes" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ChoosingASource_ShowsResultsInsideTheCollapsibleSection_BesideThePersonDetails()
    {
        var id = await CreatePersonAsync();

        await Page.GotoAsync($"{_app.BaseUrl}/people/{id}");
        await SectionToggle.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = SourceName }).ClickAsync();

        await Expect(Section.GetByRole(AriaRole.Link, new() { Name = "Open the census search" })).ToBeVisibleAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync("John Murphy");
        await Expect(Page.Locator("dl.facts").First).ToContainTextAsync("Ballina, County Mayo");

        // Collapsing hides the results along with the buttons.
        await SectionToggle.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Open the census search" })).Not.ToBeVisibleAsync();
    }
}
