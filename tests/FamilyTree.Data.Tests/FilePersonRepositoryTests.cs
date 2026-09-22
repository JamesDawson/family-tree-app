using FamilyTree.Data.Git;
using FamilyTree.Data.Models;
using FamilyTree.Data.Options;
using FamilyTree.Data.Parsing;
using FamilyTree.Data.Repository;
using Microsoft.Extensions.Options;

namespace FamilyTree.Data.Tests;

[TestClass]
public class FilePersonRepositoryTests
{
    private static readonly CommitAuthor Author = new("Test Author", "test@example.com");

    private string _tempRoot = "";
    private FilePersonRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FamilyTreeTests", Guid.NewGuid().ToString("N"));
        var paths = new FamilyTreeDataPaths { RootPath = _tempRoot };
        var options = Microsoft.Extensions.Options.Options.Create(new FamilyTreeDataOptions
        {
            RepositoryPath = _tempRoot,
            DefaultCommitAuthorName = Author.Name,
            DefaultCommitAuthorEmail = Author.Email,
        });

        var git = new LibGit2GitRepositoryService(paths, options);
        git.EnsureInitialized();
        _repository = new FilePersonRepository(paths, git, new YamlFrontMatterPersonSerializer());
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (!Directory.Exists(_tempRoot))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(_tempRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_tempRoot, recursive: true);
    }

    private static Person NewPerson(string first, string last, int? birthYear = null) => new()
    {
        Id = "",
        Name = new PersonName(first, null, last, null),
        BornOn = birthYear.HasValue ? new PartialDate(birthYear.Value, null, null) : null,
    };

    [TestMethod]
    public async Task CreateAsync_GeneratesIdWritesFileAndCommits()
    {
        var created = await _repository.CreateAsync(NewPerson("John", "Doe", 1920), Author);

        Assert.AreEqual("john-doe-1920", created.Id);
        Assert.IsTrue(File.Exists(Path.Combine(_tempRoot, "people", "john-doe-1920.md")));

        var history = await _repository.GetHistoryAsync(created.Id);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("Add person: John Doe", history[0].Message);
    }

    [TestMethod]
    public async Task UpdateAsync_WritesChangesAndCommits()
    {
        var created = await _repository.CreateAsync(NewPerson("John", "Doe", 1920), Author);

        created.Notes = "Updated notes.";
        await _repository.UpdateAsync(created, Author);

        var reloaded = await _repository.GetByIdAsync(created.Id);
        Assert.AreEqual("Updated notes.", reloaded!.Notes);
        Assert.AreEqual(2, (await _repository.GetHistoryAsync(created.Id)).Count);
    }

    [TestMethod]
    public async Task UpdateAsync_ThrowsWhenPersonDoesNotExist()
    {
        var phantom = new Person { Id = "does-not-exist", Name = new PersonName("Nobody", null, "Nowhere", null) };

        await Assert.ThrowsExactlyAsync<PersonNotFoundException>(() => _repository.UpdateAsync(phantom, Author));
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesPersonWhenUnreferenced()
    {
        var created = await _repository.CreateAsync(NewPerson("John", "Doe", 1920), Author);

        await _repository.DeleteAsync(created.Id, Author);

        Assert.IsNull(await _repository.GetByIdAsync(created.Id));
    }

    [TestMethod]
    public async Task DeleteAsync_ThrowsWhenPersonIsReferencedAsAParent()
    {
        var parent = await _repository.CreateAsync(NewPerson("John", "Doe", 1920), Author);
        var child = await _repository.CreateAsync(NewPerson("Jane", "Doe", 1952), Author);
        child.ParentIds = [parent.Id];
        await _repository.UpdateAsync(child, Author);

        var ex = await Assert.ThrowsExactlyAsync<PersonHasDependentsException>(() => _repository.DeleteAsync(parent.Id, Author));
        CollectionAssert.Contains(ex.DependentIds.ToList(), child.Id);
    }

    [TestMethod]
    public async Task DeleteAsync_ThrowsWhenPersonIsReferencedAsASpouse()
    {
        var personA = await _repository.CreateAsync(NewPerson("Robert", "Doe", 1950), Author);
        var personB = await _repository.CreateAsync(NewPerson("Jane", "Doe", 1952), Author);
        await _repository.AddSpouseRelationshipAsync(personA.Id, personB.Id, new SpouseRelationship("", new PartialDate(1974, 6, 2), null, true), Author);

        await Assert.ThrowsExactlyAsync<PersonHasDependentsException>(() => _repository.DeleteAsync(personA.Id, Author));
    }

    [TestMethod]
    public async Task AddSpouseRelationshipAsync_LinksBothPeopleInASingleCommitEach()
    {
        var personA = await _repository.CreateAsync(NewPerson("Robert", "Doe", 1950), Author);
        var personB = await _repository.CreateAsync(NewPerson("Jane", "Doe", 1952), Author);

        await _repository.AddSpouseRelationshipAsync(personA.Id, personB.Id, new SpouseRelationship("", new PartialDate(1974, 6, 2), null, true), Author);

        var reloadedA = await _repository.GetByIdAsync(personA.Id);
        var reloadedB = await _repository.GetByIdAsync(personB.Id);

        Assert.AreEqual(1, reloadedA!.Spouses.Count);
        Assert.AreEqual(personB.Id, reloadedA.Spouses[0].SpouseId);
        Assert.AreEqual(1, reloadedB!.Spouses.Count);
        Assert.AreEqual(personA.Id, reloadedB.Spouses[0].SpouseId);

        Assert.AreEqual(2, (await _repository.GetHistoryAsync(personA.Id)).Count);
        Assert.AreEqual(2, (await _repository.GetHistoryAsync(personB.Id)).Count);
    }

    [TestMethod]
    public async Task RemoveSpouseRelationshipAsync_UnlinksBothPeople()
    {
        var personA = await _repository.CreateAsync(NewPerson("Robert", "Doe", 1950), Author);
        var personB = await _repository.CreateAsync(NewPerson("Jane", "Doe", 1952), Author);
        await _repository.AddSpouseRelationshipAsync(personA.Id, personB.Id, new SpouseRelationship("", null, null, true), Author);

        await _repository.RemoveSpouseRelationshipAsync(personA.Id, personB.Id, Author);

        var reloadedA = await _repository.GetByIdAsync(personA.Id);
        var reloadedB = await _repository.GetByIdAsync(personB.Id);
        Assert.AreEqual(0, reloadedA!.Spouses.Count);
        Assert.AreEqual(0, reloadedB!.Spouses.Count);
    }

    [TestMethod]
    public async Task RevertToCommitAsync_RestoresPriorContentAsANewCommit()
    {
        var created = await _repository.CreateAsync(NewPerson("John", "Doe", 1920), Author);
        var firstSha = (await _repository.GetHistoryAsync(created.Id))[0].Sha;

        created.Notes = "Changed notes.";
        await _repository.UpdateAsync(created, Author);

        var reverted = await _repository.RevertToCommitAsync(created.Id, firstSha, Author);

        Assert.AreEqual("", reverted.Notes);
        Assert.AreEqual(3, (await _repository.GetHistoryAsync(created.Id)).Count, "Revert must add a commit, not remove one.");
    }

    [TestMethod]
    public async Task ConcurrentUpdates_CompleteWithoutExceptionAndProduceSequentialCommits()
    {
        var created = await _repository.CreateAsync(NewPerson("John", "Doe", 1920), Author);

        var updateOne = UpdateBornPlaceAsync(created.Id, "Leeds");
        var updateTwo = UpdateDiedPlaceAsync(created.Id, "Manchester");

        await Task.WhenAll(updateOne, updateTwo);

        var history = await _repository.GetHistoryAsync(created.Id);
        Assert.AreEqual(3, history.Count, "Expected create + 2 serialized updates.");
    }

    private async Task UpdateBornPlaceAsync(string id, string place)
    {
        var person = (await _repository.GetByIdAsync(id))!;
        person.BornPlace = place;
        await _repository.UpdateAsync(person, Author);
    }

    private async Task UpdateDiedPlaceAsync(string id, string place)
    {
        var person = (await _repository.GetByIdAsync(id))!;
        person.DiedPlace = place;
        await _repository.UpdateAsync(person, Author);
    }
}
