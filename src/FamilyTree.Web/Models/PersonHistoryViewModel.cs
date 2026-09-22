using FamilyTree.Data.Git;
using FamilyTree.Data.Models;

namespace FamilyTree.Web.Models;

public sealed class PersonHistoryViewModel
{
    public required Person Person { get; init; }
    public required IReadOnlyList<CommitInfo> Commits { get; init; }
}
