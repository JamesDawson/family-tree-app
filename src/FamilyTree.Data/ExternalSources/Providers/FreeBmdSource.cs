namespace FamilyTree.Data.ExternalSources.Providers;

/// <summary>
/// FreeBMD (Free UK Genealogy) transcribed civil registration indexes of births, marriages and deaths for England
/// and Wales from 1837. Searches are submitted by a POST form with a CSRF token, and the site's robots.txt disallows
/// automated access to the search endpoint, so this links to the search form and lists the values to enter.
/// </summary>
public sealed class FreeBmdSource : IExternalDataSource
{
    private const int FirstRegistrationYear = 1837;

    private static readonly Uri SearchPage = new("https://www.freebmd2.org.uk/search_queries/new");

    public string Id => "freebmd";

    public string DisplayName => "FreeBMD (England & Wales births, marriages, deaths)";

    public string? Description => "Free civil registration indexes from 1837.";

    public bool IsAvailableFor(PersonSearchContext context)
    {
        var irish = CensusSearchHelpers.LooksIrish(context.BornPlace) || CensusSearchHelpers.LooksIrish(context.DiedPlace);
        var diedBeforeRegistration = context.DeathYears is { } deaths && deaths.To < FirstRegistrationYear;
        return !irish && !diedBeforeRegistration;
    }

    public Task<ExternalSearchResponse> SearchAsync(PersonSearchContext context, CancellationToken ct)
    {
        var details = CensusSearchHelpers.NameDetails(context.Name);

        // The index is searched by event year, so give the range for each event we have a date for.
        if (context.BirthYears is { } births && births.To >= FirstRegistrationYear)
        {
            details["Birth years"] = FormatRange(births);
        }

        if (context.DeathYears is { } deaths)
        {
            details["Death years"] = FormatRange(deaths);
        }

        if (context.Spouses.Count > 0)
        {
            details["Spouse forename (for marriages)"] = string.Join(", ", context.Spouses.Select(s => s.First).Distinct());
        }

        var result = new ExternalSearchResult(
            "Open the FreeBMD search",
            "Enter these values in the search form, and tick the record types (births, marriages, deaths) you want.",
            SearchPage,
            ExternalResultKind.SearchLink,
            details);

        return Task.FromResult(new ExternalSearchResponse([result]));
    }

    private static string FormatRange(YearRange range) =>
        range.From == range.To ? $"{range.From}" : $"{Math.Max(range.From, FirstRegistrationYear)}–{range.To}";
}
