using FamilyTree.UiTests.Support;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace FamilyTree.UiTests;

/// <summary>
/// These exercise real-browser, client-side-JS behavior that an HttpClient-driven Reqnroll scenario
/// can't observe: htmx's `hx-confirm` dialog (which intercepts the request before it's even sent) and
/// whether a response is swapped into the current page in place versus causing a full navigation.
/// Everything else (data correctness, relationship derivation, git history) is already covered by the
/// FamilyTree.Data unit tests and the FamilyTree.AcceptanceTests Reqnroll scenarios — this project
/// doesn't re-test that.
/// </summary>
[TestClass]
public sealed class HtmxBehaviorTests : PageTest
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

    private async Task<string> CreatePersonAsync(string firstName, string lastName, string? bornOn = null, string? notes = null)
    {
        var fields = new Dictionary<string, string> { ["FirstName"] = firstName, ["LastName"] = lastName };
        if (bornOn is not null) fields["BornOn"] = bornOn;
        if (notes is not null) fields["Notes"] = notes;

        using var content = new FormUrlEncodedContent(fields);
        var response = await _noRedirectClient.PostAsync($"{_app.BaseUrl}/people", content);
        var location = response.Headers.Location!.ToString();
        return location.Trim('/').Split('/')[1];
    }

    [TestMethod]
    public async Task DeletingAPerson_DismissingTheConfirmDialog_KeepsThem()
    {
        var id = await CreatePersonAsync("John", "Doe", "1920");
        await Page.GotoAsync($"{_app.BaseUrl}/people/{id}");

        Page.Dialog += async (_, dialog) => await dialog.DismissAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        // Give htmx a moment to have (not) sent the request, then reload from the server to be sure.
        await Page.WaitForTimeoutAsync(300);
        await Page.GotoAsync($"{_app.BaseUrl}/people/{id}");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("John Doe");
    }

    [TestMethod]
    public async Task DeletingAPerson_AcceptingTheConfirmDialog_DeletesThem()
    {
        var id = await CreatePersonAsync("John", "Doe", "1920");
        await Page.GotoAsync($"{_app.BaseUrl}/people/{id}");

        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await Page.WaitForURLAsync($"{_app.BaseUrl}/people");

        var response = await _noRedirectClient.GetAsync($"{_app.BaseUrl}/people/{id}");
        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task SubmittingAnInvalidForm_SwapsInValidationErrorsWithoutAFullPageNavigation()
    {
        await Page.GotoAsync($"{_app.BaseUrl}/people/new");
        await Page.EvaluateAsync("window.__marker = 'still-here'");

        // Leave the required First name blank so the server re-renders the form with validation errors
        // and no HX-Redirect — this is the one case in the app that's a genuine in-place htmx swap.
        await Page.Locator("input[name=LastName]").FillAsync("NoFirstName");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(Page.Locator(".validation-summary-errors")).ToContainTextAsync("required");

        var markerSurvived = await Page.EvaluateAsync<string?>("window.__marker");
        Assert.AreEqual("still-here", markerSurvived, "A validation-failure re-render must be an in-place htmx swap, not a full page navigation.");
    }

    [TestMethod]
    public async Task RelationshipTypeahead_NarrowsResultsAsYouType()
    {
        await CreatePersonAsync("Alice", "Smith", "1930");
        await CreatePersonAsync("Bob", "Jones", "1935");
        var childId = await CreatePersonAsync("Charlie", "Smith", "1960");

        await Page.GotoAsync($"{_app.BaseUrl}/people/{childId}");
        // Fill() sets the value directly without firing keyup events, which is what htmx's
        // hx-trigger="keyup changed delay:300ms" on this input listens for — simulate real typing instead.
        await Page.Locator("input[placeholder='Add a parent…']").PressSequentiallyAsync("Alice", new() { Delay = 50 });

        await Expect(Page.Locator("#parent-search-results")).ToContainTextAsync("Alice Smith");
        await Expect(Page.Locator("#parent-search-results")).Not.ToContainTextAsync("Bob Jones");
    }

    [TestMethod]
    public async Task Notes_RenderMarkdownToRealHtmlElements()
    {
        var id = await CreatePersonAsync("Jane", "Doe", "1952", notes: "This is **important**.");

        await Page.GotoAsync($"{_app.BaseUrl}/people/{id}");

        await Expect(Page.Locator("strong")).ToHaveTextAsync("important");
    }
}
