using FamilyTree.Data.Models;

namespace FamilyTree.Data.Relationships;

public sealed class RelationshipGraphResolver : IRelationshipResolver
{
    public PersonWithRelationships Resolve(Person person, IReadOnlyList<Person> allPeople)
    {
        var byId = allPeople.ToDictionary(p => p.Id);

        var parents = person.ParentIds
            .Select(id => byId.GetValueOrDefault(id))
            .OfType<Person>()
            .ToList();

        var children = allPeople
            .Where(p => p.ParentIds.Contains(person.Id))
            .ToList();

        var parentIdSet = person.ParentIds.ToHashSet();

        var siblings = allPeople
            .Where(p => p.Id != person.Id && parentIdSet.Count > 0 && p.ParentIds.Any(parentIdSet.Contains))
            .Select(p => new SiblingRelationship(p, Full: parentIdSet.SetEquals(p.ParentIds)))
            .ToList();

        var spouses = person.Spouses
            .Select(relationship => byId.TryGetValue(relationship.SpouseId, out var spousePerson)
                ? new SpouseWithPerson(spousePerson, relationship)
                : null)
            .OfType<SpouseWithPerson>()
            .ToList();

        return new PersonWithRelationships
        {
            Person = person,
            Parents = parents,
            Children = children,
            Siblings = siblings,
            Spouses = spouses,
        };
    }
}
