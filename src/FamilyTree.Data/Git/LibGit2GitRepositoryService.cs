using FamilyTree.Data.Options;
using LibGit2Sharp;
using Microsoft.Extensions.Options;
using GitRepository = LibGit2Sharp.Repository;

namespace FamilyTree.Data.Git;

public sealed class LibGit2GitRepositoryService : IGitRepositoryService
{
    private const string ReadmeContent =
        """
        # Family Tree Data

        This folder is a git repository holding your family tree records. Each person is
        one Markdown file under `people/`, with YAML front matter for structured facts
        (names, dates, relationships) and a Markdown body for free-text notes.

        Every change made through the app is committed here automatically, so the full
        history of every record is preserved and can be rolled back from the app's
        History page for that person.
        """;

    private readonly FamilyTreeDataPaths _paths;
    private readonly FamilyTreeDataOptions _options;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public LibGit2GitRepositoryService(FamilyTreeDataPaths paths, IOptions<FamilyTreeDataOptions> options)
    {
        _paths = paths;
        _options = options.Value;
    }

    public void EnsureInitialized()
    {
        Directory.CreateDirectory(_paths.RootPath);
        Directory.CreateDirectory(_paths.PeopleDirectory);

        if (GitRepository.IsValid(_paths.RootPath))
        {
            return;
        }

        GitRepository.Init(_paths.RootPath);

        File.WriteAllText(Path.Combine(_paths.RootPath, ".gitignore"), "Thumbs.db\n.DS_Store\n");
        File.WriteAllText(Path.Combine(_paths.RootPath, "README.md"), ReadmeContent);

        using var repo = new GitRepository(_paths.RootPath);
        Commands.Stage(repo, "*");
        var signature = DefaultSignature();
        repo.Commit("Initialize family tree data repository", signature, signature);
    }

    public void CommitFiles(IReadOnlyCollection<string> relativePaths, string message, CommitAuthor author)
    {
        _writeLock.Wait();
        try
        {
            using var repo = new GitRepository(_paths.RootPath);

            foreach (var relativePath in relativePaths)
            {
                Commands.Stage(repo, Normalize(relativePath));
            }

            if (!HasStagedChanges(repo))
            {
                return;
            }

            var signature = ToSignature(author);
            repo.Commit(message, signature, signature);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void CommitDeletion(string relativePath, string message, CommitAuthor author)
    {
        _writeLock.Wait();
        try
        {
            var fullPath = _paths.ToAbsolutePath(relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            using var repo = new GitRepository(_paths.RootPath);
            Commands.Stage(repo, Normalize(relativePath));

            if (!HasStagedChanges(repo))
            {
                return;
            }

            var signature = ToSignature(author);
            repo.Commit(message, signature, signature);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public IReadOnlyList<CommitInfo> GetHistory(string relativePath)
    {
        // Deliberately hand-rolled rather than LibGit2Sharp's own Commits.QueryBy(path) helper,
        // which has a known bug (KeyNotFoundException inside its internal FileHistory walk) on
        // some straightforward linear histories. Walking repo.Commits (already newest-first,
        // reachable-from-HEAD) and diffing each commit's blob at the path against its first
        // parent's is simple, correct, and avoids that code path entirely.
        using var repo = new GitRepository(_paths.RootPath);
        var normalizedPath = Normalize(relativePath);
        var result = new List<CommitInfo>();

        foreach (var commit in repo.Commits)
        {
            var currentBlobId = BlobIdAt(commit, normalizedPath);
            var parentBlobId = commit.Parents.FirstOrDefault() is { } parent ? BlobIdAt(parent, normalizedPath) : null;

            if (currentBlobId != parentBlobId)
            {
                result.Add(ToCommitInfo(commit));
            }
        }

        return result;
    }

    private static string? BlobIdAt(Commit commit, string normalizedPath) => commit[normalizedPath]?.Target.Sha;

    public string GetFileContentAtCommit(string relativePath, string commitSha)
    {
        using var repo = new GitRepository(_paths.RootPath);
        return ReadBlobText(repo, relativePath, commitSha);
    }

    public void RevertFileToCommit(string relativePath, string commitSha, string message, CommitAuthor author)
    {
        _writeLock.Wait();
        try
        {
            using var repo = new GitRepository(_paths.RootPath);
            var historicContent = ReadBlobText(repo, relativePath, commitSha);

            var fullPath = _paths.ToAbsolutePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, historicContent);

            Commands.Stage(repo, Normalize(relativePath));

            if (!HasStagedChanges(repo))
            {
                return;
            }

            var signature = ToSignature(author);
            repo.Commit(message, signature, signature);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string ReadBlobText(GitRepository repo, string relativePath, string commitSha)
    {
        var commit = repo.Lookup<Commit>(commitSha)
            ?? throw new InvalidOperationException($"Commit '{commitSha}' was not found.");

        var normalizedPath = Normalize(relativePath);
        var entry = commit[normalizedPath]
            ?? throw new FileNotFoundException($"Path '{relativePath}' did not exist at commit '{commitSha}'.");

        if (entry.Target is not Blob blob)
        {
            throw new InvalidOperationException($"Path '{relativePath}' at commit '{commitSha}' is not a file.");
        }

        return blob.GetContentText();
    }

    private static bool HasStagedChanges(GitRepository repo)
    {
        var tip = repo.Head.Tip;
        var changes = tip is null
            ? repo.Diff.Compare<TreeChanges>(null, DiffTargets.Index)
            : repo.Diff.Compare<TreeChanges>(tip.Tree, DiffTargets.Index);

        return changes.Any();
    }

    private static CommitInfo ToCommitInfo(Commit commit) => new(
        Sha: commit.Sha,
        ShortSha: commit.Sha[..7],
        Message: commit.MessageShort,
        AuthorName: commit.Author.Name,
        When: commit.Author.When);

    private static Signature ToSignature(CommitAuthor author) => new(author.Name, author.Email, DateTimeOffset.Now);

    private Signature DefaultSignature() => new(_options.DefaultCommitAuthorName, _options.DefaultCommitAuthorEmail, DateTimeOffset.Now);

    private static string Normalize(string relativePath) => relativePath.Replace('\\', '/');
}
