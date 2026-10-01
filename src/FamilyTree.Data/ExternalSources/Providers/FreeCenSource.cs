namespace FamilyTree.Data.ExternalSources.Providers;

/// <summary>
/// FreeCEN (Free UK Genealogy) transcribed census records for Great Britain, 1841-1911. Searches are submitted
/// by a POST form with a CSRF token, so this links to the search page and lists the values to enter.
/// </summary>
public sealed class FreeCenSource : IExternalDataSource
{
    private static readonly Uri SearchPage = new("https://www.freecen.org.uk/search_queries/new");

    private static readonly int[] CensusYears = [1841, 1851, 1861, 1871, 1881, 1891, 1901, 1911];

    public string Id => "freecen";

    public string DisplayName => "FreeCEN (Great Britain census, 1841–1911)";

    public string? Description => "Free volunteer-transcribed census records. Coverage varies by county and year.";

    public bool IsAvailableFor(PersonSearchContext context)
    {
        var irish = CensusSearchHelpers.LooksIrish(context.BornPlace) || CensusSearchHelpers.LooksIrish(context.DiedPlace);
        return !irish && CensusSearchHelpers.CensusYearsAlive(context, CensusYears).Count > 0;
    }

    public Task<ExternalSearchResponse> SearchAsync(PersonSearchContext context, CancellationToken ct)
    {
        var years = CensusSearchHelpers.CensusYearsAlive(context, CensusYears);
        var details = CensusSearchHelpers.NameDetails(context.Name);

        details["Census year"] = string.Join(", ", years);

        if (context.BirthYears is { } births)
        {
            var mid = (births.From + births.To) / 2;
            details["Age"] = string.Join(", ", years.Select(y => $"{y - mid} in {y}"));
        }

        if (!string.IsNullOrWhiteSpace(context.BornPlace))
        {
            details["Birth place"] = context.BornPlace;
        }

        details["Tip"] = "Tick the fuzzy-match option: transcribed spellings vary.";

        var result = new ExternalSearchResult(
            "Open the FreeCEN search",
            "Enter these values in the search form; leave the census year blank to search all years.",
            SearchPage,
            ExternalResultKind.SearchLink,
            details);

        return Task.FromResult(new ExternalSearchResponse([result]));
    }
}
