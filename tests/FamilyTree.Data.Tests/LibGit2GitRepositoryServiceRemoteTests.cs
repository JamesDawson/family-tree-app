using FamilyTree.Data.Git;
using FamilyTree.Data.Options;
using LibGit2Sharp;
using GitRepository = LibGit2Sharp.Repository;

namespace FamilyTree.Data.Tests;

/// <summary>Clone / pull / push behaviour, using a local bare repository as the remote (no network or auth).</summary>
[TestClass]
public class LibGit2GitRepositoryServiceRemoteTests
{
    private static readonly CommitAuthor Author = new("Test Author", "test@example.com");
    private static readonly Signature Signature = new(Author.Name, Author.Email, DateTimeOffset.Now);

    private string _tempRoot = "";
    private string _remotePath = "";
    private string _dataPath = "";

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FamilyTreeRemoteTests", Guid.NewGuid().ToString("N"));
        _remotePath = Path.Combine(_tempRoot, "remote.git");
        _dataPath = Path.Combine(_tempRoot, "data");
        GitRepository.Init(_remotePath, isBare: true);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (!Directory.Exists(_tempRoot))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(_tempRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_tempRoot, recursive: true);
    }

    [TestMethod]
    public void EnsureInitialized_ClonesRemoteIntoEmptyDirectory()
    {
        PushCommitToRemote("people/jane.md", "from remote", "Add Jane");

        var git = CreateService();
        git.EnsureInitialized();

        Assert.AreEqual("from remote", File.ReadAllText(Path.Combine(_dataPath, "people", "jane.md")));
        Assert.AreEqual(1, git.GetHistory("people/jane.md").Count);
    }

    [TestMethod]
    public void EnsureInitialized_EmptyRemote_SeedsBaselineCommitAndPushesIt()
    {
        var git = CreateService();
        git.EnsureInitialized();

        Assert.IsTrue(Directory.Exists(Path.Combine(_dataPath, "people")));
        using var remote = new GitRepository(_remotePath);
        Assert.AreEqual("Initialize family tree data repository", remote.Head.Tip.MessageShort);
    }

    [TestMethod]
    public void CommitFiles_PushesCommitToRemote()
    {
        PushCommitToRemote("people/jane.md", "v1", "Add Jane");
        var git = CreateService();
        git.EnsureInitialized();

        File.WriteAllText(Path.Combine(_dataPath, "people", "john.md"), "hello");
        git.CommitFiles(["people/john.md"], "Add John", Author);

        using var remote = new GitRepository(_remotePath);
        Assert.AreEqual("Add John", remote.Head.Tip.MessageShort);
    }

    [TestMethod]
    public void EnsureInitialized_ExistingClone_FastForwardsToNewRemoteCommits()
    {
        PushCommitToRemote("people/jane.md", "v1", "Add Jane");
        CreateService().EnsureInitialized();

        PushCommitToRemote("people/john.md", "added elsewhere", "Add John elsewhere");
        var git = CreateService();
        git.EnsureInitialized();

        Assert.AreEqual("added elsewhere", File.ReadAllText(Path.Combine(_dataPath, "people", "john.md")));
    }

    [TestMethod]
    public void EnsureInitialized_DivergedRemote_DoesNotThrowAndKeepsLocalCommit()
    {
        PushCommitToRemote("people/jane.md", "v1", "Add Jane");
        var git = CreateService();
        git.EnsureInitialized();

        File.WriteAllText(Path.Combine(_dataPath, "people", "local.md"), "local");
        git.CommitFiles(["people/local.md"], "Local change", Author);
        PushCommitToRemote("people/other.md", "remote", "Remote change", force: true);

        CreateService().EnsureInitialized();

        Assert.AreEqual(1, git.GetHistory("people/local.md").Count);
    }

    [TestMethod]
    public void CommitFiles_WhenRemoteIsUnreachable_KeepsCommitLocallyWithoutThrowing()
    {
        PushCommitToRemote("people/jane.md", "v1", "Add Jane");
        var git = CreateService();
        git.EnsureInitialized();

        DeleteDirectory(_remotePath);

        File.WriteAllText(Path.Combine(_dataPath, "people", "john.md"), "hello");
        git.CommitFiles(["people/john.md"], "Add John", Author);

        Assert.AreEqual(1, git.GetHistory("people/john.md").Count);
    }

    [TestMethod]
    public void EnsureInitialized_UnreachableRemoteOnFirstRun_ThrowsClearError()
    {
        DeleteDirectory(_remotePath);

        var git = CreateService();

        var ex = Assert.ThrowsExactly<InvalidOperationException>(git.EnsureInitialized);
        StringAssert.Contains(ex.Message, "Could not clone");
    }

    [TestMethod]
    public void CreateCredentialsHandler_SuppliesTokenOnlyOnceAndOnlyForRemoteHost()
    {
        var handler = LibGit2GitRepositoryService.CreateCredentialsHandler("https://github.com/o/r.git", "secret")!;

        Assert.IsNull(handler("https://evil.example.com/o/r.git", null, SupportedCredentialTypes.UsernamePassword));

        var credentials = (UsernamePasswordCredentials)handler("https://github.com/o/r.git", null, SupportedCredentialTypes.UsernamePassword);
        Assert.AreEqual("secret", credentials.Password);

        Assert.IsNull(handler("https://github.com/o/r.git", null, SupportedCredentialTypes.UsernamePassword), "A rejected token must not be retried.");
    }

    [TestMethod]
    public void CreateCredentialsHandler_WithoutToken_ReturnsNull()
    {
        Assert.IsNull(LibGit2GitRepositoryService.CreateCredentialsHandler("https://github.com/o/r.git", ""));
        Assert.IsNull(LibGit2GitRepositoryService.CreateCredentialsHandler("https://github.com/o/r.git", null));
    }

    /// <summary>Git pack files are read-only, which Directory.Delete refuses on Windows.</summary>
    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private LibGit2GitRepositoryService CreateService()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new FamilyTreeDataOptions
        {
            RepositoryPath = _dataPath,
            DefaultCommitAuthorName = Author.Name,
            DefaultCommitAuthorEmail = Author.Email,
            RemoteUrl = _remotePath,
        });

        return new LibGit2GitRepositoryService(new FamilyTreeDataPaths { RootPath = _dataPath }, options);
    }

    /// <summary>Adds a commit to the remote via a throwaway working clone, as another user of the shared repo would.</summary>
    private void PushCommitToRemote(string relativePath, string content, string message, bool force = false)
    {
        var workPath = Path.Combine(_tempRoot, "work-" + Guid.NewGuid().ToString("N"));
        GitRepository.Init(workPath);
        using var work = new GitRepository(workPath);

        using (var remote = new GitRepository(_remotePath))
        {
            if (remote.Head.Tip is not null)
            {
                // Start from the remote's history unless we deliberately want to diverge from it.
                if (!force)
                {
                    Commands.Fetch(work, work.Network.Remotes.Add("origin", _remotePath).Name, [], null, null);
                    var tip = work.Branches["origin/" + remote.Head.FriendlyName].Tip;
                    Commands.Checkout(work, work.CreateBranch(remote.Head.FriendlyName, tip));
                }
            }
        }

        if (work.Network.Remotes["origin"] is null)
        {
            work.Network.Remotes.Add("origin", _remotePath);
        }

        var fullPath = Path.Combine(workPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        Commands.Stage(work, "*");
        work.Commit(message, Signature, Signature);

        var refSpec = (force ? "+" : "") + work.Head.CanonicalName;
        work.Network.Push(work.Network.Remotes["origin"], refSpec);
    }
}
