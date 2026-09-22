namespace FamilyTree.Data.Git;

/// <summary>
/// All paths passed to this interface are relative to the data repository root, using forward slashes,
/// e.g. "people/jane-doe-1952.md".
/// </summary>
public interface IGitRepositoryService
{
    /// <summary>Creates the repository root and initializes a git repo (with a baseline commit) if one doesn't already exist. Safe to call repeatedly.</summary>
    void EnsureInitialized();

    /// <summary>Stages and commits the given files (which must already have been written to disk). Skips the commit silently if there's no actual diff.</summary>
    void CommitFiles(IReadOnlyCollection<string> relativePaths, string message, CommitAuthor author);

    /// <summary>Deletes the given file from disk and commits the deletion.</summary>
    void CommitDeletion(string relativePath, string message, CommitAuthor author);

    /// <summary>Returns the commits that touched the given path, newest first.</summary>
    IReadOnlyList<CommitInfo> GetHistory(string relativePath);

    /// <summary>Returns the file content of the given path as of the given commit.</summary>
    string GetFileContentAtCommit(string relativePath, string commitSha);

    /// <summary>Overwrites the current working-tree file with its content as of the given commit and creates a new forward commit (never rewrites history).</summary>
    void RevertFileToCommit(string relativePath, string commitSha, string message, CommitAuthor author);
}
