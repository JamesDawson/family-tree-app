using FamilyTree.Data.Models;

namespace FamilyTree.Web.Models;

public sealed class PersonTreeViewModel
{
    public required Person Root { get; init; }
    public required IReadOnlyDictionary<string, Person> ById { get; init; }
}

public sealed class AncestorNodeViewModel
{
    public required Person Person { get; init; }
    public required IReadOnlyDictionary<string, Person> ById { get; init; }
    public required int Depth { get; init; }

    /// <summary>Keeps the recursive partial from spinning forever if someone hand-edits a data file
    /// into a parent-id cycle.</summary>
    public const int MaxDepth = 5;
}
