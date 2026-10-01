namespace FamilyTree.Data.ExternalSources.Providers;

public sealed class WikiTreeOptions
{
    public const string SectionName = "ExternalSources:WikiTree";

    /// <summary>
    /// Identifies this app to WikiTree, which applies strict rate limits to queries without one.
    /// Letters, digits, underscore and hyphen only.
    /// </summary>
    public string AppId { get; set; } = "FamilyTree";
}
