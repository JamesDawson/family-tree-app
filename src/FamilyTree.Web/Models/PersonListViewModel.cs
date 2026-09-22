using FamilyTree.Data.Models;

namespace FamilyTree.Web.Models;

public sealed class PersonListViewModel
{
    public required string Query { get; init; }
    public required IReadOnlyList<Person> People { get; init; }
}
