using FamilyTree.Data.Models;

namespace FamilyTree.Web.Models;

public sealed class PersonSearchResultsViewModel
{
    public required IReadOnlyList<Person> Matches { get; init; }
    public required string TargetPersonId { get; init; }

    /// <summary>Either "parent" or "spouse" — decides which endpoint clicking a match posts to.</summary>
    public required string RelationshipKind { get; init; }
}
