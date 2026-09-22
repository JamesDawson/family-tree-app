namespace FamilyTree.Data.Models;

public sealed class PersonWithRelationships
{
    public required Person Person { get; init; }
    public required IReadOnlyList<Person> Parents { get; init; }
    public required IReadOnlyList<Person> Children { get; init; }
    public required IReadOnlyList<SiblingRelationship> Siblings { get; init; }
    public required IReadOnlyList<SpouseWithPerson> Spouses { get; init; }
}

public sealed record SiblingRelationship(Person Person, bool Full);

public sealed record SpouseWithPerson(Person Person, SpouseRelationship Relationship);
