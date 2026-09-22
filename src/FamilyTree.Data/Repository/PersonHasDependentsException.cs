namespace FamilyTree.Data.Repository;

public sealed class PersonHasDependentsException(string id, IReadOnlyList<string> dependentIds)
    : Exception($"Cannot delete '{id}' because they are still referenced by: {string.Join(", ", dependentIds)}.")
{
    public string Id { get; } = id;
    public IReadOnlyList<string> DependentIds { get; } = dependentIds;
}
