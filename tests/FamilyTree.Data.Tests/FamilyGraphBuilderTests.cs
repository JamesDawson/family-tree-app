using FamilyTree.Data.Models;
using FamilyTree.Data.Relationships;

namespace FamilyTree.Data.Tests;

[TestClass]
public class FamilyGraphBuilderTests
{
    private readonly FamilyGraphBuilder _builder = new();

    private static Person Make(string id, string[]? parents = null, string[]? spouses = null) => new()
    {
        Id = id,
        Name = new PersonName(id, null, "Doe", null),
        ParentIds = parents ?? [],
        Spouses = [.. (spouses ?? []).Select(s => new SpouseRelationship(s, null, null, true))],
    };

    private static List<string> Ids(FamilyGraph graph) => [.. graph.Nodes.Select(n => n.Person.Id)];

    // gp-a + gp-b -> parent (+ spouse) -> child -> grandchild
    private static List<Person> ThreeGenerations() =>
    [
        Make("gp-a", spouses: ["gp-b"]),
        Make("gp-b", spouses: ["gp-a"]),
        Make("parent", parents: ["gp-a", "gp-b"], spouses: ["in-law"]),
        Make("in-law", spouses: ["parent"]),
        Make("child", parents: ["parent", "in-law"]),
        Make("grandchild", parents: ["child"]),
    ];

    [TestMethod]
    public void Build_ReturnsNullForUnknownFocus()
    {
        Assert.IsNull(_builder.Build("nobody", 2, 2, ThreeGenerations()));
    }

    [TestMethod]
    public void Build_LimitsAncestorsAndDescendantsToRequestedGenerations()
    {
        var graph = _builder.Build("parent", 1, 1, ThreeGenerations())!;

        CollectionAssert.AreEquivalent(new[] { "gp-a", "gp-b", "parent", "in-law", "child" }, Ids(graph));
    }

    [TestMethod]
    public void Build_ZeroGenerationsReturnsFocusAndSpouses()
    {
        var graph = _builder.Build("parent", 0, 0, ThreeGenerations())!;

        CollectionAssert.AreEquivalent(new[] { "parent", "in-law" }, Ids(graph));
    }

    [TestMethod]
    public void Build_OnlyReferencesNodesInsideTheSlice()
    {
        var graph = _builder.Build("parent", 1, 1, ThreeGenerations())!;
        var ids = Ids(graph).ToHashSet();

        foreach (var node in graph.Nodes)
        {
            Assert.IsTrue(node.ParentIds.All(ids.Contains), $"{node.Person.Id} has a dangling parent");
            Assert.IsTrue(node.SpouseIds.All(ids.Contains), $"{node.Person.Id} has a dangling spouse");
            Assert.IsTrue(node.ChildIds.All(ids.Contains), $"{node.Person.Id} has a dangling child");
        }
    }

    [TestMethod]
    public void Build_CountsRelativesCutOffByTheBoundary()
    {
        var graph = _builder.Build("parent", 1, 1, ThreeGenerations())!;

        var child = graph.Nodes.Single(n => n.Person.Id == "child");
        Assert.AreEqual(1, child.HiddenChildCount); // grandchild exists but isn't shown
        Assert.AreEqual(0, child.HiddenParentCount);
    }

    [TestMethod]
    public void Build_DoesNotPullInSpousesOwnFamily()
    {
        List<Person> people =
        [
            Make("me", spouses: ["partner"]),
            Make("partner", parents: ["partner-mum"], spouses: ["me"]),
            Make("partner-mum"),
        ];

        var graph = _builder.Build("me", 3, 3, people)!;

        CollectionAssert.AreEquivalent(new[] { "me", "partner" }, Ids(graph));
        Assert.AreEqual(1, graph.Nodes.Single(n => n.Person.Id == "partner").HiddenParentCount);
    }

    [TestMethod]
    public void Build_TreatsSpouseLinkRecordedOnOneSideAsMutual()
    {
        List<Person> people = [Make("a", spouses: ["b"]), Make("b")];

        var graph = _builder.Build("b", 1, 1, people)!;

        CollectionAssert.AreEquivalent(new[] { "a", "b" }, Ids(graph));
        CollectionAssert.AreEqual(new[] { "a" }, graph.Nodes.Single(n => n.Person.Id == "b").SpouseIds.ToArray());
    }

    [TestMethod]
    public void Build_SurvivesParentCycles()
    {
        List<Person> people = [Make("a", parents: ["b"]), Make("b", parents: ["a"])];

        var graph = _builder.Build("a", 8, 8, people)!;

        CollectionAssert.AreEquivalent(new[] { "a", "b" }, Ids(graph));
    }

    [TestMethod]
    public void Build_IgnoresParentIdsThatDontExist()
    {
        List<Person> people = [Make("a", parents: ["ghost"])];

        var graph = _builder.Build("a", 2, 2, people)!;

        Assert.AreEqual(1, graph.Nodes.Count);
        Assert.AreEqual(0, graph.Nodes[0].ParentIds.Count);
        Assert.AreEqual(0, graph.Nodes[0].HiddenParentCount);
    }

    [TestMethod]
    public void Build_ListsEachSharedAncestorOnceWhenCousinsMarry()
    {
        // Pedigree collapse: x and y are cousins via grandparent "gp", and have a child together.
        List<Person> people =
        [
            Make("gp"),
            Make("p1", parents: ["gp"]),
            Make("p2", parents: ["gp"]),
            Make("x", parents: ["p1"], spouses: ["y"]),
            Make("y", parents: ["p2"], spouses: ["x"]),
            Make("kid", parents: ["x", "y"]),
        ];

        var graph = _builder.Build("kid", 3, 0, people)!;

        Assert.AreEqual(graph.Nodes.Count, Ids(graph).Distinct().Count());
        CollectionAssert.AreEquivalent(new[] { "gp", "p1", "p2", "x", "y", "kid" }, Ids(graph));
    }

    [TestMethod]
    public void Build_ReusesIndexForSamePeopleListAndRebuildsForNewList()
    {
        var people = ThreeGenerations();
        Assert.IsNotNull(_builder.Build("parent", 1, 1, people));

        var extended = new List<Person>(people) { Make("late-child", parents: ["parent"]) };
        var graph = _builder.Build("parent", 0, 1, extended)!;

        CollectionAssert.Contains(Ids(graph), "late-child");
    }
}
