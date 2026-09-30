using FamilyTree.Data.Options;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private const string RemoteName = "origin";

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ILogger _logger;

    public LibGit2GitRepositoryService(
        FamilyTreeDataPaths paths,
        IOptions<FamilyTreeDataOptions> options,
        ILogger<LibGit2GitRepositoryService>? logger = null)
    {
        _paths = paths;
        _options = options.Value;
        _logger = logger ?? NullLogger<LibGit2GitRepositoryService>.Instance;
    }

    private string? RemoteUrl => string.IsNullOrWhiteSpace(_options.RemoteUrl) ? null : _options.RemoteUrl.Trim();

    public void EnsureInitialized()
    {
        EnsureRepositoryInitialized();
        ExcludeImagesDirectory();
    }

    /// <summary>Uploaded images live under the data root but must never be committed or pushed. <c>.git/info/exclude</c> is local, so it works for existing repos and clones without a commit.</summary>
    private void ExcludeImagesDirectory()
    {
        var infoDirectory = Path.Combine(_paths.RootPath, ".git", "info");
        Directory.CreateDirectory(infoDirectory);

        var excludePath = Path.Combine(infoDirectory, "exclude");
        const string rule = "/images/";
        if (File.Exists(excludePath) && File.ReadAllLines(excludePath).Any(l => l.Trim() == rule))
        {
            return;
        }

        File.AppendAllText(excludePath, $"{rule}{Environment.NewLine}");
    }

    private void EnsureRepositoryInitialized()
    {
        Directory.CreateDirectory(_paths.RootPath);

        if (GitRepository.IsValid(_paths.RootPath))
        {
            SyncExistingRepository();
            Directory.CreateDirectory(_paths.PeopleDirectory);
            return;
        }

        if (RemoteUrl is { } remoteUrl)
        {
            // Clone needs an empty directory, so people/ is only created afterwards.
            CloneRemote(remoteUrl);
            Directory.CreateDirectory(_paths.PeopleDirectory);

            using var cloned = new GitRepository(_paths.RootPath);
            if (cloned.Head.Tip is null)
            {
                // Empty remote: seed it with the baseline commit and publish it.
                CommitBaseline(cloned);
                if (TryPush(cloned))
                {
                    var branch = cloned.Head;
                    cloned.Branches.Update(branch, b =>
                    {
                        b.Remote = RemoteName;
                        b.UpstreamBranch = branch.CanonicalName;
                    });
                }
            }

            return;
        }

        Directory.CreateDirectory(_paths.PeopleDirectory);
        GitRepository.Init(_paths.RootPath);
        using var repo = new GitRepository(_paths.RootPath);
        CommitBaseline(repo);
    }

    private void CommitBaseline(GitRepository repo)
    {
        File.WriteAllText(Path.Combine(_paths.RootPath, ".gitignore"), "Thumbs.db\n.DS_Store\n");
        File.WriteAllText(Path.Combine(_paths.RootPath, "README.md"), ReadmeContent);

        Commands.Stage(repo, "*");
        var signature = DefaultSignature();
        repo.Commit("Initialize family tree data repository", signature, signature);
    }

    private void CloneRemote(string remoteUrl)
    {
        try
        {
            _logger.LogInformation("Cloning data repository from {RemoteUrl}", remoteUrl);
            var cloneOptions = new CloneOptions
            {
                FetchOptions = { CredentialsProvider = CreateCredentialsHandler(remoteUrl, _options.GitHubToken) },
            };
            GitRepository.Clone(remoteUrl, _paths.RootPath, cloneOptions);
        }
        catch (LibGit2SharpException ex)
        {
            throw new InvalidOperationException(
                $"Could not clone the data repository from '{remoteUrl}': {ex.Message}. " +
                "Check FamilyTreeData:RemoteUrl and that FamilyTreeData:GitHubToken is valid and has access.", ex);
        }
    }

    /// <summary>Makes sure origin is configured and fast-forwards to the remote. Never throws: the app can still run offline.</summary>
    private void SyncExistingRepository()
    {
        if (RemoteUrl is not { } remoteUrl)
        {
            return;
        }

        try
        {
            using var repo = new GitRepository(_paths.RootPath);
            var remote = repo.Network.Remotes[RemoteName];
            if (remote is null)
            {
                remote = repo.Network.Remotes.Add(RemoteName, remoteUrl);
            }
            else if (!string.Equals(remote.Url, remoteUrl, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Existing '{Remote}' remote points elsewhere than FamilyTreeData:RemoteUrl; leaving it unchanged", RemoteName);
            }

            var fetchOptions = new FetchOptions { CredentialsProvider = CreateCredentialsHandler(remote.Url, _options.GitHubToken) };
            Commands.Fetch(repo, RemoteName, [], fetchOptions, null);

            var tracked = repo.Head.TrackedBranch;
            if (tracked?.Tip is null || repo.Head.Tip is null)
            {
                return;
            }

            var result = repo.Merge(tracked.Tip, DefaultSignature(), new MergeOptions { FastForwardStrategy = FastForwardStrategy.FastForwardOnly });
            _logger.LogInformation("Synced data repository from remote: {Status}", result.Status);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException)
        {
            _logger.LogWarning(ex, "Could not sync the data repository from its remote; continuing with the local copy");
        }
    }

    /// <summary>
    /// Supplies the PAT only for the configured remote's host (so it can't leak to a redirect target), and only
    /// once per operation (so a rejected token fails instead of retrying forever). Returns null without a token.
    /// </summary>
    internal static CredentialsHandler? CreateCredentialsHandler(string? remoteUrl, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !Uri.TryCreate(remoteUrl, UriKind.Absolute, out var remote))
        {
            return null;
        }

        var used = false;
        return (url, _, _) =>
        {
            if (used || !Uri.TryCreate(url, UriKind.Absolute, out var requested)
                || !string.Equals(requested.Host, remote.Host, StringComparison.OrdinalIgnoreCase))
            {
                return null!;
            }

            used = true;
            return new UsernamePasswordCredentials { Username = "x-access-token", Password = token };
        };
    }

    /// <summary>Pushes the current branch to origin, if there is one. Never throws; returns whether the push succeeded.</summary>
    private bool TryPush(GitRepository repo)
    {
        if (RemoteUrl is null)
        {
            return false;
        }

        var remote = repo.Network.Remotes[RemoteName];
        if (remote is null)
        {
            return false;
        }

        try
        {
            var failed = false;
            var pushOptions = new PushOptions
            {
                CredentialsProvider = CreateCredentialsHandler(remote.Url, _options.GitHubToken),
                OnPushStatusError = error =>
                {
                    failed = true;
                    _logger.LogWarning("Push of {Reference} rejected by remote: {Message}", error.Reference, error.Message);
                },
            };
            repo.Network.Push(remote, repo.Head.CanonicalName, pushOptions);
            return !failed;
        }
        catch (LibGit2SharpException ex)
        {
            _logger.LogWarning(ex, "Could not push the data repository to its remote; the commit is kept locally");
            return false;
        }
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
            TryPush(repo);
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
            TryPush(repo);
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
            TryPush(repo);
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
