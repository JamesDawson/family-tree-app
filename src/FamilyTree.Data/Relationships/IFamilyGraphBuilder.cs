using FamilyTree.Data.Models;

namespace FamilyTree.Data.Relationships;

public interface IFamilyGraphBuilder
{
    /// <summary>
    /// Extracts the hourglass around <paramref name="focusId"/>: ancestors up to
    /// <paramref name="ancestorGenerations"/> generations, descendants down to
    /// <paramref name="descendantGenerations"/> generations, plus the spouses of everyone included.
    /// Returns null when the focus person doesn't exist. Bounded and cycle-safe, so hand-edited data
    /// with a parent loop can't hang or explode the result.
    /// </summary>
    FamilyGraph? Build(string focusId, int ancestorGenerations, int descendantGenerations, IReadOnlyList<Person> allPeople);
}
