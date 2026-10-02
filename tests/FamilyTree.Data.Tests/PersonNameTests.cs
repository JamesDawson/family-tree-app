using FamilyTree.Data.Models;

namespace FamilyTree.Data.Tests;

[TestClass]
public class PersonNameTests
{
    [TestMethod]
    public void FullDisplayName_WithMaidenName_AppendsNee()
    {
        var name = new PersonName("Jane", null, "Smith", "Jones");

        Assert.AreEqual("Jane Smith (née Jones)", name.FullDisplayName);
    }

    [TestMethod]
    public void FullDisplayName_IncludesMiddleName()
    {
        var name = new PersonName("Jane", "Ann", "Smith", "Jones");

        Assert.AreEqual("Jane Ann Smith (née Jones)", name.FullDisplayName);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public void FullDisplayName_WithoutMaidenName_EqualsDisplayName(string? maidenName)
    {
        var name = new PersonName("Jane", null, "Smith", maidenName);

        Assert.AreEqual("Jane Smith", name.FullDisplayName);
    }
}
