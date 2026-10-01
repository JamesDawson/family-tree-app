using System.Text.Json;
using System.Text.RegularExpressions;
using FamilyTree.Data.ExternalSources.Http;

namespace FamilyTree.Data.ExternalSources.Providers;

/// <summary>
/// The UK National Archives' Discovery catalogue: item-level records (wills, military and naval records and so on)
/// that name the person. Terms: no more than 3,000 calls a day at one request a second, and API content must not be
/// cached or stored, so results are fetched fresh on each search and never persisted.
/// </summary>
public sealed partial class NationalArchivesDiscoverySource(IHttpClientFactory httpClientFactory) : IExternalDataSource
{
    public const string ClientName = "tna-discovery";
    public const string BaseAddress = "https://discovery.nationalarchives.gov.uk/";

    private const int MaxResults = 10;

    /// <summary>Records such as wills and probate follow a death, so look a little past it.</summary>
    private const int YearsAfterDeath = 25;

    private const int AssumedLifespan = 100;

    public string Id => "tna-discovery";

    public string DisplayName => "The National Archives (UK)";

    public string? Description => "Catalogue records naming the person, such as wills and military or naval records.";

    public bool IsAvailableFor(PersonSearchContext context) => true;

    public async Task<ExternalSearchResponse> SearchAsync(PersonSearchContext context, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(ClientName);
        var phrase = $"\"{context.Name.Last}, {context.Name.First}\"";
        var (from, to) = DateWindow(context);

        var call = await ExternalApiCaller.GetJsonAsync(client, BuildApiUri(phrase, from, to), DisplayName, ct);
        if (call.Error is not null)
        {
            return ExternalSearchResponse.Failed(call.Error);
        }

        using var json = call.Json!;
        var root = json.RootElement;
        var count = root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n) ? n : 0;

        var results = new List<ExternalSearchResult>();
        if (root.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
        {
            results.AddRange(records.EnumerateArray().Take(MaxResults).Select(ToResult).OfType<ExternalSearchResult>());
        }

        return new ExternalSearchResponse(
            results,
            TotalCount: count > results.Count ? count : null,
            Attribution: "Source: The National Archives, Discovery catalogue (Open Government Licence v3.0)",
            MoreResultsUrl: count > results.Count ? BuildSearchPageUri(phrase) : null);
    }

    /// <summary>The years to search: from birth to a little after death, or a plausible lifespan. Null where unknown.</summary>
    private static (int? From, int? To) DateWindow(PersonSearchContext context)
    {
        int? from = context.BirthYears?.From;
        int? to = context.DeathYears is { } deaths
            ? deaths.To + YearsAfterDeath
            : context.BirthYears is { } births ? births.To + AssumedLifespan : null;

        return (from, to is { } t ? Math.Min(t, DateTime.UtcNow.Year) : null);
    }

    private static Uri BuildApiUri(string phrase, int? from, int? to)
    {
        var query = new List<string>
        {
            $"sps.searchQuery={Uri.EscapeDataString(phrase)}",
            $"sps.resultsPageSize={MaxResults}",
        };

        if (from is { } f)
        {
            query.Add($"sps.dateFrom={f:D4}-01-01");
        }

        if (to is { } t)
        {
            query.Add($"sps.dateTo={t:D4}-12-31");
        }

        return new Uri($"API/search/records?{string.Join('&', query)}", UriKind.Relative);
    }

    // Only the query: the page can't be fetched without a browser, so the date parameters couldn't be checked.
    private static Uri BuildSearchPageUri(string phrase) =>
        new($"{BaseAddress}results/r?_q={Uri.EscapeDataString(phrase)}");

    private static ExternalSearchResult? ToResult(JsonElement record)
    {
        var id = Text(record, "id");
        var title = Text(record, "title");
        if (id is null || title is null)
        {
            return null;
        }

        var details = new Dictionary<string, string>();
        AddDetail(details, "Reference", Text(record, "reference"));
        AddDetail(details, "Dates", Text(record, "coveringDates"));
        AddDetail(details, "Department", Text(record, "department"));
        if (record.TryGetProperty("heldBy", out var heldBy) && heldBy.ValueKind == JsonValueKind.Array)
        {
            AddDetail(details, "Held by", string.Join(", ", heldBy.EnumerateArray().Select(h => h.GetString()).Where(h => !string.IsNullOrWhiteSpace(h))));
        }

        return new ExternalSearchResult(
            title,
            "Possible match: check that this is the same person",
            new Uri($"{BaseAddress}details/r/{Uri.EscapeDataString(id)}"),
            ExternalResultKind.Record,
            details);
    }

    private static void AddDetail(Dictionary<string, string> details, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            details[key] = value;
        }
    }

    /// <summary>Catalogue text can carry highlight markup; show it as plain text.</summary>
    private static string? Text(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = Markup().Replace(value.GetString() ?? "", "").Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Markup();
}
