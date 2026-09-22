using FamilyTree.Data.Models;
using FamilyTree.Data.Relationships;
using FamilyTree.Data.Repository;
using FamilyTree.Web.Infrastructure;
using FamilyTree.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTree.Web.Controllers;

[Route("people")]
public sealed class PeopleController(
    IPersonRepository repository,
    IRelationshipResolver relationshipResolver,
    ICommitAuthorProvider commitAuthorProvider) : HtmxController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var people = await repository.GetAllAsync(ct);

        var filtered = string.IsNullOrWhiteSpace(q)
            ? people
            : people.Where(p => p.Name.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        var model = new PersonListViewModel
        {
            Query = q ?? "",
            People = [.. filtered.OrderBy(p => p.Name.Last).ThenBy(p => p.Name.First)],
        };

        return View(model);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
        {
            return NotFound();
        }

        var all = await repository.GetAllAsync(ct);
        var resolved = relationshipResolver.Resolve(person, all);

        return View(resolved);
    }

    [HttpGet("new")]
    public IActionResult Create() => View(new PersonFormModel());

    // Named explicitly: the GET (/people/new) and POST (/people) forms of "Create" share an action
    // name but have different route templates, which makes asp-action="Create" in the view ambiguous
    // (it resolved to the GET route). asp-route="PeopleCreate" in Create.cshtml disambiguates it.
    [HttpPost("", Name = "PeopleCreate")]
    public async Task<IActionResult> Create(PersonFormModel form, CancellationToken ct)
    {
        var person = new Person { Id = "", Name = new PersonName("", null, "", null) };

        if (!TryApplyFormToPerson(form, person))
        {
            return View(form);
        }

        var created = await repository.CreateAsync(person, commitAuthorProvider.Current, ct);
        return RedirectAfterSave(Url.Action(nameof(Details), new { id = created.Id })!);
    }

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
        {
            return NotFound();
        }

        ViewData["PersonId"] = id;
        return View(ToFormModel(person));
    }

    [HttpPost("{id}/edit")]
    public async Task<IActionResult> Edit(string id, PersonFormModel form, CancellationToken ct)
    {
        ViewData["PersonId"] = id;

        var existing = await repository.GetByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound();
        }

        if (!TryApplyFormToPerson(form, existing))
        {
            return View(form);
        }

        await repository.UpdateAsync(existing, commitAuthorProvider.Current, ct);
        return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
    }

    [HttpPost("{id}/delete")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        try
        {
            await repository.DeleteAsync(id, commitAuthorProvider.Current, ct);
        }
        catch (PersonNotFoundException)
        {
            return NotFound();
        }
        catch (PersonHasDependentsException ex)
        {
            TempData["DeleteError"] = $"Can't delete — still referenced by: {string.Join(", ", ex.DependentIds)}.";
            return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
        }

        return RedirectAfterSave(Url.Action(nameof(Index))!);
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> History(string id, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
        {
            return NotFound();
        }

        var commits = await repository.GetHistoryAsync(id, ct);
        return View(new PersonHistoryViewModel { Person = person, Commits = commits });
    }

    [HttpPost("{id}/history/{sha}/revert")]
    public async Task<IActionResult> Revert(string id, string sha, CancellationToken ct)
    {
        await repository.RevertToCommitAsync(id, sha, commitAuthorProvider.Current, ct);
        return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
    }

    [HttpPost("{id}/relationships/parent")]
    public async Task<IActionResult> AddParent(string id, [FromForm] string parentId, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(parentId) && !person.ParentIds.Contains(parentId))
        {
            person.ParentIds = [.. person.ParentIds, parentId];
            await repository.UpdateAsync(person, commitAuthorProvider.Current, ct);
        }

        return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
    }

    [HttpPost("{id}/relationships/parent/{parentId}/remove")]
    public async Task<IActionResult> RemoveParent(string id, string parentId, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
        {
            return NotFound();
        }

        person.ParentIds = [.. person.ParentIds.Where(p => p != parentId)];
        await repository.UpdateAsync(person, commitAuthorProvider.Current, ct);

        return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
    }

    [HttpPost("{id}/relationships/spouse")]
    public async Task<IActionResult> AddSpouse(string id, [FromForm] string spouseId, [FromForm] string? marriedOn, [FromForm] string? divorcedOn, [FromForm] bool current, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(spouseId))
        {
            return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
        }

        try
        {
            var relationship = new SpouseRelationship(spouseId, PartialDate.Parse(marriedOn), PartialDate.Parse(divorcedOn), current);
            await repository.AddSpouseRelationshipAsync(id, spouseId, relationship, commitAuthorProvider.Current, ct);
        }
        catch (FormatException ex)
        {
            TempData["DeleteError"] = ex.Message;
        }

        return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
    }

    [HttpPost("{id}/relationships/spouse/{spouseId}/remove")]
    public async Task<IActionResult> RemoveSpouse(string id, string spouseId, CancellationToken ct)
    {
        await repository.RemoveSpouseRelationshipAsync(id, spouseId, commitAuthorProvider.Current, ct);
        return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string? q, string personId, string relationshipKind, CancellationToken ct)
    {
        var people = await repository.GetAllAsync(ct);

        var matches = string.IsNullOrWhiteSpace(q)
            ? []
            : people
                .Where(p => p.Id != personId && p.Name.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Name.DisplayName)
                .Take(10)
                .ToList();

        return PartialView("_PersonSearchResults", new PersonSearchResultsViewModel
        {
            Matches = matches,
            TargetPersonId = personId,
            RelationshipKind = relationshipKind,
        });
    }

    [HttpGet("{id}/tree")]
    public async Task<IActionResult> Tree(string id, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
        {
            return NotFound();
        }

        var all = await repository.GetAllAsync(ct);
        return View(new PersonTreeViewModel { Root = person, ById = all.ToDictionary(p => p.Id) });
    }

    private bool TryApplyFormToPerson(PersonFormModel form, Person person)
    {
        try
        {
            var bornOn = PartialDate.Parse(form.BornOn);
            var diedOn = PartialDate.Parse(form.DiedOn);

            // Model binding converts an empty submitted form field to null (ModelMetadata's
            // ConvertEmptyStringToNull default), so FirstName/LastName can be null here even though
            // the view model declares them non-nullable — that's exactly the case ModelState.IsValid
            // below catches (via [Required]), but building the object must not throw before we get there.
            person.Name = new PersonName((form.FirstName ?? "").Trim(), NullIfBlank(form.MiddleName), (form.LastName ?? "").Trim(), NullIfBlank(form.MaidenName));
            person.Sex = ParseSex(form.Sex);
            person.BornOn = bornOn;
            person.BornPlace = NullIfBlank(form.BornPlace);
            person.DiedOn = diedOn;
            person.DiedPlace = NullIfBlank(form.DiedPlace);
            person.Notes = form.Notes ?? "";
        }
        catch (FormatException ex)
        {
            ModelState.AddModelError("", ex.Message);
        }

        return ModelState.IsValid;
    }

    private static PersonFormModel ToFormModel(Person person) => new()
    {
        FirstName = person.Name.First,
        MiddleName = person.Name.Middle,
        LastName = person.Name.Last,
        MaidenName = person.Name.MaidenName,
        Sex = person.Sex.ToString().ToLowerInvariant(),
        BornOn = person.BornOn?.ToString(),
        BornPlace = person.BornPlace,
        DiedOn = person.DiedOn?.ToString(),
        DiedPlace = person.DiedPlace,
        Notes = person.Notes,
    };

    private static Sex ParseSex(string? value) => value?.ToLowerInvariant() switch
    {
        "female" => Sex.Female,
        "male" => Sex.Male,
        _ => Sex.Unknown,
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
