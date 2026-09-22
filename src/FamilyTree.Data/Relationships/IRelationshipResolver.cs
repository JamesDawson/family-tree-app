using FamilyTree.Data.Models;

namespace FamilyTree.Data.Relationships;

public interface IRelationshipResolver
{
    /// <summary>Computes children, siblings, and resolved parent/spouse objects for a person, derived
    /// from the full in-memory people set (children/siblings are never stored, only derived).</summary>
    PersonWithRelationships Resolve(Person person, IReadOnlyList<Person> allPeople);
}
