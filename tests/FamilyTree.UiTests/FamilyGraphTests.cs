using System.Text.Json;
using FamilyTree.UiTests.Support;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace FamilyTree.UiTests;

/// <summary>
/// Covers the interactive family graph: the JSON slice endpoint and the browser behaviour that
/// an HttpClient can't see (d3/family-chart actually rendering, tap-to-refocus, htmx-boosted entry, phone viewport).
/// </summary>
[TestClass]
public sealed class FamilyGraphTests : PageTest
{
    private RunningApp _app = null!;
    private HttpClient _http = null!;

    [TestInitialize]
    public void StartApp()
    {
        _app = new RunningApp();
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    }

    [TestCleanup]
    public void StopApp()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private async Task<string> CreatePersonAsync(string firstName, string lastName, string bornOn)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FirstName"] = firstName,
            ["LastName"] = lastName,
            ["BornOn"] = bornOn,
        });
        var response = await _http.PostAsync($"{_app.BaseUrl}/people", content);
        return response.Headers.Location!.ToString().Trim('/').Split('/')[1];
    }

    private async Task LinkParentAsync(string childId, string parentId)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["parentId"] = parentId });
        await _http.PostAsync($"{_app.BaseUrl}/people/{childId}/relationships/parent", content);
    }

    private async Task<(string ParentId, string ChildId)> CreateParentAndChildAsync()
    {
        var parentId = await CreatePersonAsync("Alice", "Smith", "1930");
        var childId = await CreatePersonAsync("Charlie", "Smith", "1960");
        await LinkParentAsync(childId, parentId);
        return (parentId, childId);
    }

    [TestMethod]
    public async Task GraphData_ReturnsFamilyChartShapeWithRelationshipsBothWays()
    {
        var (parentId, childId) = await CreateParentAndChildAsync();

        var json = await _http.GetStringAsync($"{_app.BaseUrl}/people/{childId}/graph/data");
        using var doc = JsonDocument.Parse(json);
        var people = doc.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!);

        Assert.AreEqual(2, people.Count);
        Assert.AreEqual(parentId, people[childId].GetProperty("rels").GetProperty("parents")[0].GetString());
        Assert.AreEqual(childId, people[parentId].GetProperty("rels").GetProperty("children")[0].GetString());
        Assert.AreEqual("Alice Smith", people[parentId].GetProperty("data").GetProperty("name").GetString());
        Assert.AreEqual("1930–", people[parentId].GetProperty("data").GetProperty("lifespan").GetString());
    }

    [TestMethod]
    public async Task GraphCard_ShowsMaidenNameOnlyWhenPresent()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FirstName"] = "Jane",
            ["LastName"] = "Smith",
            ["MaidenName"] = "Jones",
            ["BornOn"] = "1950",
        });
        var response = await _http.PostAsync($"{_app.BaseUrl}/people", content);
        var janeId = response.Headers.Location!.ToString().Trim('/').Split('/')[1];
        var childId = await CreatePersonAsync("Charlie", "Smith", "1980");
        await LinkParentAsync(childId, janeId);

        await Page.GotoAsync($"{_app.BaseUrl}/people/{childId}/graph");

        await Expect(Page.Locator(".card", new() { HasText = "Jane Smith" }).Locator(".graph-card-maiden")).ToHaveTextAsync("(née Jones)");
        await Expect(Page.Locator(".card-main .graph-card-maiden")).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task GraphData_HonoursGenerationLimitsAndReportsHiddenRelatives()
    {
        var (parentId, childId) = await CreateParentAndChildAsync();

        var json = await _http.GetStringAsync($"{_app.BaseUrl}/people/{childId}/graph/data?up=0&down=0");
        using var doc = JsonDocument.Parse(json);

        var only = doc.RootElement.EnumerateArray().Single();
        Assert.AreEqual(childId, only.GetProperty("id").GetString());
        Assert.AreEqual(1, only.GetProperty("data").GetProperty("hiddenParents").GetInt32());
        Assert.AreNotEqual(parentId, only.GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task GraphData_UnknownPerson_Returns404()
    {
        var response = await _http.GetAsync($"{_app.BaseUrl}/people/nobody-1900/graph/data");

        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GraphPage_RendersCardsForFocusAndParent()
    {
        var (_, childId) = await CreateParentAndChildAsync();

        await Page.GotoAsync($"{_app.BaseUrl}/people/{childId}/graph");

        await Expect(Page.Locator(".graph-card-name", new() { HasText = "Alice Smith" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".card-main .graph-card-name")).ToHaveTextAsync("Charlie Smith");
    }

    [TestMethod]
    public async Task GraphPage_ReachedFromDetailsByBoostedLink_InitialisesWithoutAFullPageLoad()
    {
        var (_, childId) = await CreateParentAndChildAsync();
        await Page.GotoAsync($"{_app.BaseUrl}/people/{childId}");
        await Page.EvaluateAsync("window.__marker = 'still-here'");

        // Scoped to the page content: the site header's brand link is also called "Family Tree".
        await Page.Locator("#main-content").GetByRole(AriaRole.Link, new() { Name = "Family Tree", Exact = true }).ClickAsync();

        await Expect(Page.Locator(".graph-card-name", new() { HasText = "Alice Smith" })).ToBeVisibleAsync();
        Assert.AreEqual("still-here", await Page.EvaluateAsync<string?>("window.__marker"));
    }

    [TestMethod]
    public async Task TappingACard_RefocusesTheGraphInPlace()
    {
        var (_, childId) = await CreateParentAndChildAsync();
        await Page.GotoAsync($"{_app.BaseUrl}/people/{childId}/graph");
        await Page.EvaluateAsync("window.__marker = 'still-here'");

        // Click the card's padding rather than the name link, which navigates to the profile instead.
        await Page.Locator(".card", new() { HasText = "Alice Smith" }).Locator(".graph-card-dates").ClickAsync();

        await Expect(Page.Locator(".card-main .graph-card-name")).ToHaveTextAsync("Alice Smith");
        Assert.AreEqual("still-here", await Page.EvaluateAsync<string?>("window.__marker"));
    }

    [TestMethod]
    public async Task ClickingANameOnTheGraph_OpensThatPersonsProfile()
    {
        var (parentId, childId) = await CreateParentAndChildAsync();
        await Page.GotoAsync($"{_app.BaseUrl}/people/{childId}/graph");

        await Page.Locator(".graph-card-name", new() { HasText = "Alice Smith" }).ClickAsync();

        await Page.WaitForURLAsync($"{_app.BaseUrl}/people/{parentId}");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Alice Smith");
    }

    [TestMethod]
    public async Task GraphPage_OnAPhoneViewport_FillsTheWidthWithoutHorizontalPageScroll()
    {
        var (_, childId) = await CreateParentAndChildAsync();
        await Page.SetViewportSizeAsync(390, 844);

        await Page.GotoAsync($"{_app.BaseUrl}/people/{childId}/graph");
        await Expect(Page.Locator(".card-main .graph-card-name")).ToBeVisibleAsync();

        var overflows = await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.IsFalse(overflows, "The graph must pan inside its own container, not widen the page.");

        var box = await Page.Locator(".graph-canvas").BoundingBoxAsync();
        Assert.IsTrue(box!.Width > 350, $"Graph should use the full phone width but was {box.Width}px.");
    }
}
