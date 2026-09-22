using FamilyTree.Data.Git;
using FamilyTree.Data.Options;
using Microsoft.Extensions.Options;

namespace FamilyTree.Web.Infrastructure;

/// <summary>Provides the git commit author identity to use for changes made through the app. For
/// Phase 1 (no auth) this is always the configured default; a future auth phase would swap this
/// implementation for one that reads the signed-in user's identity, without touching any controller.</summary>
public interface ICommitAuthorProvider
{
    CommitAuthor Current { get; }
}

public sealed class DefaultCommitAuthorProvider(IOptions<FamilyTreeDataOptions> options) : ICommitAuthorProvider
{
    public CommitAuthor Current => new(options.Value.DefaultCommitAuthorName, options.Value.DefaultCommitAuthorEmail);
}
