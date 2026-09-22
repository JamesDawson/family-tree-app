namespace FamilyTree.Data.Parsing;

/// <summary>The literal shape (de)serialized to/from YAML. Kept distinct from the domain model so
/// date/enum parsing logic lives in one place (<see cref="YamlFrontMatterPersonSerializer"/>).</summary>
internal sealed class PersonFrontMatterDto
{
    public string? Id { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleNames { get; set; }
    public string? LastName { get; set; }
    public string? MaidenName { get; set; }
    public string? Sex { get; set; }
    public string? BornOn { get; set; }
    public string? BornPlace { get; set; }
    public string? DiedOn { get; set; }
    public string? DiedPlace { get; set; }
    public List<string>? Parents { get; set; }
    public List<SpouseFrontMatterDto>? Spouses { get; set; }
}

internal sealed class SpouseFrontMatterDto
{
    public string? Id { get; set; }
    public string? MarriedOn { get; set; }
    public string? DivorcedOn { get; set; }
    public bool? Current { get; set; }
}
