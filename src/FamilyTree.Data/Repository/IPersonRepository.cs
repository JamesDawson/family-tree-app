using FamilyTree.Data.Git;
using FamilyTree.Data.Models;

namespace FamilyTree.Data.Repository;

public interface IPersonRepository
{
    Task<IReadOnlyList<Person>> GetAllAsync(CancellationToken ct = default);
    Task<Person?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Creates a new person. Whatever <see cref="Person.Id"/> is set on the input is ignored —
    /// the repository always assigns a fresh id derived from the person's name and birth year.</summary>
    Task<Person> CreateAsync(Person person, CommitAuthor author, CancellationToken ct = default);

    /// <summary>Creates a new person and links them to an existing person in a single commit.
    /// <list type="bullet">
    /// <item>Child: the new person's parents are <paramref name="relatedToId"/> and optionally <paramref name="secondParentId"/>.</item>
    /// <item>Sibling: the new person gets the same parents as <paramref name="relatedToId"/>.</item>
    /// <item>Parent: the new person is added to <paramref name="relatedToId"/>'s parents (at most two).</item>
    /// <item>Spouse: a reciprocal spouse link is written using <paramref name="spouse"/> for the dates/current flag.</item>
    /// </list>
    /// Throws <see cref="PersonNotFoundException"/> for an unknown related person and
    /// <see cref="InvalidOperationException"/> if the relationship can't be formed.</summary>
    Task<Person> CreateRelatedAsync(Person person, RelationKind kind, string relatedToId, string? secondParentId, SpouseRelationship? spouse, CommitAuthor author, CancellationToken ct = default);

    Task<Person> UpdateAsync(Person person, CommitAuthor author, CancellationToken ct = default);

    /// <summary>Deletes a person. Throws <see cref="PersonHasDependentsException"/> if anyone else still
    /// references them as a parent or spouse.</summary>
    Task DeleteAsync(string id, CommitAuthor author, CancellationToken ct = default);

    /// <summary>Adds (or replaces) a spouse relationship between two people, updating and committing both
    /// of their files together in a single commit.</summary>
    Task AddSpouseRelationshipAsync(string personAId, string personBId, SpouseRelationship relationship, CommitAuthor author, CancellationToken ct = default);

    Task RemoveSpouseRelationshipAsync(string personAId, string personBId, CommitAuthor author, CancellationToken ct = default);

    Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(string id, CancellationToken ct = default);

    /// <summary>Returns the most recently changed people with their latest commit, newest first.</summary>
    Task<IReadOnlyList<(Person Person, CommitInfo LatestCommit)>> GetRecentlyChangedAsync(int count, CancellationToken ct = default);

    /// <summary>Reverts a person's file to its content as of the given commit, as a new forward commit.</summary>
    Task<Person> RevertToCommitAsync(string id, string commitSha, CommitAuthor author, CancellationToken ct = default);
}
