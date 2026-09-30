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
    [DataRow("15-Jun-1976", 1976, 6, 15, DateQualifier.Exact)]
    [DataRow("15-jun-1976", 1976, 6, 15, DateQualifier.Exact)]
    [DataRow("5-JUN-1976", 1976, 6, 5, DateQualifier.Exact)]
    [DataRow("Jun-1976", 1976, 6, null, DateQualifier.Exact)]
    [DataRow("abt Jun-1976", 1976, 6, null, DateQualifier.About)]
    [DataRow("bef 02-Nov-1998", 1998, 11, 2, DateQualifier.Before)]
    public void Parse_RecognizesDisplayFormat(string input, int year, int? month, int? day, DateQualifier qualifier)
    {
        Assert.AreEqual(new PartialDate(year, month, day, qualifier), PartialDate.Parse(input));
    }

    [TestMethod]
    [DataRow("1952-13-01")]
    [DataRow("1952-00")]
    [DataRow("1952-02-30")]
    [DataRow("31-Apr-1952")]
    [DataRow("32-Jan-1952")]
    public void Parse_ThrowsForOutOfRangeMonthOrDay(string input)
    {
        Assert.ThrowsExactly<FormatException>(() => PartialDate.Parse(input));
    }

    [TestMethod]
    [DataRow("1976-06-15", "15-Jun-1976")]
    [DataRow("1976-06", "Jun-1976")]
    [DataRow("1976", "1976")]
    [DataRow("abt 1976-06-05", "abt 05-Jun-1976")]
    public void ToDisplayString_UsesDdMonYyyy_AndToStringStaysCanonical(string canonical, string display)
    {
        var date = PartialDate.Parse(canonical)!;

        Assert.AreEqual(display, date.ToDisplayString());
        Assert.AreEqual(canonical, date.ToString());
        Assert.AreEqual(canonical, PartialDate.Parse(display)!.ToString());
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
