namespace FamilyTree.Data.Models;

public sealed record PersonName(string First, string? Middle, string Last, string? MaidenName)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Middle)
        ? $"{First} {Last}"
        : $"{First} {Middle} {Last}";
}
