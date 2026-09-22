using FamilyTree.Data.Models;
using FamilyTree.Data.Relationships;

namespace FamilyTree.Data.Tests;

[TestClass]
public class RelationshipGraphResolverTests
{
    private readonly RelationshipGraphResolver _resolver = new();

    private static Person Make(string id, string first, string last, params string[] parentIds) => new()
    {
        Id = id,
        Name = new PersonName(first, null, last, null),
        ParentIds = parentIds,
    };

    [TestMethod]
    public void Resolve_DerivesChildrenByReverseIndexingParentIds()
    {
        var dad = Make("dad-1", "Dad", "Doe");
        var mum = Make("mum-1", "Mum", "Doe");
        var child = Make("child-1", "Child", "Doe", "dad-1", "mum-1");
        var all = new List<Person> { dad, mum, child };

        var resolved = _resolver.Resolve(dad, all);

        Assert.AreEqual(1, resolved.Children.Count);
        Assert.AreEqual("child-1", resolved.Children[0].Id);
    }

    [TestMethod]
    public void Resolve_ClassifiesFullSiblingsWhenBothParentsMatch()
    {
        var childA = Make("child-a", "A", "Doe", "dad-1", "mum-1");
        var childB = Make("child-b", "B", "Doe", "dad-1", "mum-1");
        var all = new List<Person> { childA, childB };

        var resolved = _resolver.Resolve(childA, all);

        Assert.AreEqual(1, resolved.Siblings.Count);
        Assert.AreEqual("child-b", resolved.Siblings[0].Person.Id);
        Assert.IsTrue(resolved.Siblings[0].Full);
    }

    [TestMethod]
    public void Resolve_ClassifiesHalfSiblingsWhenOnlyOneParentMatches()
    {
        var childA = Make("child-a", "A", "Doe", "dad-1", "mum-1");
        var childB = Make("child-b", "B", "Doe", "dad-1", "other-mum");
        var all = new List<Person> { childA, childB };

        var resolved = _resolver.Resolve(childA, all);

        Assert.AreEqual(1, resolved.Siblings.Count);
        Assert.IsFalse(resolved.Siblings[0].Full);
    }

    [TestMethod]
    public void Resolve_DoesNotTreatUnrelatedPeopleWithNoParentsAsSiblings()
    {
        var orphanA = Make("orphan-a", "A", "Doe");
        var orphanB = Make("orphan-b", "B", "Doe");
        var all = new List<Person> { orphanA, orphanB };

        var resolved = _resolver.Resolve(orphanA, all);

        Assert.AreEqual(0, resolved.Siblings.Count);
    }

    [TestMethod]
    public void Resolve_ResolvesSpousesBothDirectionsWithMetadata()
    {
        var relationship = new SpouseRelationship("spouse-1", new PartialDate(1974, 6, 2), null, true);
        var person = new Person
        {
            Id = "person-1",
            Name = new PersonName("Jane", null, "Doe", null),
            Spouses = [relationship],
        };
        var spouse = Make("spouse-1", "Robert", "Doe");
        var all = new List<Person> { person, spouse };

        var resolved = _resolver.Resolve(person, all);

        Assert.AreEqual(1, resolved.Spouses.Count);
        Assert.AreEqual("spouse-1", resolved.Spouses[0].Person.Id);
        Assert.AreEqual(relationship, resolved.Spouses[0].Relationship);
    }

    [TestMethod]
    public void Resolve_ResolvesParentsFromParentIds()
    {
        var dad = Make("dad-1", "Dad", "Doe");
        var child = Make("child-1", "Child", "Doe", "dad-1");
        var all = new List<Person> { dad, child };

        var resolved = _resolver.Resolve(child, all);

        Assert.AreEqual(1, resolved.Parents.Count);
        Assert.AreEqual("dad-1", resolved.Parents[0].Id);
    }
}
