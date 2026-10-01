using FamilyTree.UiTests.Support;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace FamilyTree.UiTests;

/// <summary>
/// The recently-visited breadcrumb trail is built client-side (localStorage) and must keep up with
/// hx-boost navigation, which swaps only #main-content — so it needs a real browser.
/// </summary>
[TestClass]
public sealed class BreadcrumbTests : PageTest
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

    private async Task<string> CreatePersonAsync(string firstName, string lastName)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FirstName"] = firstName,
            ["LastName"] = lastName,
        });
        var response = await _noRedirectClient.PostAsync($"{_app.BaseUrl}/people", content);
        return response.Headers.Location!.ToString().Trim('/').Split('/')[1];
    }

    private ILocator Crumbs => Page.Locator("#breadcrumbs li");

    [TestMethod]
    public async Task BoostedNavigation_AppendsToTheTrail_AndEarlierCrumbsLinkBack()
    {
        var id = await CreatePersonAsync("Alice", "Smith");

        await Page.GotoAsync($"{_app.BaseUrl}/people");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Alice Smith" }).First.ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Alice Smith");

        await Expect(Crumbs).ToHaveTextAsync(new[] { "People", "Alice Smith" });
        await Expect(Page).ToHaveTitleAsync("Alice Smith - Family Tree");

        await Page.Locator("#breadcrumbs").GetByRole(AriaRole.Link, new() { Name = "People" }).ClickAsync();
        await Page.WaitForURLAsync($"{_app.BaseUrl}/people");
        await Expect(Crumbs).ToHaveTextAsync(new[] { "Alice Smith", "People" });
    }

    [TestMethod]
    public async Task Trail_SurvivesAReload_AndIsCapped()
    {
        var id = await CreatePersonAsync("Alice", "Smith");

        for (var i = 0; i < 6; i++)
        {
            await Page.GotoAsync($"{_app.BaseUrl}/people/{id}");
            await Page.GotoAsync($"{_app.BaseUrl}/people/{id}/history");
            await Page.GotoAsync($"{_app.BaseUrl}/people/{id}/graph");
            await Page.GotoAsync($"{_app.BaseUrl}/people/{id}/tree");
        }

        await Page.ReloadAsync();
        await Expect(Crumbs).ToHaveCountAsync(4);
        await Expect(Crumbs.Last).ToHaveAttributeAsync("aria-current", "page");
    }

    [TestMethod]
    public async Task DeletingAPerson_RemovesTheirPagesFromTheTrail()
    {
        var id = await CreatePersonAsync("Alice", "Smith");
        await Page.GotoAsync($"{_app.BaseUrl}/people/{id}");

        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await Page.WaitForURLAsync($"{_app.BaseUrl}/people");

        await Expect(Crumbs).ToHaveTextAsync(new[] { "People" });
    }
}
