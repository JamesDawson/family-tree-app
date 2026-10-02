namespace FamilyTree.Data.Models;

public sealed record PersonName(string First, string? Middle, string Last, string? MaidenName)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Middle)
        ? $"{First} {Last}"
        : $"{First} {Middle} {Last}";

    /// <summary>
    /// Case-insensitive match against the display name that treats typographic apostrophes
    /// (’ ‘ ʼ `) as equivalent to a straight apostrophe.
    /// </summary>
    public bool Matches(string query) =>
        NormalizeApostrophes(DisplayName).Contains(NormalizeApostrophes(query), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeApostrophes(string value) =>
        value.Replace('’', '\'').Replace('‘', '\'').Replace('ʼ', '\'').Replace('`', '\'');
}
