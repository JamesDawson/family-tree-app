namespace FamilyTree.Data.ExternalSources.Providers;

/// <summary>
/// National Archives of Ireland census search (1901, 1911, 1926). The site has no API and its search form can't be
/// prefilled from a URL, so this links to the search page and lists the values to enter.
/// </summary>
public sealed class NationalArchivesIrelandCensusSource : IExternalDataSource
{
    internal static readonly Uri SearchPage = new("https://nationalarchives.ie/collections/search-the-census/");

    private static readonly int[] CensusYears = [1901, 1911, 1926];

    public string Id => "nai-census";

    public string DisplayName => "Irish census (National Archives of Ireland)";

    public string? Description => "Free, searchable household returns for 1901, 1911 and 1926.";

    public bool IsAvailableFor(PersonSearchContext context)
    {
        var places = new[] { context.BornPlace, context.DiedPlace }.Where(p => p is not null).ToList();
        var placeFits = places.Count == 0 || places.Any(CensusSearchHelpers.LooksIrish);
        return placeFits && CensusSearchHelpers.CensusYearsAlive(context, CensusYears).Count > 0;
    }

    public Task<ExternalSearchResponse> SearchAsync(PersonSearchContext context, CancellationToken ct)
    {
        var years = CensusSearchHelpers.CensusYearsAlive(context, CensusYears);
        var details = CensusSearchHelpers.NameDetails(context.Name);

        details["Census year"] = string.Join(", ", years);

        // The form's age is "+/- 5 years", so one middle estimate per census year is enough.
        if (context.BirthYears is { } births)
        {
            var mid = (births.From + births.To) / 2;
            details["Age"] = string.Join(", ", years.Select(y => $"{y - mid} in {y}"));
        }

        var county = CensusSearchHelpers.FindIrishCounty(context.BornPlace)
            ?? CensusSearchHelpers.FindIrishCounty(context.DiedPlace);
        if (county is not null)
        {
            details["County"] = county;
        }

        details["Sex"] = context.Sex switch { Models.Sex.Male => "Male", Models.Sex.Female => "Female", _ => "Both" };

        var result = new ExternalSearchResult(
            "Open the census search",
            "Enter these values in the search form. 1926 returns are searched separately from 1901 and 1911.",
            SearchPage,
            ExternalResultKind.SearchLink,
            details);

        return Task.FromResult(new ExternalSearchResponse([result]));
    }
}
