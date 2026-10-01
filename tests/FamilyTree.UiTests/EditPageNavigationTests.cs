using FamilyTree.UiTests.Support;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace FamilyTree.UiTests;

[TestClass]
public sealed class EditPageNavigationTests : PageTest
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

    [TestMethod]
    public async Task EditPage_LinksBackToThePersonsDetails()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FirstName"] = "Alice",
            ["LastName"] = "Smith",
        });
        var response = await _noRedirectClient.PostAsync($"{_app.BaseUrl}/people", content);
        var id = response.Headers.Location!.ToString().Trim('/').Split('/')[1];

        await Page.GotoAsync($"{_app.BaseUrl}/people/{id}/edit");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Back to Alice Smith" }).ClickAsync();

        await Page.WaitForURLAsync($"{_app.BaseUrl}/people/{id}");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Alice Smith");
    }
}
