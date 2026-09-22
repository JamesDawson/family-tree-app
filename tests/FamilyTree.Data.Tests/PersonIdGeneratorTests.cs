using FamilyTree.Data.Ids;

namespace FamilyTree.Data.Tests;

[TestClass]
public class PersonIdGeneratorTests
{
    [TestMethod]
    public void Generate_ProducesKebabCaseSlugWithBirthYear()
    {
        var id = PersonIdGenerator.Generate("Jane", "Doe", 1952, _ => false);

        Assert.AreEqual("jane-doe-1952", id);
    }

    [TestMethod]
    public void Generate_FoldsDiacriticsAndLowercases()
    {
        // "å" has a canonical Unicode decomposition ('a' + combining ring above) and so folds cleanly;
        // letters like "Ø" do not decompose (they're distinct letters, not base+accent) and are outside
        // what NFD-based folding can handle — not tested here.
        var id = PersonIdGenerator.Generate("Renée", "Håkonsen", 1901, _ => false);

        Assert.AreEqual("renee-hakonsen-1901", id);
    }

    [TestMethod]
    public void Generate_AppendsNumericSuffixOnCollision()
    {
        var existing = new HashSet<string> { "jane-doe-1952", "jane-doe-1952-2" };

        var id = PersonIdGenerator.Generate("Jane", "Doe", 1952, existing.Contains);

        Assert.AreEqual("jane-doe-1952-3", id);
    }

    [TestMethod]
    public void Generate_UsesShortDisambiguatorWhenBirthYearUnknown()
    {
        var id = PersonIdGenerator.Generate("Jane", "Doe", null, _ => false);

        Assert.IsTrue(id.StartsWith("jane-doe-", StringComparison.Ordinal));
        Assert.IsFalse(int.TryParse(id["jane-doe-".Length..], out _), "Expected a non-numeric disambiguator, not a fabricated year.");
    }
}
