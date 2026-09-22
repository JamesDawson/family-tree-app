using FamilyTree.Data.Models;
using FamilyTree.Data.Parsing;

namespace FamilyTree.Data.Tests;

[TestClass]
public class FrontMatterRoundTripTests
{
    private readonly YamlFrontMatterPersonSerializer _serializer = new();

    [TestMethod]
    public void RoundTrip_PreservesAllFields()
    {
        var person = new Person
        {
            Id = "jane-doe-1952",
            Name = new PersonName("Jane", "Elizabeth", "Doe", "Smith"),
            Sex = Sex.Female,
            BornOn = new PartialDate(1952, 3, 14),
            BornPlace = "Leeds, England",
            DiedOn = null,
            DiedPlace = null,
            ParentIds = ["john-doe-1920", "mary-smith-1925"],
            Spouses =
            [
                new SpouseRelationship("robert-doe-1950", new PartialDate(1974, 6, 2), null, true),
            ],
            Notes = "Jane grew up in Leeds and trained as a teacher.",
        };

        var roundTripped = RoundTrip(person);

        Assert.AreEqual(person.Id, roundTripped.Id);
        Assert.AreEqual(person.Name, roundTripped.Name);
        Assert.AreEqual(person.Sex, roundTripped.Sex);
        Assert.AreEqual(person.BornOn, roundTripped.BornOn);
        Assert.AreEqual(person.BornPlace, roundTripped.BornPlace);
        Assert.IsNull(roundTripped.DiedOn);
        Assert.IsNull(roundTripped.DiedPlace);
        CollectionAssert.AreEqual((System.Collections.ICollection)person.ParentIds, (System.Collections.ICollection)roundTripped.ParentIds);
        CollectionAssert.AreEqual((System.Collections.ICollection)person.Spouses, (System.Collections.ICollection)roundTripped.Spouses);
        Assert.AreEqual(person.Notes, roundTripped.Notes);
    }

    [TestMethod]
    public void RoundTrip_PreservesMultipleSpousesWithMixedMetadata()
    {
        var person = new Person
        {
            Id = "person-1",
            Name = new PersonName("Alex", null, "Smith", null),
            Spouses =
            [
                new SpouseRelationship("first-spouse", new PartialDate(1980, 1, 1), new PartialDate(1990, 1, 1), false),
                new SpouseRelationship("second-spouse", new PartialDate(1995, 6, 15), null, true),
            ],
        };

        var roundTripped = RoundTrip(person);

        Assert.AreEqual(2, roundTripped.Spouses.Count);
        Assert.AreEqual(person.Spouses[0], roundTripped.Spouses[0]);
        Assert.AreEqual(person.Spouses[1], roundTripped.Spouses[1]);
    }

    [TestMethod]
    public void RoundTrip_PreservesUnicodeNames()
    {
        var person = new Person
        {
            Id = "person-2",
            Name = new PersonName("Renée", "Øystein", "Håkonsen", "Bjørnstad"),
        };

        var roundTripped = RoundTrip(person);

        Assert.AreEqual(person.Name, roundTripped.Name);
    }

    [TestMethod]
    public void RoundTrip_PreservesNotesContainingAMarkdownHorizontalRule()
    {
        var person = new Person
        {
            Id = "person-3",
            Name = new PersonName("Alex", null, "Smith", null),
            Notes = "Some text before.\n\n---\n\nMore text after a horizontal rule.",
        };

        var roundTripped = RoundTrip(person);

        Assert.AreEqual(person.Notes, roundTripped.Notes);
    }

    [TestMethod]
    public void RoundTrip_PreservesEmptyNotesAndNoSpouses()
    {
        var person = new Person
        {
            Id = "person-4",
            Name = new PersonName("Alex", null, "Smith", null),
        };

        var roundTripped = RoundTrip(person);

        Assert.AreEqual("", roundTripped.Notes);
        Assert.AreEqual(0, roundTripped.Spouses.Count);
        Assert.AreEqual(0, roundTripped.ParentIds.Count);
    }

    private Person RoundTrip(Person person)
    {
        var fileContents = _serializer.Serialize(person);
        return _serializer.Parse(person.Id, fileContents);
    }
}
