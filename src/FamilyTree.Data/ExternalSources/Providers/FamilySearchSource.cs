namespace FamilyTree.Data.ExternalSources.Providers;

/// <summary>
/// FamilySearch historical records and Family Tree. Its API needs an approved developer key and per-user sign-in, so
/// this links to FamilySearch's own search pages (a free FamilySearch account is needed to view results) and lists the
/// values to enter. Nothing is sent from the server.
/// </summary>
/// <remarks>
/// The query parameter names (<c>q.givenName</c>, <c>q.birthLikeDate.from</c> and so on) are the ones FamilySearch documents
/// for its API; whether the website's search pages honour them hasn't been confirmed. If they are ignored the user lands
/// on an empty search form, with the same values listed beside the link.
/// </remarks>
public sealed class FamilySearchSource : IExternalDataSource
{
    private const string Host = "https://www.familysearch.org";

    public string Id => "familysearch";

    public string DisplayName => "FamilySearch (historical records and Family Tree)";

    public string? Description => "Free account required to view results.";

    public bool IsAvailableFor(PersonSearchContext context) => true;

    public Task<ExternalSearchResponse> SearchAsync(PersonSearchContext context, CancellationToken ct)
    {
        var details = Details(context);
        var query = Query(context);

        ExternalSearchResult[] results =
        [
            new("Search historical records", "Census, births, marriages, deaths, immigration and more.", new Uri($"{Host}/search/record/results{query}"), ExternalResultKind.SearchLink, details),
            new("Search the Family Tree", "Profiles in FamilySearch's shared tree.", new Uri($"{Host}/search/tree/results{query}"), ExternalResultKind.SearchLink, details),
        ];

        return Task.FromResult(new ExternalSearchResponse(results));
    }

    private static string Query(PersonSearchContext context)
    {
        var parameters = new List<KeyValuePair<string, string>>();

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters.Add(new(key, value));
            }
        }

        Add("q.givenName", string.Join(' ', new[] { context.Name.First, context.Name.Middle }.Where(s => !string.IsNullOrWhiteSpace(s))));
        Add("q.surname", context.Name.Last);

        if (context.BirthYears is { } births)
        {
            Add("q.birthLikeDate.from", births.From.ToString());
            Add("q.birthLikeDate.to", births.To.ToString());
        }

        Add("q.birthLikePlace", context.BornPlace);

        if (context.DeathYears is { } deaths)
        {
            Add("q.deathLikeDate.from", deaths.From.ToString());
            Add("q.deathLikeDate.to", deaths.To.ToString());
        }

        Add("q.deathLikePlace", context.DiedPlace);

        return parameters.Count == 0
            ? ""
            : "?" + string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
    }

    private static Dictionary<string, string> Details(PersonSearchContext context)
    {
        var details = CensusSearchHelpers.NameDetails(context.Name);

        if (context.BirthYears is { } births)
        {
            details["Birth years"] = Range(births);
        }

        if (context.BornPlace is not null)
        {
            details["Birth place"] = context.BornPlace;
        }

        if (context.DeathYears is { } deaths)
        {
            details["Death years"] = Range(deaths);
        }

        if (context.DiedPlace is not null)
        {
            details["Death place"] = context.DiedPlace;
        }

        details["Note"] = "A free FamilySearch account is required to view results.";
        return details;
    }

    private static string Range(YearRange range) => range.From == range.To ? $"{range.From}" : $"{range.From}–{range.To}";
}
