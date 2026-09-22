using System.Text.RegularExpressions;

namespace FamilyTree.Data.Models;

public enum DateQualifier { Exact, About, Before, After, Estimated }

public sealed partial record PartialDate(int Year, int? Month, int? Day, DateQualifier Qualifier = DateQualifier.Exact)
{
    public static PartialDate? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = ParsePattern().Match(text.Trim());
        if (!match.Success)
        {
            throw new FormatException($"'{text}' is not a recognized partial date. Expected formats like '1952', '1952-03', '1952-03-14', 'abt 1952', 'bef 1960', 'aft 1960', 'est 1960'.");
        }

        var qualifier = match.Groups["qualifier"].Value.ToLowerInvariant() switch
        {
            "abt" => DateQualifier.About,
            "bef" => DateQualifier.Before,
            "aft" => DateQualifier.After,
            "est" => DateQualifier.Estimated,
            _ => DateQualifier.Exact,
        };

        var year = int.Parse(match.Groups["year"].Value);
        int? month = match.Groups["month"].Success ? int.Parse(match.Groups["month"].Value) : null;
        int? day = match.Groups["day"].Success ? int.Parse(match.Groups["day"].Value) : null;

        return new PartialDate(year, month, day, qualifier);
    }

    public override string ToString()
    {
        var prefix = Qualifier switch
        {
            DateQualifier.About => "abt ",
            DateQualifier.Before => "bef ",
            DateQualifier.After => "aft ",
            DateQualifier.Estimated => "est ",
            _ => "",
        };

        var datePart = (Month, Day) switch
        {
            (int m, int d) => $"{Year:D4}-{m:D2}-{d:D2}",
            (int m, null) => $"{Year:D4}-{m:D2}",
            _ => $"{Year:D4}",
        };

        return prefix + datePart;
    }

    [GeneratedRegex(@"^(?:(?<qualifier>abt|bef|aft|est)\.?\s+)?(?<year>\d{1,4})(?:-(?<month>\d{2})(?:-(?<day>\d{2}))?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ParsePattern();
}
