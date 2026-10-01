using FamilyTree.Data.Models;

namespace FamilyTree.Data.ExternalSources;

/// <summary>An inclusive range of years, used where a person's dates are approximate.</summary>
public sealed record YearRange(int From, int To)
{
    /// <summary>Widens a <see cref="PartialDate"/> according to its qualifier.</summary>
    public static YearRange Around(PartialDate date) => date.Qualifier switch
    {
        DateQualifier.About or DateQualifier.Estimated => new YearRange(date.Year - 2, date.Year + 2),
        DateQualifier.Before => new YearRange(date.Year - 10, date.Year),
        DateQualifier.After => new YearRange(date.Year, date.Year + 10),
        _ => new YearRange(date.Year, date.Year),
    };
}

/// <summary>The facts about a person that a source may use to build a query. Deliberately independent of the storage model.</summary>
public sealed record PersonSearchContext(
    string PersonId,
    PersonName Name,
    Sex Sex,
    PartialDate? BornOn,
    string? BornPlace,
    PartialDate? DiedOn,
    string? DiedPlace,
    IReadOnlyList<PersonName> Parents,
    IReadOnlyList<PersonName> Spouses)
{
    public YearRange? BirthYears => BornOn is null ? null : YearRange.Around(BornOn);

    public YearRange? DeathYears => DiedOn is null ? null : YearRange.Around(DiedOn);
}

public enum ExternalResultKind
{
    /// <summary>A prebuilt search on the source's own site (for sources with no API).</summary>
    SearchLink,

    /// <summary>A record returned by the source.</summary>
    Record,

    /// <summary>Aggregate statistics about an area rather than a person.</summary>
    AreaStatistic,
}

public sealed record ExternalSearchResult(
    string Title,
    string? Summary,
    Uri? Url,
    ExternalResultKind Kind,
    IReadOnlyDictionary<string, string>? Details = null);

/// <param name="TotalCount">How many matches the source found, when that exceeds the results returned.</param>
/// <param name="Attribution">Where the data came from, shown beneath the results.</param>
/// <param name="MoreResultsUrl">The same search on the source's own site, for when the results are truncated.</param>
public sealed record ExternalSearchResponse(
    IReadOnlyList<ExternalSearchResult> Results,
    string? Error = null,
    int? TotalCount = null,
    string? Attribution = null,
    Uri? MoreResultsUrl = null)
{
    public static ExternalSearchResponse Failed(string error) => new([], error);
}
