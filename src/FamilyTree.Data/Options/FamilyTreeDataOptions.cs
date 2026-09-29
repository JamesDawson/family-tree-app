namespace FamilyTree.Data.Options;

public sealed class FamilyTreeDataOptions
{
    public const string SectionName = "FamilyTreeData";

    /// <summary>Path to the data repository root. Relative paths are resolved against the app's content root.</summary>
    public required string RepositoryPath { get; set; }

    /// <summary>Git commit author name used when no other identity is available. Must be set before first run.</summary>
    public required string DefaultCommitAuthorName { get; set; }

    /// <summary>Git commit author email used when no other identity is available. Must be set before first run.</summary>
    public required string DefaultCommitAuthorEmail { get; set; }

    /// <summary>Optional HTTPS URL of the remote repository to clone into <see cref="RepositoryPath"/> on first run and push every commit to. Blank keeps the data local-only.</summary>
    public string? RemoteUrl { get; set; }

    /// <summary>Optional GitHub personal access token used to authorise cloning, fetching and pushing to <see cref="RemoteUrl"/>. Never stored in the repository's git config.</summary>
    public string? GitHubToken { get; set; }
}
