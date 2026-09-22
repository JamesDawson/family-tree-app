using System.Text.RegularExpressions;

namespace FamilyTree.Data.Parsing;

/// <summary>
/// Splits a person file into its YAML front matter and Markdown body. Only the delimiter pair at the
/// very start of the file is treated as the front matter boundary, so a Markdown horizontal rule
/// ("---") appearing later in the notes body is never mistaken for a delimiter.
/// </summary>
public static partial class FrontMatterSplitter
{
    public static (string FrontMatter, string Body) Split(string fileContents)
    {
        var normalized = fileContents.Replace("\r\n", "\n");
        var match = SplitPattern().Match(normalized);

        if (!match.Success)
        {
            throw new FormatException("File does not start with a '---' / '---' YAML front matter block.");
        }

        return (match.Groups["frontMatter"].Value, match.Groups["body"].Value);
    }

    public static string Combine(string frontMatterYaml, string body)
    {
        var trimmedFrontMatter = frontMatterYaml.TrimEnd('\n');
        var trimmedBody = body.Trim('\n');

        return trimmedBody.Length == 0
            ? $"---\n{trimmedFrontMatter}\n---\n"
            : $"---\n{trimmedFrontMatter}\n---\n\n{trimmedBody}\n";
    }

    [GeneratedRegex(@"\A---\n(?<frontMatter>.*?)\n---[ \t]*\n?(?<body>.*)\z", RegexOptions.Singleline)]
    private static partial Regex SplitPattern();
}
