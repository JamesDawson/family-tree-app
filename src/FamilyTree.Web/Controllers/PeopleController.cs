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
    IFamilyGraphBuilder familyGraphBuilder,
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

    // Optional ?relation={parent|child|sibling|spouse}&of={personId} presets the new person as a relative
    // of an existing person (used by the "Add relative" buttons on the edit page).
    [HttpGet("new")]
    public async Task<IActionResult> Create(string? relation, string? of, CancellationToken ct)
    {
        var form = new PersonFormModel();

        if (!string.IsNullOrEmpty(relation) || !string.IsNullOrEmpty(of))
        {
            form.Relation = relation;
            form.RelatedToId = of;

            if (!await PrepareRelationContextAsync(form, ct))
            {
                return NotFound();
            }

            if (form.Relation == "child")
            {
                // Default the other parent to the current spouse, if there is one.
                var related = await repository.GetByIdAsync(of!, ct);
                form.SecondParentId = related!.Spouses.FirstOrDefault(s => s.Current)?.SpouseId;
            }
        }

        return View(form);
    }

    // Named explicitly: the GET (/people/new) and POST (/people) forms of "Create" share an action
    // name but have different route templates, which makes asp-action="Create" in the view ambiguous
    // (it resolved to the GET route). asp-route="PeopleCreate" in Create.cshtml disambiguates it.
    [HttpPost("", Name = "PeopleCreate")]
    public async Task<IActionResult> Create(PersonFormModel form, CancellationToken ct)
    {
        var person = new Person { Id = "", Name = new PersonName("", null, "", null) };
        var isRelative = !string.IsNullOrEmpty(form.Relation) || !string.IsNullOrEmpty(form.RelatedToId);

        if (isRelative && !await PrepareRelationContextAsync(form, ct))
        {
            return NotFound();
        }

        SpouseRelationship? spouse = null;
        if (isRelative && form.Relation == "spouse")
        {
            try
            {
                spouse = new SpouseRelationship(form.RelatedToId!, PartialDate.Parse(form.MarriedOn), PartialDate.Parse(form.DivorcedOn), form.CurrentSpouse);
            }
            catch (FormatException ex)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        if (!TryApplyFormToPerson(form, person))
        {
            return View(form);
        }

        if (!isRelative)
        {
            var created = await repository.CreateAsync(person, commitAuthorProvider.Current, ct);
            return RedirectAfterSave(Url.Action(nameof(Details), new { id = created.Id })!);
        }

        try
        {
            await repository.CreateRelatedAsync(
                person, ParseRelation(form.Relation)!.Value, form.RelatedToId!, form.SecondParentId, spouse, commitAuthorProvider.Current, ct);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(form);
        }
        catch (PersonNotFoundException)
        {
            return NotFound();
        }

        // Back to the person we started from so several relatives can be added in a row.
        return RedirectAfterSave(Url.Action(nameof(Edit), new { id = form.RelatedToId })!);
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
        ViewData["ParentCount"] = person.ParentIds.Count;
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

        ViewData["ParentCount"] = existing.ParentIds.Count;

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

    // A child link is stored on the child's file (as a parent id), so this updates the child, not {id}.
    [HttpPost("{id}/relationships/child")]
    public async Task<IActionResult> AddChild(string id, [FromForm] string childId, CancellationToken ct)
    {
        var parent = await repository.GetByIdAsync(id, ct);
        if (parent is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(childId) || childId == id)
        {
            return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
        }

        var child = await repository.GetByIdAsync(childId, ct);
        if (child is null)
        {
            return NotFound();
        }

        if (child.ParentIds.Contains(id))
        {
            // Already linked.
        }
        else if (child.ParentIds.Count >= 2)
        {
            TempData["DeleteError"] = $"{child.Name.DisplayName} already has two parents.";
        }
        else
        {
            child.ParentIds = [.. child.ParentIds, id];
            await repository.UpdateAsync(child, commitAuthorProvider.Current, ct);
        }

        return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
    }

    [HttpPost("{id}/relationships/child/{childId}/remove")]
    public async Task<IActionResult> RemoveChild(string id, string childId, CancellationToken ct)
    {
        var child = await repository.GetByIdAsync(childId, ct);
        if (child is null)
        {
            return NotFound();
        }

        if (child.ParentIds.Contains(id))
        {
            child.ParentIds = [.. child.ParentIds.Where(p => p != id)];
            await repository.UpdateAsync(child, commitAuthorProvider.Current, ct);
        }

        return RedirectAfterSave(Url.Action(nameof(Details), new { id })!);
    }

    // Siblings are derived from shared parents, so linking an existing sibling copies this person's parents
    // onto the sibling's file (up to two parents in total). Someone who already shares a parent is left alone.
    [HttpPost("{id}/relationships/sibling")]
    public async Task<IActionResult> AddSibling(string id, [FromForm] string siblingId, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
        {
            return NotFound();
        }

        var details = RedirectAfterSave(Url.Action(nameof(Details), new { id })!);

        if (string.IsNullOrWhiteSpace(siblingId) || siblingId == id)
        {
            return details;
        }

        var sibling = await repository.GetByIdAsync(siblingId, ct);
        if (sibling is null)
        {
            return NotFound();
        }

        if (person.ParentIds.Count == 0)
        {
            TempData["DeleteError"] = $"{person.Name.DisplayName} has no parents recorded yet — add a parent first so the sibling can share them.";
            return details;
        }

        if (sibling.ParentIds.Intersect(person.ParentIds).Any())
        {
            return details;
        }

        var toAdd = person.ParentIds.Take(2 - sibling.ParentIds.Count).ToList();
        if (toAdd.Count == 0)
        {
            TempData["DeleteError"] = $"{sibling.Name.DisplayName} already has two other parents.";
            return details;
        }

        sibling.ParentIds = [.. sibling.ParentIds, .. toAdd];
        await repository.UpdateAsync(sibling, commitAuthorProvider.Current, ct);

        if (toAdd.Count < person.ParentIds.Count)
        {
            TempData["DeleteError"] = $"{sibling.Name.DisplayName} already had a parent, so they're now a half-sibling.";
        }

        return details;
    }

    [HttpPost("{id}/relationships/sibling/{siblingId}/remove")]
    public async Task<IActionResult> RemoveSibling(string id, string siblingId, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        var sibling = await repository.GetByIdAsync(siblingId, ct);
        if (person is null || sibling is null)
        {
            return NotFound();
        }

        if (sibling.ParentIds.Intersect(person.ParentIds).Any())
        {
            sibling.ParentIds = [.. sibling.ParentIds.Except(person.ParentIds)];
            await repository.UpdateAsync(sibling, commitAuthorProvider.Current, ct);
        }

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
	
    [HttpGet("{id}/graph")]
    public async Task<IActionResult> Graph(string id, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
        {
            return NotFound();
        }

        return View(new PersonGraphViewModel { Person = person });
    }

    /// <summary>The slice of the family graph the chart renders, in family-chart's data shape. Only the
    /// requested number of generations is returned so payload and DOM size stay bounded however large the
    /// archive grows; the client asks again (with a new focus) as the user explores.</summary>
    [HttpGet("{id}/graph/data")]
    public async Task<IActionResult> GraphData(string id, int? up, int? down, CancellationToken ct)
    {
        var all = await repository.GetAllAsync(ct);
        var graph = familyGraphBuilder.Build(
            id,
            Math.Clamp(up ?? PersonGraphViewModel.DefaultAncestors, 0, PersonGraphViewModel.MaxGenerations),
            Math.Clamp(down ?? PersonGraphViewModel.DefaultDescendants, 0, PersonGraphViewModel.MaxGenerations),
            all);

        if (graph is null)
        {
            return NotFound();
        }

        return Json(graph.Nodes.Select(ToChartDatum));
    }

    private FamilyChartDatum ToChartDatum(FamilyGraph.Node node)
    {
        var person = node.Person;
        return new FamilyChartDatum
        {
            Id = person.Id,
            Data = new Dictionary<string, object?>
            {
                // family-chart only knows M/F; the real value is kept in "sex" so the card can style unknowns neutrally.
                ["gender"] = person.Sex == Sex.Female ? "F" : "M",
                ["sex"] = person.Sex.ToString().ToLowerInvariant(),
                ["name"] = person.Name.DisplayName,
                ["maidenName"] = person.Name.MaidenName,
                ["lifespan"] = FormatLifespan(person),
                ["url"] = Url.Action(nameof(Details), new { id = person.Id }),
                ["hiddenParents"] = node.HiddenParentCount,
                ["hiddenChildren"] = node.HiddenChildCount,
            },
            Rels = new FamilyChartRels
            {
                Parents = node.ParentIds,
                Spouses = node.SpouseIds,
                Children = node.ChildIds,
            },
        };
    }

    private static string FormatLifespan(Person person)
    {
        if (person.BornOn is null && person.DiedOn is null)
        {
            return "";
        }

        return $"{FormatYear(person.BornOn)}–{FormatYear(person.DiedOn)}";

        static string FormatYear(PartialDate? date) => date is null
            ? ""
            : date.Qualifier is DateQualifier.About or DateQualifier.Estimated ? $"c. {date.Year}" : date.Year.ToString();
    }

    private static RelationKind? ParseRelation(string? value) => value?.ToLowerInvariant() switch
    {
        "parent" => RelationKind.Parent,
        "child" => RelationKind.Child,
        "sibling" => RelationKind.Sibling,
        "spouse" => RelationKind.Spouse,
        _ => null,
    };

    /// <summary>Validates the relation/related-person on the form and fills in the ViewData the Create view
    /// needs (related person, label, other-parent choices). Returns false if either is missing or unknown.</summary>
    private async Task<bool> PrepareRelationContextAsync(PersonFormModel form, CancellationToken ct)
    {
        var kind = ParseRelation(form.Relation);
        if (kind is null || string.IsNullOrWhiteSpace(form.RelatedToId))
        {
            return false;
        }

        var related = await repository.GetByIdAsync(form.RelatedToId, ct);
        if (related is null)
        {
            return false;
        }

        form.Relation = kind.Value.ToString().ToLowerInvariant();
        ViewData["RelatedPerson"] = related;

        if (kind == RelationKind.Child)
        {
            var spouses = new List<Person>();
            foreach (var s in related.Spouses)
            {
                var spouse = await repository.GetByIdAsync(s.SpouseId, ct);
                if (spouse is not null)
                {
                    spouses.Add(spouse);
                }
            }

            ViewData["SpouseChoices"] = spouses;
        }

        return true;
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
        BornOn = person.BornOn?.ToDisplayString(),
        BornPlace = person.BornPlace,
        DiedOn = person.DiedOn?.ToDisplayString(),
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
