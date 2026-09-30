using FamilyTree.Data.Git;
using FamilyTree.Data.Images;
using FamilyTree.Data.Options;
using SkiaSharp;

namespace FamilyTree.Data.Tests;

[TestClass]
public class LocalPersonImageStoreTests
{
    private string _tempRoot = "";
    private FamilyTreeDataPaths _paths = null!;
    private LocalPersonImageStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FamilyTreeTests", Guid.NewGuid().ToString("N"));
        _paths = new FamilyTreeDataPaths { RootPath = _tempRoot };
        _store = NewStore(maxBytes: 10 * 1024 * 1024);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (!Directory.Exists(_tempRoot))
        {
            return;
        }

        // Git object files are read-only.
        foreach (var file in Directory.GetFiles(_tempRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_tempRoot, recursive: true);
    }

    private LocalPersonImageStore NewStore(long maxBytes) => new(_paths, Microsoft.Extensions.Options.Options.Create(new FamilyTreeDataOptions
    {
        RepositoryPath = _tempRoot,
        DefaultCommitAuthorName = "Test",
        DefaultCommitAuthorEmail = "test@example.com",
        MaxImageBytes = maxBytes,
    }));

    private static byte[] Encode(SKEncodedImageFormat format, int width = 800, int height = 600)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, 90).ToArray();
    }

    [TestMethod]
    [DataRow(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [DataRow(SKEncodedImageFormat.Png, "image/png")]
    [DataRow(SKEncodedImageFormat.Webp, "image/webp")]
    public async Task SaveAsync_AcceptsSupportedFormatsAndDetectsTypeFromContent(SKEncodedImageFormat format, string expectedContentType)
    {
        using var content = new MemoryStream(Encode(format));

        var saved = await _store.SaveAsync("person-1", "holiday.bin", content);

        Assert.AreEqual(expectedContentType, saved.ContentType);
        Assert.AreEqual("holiday.bin", saved.OriginalFileName);
        var listed = await _store.ListAsync("person-1");
        Assert.HasCount(1, listed);
        Assert.AreEqual(saved.Id, listed[0].Id);
    }

    [TestMethod]
    public async Task SaveAsync_CreatesThumbnailNoLargerThanLimit()
    {
        using var content = new MemoryStream(Encode(SKEncodedImageFormat.Png, 2000, 1000));
        var saved = await _store.SaveAsync("person-1", "big.png", content);

        await using var thumb = (await _store.OpenReadAsync("person-1", saved.Id, thumbnail: true))!.Content;
        using var bitmap = SKBitmap.Decode(thumb);

        Assert.AreEqual(400, bitmap.Width);
        Assert.AreEqual(200, bitmap.Height);
    }

    [TestMethod]
    public async Task SaveAsync_RejectsNonImageEvenWithImageExtension()
    {
        using var content = new MemoryStream("MZ this is not an image"u8.ToArray());

        await Assert.ThrowsExactlyAsync<InvalidImageException>(() => _store.SaveAsync("person-1", "evil.png", content));

        Assert.IsEmpty(await _store.ListAsync("person-1"));
        Assert.IsEmpty(Directory.GetFiles(Path.Combine(_paths.ImagesDirectory, "person-1"), "*.png"));
    }

    [TestMethod]
    public async Task SaveAsync_RejectsTruncatedImage()
    {
        var bytes = Encode(SKEncodedImageFormat.Png);
        using var content = new MemoryStream(bytes, 0, 20);

        await Assert.ThrowsExactlyAsync<InvalidImageException>(() => _store.SaveAsync("person-1", "cut.png", content));

        Assert.IsEmpty(await _store.ListAsync("person-1"));
    }

    [TestMethod]
    public async Task SaveAsync_RejectsEmptyFile()
    {
        using var content = new MemoryStream();

        await Assert.ThrowsExactlyAsync<InvalidImageException>(() => _store.SaveAsync("person-1", "empty.png", content));
    }

    [TestMethod]
    public async Task SaveAsync_RejectsFilesOverTheSizeLimit()
    {
        var store = NewStore(maxBytes: 1000);
        using var content = new MemoryStream(Encode(SKEncodedImageFormat.Png, 600, 600).Concat(new byte[2000]).ToArray());

        await Assert.ThrowsExactlyAsync<InvalidImageException>(() => store.SaveAsync("person-1", "huge.png", content));

        Assert.IsEmpty(await store.ListAsync("person-1"));
    }

    [TestMethod]
    public async Task SaveAsync_NeverUsesClientFileNameOnDisk()
    {
        using var content = new MemoryStream(Encode(SKEncodedImageFormat.Png));

        var saved = await _store.SaveAsync("person-1", @"..\..\..\escape.png", content);

        Assert.AreEqual("escape.png", saved.OriginalFileName);
        Assert.IsFalse(File.Exists(Path.Combine(_tempRoot, "escape.png")));
        Assert.IsTrue(File.Exists(Path.Combine(_paths.ImagesDirectory, "person-1", saved.Id + ".png")));
    }

    [TestMethod]
    [DataRow("..")]
    [DataRow("a/b")]
    [DataRow("")]
    public async Task InvalidPersonIds_AreRejected(string personId)
    {
        using var content = new MemoryStream(Encode(SKEncodedImageFormat.Png));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _store.SaveAsync(personId, "a.png", content));
    }

    [TestMethod]
    public async Task OpenReadAsync_ReturnsOriginalBytes()
    {
        var bytes = Encode(SKEncodedImageFormat.Jpeg);
        var saved = await _store.SaveAsync("person-1", "a.jpg", new MemoryStream(bytes));

        await using var opened = (await _store.OpenReadAsync("person-1", saved.Id, thumbnail: false))!.Content;
        using var copy = new MemoryStream();
        await opened.CopyToAsync(copy);

        CollectionAssert.AreEqual(bytes, copy.ToArray());
    }

    [TestMethod]
    public async Task OpenReadAsync_ReturnsNullForUnknownOrMalformedId()
    {
        Assert.IsNull(await _store.OpenReadAsync("person-1", Guid.NewGuid().ToString("N"), thumbnail: false));
        Assert.IsNull(await _store.OpenReadAsync("person-1", "../index", thumbnail: false));
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesFilesAndIndexEntry()
    {
        var saved = await _store.SaveAsync("person-1", "a.png", new MemoryStream(Encode(SKEncodedImageFormat.Png)));

        Assert.IsTrue(await _store.DeleteAsync("person-1", saved.Id));

        Assert.IsEmpty(await _store.ListAsync("person-1"));
        Assert.IsFalse(File.Exists(Path.Combine(_paths.ImagesDirectory, "person-1", saved.Id + ".png")));
        Assert.IsFalse(File.Exists(Path.Combine(_paths.ImagesDirectory, "person-1", saved.Id + ".thumb.jpg")));
        Assert.IsFalse(await _store.DeleteAsync("person-1", saved.Id));
    }

    [TestMethod]
    public async Task DeleteAllAsync_RemovesPersonDirectory()
    {
        await _store.SaveAsync("person-1", "a.png", new MemoryStream(Encode(SKEncodedImageFormat.Png)));

        await _store.DeleteAllAsync("person-1");

        Assert.IsFalse(Directory.Exists(Path.Combine(_paths.ImagesDirectory, "person-1")));
    }

    [TestMethod]
    public async Task SaveAsync_ConcurrentUploadsAreAllRecorded()
    {
        var bytes = Encode(SKEncodedImageFormat.Png, 100, 100);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => _store.SaveAsync("person-1", $"{i}.png", new MemoryStream(bytes))));

        Assert.HasCount(8, await _store.ListAsync("person-1"));
    }

    [TestMethod]
    public void EnsureInitialized_ExcludesImagesDirectoryFromGit()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new FamilyTreeDataOptions
        {
            RepositoryPath = _tempRoot,
            DefaultCommitAuthorName = "Test",
            DefaultCommitAuthorEmail = "test@example.com",
        });
        var git = new LibGit2GitRepositoryService(_paths, options);

        git.EnsureInitialized();
        git.EnsureInitialized(); // idempotent

        var exclude = File.ReadAllLines(Path.Combine(_tempRoot, ".git", "info", "exclude"));
        Assert.AreEqual(1, exclude.Count(l => l.Trim() == "/images/"));
    }
}
