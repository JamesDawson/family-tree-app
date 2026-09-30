using System.Text.RegularExpressions;

namespace FamilyTree.Data.Models;

public enum DateQualifier { Exact, About, Before, After, Estimated }

public sealed partial record PartialDate(int Year, int? Month, int? Day, DateQualifier Qualifier = DateQualifier.Exact)
{
    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>Parses the canonical ISO form (1952-03-14) or the display form (14-Mar-1952).</summary>
    public static PartialDate? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        var match = ParsePattern().Match(trimmed);
        if (!match.Success)
        {
            match = DisplayParsePattern().Match(trimmed);
        }

        if (!match.Success)
        {
            throw new FormatException($"'{text}' is not a recognized date. Use DD-MON-YYYY (e.g. 15-Jun-1976), MON-YYYY or YYYY, optionally prefixed with 'abt', 'bef', 'aft' or 'est'.");
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
        int? month = null;
        if (match.Groups["month"].Success)
        {
            var monthText = match.Groups["month"].Value;
            month = char.IsDigit(monthText[0])
                ? int.Parse(monthText)
                : Array.FindIndex(MonthNames, m => m.Equals(monthText, StringComparison.OrdinalIgnoreCase)) + 1;
        }

        int? day = match.Groups["day"].Success ? int.Parse(match.Groups["day"].Value) : null;

        if (month is < 1 or > 12)
        {
            throw new FormatException($"'{text}' has an invalid month.");
        }

        if (day is int d && (d < 1 || d > DateTime.DaysInMonth(year < 1 ? 1 : year, month!.Value)))
        {
            throw new FormatException($"'{text}' has an invalid day.");
        }

        return new PartialDate(year, month, day, qualifier);
    }

    /// <summary>Canonical storage form: YYYY, YYYY-MM or YYYY-MM-DD, with any qualifier prefix.</summary>
    public override string ToString()
    {
        var datePart = (Month, Day) switch
        {
            (int m, int d) => $"{Year:D4}-{m:D2}-{d:D2}",
            (int m, null) => $"{Year:D4}-{m:D2}",
            _ => $"{Year:D4}",
        };

        return QualifierPrefix() + datePart;
    }

    /// <summary>Display form: DD-MON-YYYY (e.g. 15-Jun-1976), MON-YYYY or YYYY, with any qualifier prefix.</summary>
    public string ToDisplayString()
    {
        var datePart = (Month, Day) switch
        {
            (int m, int d) => $"{d:D2}-{MonthNames[m - 1]}-{Year:D4}",
            (int m, null) => $"{MonthNames[m - 1]}-{Year:D4}",
            _ => $"{Year:D4}",
        };

        return QualifierPrefix() + datePart;
    }

    private string QualifierPrefix() => Qualifier switch
    {
        DateQualifier.About => "abt ",
        DateQualifier.Before => "bef ",
        DateQualifier.After => "aft ",
        DateQualifier.Estimated => "est ",
        _ => "",
    };

    [GeneratedRegex(@"^(?:(?<qualifier>abt|bef|aft|est)\.?\s+)?(?<year>\d{1,4})(?:-(?<month>\d{2})(?:-(?<day>\d{2}))?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ParsePattern();

    [GeneratedRegex(@"^(?:(?<qualifier>abt|bef|aft|est)\.?\s+)?(?:(?:(?<day>\d{1,2})-)?(?<month>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)-)?(?<year>\d{1,4})$", RegexOptions.IgnoreCase)]
    private static partial Regex DisplayParsePattern();
}
