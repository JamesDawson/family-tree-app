using System.Text.Json;
using FamilyTree.Data.ExternalSources.Http;
using Microsoft.Extensions.Options;

namespace FamilyTree.Data.ExternalSources.Providers;

/// <summary>WikiTree's collaborative family tree: profiles of possible ancestors and relatives, via its public API.</summary>
public sealed class WikiTreeSource(IHttpClientFactory httpClientFactory, IOptions<WikiTreeOptions> options) : IExternalDataSource
{
    public const string ClientName = "wikitree";
    public const string BaseAddress = "https://api.wikitree.com/";

    private const int MaxResults = 10;
    private const string Fields = "Name,FirstName,MiddleName,LastNameAtBirth,BirthDate,DeathDate,BirthLocation,DeathLocation,IsLiving";

    public string Id => "wikitree";

    public string DisplayName => "WikiTree";

    public string? Description => "Profiles in WikiTree's shared family tree that may match this person.";

    public bool IsAvailableFor(PersonSearchContext context) => true;

    public async Task<ExternalSearchResponse> SearchAsync(PersonSearchContext context, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(ClientName);

        // Women are often recorded under their birth surname, so search that as well when we have one.
        var surnames = new[] { context.Name.Last, context.Name.MaidenName }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>();

        var profiles = new Dictionary<string, Profile>();
        var total = 0;

        foreach (var surname in surnames)
        {
            var call = await ExternalApiCaller.GetJsonAsync(client, BuildUri(context, surname), DisplayName, ct);
            if (call.Error is not null)
            {
                return ExternalSearchResponse.Failed(call.Error);
            }

            using (call.Json)
            {
                total += Parse(call.Json!.RootElement, profiles);
            }
        }

        var center = context.BirthYears is { } births ? (births.From + births.To) / 2 : (int?)null;
        var ranked = profiles.Values
            .OrderBy(p => center is { } c && p.BirthYear is { } y ? Math.Abs(y - c) : int.MaxValue)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .Take(MaxResults)
            .Select(ToResult)
            .ToList();

        return new ExternalSearchResponse(
            ranked,
            TotalCount: total > ranked.Count ? total : null,
            Attribution: "Source: WikiTree");
    }

    private Uri BuildUri(PersonSearchContext context, string surname)
    {
        var query = new List<string>
        {
            "action=searchPerson",
            $"appId={Uri.EscapeDataString(options.Value.AppId)}",
            $"FirstName={Uri.EscapeDataString(context.Name.First)}",
            $"LastName={Uri.EscapeDataString(surname)}",
            $"limit={MaxResults * 2}",
            $"fields={Fields}",
        };

        if (context.BirthYears is { } births)
        {
            // WikiTree accepts a year with a spread of 1-20 years either side.
            query.Add($"BirthDate={(births.From + births.To) / 2}");
            query.Add($"dateSpread={Math.Clamp((births.To - births.From) / 2 + 3, 1, 20)}");
        }

        return new Uri($"api.php?{string.Join('&', query)}", UriKind.Relative);
    }

    /// <summary>Adds the matches to <paramref name="profiles"/> (living people excluded) and returns the API's total.</summary>
    private static int Parse(JsonElement root, Dictionary<string, Profile> profiles)
    {
        // The response is a one-element array: [{ "status": 0, "matches": [...], "total": n }].
        var body = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 ? root[0] : root;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        foreach (var match in matches.EnumerateArray())
        {
            var name = Text(match, "Name");
            if (name is null || IsLiving(match))
            {
                continue;
            }

            profiles.TryAdd(name, new Profile(
                name,
                string.Join(' ', new[] { Text(match, "FirstName"), Text(match, "MiddleName"), Text(match, "LastNameAtBirth") }.Where(s => s is not null)),
                FormatDate(Text(match, "BirthDate")),
                Text(match, "BirthLocation"),
                FormatDate(Text(match, "DeathDate")),
                Text(match, "DeathLocation")));
        }

        return body.TryGetProperty("total", out var total) && total.TryGetInt32(out var count) ? count : 0;
    }

    private static ExternalSearchResult ToResult(Profile p)
    {
        var details = new Dictionary<string, string>();
        if (p.Born is not null)
        {
            details["Born"] = p.BornPlace is null ? p.Born : $"{p.Born}, {p.BornPlace}";
        }
        else if (p.BornPlace is not null)
        {
            details["Born"] = p.BornPlace;
        }

        if (p.Died is not null)
        {
            details["Died"] = p.DiedPlace is null ? p.Died : $"{p.Died}, {p.DiedPlace}";
        }
        else if (p.DiedPlace is not null)
        {
            details["Died"] = p.DiedPlace;
        }

        details["WikiTree ID"] = p.Name;

        return new ExternalSearchResult(
            string.IsNullOrWhiteSpace(p.DisplayName) ? p.Name : p.DisplayName,
            "Possible match",
            new Uri($"https://www.wikitree.com/wiki/{Uri.EscapeDataString(p.Name)}"),
            ExternalResultKind.Record,
            details);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static bool IsLiving(JsonElement match) =>
        match.TryGetProperty("IsLiving", out var value)
        && value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var n) && n != 0,
            JsonValueKind.True => true,
            JsonValueKind.String => value.GetString() is "1" or "true",
            _ => false,
        };

    /// <summary>WikiTree pads unknown parts with zeros ("1853-00-00"); show only what's known.</summary>
    private static string? FormatDate(string? date)
    {
        if (date is null || date.StartsWith("0000", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = date.Split('-');
        var known = parts.TakeWhile((p, i) => i == 0 || p != "00").ToArray();
        return string.Join('-', known);
    }

    private sealed record Profile(string Name, string DisplayName, string? Born, string? BornPlace, string? Died, string? DiedPlace)
    {
        public int? BirthYear => Born is { Length: >= 4 } b && int.TryParse(b[..4], out var y) ? y : null;
    }
}
