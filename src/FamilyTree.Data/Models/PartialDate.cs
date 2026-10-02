using System.Text.RegularExpressions;

namespace FamilyTree.Data.Models;

public enum DateQualifier { Exact, About, Before, After, Estimated }

public sealed partial record PartialDate(int Year, int? Month, int? Day, DateQualifier Qualifier = DateQualifier.Exact, int? Quarter = null)
{
    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>First month of the period this date covers (the quarter's first month, or the month).</summary>
    public int? StartMonth => Quarter is int q ? 3 * (q - 1) + 1 : Month;

    /// <summary>Parses the canonical ISO form (1952-03-14, 1952-Q1) or the display form (14-Mar-1952, Jan/Mar-1952).</summary>
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
            throw new FormatException($"'{text}' is not a recognized date. Use DD-MON-YYYY (e.g. 15-Jun-1976), MON-YYYY, a 3-month quarter (e.g. Jul/Sep-1976) or YYYY, optionally prefixed with 'abt', 'bef', 'aft' or 'est'.");
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

        int? quarter = null;
        if (match.Groups["quarter"].Success)
        {
            quarter = int.Parse(match.Groups["quarter"].Value);
        }
        else if (match.Groups["qstart"].Success)
        {
            var start = MonthIndex(match.Groups["qstart"].Value);
            var end = MonthIndex(match.Groups["qend"].Value);
            if ((start - 1) % 3 != 0 || end != start + 2)
            {
                throw new FormatException($"'{text}' is not a 3-month quarter. Use Jan/Mar, Apr/Jun, Jul/Sep or Oct/Dec.");
            }

            quarter = (start - 1) / 3 + 1;
        }

        int? month = null;
        if (match.Groups["month"].Success)
        {
            var monthText = match.Groups["month"].Value;
            month = char.IsDigit(monthText[0]) ? int.Parse(monthText) : MonthIndex(monthText);
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

        return new PartialDate(year, month, day, qualifier, quarter);
    }

    private static int MonthIndex(string name) =>
        Array.FindIndex(MonthNames, m => m.Equals(name, StringComparison.OrdinalIgnoreCase)) + 1;

    /// <summary>Canonical storage form: YYYY, YYYY-Qn, YYYY-MM or YYYY-MM-DD, with any qualifier prefix.</summary>
    public override string ToString()
    {
        var datePart = (Month, Day) switch
        {
            _ when Quarter is int q => $"{Year:D4}-Q{q}",
            (int m, int d) => $"{Year:D4}-{m:D2}-{d:D2}",
            (int m, null) => $"{Year:D4}-{m:D2}",
            _ => $"{Year:D4}",
        };

        return QualifierPrefix() + datePart;
    }

    /// <summary>Display form: DD-MON-YYYY (e.g. 15-Jun-1976), MON-YYYY, MON/MON-YYYY (quarter) or YYYY, with any qualifier prefix.</summary>
    public string ToDisplayString()
    {
        var datePart = (Month, Day) switch
        {
            _ when Quarter is int q => $"{MonthNames[3 * (q - 1)]}/{MonthNames[3 * (q - 1) + 2]}-{Year:D4}",
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

    [GeneratedRegex(@"^(?:(?<qualifier>abt|bef|aft|est)\.?\s+)?(?<year>\d{1,4})(?:-(?:Q(?<quarter>[1-4])|(?<month>\d{2})(?:-(?<day>\d{2}))?))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ParsePattern();

    [GeneratedRegex(@"^(?:(?<qualifier>abt|bef|aft|est)\.?\s+)?(?:(?:(?<day>\d{1,2})-)?(?<month>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)-|(?<qstart>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[/-](?<qend>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[-\s]|Q(?<quarter>[1-4])[-\s])?(?<year>\d{1,4})$", RegexOptions.IgnoreCase)]
    private static partial Regex DisplayParsePattern();
}
