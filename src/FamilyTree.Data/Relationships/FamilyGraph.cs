using FamilyTree.Data.Models;

namespace FamilyTree.Data.Relationships;

/// <summary>
/// A bounded slice of the family graph centred on one person. Every id in <see cref="Node.ParentIds"/>,
/// <see cref="Node.SpouseIds"/> and <see cref="Node.ChildIds"/> refers to another node in the same slice,
/// so a renderer never has to deal with dangling references.
/// </summary>
public sealed record FamilyGraph(string FocusId, IReadOnlyList<FamilyGraph.Node> Nodes)
{
    /// <param name="HiddenParentCount">Parents that exist but fall outside the slice (more ancestors to explore).</param>
    /// <param name="HiddenChildCount">Children that exist but fall outside the slice (more descendants to explore).</param>
    public sealed record Node(
        Person Person,
        IReadOnlyList<string> ParentIds,
        IReadOnlyList<string> SpouseIds,
        IReadOnlyList<string> ChildIds,
        int HiddenParentCount,
        int HiddenChildCount);
}
