using FamilyTree.Data.Images;
using FamilyTree.Data.Repository;
using FamilyTree.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTree.Web.Controllers;

[Route("people/{id}/images")]
public sealed class PersonImagesController(
    IPersonRepository repository,
    IPersonImageStore imageStore) : HtmxController
{
    // Per-file size is enforced by the store; this only bounds the whole multipart request
    // (Kestrel's default would otherwise apply) so several images can be uploaded at once.
    private const long MaxRequestBytes = 100 * 1024 * 1024;

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<IActionResult> Upload(string id, List<IFormFile> files, CancellationToken ct)
    {
        if (await repository.GetByIdAsync(id, ct) is null)
        {
            return NotFound();
        }

        var errors = new List<string>();
        foreach (var file in files.Where(f => f.Length > 0))
        {
            try
            {
                await using var stream = file.OpenReadStream();
                await imageStore.SaveAsync(id, file.FileName, stream, ct);
            }
            catch (InvalidImageException ex)
            {
                errors.Add($"{Path.GetFileName(file.FileName)}: {ex.Message}");
            }
        }

        if (files.Count == 0 || files.All(f => f.Length == 0))
        {
            errors.Add("Choose at least one image to upload.");
        }

        if (errors.Count > 0)
        {
            TempData["ImageError"] = string.Join(" ", errors);
        }

        return RedirectAfterSave(DetailsUrl(id));
    }

    [HttpGet("{imageId}")]
    public Task<IActionResult> Original(string id, string imageId, CancellationToken ct) => Serve(id, imageId, thumbnail: false, ct);

    [HttpGet("{imageId}/thumb")]
    public Task<IActionResult> Thumbnail(string id, string imageId, CancellationToken ct) => Serve(id, imageId, thumbnail: true, ct);

    [HttpPost("{imageId}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, string imageId, CancellationToken ct)
    {
        if (!await imageStore.DeleteAsync(id, imageId, ct))
        {
            return NotFound();
        }

        return RedirectAfterSave(DetailsUrl(id));
    }

    private async Task<IActionResult> Serve(string id, string imageId, bool thumbnail, CancellationToken ct)
    {
        var image = await imageStore.OpenReadAsync(id, imageId, thumbnail, ct);
        if (image is null)
        {
            return NotFound();
        }

        // Image ids are unique per upload, so a given URL's content never changes.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image.Content, image.ContentType, enableRangeProcessing: true);
    }

    private string DetailsUrl(string id) => Url.Action("Details", "People", new { id })!;
}
