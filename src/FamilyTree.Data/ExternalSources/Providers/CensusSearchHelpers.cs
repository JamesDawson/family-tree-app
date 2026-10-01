using FamilyTree.Data.Models;

namespace FamilyTree.Data.ExternalSources.Providers;

internal static class CensusSearchHelpers
{
    private static readonly string[] IrishCounties =
    [
        "Antrim", "Armagh", "Carlow", "Cavan", "Clare", "Cork", "Derry", "Londonderry", "Donegal", "Down", "Dublin",
        "Fermanagh", "Galway", "Kerry", "Kildare", "Kilkenny", "Laois", "Queen's County", "Leitrim", "Limerick",
        "Longford", "Louth", "Mayo", "Meath", "Monaghan", "Offaly", "King's County", "Roscommon", "Sligo",
        "Tipperary", "Tyrone", "Waterford", "Westmeath", "Wexford", "Wicklow",
    ];

    /// <summary>The Irish county named in the place text, if any.</summary>
    public static string? FindIrishCounty(string? place) =>
        place is null ? null : IrishCounties.FirstOrDefault(c => place.Contains(c, StringComparison.OrdinalIgnoreCase));

    public static bool LooksIrish(string? place) =>
        place is not null
        && (FindIrishCounty(place) is not null
            || place.Contains("Ireland", StringComparison.OrdinalIgnoreCase)
            || place.Contains("Éire", StringComparison.OrdinalIgnoreCase)
            || place.Contains("Eire", StringComparison.OrdinalIgnoreCase));

    /// <summary>Census years in which the person could have been alive, given their (possibly approximate) dates.</summary>
    public static IReadOnlyList<int> CensusYearsAlive(PersonSearchContext context, IEnumerable<int> censusYears) =>
        [.. censusYears.Where(y =>
            (context.BirthYears is null || context.BirthYears.From <= y)
            && (context.DeathYears is null || context.DeathYears.To >= y))];

    /// <summary>Name details common to both sources. A maiden name is offered separately: records before marriage use it.</summary>
    public static Dictionary<string, string> NameDetails(PersonName name)
    {
        var details = new Dictionary<string, string> { ["Surname"] = name.Last, ["Forename"] = name.First };
        if (!string.IsNullOrWhiteSpace(name.Middle))
        {
            details["Middle name(s)"] = name.Middle;
        }

        if (!string.IsNullOrWhiteSpace(name.MaidenName))
        {
            details["Maiden name (for records before marriage)"] = name.MaidenName;
        }

        return details;
    }
}
