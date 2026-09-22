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

    Task<Person> UpdateAsync(Person person, CommitAuthor author, CancellationToken ct = default);

    /// <summary>Deletes a person. Throws <see cref="PersonHasDependentsException"/> if anyone else still
    /// references them as a parent or spouse.</summary>
    Task DeleteAsync(string id, CommitAuthor author, CancellationToken ct = default);

    /// <summary>Adds (or replaces) a spouse relationship between two people, updating and committing both
    /// of their files together in a single commit.</summary>
    Task AddSpouseRelationshipAsync(string personAId, string personBId, SpouseRelationship relationship, CommitAuthor author, CancellationToken ct = default);

    Task RemoveSpouseRelationshipAsync(string personAId, string personBId, CommitAuthor author, CancellationToken ct = default);

    Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(string id, CancellationToken ct = default);

    /// <summary>Reverts a person's file to its content as of the given commit, as a new forward commit.</summary>
    Task<Person> RevertToCommitAsync(string id, string commitSha, CommitAuthor author, CancellationToken ct = default);
}
