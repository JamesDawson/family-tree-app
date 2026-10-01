using FamilyTree.Data.ExternalSources;

namespace FamilyTree.Web.Models;

public sealed class ExternalSourceListViewModel
{
    public required string PersonId { get; init; }
    public required IReadOnlyList<IExternalDataSource> Sources { get; init; }
}

public sealed class ExternalSearchResultsViewModel
{
    public required string SourceName { get; init; }
    public required ExternalSearchResponse Response { get; init; }
}
