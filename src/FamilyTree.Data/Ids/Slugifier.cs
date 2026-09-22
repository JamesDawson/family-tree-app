using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyTree.Data.Ids;

public static partial class Slugifier
{
    public static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new StringBuilder();

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                withoutDiacritics.Append(c);
            }
        }

        var lowered = withoutDiacritics.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var withDashes = NonAlphaNumericRun().Replace(lowered, "-");
        return withDashes.Trim('-');
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphaNumericRun();
}
