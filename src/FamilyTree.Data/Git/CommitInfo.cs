namespace FamilyTree.Data.Git;

public sealed record CommitInfo(string Sha, string ShortSha, string Message, string AuthorName, DateTimeOffset When);
