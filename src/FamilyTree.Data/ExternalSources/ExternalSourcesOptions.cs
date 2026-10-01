namespace FamilyTree.Data.ExternalSources;

public sealed class ExternalSourcesOptions
{
    public const string SectionName = "ExternalSources";

    /// <summary>Per-source settings keyed by <see cref="IExternalDataSource.Id"/>. A source with no entry is enabled.</summary>
    public Dictionary<string, ExternalSourceOptions> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ExternalSourceOptions
{
    public bool Enabled { get; set; } = true;
}
