namespace FamilyTree.Data.Repository;

public sealed class PersonNotFoundException(string id) : Exception($"No person with id '{id}' was found.")
{
    public string Id { get; } = id;
}
