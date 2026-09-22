namespace FamilyTree.Data.Models;

public sealed class Person
{
    public required string Id { get; init; }
    public required PersonName Name { get; set; }
    public Sex Sex { get; set; } = Sex.Unknown;
    public PartialDate? BornOn { get; set; }
    public string? BornPlace { get; set; }
    public PartialDate? DiedOn { get; set; }
    public string? DiedPlace { get; set; }
    public IReadOnlyList<string> ParentIds { get; set; } = [];
    public IReadOnlyList<SpouseRelationship> Spouses { get; set; } = [];

    /// <summary>Raw Markdown notes body (everything after the front matter).</summary>
    public string Notes { get; set; } = "";
}
