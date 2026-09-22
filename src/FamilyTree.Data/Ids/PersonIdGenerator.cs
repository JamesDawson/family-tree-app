namespace FamilyTree.Data.Ids;

/// <summary>
/// Generates immutable, human-readable, git-diff-friendly person ids in the form
/// "{first}-{last}-{birthYear}" (e.g. "jane-doe-1952"), falling back to a short
/// disambiguator when the birth year is unknown, and appending a numeric suffix
/// on collision.
/// </summary>
public static class PersonIdGenerator
{
    public static string Generate(string firstName, string lastName, int? birthYear, Func<string, bool> idExists)
    {
        var namePart = Slugifier.Slugify($"{firstName} {lastName}");
        var suffix = birthYear?.ToString() ?? Guid.NewGuid().ToString("N")[..8];
        var baseSlug = $"{namePart}-{suffix}";

        if (!idExists(baseSlug))
        {
            return baseSlug;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseSlug}-{i}";
            if (!idExists(candidate))
            {
                return candidate;
            }
        }
    }
}
