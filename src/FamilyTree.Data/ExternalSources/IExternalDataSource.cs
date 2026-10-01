namespace FamilyTree.Data.ExternalSources;

/// <summary>
/// A searchable external data source (an API, or a website reached by a deep link).
/// Implementations are interchangeable: register one more <see cref="IExternalDataSource"/> and it appears on the person page.
/// </summary>
public interface IExternalDataSource
{
    /// <summary>Stable slug used in URLs and configuration, e.g. "nai-census".</summary>
    string Id { get; }

    string DisplayName { get; }

    string? Description { get; }

    /// <summary>Whether this source is worth offering for the person (e.g. an Ireland-only source for a person born elsewhere).</summary>
    bool IsAvailableFor(PersonSearchContext context);

    /// <summary>
    /// Runs the search. Implementations should report expected failures (network, rate limits) through
    /// <see cref="ExternalSearchResponse.Error"/> rather than throwing.
    /// </summary>
    Task<ExternalSearchResponse> SearchAsync(PersonSearchContext context, CancellationToken ct);
}
