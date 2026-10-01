using FamilyTree.Data.Models;

namespace FamilyTree.Data.ExternalSources;

public static class PersonSearchContextFactory
{
    public static PersonSearchContext Create(PersonWithRelationships resolved)
    {
        var person = resolved.Person;
        return new PersonSearchContext(
            person.Id,
            person.Name,
            person.Sex,
            person.BornOn,
            NullIfBlank(person.BornPlace),
            person.DiedOn,
            NullIfBlank(person.DiedPlace),
            [.. resolved.Parents.Select(p => p.Name)],
            [.. resolved.Spouses.Select(s => s.Person.Name)]);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
