using FamilyTree.Data.ExternalSources;
using FamilyTree.Data.Git;
using FamilyTree.Data.Models;
using FamilyTree.Data.Repository;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FamilyTree.AcceptanceTests;

/// <summary>
/// Drives the external-search endpoints against the real web app with only fake sources registered, so no test
/// ever calls a third-party service.
/// </summary>
[TestClass]
public sealed class ExternalSearchEndpointTests
{
    private string _dataDirectory = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _personId = null!;

    [TestInitialize]
    public async Task StartApp()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "FamilyTreeAcceptanceTests", Guid.NewGuid().ToString("N"));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FamilyTreeData:RepositoryPath"] = _dataDirectory,
                ["FamilyTreeData:DefaultCommitAuthorName"] = "Acceptance Test",
                ["FamilyTreeData:DefaultCommitAuthorEmail"] = "acceptance-test@example.com",
            }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExternalDataSourceRegistry>();
                services.AddSingleton<IExternalDataSourceRegistry>(new ExternalDataSourceRegistry(
                    [new FakeSource("works", Succeed), new FakeSource("throws", (_, _) => throw new InvalidOperationException("boom"))],
                    Microsoft.Extensions.Options.Options.Create(new ExternalSourcesOptions())));
            });
        });

        _client = _factory.CreateClient();

        var repository = _factory.Services.GetRequiredService<IPersonRepository>();
        var created = await repository.CreateAsync(
            new Person { Id = "ann-1", Name = new PersonName("Ann", null, "Walsh", null), BornOn = PartialDate.Parse("1850") },
            new CommitAuthor("Test", "test@example.com"));
        _personId = created.Id;
    }

    [TestCleanup]
    public void StopApp()
    {
        _client.Dispose();
        _factory.Dispose();

        if (!Directory.Exists(_dataDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(_dataDirectory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_dataDirectory, recursive: true);
    }

    private static Task<ExternalSearchResponse> Succeed(PersonSearchContext context, CancellationToken ct) =>
        Task.FromResult(new ExternalSearchResponse(
            [new ExternalSearchResult(
                $"Record for {context.Name.DisplayName}",
                "Possible match",
                new Uri("https://example.org/record/1"),
                ExternalResultKind.Record,
                new Dictionary<string, string> { ["Born"] = "1850" })],
            TotalCount: 25,
            Attribution: "Source: Fake Archive",
            MoreResultsUrl: new Uri("https://example.org/search?q=Walsh")));

    [TestMethod]
    public async Task Sources_lists_the_registered_sources_for_the_person()
    {
        var html = await _client.GetStringAsync($"/people/{_personId}/external-search");

        StringAssert.Contains(html, $"/people/{_personId}/external-search/works");
        StringAssert.Contains(html, $"/people/{_personId}/external-search/throws");
    }

    [TestMethod]
    public async Task Search_renders_records_with_total_attribution_and_more_link()
    {
        var html = await _client.GetStringAsync($"/people/{_personId}/external-search/works");

        StringAssert.Contains(html, "Record for Ann Walsh");
        StringAssert.Contains(html, "https://example.org/record/1");
        StringAssert.Contains(html, "Showing 1 of 25 matches");
        StringAssert.Contains(html, "https://example.org/search?q=Walsh");
        StringAssert.Contains(html, "Source: Fake Archive");
    }

    [TestMethod]
    public async Task A_source_that_throws_shows_an_error_instead_of_a_server_error()
    {
        var response = await _client.GetAsync($"/people/{_personId}/external-search/throws");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "This source failed");
    }

    [TestMethod]
    public async Task Unknown_person_or_source_is_a_404()
    {
        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, (await _client.GetAsync("/people/nobody/external-search")).StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, (await _client.GetAsync($"/people/{_personId}/external-search/bogus")).StatusCode);
    }

    private sealed class FakeSource(string id, Func<PersonSearchContext, CancellationToken, Task<ExternalSearchResponse>> search) : IExternalDataSource
    {
        public string Id => id;
        public string DisplayName => id;
        public string? Description => null;
        public bool IsAvailableFor(PersonSearchContext context) => true;
        public Task<ExternalSearchResponse> SearchAsync(PersonSearchContext context, CancellationToken ct) => search(context, ct);
    }
}
