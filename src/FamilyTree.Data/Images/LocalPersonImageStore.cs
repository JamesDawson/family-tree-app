using System.Text.Json;
using FamilyTree.Data.Options;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace FamilyTree.Data.Images;

/// <summary>
/// Stores images under <see cref="FamilyTreeDataPaths.ImagesDirectory"/>/&lt;personId&gt;/: the original as
/// &lt;imageId&gt;.&lt;ext&gt;, a thumbnail as &lt;imageId&gt;.thumb.jpg, and metadata in index.json.
/// </summary>
public sealed class LocalPersonImageStore(FamilyTreeDataPaths paths, IOptions<FamilyTreeDataOptions> options) : IPersonImageStore
{
    private const int ThumbnailMaxDimension = 400;
    private const long MaxPixels = 50_000_000;
    private const string IndexFileName = "index.json";

    // Explicit bytes: a "\x89..."u8 literal would encode U+0089 as two UTF-8 bytes.
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task<PersonImage> SaveAsync(string personId, string originalFileName, Stream content, CancellationToken ct = default)
    {
        var directory = DirectoryFor(personId);
        var maxBytes = options.Value.MaxImageBytes;
        var imageId = Guid.NewGuid().ToString("N");

        await _writeLock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(directory);
            var tempPath = Path.Combine(directory, imageId + ".upload");
            string? originalPath = null;
            string? thumbPath = null;

            try
            {
                long size;
                await using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    size = await CopyWithLimitAsync(content, temp, maxBytes, ct);
                }

                var (contentType, extension) = await SniffAsync(tempPath, ct);

                thumbPath = Path.Combine(directory, imageId + ".thumb.jpg");
                await CreateThumbnailAsync(tempPath, thumbPath, ct);

                originalPath = Path.Combine(directory, imageId + extension);
                File.Move(tempPath, originalPath);

                var image = new PersonImage(imageId, personId, CleanFileName(originalFileName), contentType, size, DateTimeOffset.UtcNow);
                var images = await ReadIndexAsync(directory, ct);
                images.Add(image);
                await WriteIndexAsync(directory, images, ct);
                return image;
            }
            catch
            {
                TryDelete(tempPath);
                TryDelete(thumbPath);
                TryDelete(originalPath);
                throw;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<PersonImage>> ListAsync(string personId, CancellationToken ct = default)
    {
        var directory = DirectoryFor(personId);
        await _writeLock.WaitAsync(ct);
        try
        {
            return [.. (await ReadIndexAsync(directory, ct)).OrderBy(i => i.UploadedOn)];
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<PersonImageContent?> OpenReadAsync(string personId, string imageId, bool thumbnail, CancellationToken ct = default)
    {
        var directory = DirectoryFor(personId);
        if (!Guid.TryParseExact(imageId, "N", out _))
        {
            return null;
        }

        PersonImage? image;
        await _writeLock.WaitAsync(ct);
        try
        {
            image = (await ReadIndexAsync(directory, ct)).FirstOrDefault(i => i.Id == imageId);
        }
        finally
        {
            _writeLock.Release();
        }

        if (image is null)
        {
            return null;
        }

        var path = thumbnail
            ? Path.Combine(directory, imageId + ".thumb.jpg")
            : Path.Combine(directory, imageId + ExtensionFor(image.ContentType));
        if (!File.Exists(path))
        {
            return null;
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return new PersonImageContent(stream, thumbnail ? "image/jpeg" : image.ContentType);
    }

    public async Task<bool> DeleteAsync(string personId, string imageId, CancellationToken ct = default)
    {
        var directory = DirectoryFor(personId);
        if (!Guid.TryParseExact(imageId, "N", out _))
        {
            return false;
        }

        await _writeLock.WaitAsync(ct);
        try
        {
            var images = await ReadIndexAsync(directory, ct);
            var image = images.FirstOrDefault(i => i.Id == imageId);
            if (image is null)
            {
                return false;
            }

            images.Remove(image);
            await WriteIndexAsync(directory, images, ct);
            TryDelete(Path.Combine(directory, imageId + ExtensionFor(image.ContentType)));
            TryDelete(Path.Combine(directory, imageId + ".thumb.jpg"));
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteAllAsync(string personId, CancellationToken ct = default)
    {
        var directory = DirectoryFor(personId);
        await _writeLock.WaitAsync(ct);
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private string DirectoryFor(string personId)
    {
        if (string.IsNullOrWhiteSpace(personId)
            || personId is "." or ".."
            || personId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Invalid person id.", nameof(personId));
        }

        return Path.Combine(paths.ImagesDirectory, personId);
    }

    private static async Task<long> CopyWithLimitAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidImageException($"Image is too large (maximum {maxBytes / (1024 * 1024)} MB).");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        if (total == 0)
        {
            throw new InvalidImageException("The file is empty.");
        }

        return total;
    }

    /// <summary>Identifies JPEG, PNG and WebP from the file's leading bytes. The client's file name and content type are never trusted.</summary>
    private static async Task<(string ContentType, string Extension)> SniffAsync(string path, CancellationToken ct)
    {
        var header = new byte[12];
        int read;
        await using (var stream = File.OpenRead(path))
        {
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        }

        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ("image/jpeg", ".jpg");
        }

        if (read >= 8 && header.AsSpan(0, 8).SequenceEqual(PngSignature))
        {
            return ("image/png", ".png");
        }

        if (read >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return ("image/webp", ".webp");
        }

        throw new InvalidImageException("Only JPEG, PNG and WebP images are supported.");
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg",
    };

    private static Task CreateThumbnailAsync(string sourcePath, string thumbPath, CancellationToken ct) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var codec = SKCodec.Create(sourcePath)
                ?? throw new InvalidImageException("The file could not be read as an image.");
            if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
            {
                throw new InvalidImageException("Image dimensions are too large.");
            }

            using var decoded = SKBitmap.Decode(codec)
                ?? throw new InvalidImageException("The file could not be read as an image.");
            using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);

            var scale = Math.Min(1.0, (double)ThumbnailMaxDimension / Math.Max(oriented.Width, oriented.Height));
            var width = Math.Max(1, (int)Math.Round(oriented.Width * scale));
            var height = Math.Max(1, (int)Math.Round(oriented.Height * scale));

            using var resized = oriented.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
                ?? throw new InvalidImageException("The file could not be read as an image.");
            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
            using var output = File.Create(thumbPath);
            data.SaveTo(output);
        }, ct);

    /// <summary>Rotates the pixels upright per the EXIF orientation so the thumbnail (which carries no EXIF) displays correctly. Mirrored origins are left as-is.</summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        var degrees = origin switch
        {
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0,
        };
        if (degrees == 0)
        {
            return source.Copy();
        }

        var swap = degrees != 180;
        var rotated = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(rotated);
        canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        using var sourceImage = SKImage.FromBitmap(source);
        canvas.DrawImage(sourceImage, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell));
        return rotated;
    }

    private static string CleanFileName(string name)
    {
        var clean = Path.GetFileName(name ?? "");
        clean = new string([.. clean.Where(c => !char.IsControl(c))]).Trim();
        if (clean.Length == 0)
        {
            return "image";
        }

        return clean.Length > 120 ? clean[..120] : clean;
    }

    private static async Task<List<PersonImage>> ReadIndexAsync(string directory, CancellationToken ct)
    {
        var indexPath = Path.Combine(directory, IndexFileName);
        if (!File.Exists(indexPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(indexPath);
        return await JsonSerializer.DeserializeAsync<List<PersonImage>>(stream, JsonOptions, ct) ?? [];
    }

    private static async Task WriteIndexAsync(string directory, List<PersonImage> images, CancellationToken ct)
    {
        var indexPath = Path.Combine(directory, IndexFileName);
        var tempPath = indexPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, images, JsonOptions, ct);
        }

        File.Move(tempPath, indexPath, overwrite: true);
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
