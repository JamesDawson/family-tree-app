using FamilyTree.Data.Git;
using FamilyTree.Data.Ids;
using FamilyTree.Data.Models;
using FamilyTree.Data.Options;
using FamilyTree.Data.Parsing;

namespace FamilyTree.Data.Repository;

public sealed class FilePersonRepository : IPersonRepository
{
    private readonly FamilyTreeDataPaths _paths;
    private readonly IGitRepositoryService _git;
    private readonly IPersonFileSerializer _serializer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Parsing every people/*.md on every request doesn't scale, so GetAllAsync serves a snapshot. The
    // snapshot is dropped on every write made through this class, and is also re-validated against a cheap
    // fingerprint of the directory (file names, sizes, mtimes) so out-of-band changes — a git pull, a
    // hand edit — are picked up too. Person instances in a snapshot are shared between callers: treat them
    // as read-only (use GetByIdAsync for an instance you intend to modify).
    private volatile Snapshot? _snapshot;

    private sealed record Snapshot(long Fingerprint, IReadOnlyList<Person> People);

    public FilePersonRepository(FamilyTreeDataPaths paths, IGitRepositoryService git, IPersonFileSerializer serializer)
    {
        _paths = paths;
        _git = git;
        _serializer = serializer;
        Directory.CreateDirectory(_paths.PeopleDirectory);
    }

    public async Task<IReadOnlyList<Person>> GetAllAsync(CancellationToken ct = default)
    {
        var fingerprint = ComputeFingerprint();
        var cached = _snapshot;
        if (cached is not null && cached.Fingerprint == fingerprint)
        {
            return cached.People;
        }

        var people = await LoadAllAsync(ct);
        _snapshot = new Snapshot(fingerprint, people);
        return people;
    }

    public async Task<Person?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var path = FullPathFor(id);
        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, ct);
        return _serializer.Parse(id, content);
    }

    public async Task<Person> CreateAsync(Person person, CommitAuthor author, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var existingIds = LoadAllIds();
            var id = PersonIdGenerator.Generate(person.Name.First, person.Name.Last, person.BornOn?.Year, existingIds.Contains);

            var toSave = new Person
            {
                Id = id,
                Name = person.Name,
                Sex = person.Sex,
                BornOn = person.BornOn,
                BornPlace = person.BornPlace,
                DiedOn = person.DiedOn,
                DiedPlace = person.DiedPlace,
                ParentIds = person.ParentIds,
                Spouses = person.Spouses,
                Notes = person.Notes,
            };

            await WriteFileAsync(toSave, ct);
            _git.CommitFiles([RelativePathFor(id)], $"Add person: {toSave.Name.DisplayName}", author);

            return toSave;
        }
        finally
        {
            InvalidateSnapshot();
            _writeLock.Release();
        }
    }

    public async Task<Person> CreateRelatedAsync(Person person, RelationKind kind, string relatedToId, string? secondParentId, SpouseRelationship? spouse, CommitAuthor author, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var related = await LoadRequiredAsync(relatedToId, ct);
            var existingIds = LoadAllIds();

            if (kind == RelationKind.Child && !string.IsNullOrWhiteSpace(secondParentId))
            {
                if (secondParentId == relatedToId || !existingIds.Contains(secondParentId))
                {
                    throw new PersonNotFoundException(secondParentId);
                }
            }

            if (kind == RelationKind.Parent && related.ParentIds.Count >= 2)
            {
                throw new InvalidOperationException($"{related.Name.DisplayName} already has two parents.");
            }

            var id = PersonIdGenerator.Generate(person.Name.First, person.Name.Last, person.BornOn?.Year, existingIds.Contains);

            var toSave = new Person
            {
                Id = id,
                Name = person.Name,
                Sex = person.Sex,
                BornOn = person.BornOn,
                BornPlace = person.BornPlace,
                DiedOn = person.DiedOn,
                DiedPlace = person.DiedPlace,
                ParentIds = [],
                Spouses = [],
                Notes = person.Notes,
            };

            var paths = new List<string> { RelativePathFor(id) };

            switch (kind)
            {
                case RelationKind.Child:
                    toSave.ParentIds = string.IsNullOrWhiteSpace(secondParentId) ? [relatedToId] : [relatedToId, secondParentId];
                    break;

                case RelationKind.Sibling:
                    toSave.ParentIds = [.. related.ParentIds];
                    break;

                case RelationKind.Parent:
                    related.ParentIds = [.. related.ParentIds, id];
                    await WriteFileAsync(related, ct);
                    paths.Add(RelativePathFor(relatedToId));
                    break;

                case RelationKind.Spouse:
                    var relationship = spouse ?? new SpouseRelationship(relatedToId, null, null, false);
                    toSave.Spouses = [relationship with { SpouseId = relatedToId }];
                    related.Spouses = [.. related.Spouses.Where(s => s.SpouseId != id), relationship with { SpouseId = id }];
                    await WriteFileAsync(related, ct);
                    paths.Add(RelativePathFor(relatedToId));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }

            await WriteFileAsync(toSave, ct);

            _git.CommitFiles(
                paths,
                $"Add {kind.ToString().ToLowerInvariant()}: {toSave.Name.DisplayName} (of {related.Name.DisplayName})",
                author);

            return toSave;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<Person> UpdateAsync(Person person, CommitAuthor author, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(FullPathFor(person.Id)))
            {
                throw new PersonNotFoundException(person.Id);
            }

            await WriteFileAsync(person, ct);
            _git.CommitFiles([RelativePathFor(person.Id)], $"Update person: {person.Name.DisplayName}", author);

            return person;
        }
        finally
        {
            InvalidateSnapshot();
            _writeLock.Release();
        }
    }

    public async Task DeleteAsync(string id, CommitAuthor author, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var all = await LoadAllAsync(ct);
            var person = all.FirstOrDefault(p => p.Id == id) ?? throw new PersonNotFoundException(id);

            var dependents = all
                .Where(p => p.Id != id && (p.ParentIds.Contains(id) || p.Spouses.Any(s => s.SpouseId == id)))
                .Select(p => p.Id)
                .ToList();

            if (dependents.Count > 0)
            {
                throw new PersonHasDependentsException(id, dependents);
            }

            _git.CommitDeletion(RelativePathFor(id), $"Delete person: {person.Name.DisplayName}", author);
        }
        finally
        {
            InvalidateSnapshot();
            _writeLock.Release();
        }
    }

    public async Task AddSpouseRelationshipAsync(string personAId, string personBId, SpouseRelationship relationship, CommitAuthor author, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var personA = await LoadRequiredAsync(personAId, ct);
            var personB = await LoadRequiredAsync(personBId, ct);

            personA.Spouses = [.. personA.Spouses.Where(s => s.SpouseId != personBId), relationship with { SpouseId = personBId }];
            personB.Spouses = [.. personB.Spouses.Where(s => s.SpouseId != personAId), relationship with { SpouseId = personAId }];

            await WriteFileAsync(personA, ct);
            await WriteFileAsync(personB, ct);

            _git.CommitFiles(
                [RelativePathFor(personAId), RelativePathFor(personBId)],
                $"Link spouses: {personA.Name.DisplayName} & {personB.Name.DisplayName}",
                author);
        }
        finally
        {
            InvalidateSnapshot();
            _writeLock.Release();
        }
    }

    public async Task RemoveSpouseRelationshipAsync(string personAId, string personBId, CommitAuthor author, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var personA = await LoadRequiredAsync(personAId, ct);
            var personB = await LoadRequiredAsync(personBId, ct);

            personA.Spouses = [.. personA.Spouses.Where(s => s.SpouseId != personBId)];
            personB.Spouses = [.. personB.Spouses.Where(s => s.SpouseId != personAId)];

            await WriteFileAsync(personA, ct);
            await WriteFileAsync(personB, ct);

            _git.CommitFiles(
                [RelativePathFor(personAId), RelativePathFor(personBId)],
                $"Unlink spouses: {personA.Name.DisplayName} & {personB.Name.DisplayName}",
                author);
        }
        finally
        {
            InvalidateSnapshot();
            _writeLock.Release();
        }
    }

    public Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_git.GetHistory(RelativePathFor(id)));

    public async Task<Person> RevertToCommitAsync(string id, string commitSha, CommitAuthor author, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            _git.RevertFileToCommit(RelativePathFor(id), commitSha, $"Revert '{id}' to {commitSha[..7]}", author);

            var content = await File.ReadAllTextAsync(FullPathFor(id), ct);
            return _serializer.Parse(id, content);
        }
        finally
        {
            InvalidateSnapshot();
            _writeLock.Release();
        }
    }

    // Called from each write's finally block: files may already have changed even if the commit threw.
    private void InvalidateSnapshot() => _snapshot = null;

    private long ComputeFingerprint()
    {
        // Order-insensitive so it doesn't depend on directory enumeration order.
        long sum = 0;
        var count = 0;
        foreach (var file in new DirectoryInfo(_paths.PeopleDirectory).EnumerateFiles("*.md"))
        {
            sum += HashCode.Combine(file.Name, file.LastWriteTimeUtc.Ticks, file.Length);
            count++;
        }

        return HashCode.Combine(sum, count);
    }

    private string FullPathFor(string id) => Path.Combine(_paths.PeopleDirectory, $"{id}.md");

    private static string RelativePathFor(string id) => $"people/{id}.md";

    private async Task<Person> LoadRequiredAsync(string id, CancellationToken ct)
    {
        var path = FullPathFor(id);
        if (!File.Exists(path))
        {
            throw new PersonNotFoundException(id);
        }

        var content = await File.ReadAllTextAsync(path, ct);
        return _serializer.Parse(id, content);
    }

    private async Task WriteFileAsync(Person person, CancellationToken ct)
    {
        var content = _serializer.Serialize(person);
        await File.WriteAllTextAsync(FullPathFor(person.Id), content, ct);
    }

    private async Task<List<Person>> LoadAllAsync(CancellationToken ct)
    {
        var result = new List<Person>();

        foreach (var file in Directory.EnumerateFiles(_paths.PeopleDirectory, "*.md"))
        {
            ct.ThrowIfCancellationRequested();
            var id = Path.GetFileNameWithoutExtension(file);
            var content = await File.ReadAllTextAsync(file, ct);
            result.Add(_serializer.Parse(id, content));
        }

        return result;
    }

    private HashSet<string> LoadAllIds() =>
        [.. Directory.EnumerateFiles(_paths.PeopleDirectory, "*.md").Select(f => Path.GetFileNameWithoutExtension(f)!)];
}
