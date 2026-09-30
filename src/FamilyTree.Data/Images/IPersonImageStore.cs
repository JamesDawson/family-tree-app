namespace FamilyTree.Data.Images;

/// <summary>Storage for images attached to a person. Implementations (local folder, blob storage, ...) are interchangeable.</summary>
public interface IPersonImageStore
{
    /// <summary>Validates and stores an image. Throws <see cref="InvalidImageException"/> if it is not a JPEG, PNG or WebP within the size limit.</summary>
    Task<PersonImage> SaveAsync(string personId, string originalFileName, Stream content, CancellationToken ct = default);

    /// <summary>Images for a person, oldest first.</summary>
    Task<IReadOnlyList<PersonImage>> ListAsync(string personId, CancellationToken ct = default);

    /// <summary>Opens the original or the thumbnail. Returns null if the image doesn't exist.</summary>
    Task<PersonImageContent?> OpenReadAsync(string personId, string imageId, bool thumbnail, CancellationToken ct = default);

    /// <summary>Returns false if the image didn't exist.</summary>
    Task<bool> DeleteAsync(string personId, string imageId, CancellationToken ct = default);

    /// <summary>Removes every image for a person (used when the person is deleted).</summary>
    Task DeleteAllAsync(string personId, CancellationToken ct = default);
}
