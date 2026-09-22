using FamilyTree.Data.Models;

namespace FamilyTree.Data.Tests;

[TestClass]
public class PartialDateTests
{
    [TestMethod]
    [DataRow("1952", 1952, null, null, DateQualifier.Exact)]
    [DataRow("1952-03", 1952, 3, null, DateQualifier.Exact)]
    [DataRow("1952-03-14", 1952, 3, 14, DateQualifier.Exact)]
    [DataRow("abt 1952", 1952, null, null, DateQualifier.About)]
    [DataRow("bef 1960", 1960, null, null, DateQualifier.Before)]
    [DataRow("aft 1960", 1960, null, null, DateQualifier.After)]
    [DataRow("est 1960", 1960, null, null, DateQualifier.Estimated)]
    public void Parse_RecognizesAllSupportedFormats(string input, int year, int? month, int? day, DateQualifier qualifier)
    {
        var result = PartialDate.Parse(input);

        Assert.IsNotNull(result);
        Assert.AreEqual(new PartialDate(year, month, day, qualifier), result);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Parse_ReturnsNullForBlank(string? input)
    {
        Assert.IsNull(PartialDate.Parse(input));
    }

    [TestMethod]
    public void Parse_ThrowsForUnrecognizedText()
    {
        Assert.ThrowsExactly<FormatException>(() => PartialDate.Parse("not a date"));
    }

    [TestMethod]
    public void ToString_RoundTripsThroughParse()
    {
        var original = new PartialDate(1952, 3, 14, DateQualifier.About);

        var roundTripped = PartialDate.Parse(original.ToString());

        Assert.AreEqual(original, roundTripped);
    }
}
