using System.Net;
using System.Text;
using FamilyTree.Data.ExternalSources;
using FamilyTree.Data.ExternalSources.Providers;
using FamilyTree.Data.Models;

namespace FamilyTree.Data.Tests;

[TestClass]
public sealed class ExternalApiSourcesTests
{
    // Shaped like real responses captured from api.wikitree.com and discovery.nationalarchives.gov.uk.
    private const string WikiTreeBody = """
        [{"status":0,"matches":[
          {"Name":"Murphy-1","FirstName":"John","MiddleName":"","LastNameAtBirth":"Murphy","BirthDate":"1853-00-00","DeathDate":"1920-05-01","BirthLocation":"Cork, Ireland","DeathLocation":"","IsLiving":0,"index":0},
          {"Name":"Murphy-2","FirstName":"John","MiddleName":"Patrick","LastNameAtBirth":"Murphy","BirthDate":"1880-04-00","DeathDate":"0000-00-00","BirthLocation":"Leeds, England","DeathLocation":"","IsLiving":0,"index":1},
          {"Name":"Murphy-3","FirstName":"John","MiddleName":"","LastNameAtBirth":"Murphy","BirthDate":"1881-00-00","DeathDate":"","BirthLocation":"","DeathLocation":"","IsLiving":1,"index":2}
        ],"total":3,"start":0,"limit":20}]
        """;

    private const string TnaBody = """
        {"records":[
          {"id":"D7479051","title":"Will of <span class=\"highlight\">Murphy</span>, John Ship Name: Edgar.","description":"Will of Murphy, John Ship Name: Edgar.","reference":"ADM 48/59/142","coveringDates":"09 July 1794","department":"ADM","heldBy":["The National Archives, Kew"],"catalogueLevel":7},
          {"id":"D7479052","title":"Will of Murphy, John Ship Name: Excellent.","reference":"ADM 48/59/143","coveringDates":"08 January 1798","department":"ADM","heldBy":[]}
        ],"count":13,"nextBatchMark":""}
        """;

    private static PersonSearchContext Context(string? born = "1880", string? died = null, PersonName? name = null) =>
        new("p1", name ?? new PersonName("John", null, "Murphy", null), Sex.Male,
            PartialDate.Parse(born), null, PartialDate.Parse(died), null, [], []);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (IHttpClientFactory Factory, List<Uri> Requests) Fake(Func<HttpRequestMessage, HttpResponseMessage> respond, string baseAddress)
    {
        var requests = new List<Uri>();
        var handler = new FakeHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return respond(request);
        });
        return (new FakeFactory(new HttpClient(handler) { BaseAddress = new Uri(baseAddress) }), requests);
    }

    private static WikiTreeSource WikiTree(IHttpClientFactory factory, string appId = "TestApp") =>
        new(factory, Microsoft.Extensions.Options.Options.Create(new WikiTreeOptions { AppId = appId }));

    // ---- WikiTree ----

    [TestMethod]
    public async Task WikiTree_maps_profiles_dropping_living_and_ranking_by_birth_year()
    {
        var (factory, _) = Fake(_ => Json(WikiTreeBody), WikiTreeSource.BaseAddress);

        var response = await WikiTree(factory).SearchAsync(Context(born: "1880"), CancellationToken.None);

        Assert.IsNull(response.Error);
        Assert.AreEqual(2, response.Results.Count);
        var first = response.Results[0];
        Assert.AreEqual("John Patrick Murphy", first.Title);
        Assert.AreEqual(ExternalResultKind.Record, first.Kind);
        Assert.AreEqual("https://www.wikitree.com/wiki/Murphy-2", first.Url!.ToString());
        Assert.AreEqual("1880-04, Leeds, England", first.Details!["Born"]);
        Assert.AreEqual("John Murphy", response.Results[1].Title);
        Assert.AreEqual("1853", response.Results[1].Details!["Born"].Split(',')[0]);
        Assert.AreEqual("Source: WikiTree", response.Attribution);
    }

    [TestMethod]
    public async Task WikiTree_sends_name_birth_year_spread_and_app_id()
    {
        var (factory, requests) = Fake(_ => Json(WikiTreeBody), WikiTreeSource.BaseAddress);

        await WikiTree(factory, appId: "TestApp").SearchAsync(Context(born: "abt 1880"), CancellationToken.None);

        var uri = requests.Single();
        StringAssert.Contains(uri.Query, "action=searchPerson");
        StringAssert.Contains(uri.Query, "appId=TestApp");
        StringAssert.Contains(uri.Query, "FirstName=John");
        StringAssert.Contains(uri.Query, "LastName=Murphy");
        StringAssert.Contains(uri.Query, "BirthDate=1880");
        StringAssert.Contains(uri.Query, "dateSpread=5");
    }

    [TestMethod]
    public async Task WikiTree_omits_the_date_when_birth_is_unknown()
    {
        var (factory, requests) = Fake(_ => Json(WikiTreeBody), WikiTreeSource.BaseAddress);

        await WikiTree(factory).SearchAsync(Context(born: null), CancellationToken.None);

        Assert.DoesNotContain("BirthDate=", requests.Single().Query);
    }

    [TestMethod]
    public async Task WikiTree_also_searches_the_maiden_name_and_deduplicates()
    {
        var (factory, requests) = Fake(_ => Json(WikiTreeBody), WikiTreeSource.BaseAddress);
        var name = new PersonName("Mary", null, "Walsh", "Murphy");

        var response = await WikiTree(factory).SearchAsync(Context(name: name), CancellationToken.None);

        Assert.HasCount(2, requests);
        StringAssert.Contains(requests[0].Query, "LastName=Walsh");
        StringAssert.Contains(requests[1].Query, "LastName=Murphy");
        // Both queries return the same profiles; each appears once.
        Assert.HasCount(2, response.Results);
    }

    [TestMethod]
    public async Task WikiTree_reports_total_when_more_matches_exist()
    {
        var body = """[{"status":0,"matches":[{"Name":"Murphy-1","FirstName":"John","LastNameAtBirth":"Murphy","IsLiving":0}],"total":25}]""";
        var (factory, _) = Fake(_ => Json(body), WikiTreeSource.BaseAddress);

        var response = await WikiTree(factory).SearchAsync(Context(), CancellationToken.None);

        Assert.AreEqual(25, response.TotalCount);
    }

    [TestMethod]
    public async Task WikiTree_reports_rate_limiting_with_retry_after()
    {
        var (factory, _) = Fake(_ =>
        {
            var response = Json("", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        }, WikiTreeSource.BaseAddress);

        var response = await WikiTree(factory).SearchAsync(Context(), CancellationToken.None);

        Assert.IsEmpty(response.Results);
        StringAssert.Contains(response.Error, "limiting requests");
        StringAssert.Contains(response.Error, "30 seconds");
    }

    [TestMethod]
    public async Task Failures_become_error_messages_rather_than_exceptions()
    {
        var (serverError, _) = Fake(_ => Json("", HttpStatusCode.InternalServerError), WikiTreeSource.BaseAddress);
        var (unreachable, _) = Fake(_ => throw new HttpRequestException("boom"), WikiTreeSource.BaseAddress);
        var (timeout, _) = Fake(_ => throw new TaskCanceledException("timed out"), WikiTreeSource.BaseAddress);
        var (garbage, _) = Fake(_ => Json("<html>not json"), WikiTreeSource.BaseAddress);

        StringAssert.Contains((await WikiTree(serverError).SearchAsync(Context(), CancellationToken.None)).Error, "HTTP 500");
        StringAssert.Contains((await WikiTree(unreachable).SearchAsync(Context(), CancellationToken.None)).Error, "could not be reached");
        StringAssert.Contains((await WikiTree(timeout).SearchAsync(Context(), CancellationToken.None)).Error, "did not respond in time");
        StringAssert.Contains((await WikiTree(garbage).SearchAsync(Context(), CancellationToken.None)).Error, "couldn't be read");
    }

    [TestMethod]
    public async Task Caller_cancellation_still_propagates()
    {
        var (factory, _) = Fake(_ => throw new TaskCanceledException(), WikiTreeSource.BaseAddress);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => WikiTree(factory).SearchAsync(Context(), cts.Token));
    }

    // ---- TNA Discovery ----

    [TestMethod]
    public async Task Tna_maps_records_and_strips_markup()
    {
        var (factory, _) = Fake(_ => Json(TnaBody), NationalArchivesDiscoverySource.BaseAddress);

        var response = await new NationalArchivesDiscoverySource(factory).SearchAsync(Context(), CancellationToken.None);

        Assert.IsNull(response.Error);
        Assert.HasCount(2, response.Results);
        var first = response.Results[0];
        Assert.AreEqual("Will of Murphy, John Ship Name: Edgar.", first.Title);
        Assert.AreEqual("https://discovery.nationalarchives.gov.uk/details/r/D7479051", first.Url!.ToString());
        Assert.AreEqual("ADM 48/59/142", first.Details!["Reference"]);
        Assert.AreEqual("09 July 1794", first.Details["Dates"]);
        Assert.AreEqual("The National Archives, Kew", first.Details["Held by"]);
        Assert.DoesNotContain("Held by", response.Results[1].Details!.Keys.ToList());
        StringAssert.Contains(response.Attribution, "National Archives");
    }

    [TestMethod]
    public async Task Tna_searches_the_quoted_name_within_the_persons_lifetime()
    {
        var (factory, requests) = Fake(_ => Json(TnaBody), NationalArchivesDiscoverySource.BaseAddress);

        await new NationalArchivesDiscoverySource(factory).SearchAsync(Context(born: "1790", died: "1850"), CancellationToken.None);

        var query = Uri.UnescapeDataString(requests.Single().Query);
        StringAssert.Contains(query, "sps.searchQuery=\"Murphy, John\"");
        StringAssert.Contains(query, "sps.dateFrom=1790-01-01");
        StringAssert.Contains(query, "sps.dateTo=1875-12-31");
        StringAssert.Contains(query, "sps.resultsPageSize=10");
    }

    [TestMethod]
    public async Task Tna_leaves_out_dates_when_none_are_known()
    {
        var (factory, requests) = Fake(_ => Json(TnaBody), NationalArchivesDiscoverySource.BaseAddress);

        await new NationalArchivesDiscoverySource(factory).SearchAsync(Context(born: null), CancellationToken.None);

        Assert.DoesNotContain("sps.date", requests.Single().Query);
    }

    [TestMethod]
    public async Task Tna_reports_the_total_and_links_to_the_full_search_when_truncated()
    {
        var (factory, _) = Fake(_ => Json(TnaBody), NationalArchivesDiscoverySource.BaseAddress);

        var response = await new NationalArchivesDiscoverySource(factory).SearchAsync(Context(), CancellationToken.None);

        Assert.AreEqual(13, response.TotalCount);
        StringAssert.Contains(Uri.UnescapeDataString(response.MoreResultsUrl!.ToString()), "_q=\"Murphy, John\"");
    }

    [TestMethod]
    public async Task Tna_failure_becomes_an_error_message()
    {
        var (factory, _) = Fake(_ => Json("", HttpStatusCode.ServiceUnavailable), NationalArchivesDiscoverySource.BaseAddress);

        var response = await new NationalArchivesDiscoverySource(factory).SearchAsync(Context(), CancellationToken.None);

        StringAssert.Contains(response.Error, "HTTP 503");
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class FakeFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
