using FamilyTree.Data.Git;
using FamilyTree.Data.Options;
using Microsoft.Extensions.Options;

namespace FamilyTree.Data.Tests;

[TestClass]
public class LibGit2GitRepositoryServiceTests
{
    private static readonly CommitAuthor Author = new("Test Author", "test@example.com");

    private string _tempRoot = "";
    private LibGit2GitRepositoryService _git = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FamilyTreeTests", Guid.NewGuid().ToString("N"));
        var paths = new FamilyTreeDataPaths { RootPath = _tempRoot };
        var options = Microsoft.Extensions.Options.Options.Create(new FamilyTreeDataOptions
        {
            RepositoryPath = _tempRoot,
            DefaultCommitAuthorName = Author.Name,
            DefaultCommitAuthorEmail = Author.Email,
        });

        _git = new LibGit2GitRepositoryService(paths, options);
        _git.EnsureInitialized();
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
    public void EnsureInitialized_CreatesGitRepoWithBaselineCommit()
    {
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempRoot, ".git")));
        Assert.AreEqual(1, _git.GetHistory("README.md").Count);
    }

    [TestMethod]
    public void EnsureInitialized_IsIdempotent()
    {
        _git.EnsureInitialized();
        _git.EnsureInitialized();

        Assert.AreEqual(1, _git.GetHistory("README.md").Count);
    }

    [TestMethod]
    public void CommitFiles_ProducesExactlyOneCommitWithExpectedAuthorAndMessage()
    {
        const string relativePath = "people/jane-doe-1952.md";
        WriteFile(relativePath, "---\nid: jane-doe-1952\n---\n");

        _git.CommitFiles([relativePath], "Add person: Jane Doe", Author);

        var history = _git.GetHistory(relativePath);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("Add person: Jane Doe", history[0].Message);
        Assert.AreEqual(Author.Name, history[0].AuthorName);
    }

    [TestMethod]
    public void CommitFiles_SkipsCommitWhenThereIsNoActualDiff()
    {
        const string relativePath = "people/jane.md";
        WriteFile(relativePath, "same content");
        _git.CommitFiles([relativePath], "First", Author);

        WriteFile(relativePath, "same content");
        _git.CommitFiles([relativePath], "Second (no-op)", Author);

        Assert.AreEqual(1, _git.GetHistory(relativePath).Count);
    }

    [TestMethod]
    public void GetHistory_ReturnsOnlyCommitsTouchingThatPathNewestFirst()
    {
        WriteFile("people/john.md", "v1");
        _git.CommitFiles(["people/john.md"], "Add John", Author);

        WriteFile("people/jane.md", "v1");
        _git.CommitFiles(["people/jane.md"], "Add Jane", Author);

        WriteFile("people/john.md", "v2");
        _git.CommitFiles(["people/john.md"], "Update John", Author);

        var johnHistory = _git.GetHistory("people/john.md");

        Assert.AreEqual(2, johnHistory.Count);
        Assert.AreEqual("Update John", johnHistory[0].Message);
        Assert.AreEqual("Add John", johnHistory[1].Message);
    }

    [TestMethod]
    public void GetFileContentAtCommit_ReturnsHistoricContent()
    {
        WriteFile("people/jane.md", "v1");
        _git.CommitFiles(["people/jane.md"], "v1", Author);
        var sha = _git.GetHistory("people/jane.md")[0].Sha;

        WriteFile("people/jane.md", "v2");
        _git.CommitFiles(["people/jane.md"], "v2", Author);

        Assert.AreEqual("v1", _git.GetFileContentAtCommit("people/jane.md", sha));
    }

    [TestMethod]
    public void RevertFileToCommit_IncreasesCommitCountAndLeavesUnrelatedFilesUntouched()
    {
        WriteFile("people/jane.md", "v1");
        _git.CommitFiles(["people/jane.md"], "Add Jane v1", Author);
        var firstCommitSha = _git.GetHistory("people/jane.md")[0].Sha;

        WriteFile("people/jane.md", "v2");
        _git.CommitFiles(["people/jane.md"], "Update Jane v2", Author);

        WriteFile("people/john.md", "unrelated");
        _git.CommitFiles(["people/john.md"], "Add John", Author);
        var johnHistoryBefore = _git.GetHistory("people/john.md");

        _git.RevertFileToCommit("people/jane.md", firstCommitSha, "Revert Jane to v1", Author);

        var janeHistory = _git.GetHistory("people/jane.md");
        Assert.AreEqual(3, janeHistory.Count, "Reverting must add a new commit, never rewrite history.");
        Assert.AreEqual("v1", File.ReadAllText(Path.Combine(_tempRoot, "people", "jane.md")));

        var johnHistoryAfter = _git.GetHistory("people/john.md");
        CollectionAssert.AreEqual(
            johnHistoryBefore.Select(c => c.Sha).ToList(),
            johnHistoryAfter.Select(c => c.Sha).ToList(),
            "Reverting one person's file must not touch another person's history.");
    }

    [TestMethod]
    public void CommitDeletion_RemovesFileFromDiskAndCommits()
    {
        WriteFile("people/jane.md", "content");
        _git.CommitFiles(["people/jane.md"], "Add Jane", Author);

        _git.CommitDeletion("people/jane.md", "Delete Jane", Author);

        Assert.IsFalse(File.Exists(Path.Combine(_tempRoot, "people", "jane.md")));
        var history = _git.GetHistory("people/jane.md");
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual("Delete Jane", history[0].Message);
    }

    private void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
