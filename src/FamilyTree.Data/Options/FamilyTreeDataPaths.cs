namespace FamilyTree.Data.Options;

/// <summary>Resolved (absolute) filesystem locations for the data repository. Registered once at startup.</summary>
public sealed class FamilyTreeDataPaths
{
    public required string RootPath { get; init; }

    public string PeopleDirectory => Path.Combine(RootPath, "people");

    /// <summary>Uploaded images. Lives inside the data volume but is kept out of git (see <c>LibGit2GitRepositoryService.EnsureInitialized</c>).</summary>
    public string ImagesDirectory => Path.Combine(RootPath, "images");

    public string ToAbsolutePath(string relativePath) => Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
