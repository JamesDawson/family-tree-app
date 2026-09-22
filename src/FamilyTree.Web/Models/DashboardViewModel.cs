using FamilyTree.Data.Git;
using FamilyTree.Data.Models;

namespace FamilyTree.Web.Models;

public sealed class DashboardViewModel
{
    public required int PersonCount { get; init; }
    public required IReadOnlyList<RecentChange> RecentChanges { get; init; }
}

public sealed record RecentChange(Person Person, CommitInfo LatestCommit);
