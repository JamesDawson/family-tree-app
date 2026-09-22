namespace FamilyTree.Data.Options;

/// <summary>Resolved (absolute) filesystem locations for the data repository. Registered once at startup.</summary>
public sealed class FamilyTreeDataPaths
{
    public required string RootPath { get; init; }

    public string PeopleDirectory => Path.Combine(RootPath, "people");

    public string ToAbsolutePath(string relativePath) => Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
