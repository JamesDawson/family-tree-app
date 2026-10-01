using FamilyTree.Data.ExternalSources;
using FamilyTree.Data.Relationships;
using FamilyTree.Data.Repository;
using FamilyTree.Web.Infrastructure;
using FamilyTree.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTree.Web.Controllers;

[Route("people/{id}/external-search")]
public sealed class ExternalSearchController(
    IPersonRepository repository,
    IRelationshipResolver relationshipResolver,
    IExternalDataSourceRegistry registry,
    ILogger<ExternalSearchController> logger) : HtmxController
{
    /// <summary>The sources that apply to this person. Makes no external calls.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Sources(string id, CancellationToken ct)
    {
        var context = await BuildContextAsync(id, ct);
        if (context is null)
        {
            return NotFound();
        }

        return PartialView("_ExternalSources", new ExternalSourceListViewModel
        {
            PersonId = id,
            Sources = registry.GetAvailable(context),
        });
    }

    /// <summary>Runs one source for this person. Only called when the user chooses a source.</summary>
    [HttpGet("{sourceId}")]
    public async Task<IActionResult> Search(string id, string sourceId, CancellationToken ct)
    {
        var source = registry.Find(sourceId);
        var context = source is null ? null : await BuildContextAsync(id, ct);
        if (source is null || context is null)
        {
            return NotFound();
        }

        ExternalSearchResponse response;
        try
        {
            response = await source.SearchAsync(context, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A misbehaving source must not break the page.
            logger.LogError(ex, "External source {SourceId} failed", source.Id);
            response = ExternalSearchResponse.Failed("This source failed. Try again later.");
        }

        return PartialView("_ExternalSearchResults", new ExternalSearchResultsViewModel
        {
            SourceName = source.DisplayName,
            Response = response,
        });
    }

    private async Task<PersonSearchContext?> BuildContextAsync(string id, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
        {
            return null;
        }

        var all = await repository.GetAllAsync(ct);
        return PersonSearchContextFactory.Create(relationshipResolver.Resolve(person, all));
    }
}
