using FamilyTree.Data.Models;

namespace FamilyTree.Web.Models;

public sealed class PersonGraphViewModel
{
    public const int DefaultAncestors = 3;
    public const int DefaultDescendants = 2;

    /// <summary>Upper bound on generations a single request may ask for, so one call can't pull the whole archive.</summary>
    public const int MaxGenerations = 8;

    public required Person Person { get; init; }
}

/// <summary>One person in the JSON shape family-chart consumes (<c>{ id, data, rels }</c>).</summary>
public sealed class FamilyChartDatum
{
    public required string Id { get; init; }
    public required IReadOnlyDictionary<string, object?> Data { get; init; }
    public required FamilyChartRels Rels { get; init; }
}

public sealed class FamilyChartRels
{
    public required IReadOnlyList<string> Parents { get; init; }
    public required IReadOnlyList<string> Spouses { get; init; }
    public required IReadOnlyList<string> Children { get; init; }
}
