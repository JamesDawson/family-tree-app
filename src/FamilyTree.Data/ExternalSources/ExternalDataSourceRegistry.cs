using Microsoft.Extensions.Options;

namespace FamilyTree.Data.ExternalSources;

public interface IExternalDataSourceRegistry
{
    /// <summary>Enabled sources that apply to the person.</summary>
    IReadOnlyList<IExternalDataSource> GetAvailable(PersonSearchContext context);

    /// <summary>An enabled source by id, or null.</summary>
    IExternalDataSource? Find(string sourceId);
}

public sealed class ExternalDataSourceRegistry(
    IEnumerable<IExternalDataSource> sources,
    IOptions<ExternalSourcesOptions> options) : IExternalDataSourceRegistry
{
    private readonly IReadOnlyList<IExternalDataSource> enabled =
    [
        .. sources.Where(s => !options.Value.Sources.TryGetValue(s.Id, out var o) || o.Enabled),
    ];

    public IReadOnlyList<IExternalDataSource> GetAvailable(PersonSearchContext context) =>
        [.. enabled.Where(s => s.IsAvailableFor(context))];

    public IExternalDataSource? Find(string sourceId) =>
        enabled.FirstOrDefault(s => s.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase));
}
