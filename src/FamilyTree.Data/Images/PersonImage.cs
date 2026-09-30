namespace FamilyTree.Data.Images;

/// <param name="Id">Generated GUID (no dashes). Also the on-disk file name, so client-supplied names never reach the filesystem.</param>
public sealed record PersonImage(
    string Id,
    string PersonId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedOn);

/// <summary>An opened image file. The caller owns and must dispose <see cref="Content"/>.</summary>
public sealed record PersonImageContent(Stream Content, string ContentType);

/// <summary>Thrown when an upload is not an accepted image (wrong type, too large, or undecodable).</summary>
public sealed class InvalidImageException(string message) : Exception(message);
