using System.Net;
using FamilyTree.AcceptanceTests.Support;
using FamilyTree.Data.Git;
using FamilyTree.Data.Repository;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace FamilyTree.AcceptanceTests.Steps;

[Binding]
public sealed class PersonSteps(AppFixture fixture, ScenarioState state)
{
    [Given(@"a person ""(.*)"" born in (\d+) exists")]
    [When(@"I create a person ""(.*)"" born in (\d+)")]
    public async Task CreatePersonAsync(string fullName, int birthYear)
    {
        var (first, last) = SplitName(fullName);

        var response = await fixture.Client.PostAsync("/people", FormContent(
            ("FirstName", first), ("LastName", last), ("BornOn", birthYear.ToString())));

        var location = await AssertRedirectAsync(response);
        state.RememberId(fullName, ExtractPersonId(location));
    }

    [When(@"I add a (parent|child|sibling|spouse) ""(.*)"" born in (\d+) to ""(.*)""")]
    public async Task AddRelativeAsync(string relation, string fullName, int birthYear, string ofName)
    {
        var (first, last) = SplitName(fullName);
        var ofId = state.IdFor(ofName);

        var response = await fixture.Client.PostAsync("/people", FormContent(
            ("FirstName", first), ("LastName", last), ("BornOn", birthYear.ToString()),
            ("Relation", relation), ("RelatedToId", ofId)));

        var location = await AssertRedirectAsync(response);
        Assert.IsTrue(location.EndsWith($"/people/{ofId}/edit", StringComparison.Ordinal), $"Expected redirect back to {ofName}'s edit page but got {location}.");

        var repository = fixture.Factory.Services.GetRequiredService<IPersonRepository>();
        var created = (await repository.GetAllAsync()).Single(p => p.Name.First == first && p.Name.Last == last);
        state.RememberId(fullName, created.Id);
    }

    [When(@"I set ""(.*)"" as a parent of ""(.*)""")]
    [Given(@"I set ""(.*)"" as a parent of ""(.*)""")]
    public async Task SetAsParentOfAsync(string parentName, string childName)
    {
        var response = await fixture.Client.PostAsync(
            $"/people/{state.IdFor(childName)}/relationships/parent",
            FormContent(("parentId", state.IdFor(parentName))));

        await AssertRedirectAsync(response);
    }

    [When(@"I link ""(.*)"" as an existing child of ""(.*)""")]
    public async Task LinkExistingChildAsync(string childName, string parentName)
    {
        var response = await fixture.Client.PostAsync(
            $"/people/{state.IdFor(parentName)}/relationships/child",
            FormContent(("childId", state.IdFor(childName))));

        await AssertRedirectAsync(response);
    }

    [When(@"I unlink ""(.*)"" as a child of ""(.*)""")]
    public async Task UnlinkChildAsync(string childName, string parentName)
    {
        var response = await fixture.Client.PostAsync(
            $"/people/{state.IdFor(parentName)}/relationships/child/{state.IdFor(childName)}/remove",
            FormContent());

        await AssertRedirectAsync(response);
    }

    [When(@"I link ""(.*)"" as an existing sibling of ""(.*)""")]
    public async Task LinkExistingSiblingAsync(string siblingName, string personName)
    {
        var response = await fixture.Client.PostAsync(
            $"/people/{state.IdFor(personName)}/relationships/sibling",
            FormContent(("siblingId", state.IdFor(siblingName))));

        await AssertRedirectAsync(response);
    }

    [When(@"I unlink ""(.*)"" as a sibling of ""(.*)""")]
    public async Task UnlinkSiblingAsync(string siblingName, string personName)
    {
        var response = await fixture.Client.PostAsync(
            $"/people/{state.IdFor(personName)}/relationships/sibling/{state.IdFor(siblingName)}/remove",
            FormContent());

        await AssertRedirectAsync(response);
    }

    [When(@"I link ""(.*)"" and ""(.*)"" as spouses married on ""(.*)""")]
    [Given(@"""(.*)"" and ""(.*)"" are linked as spouses married on ""(.*)""")]
    public async Task LinkAsSpousesAsync(string nameA, string nameB, string marriedOn)
    {
        var response = await fixture.Client.PostAsync(
            $"/people/{state.IdFor(nameA)}/relationships/spouse",
            FormContent(("spouseId", state.IdFor(nameB)), ("marriedOn", marriedOn), ("current", "true")));

        await AssertRedirectAsync(response);
    }

    [When(@"I remove the spouse link between ""(.*)"" and ""(.*)""")]
    public async Task RemoveSpouseLinkAsync(string nameA, string nameB)
    {
        var response = await fixture.Client.PostAsync(
            $"/people/{state.IdFor(nameA)}/relationships/spouse/{state.IdFor(nameB)}/remove",
            FormContent());

        await AssertRedirectAsync(response);
    }

    [When(@"I attempt to delete ""(.*)""")]
    public async Task AttemptToDeleteAsync(string name)
    {
        var response = await fixture.Client.PostAsync($"/people/{state.IdFor(name)}/delete", FormContent());
        state.LastRedirectLocation = await AssertRedirectAsync(response);
    }

    [When(@"I update ""(.*)""'s notes to ""(.*)""")]
    [Given(@"I update ""(.*)""'s notes to ""(.*)""")]
    public async Task UpdateNotesAsync(string name, string notes)
    {
        var id = state.IdFor(name);
        var repository = fixture.Factory.Services.GetRequiredService<IPersonRepository>();
        var person = await repository.GetByIdAsync(id) ?? throw new InvalidOperationException($"'{name}' does not exist.");

        var response = await fixture.Client.PostAsync($"/people/{id}/edit", FormContent(
            ("FirstName", person.Name.First),
            ("LastName", person.Name.Last),
            ("Sex", person.Sex.ToString().ToLowerInvariant()),
            ("BornOn", person.BornOn?.ToString() ?? ""),
            ("Notes", notes)));

        await AssertRedirectAsync(response);
    }

    [When(@"I revert ""(.*)"" to their first commit")]
    public async Task RevertToFirstCommitAsync(string name)
    {
        var id = state.IdFor(name);
        var gitService = fixture.Factory.Services.GetRequiredService<IGitRepositoryService>();
        var history = gitService.GetHistory($"people/{id}.md");
        var firstCommit = history[^1];

        var response = await fixture.Client.PostAsync($"/people/{id}/history/{firstCommit.Sha}/revert", FormContent());
        await AssertRedirectAsync(response);
    }

    [Then(@"""(.*)""'s details page lists ""(.*)"" as a child")]
    public async Task DetailsPageListsAsChildAsync(string parentName, string childName)
    {
        var content = await GetDetailsPageAsync(parentName);
        Assert.IsTrue(content.Contains(childName, StringComparison.Ordinal), $"Expected {parentName}'s Details page to list {childName} as a child.\n{content}");
    }

    [Then(@"""(.*)""'s details page does not list ""(.*)"" as a child")]
    public async Task DetailsPageDoesNotListChildAsync(string parentName, string childName)
    {
        var content = await GetDetailsPageAsync(parentName);
        var start = content.IndexOf("<h2>Children</h2>", StringComparison.Ordinal);
        var end = content.IndexOf("<h2>Siblings</h2>", StringComparison.Ordinal);
        Assert.IsFalse(content[start..end].Contains(childName, StringComparison.Ordinal), $"Expected {parentName}'s Children section to NOT list {childName}.");
    }

    [Then(@"""(.*)""'s details page lists ""(.*)"" as a sibling")]
    public async Task DetailsPageListsAsSiblingAsync(string name, string siblingName)
    {
        var content = await GetDetailsPageAsync(name);
        var start = content.IndexOf("<h2>Siblings</h2>", StringComparison.Ordinal);
        var end = content.IndexOf("<h2>Spouses</h2>", StringComparison.Ordinal);
        Assert.IsTrue(content[start..end].Contains(siblingName, StringComparison.Ordinal), $"Expected {name}'s Siblings section to list {siblingName}.");
    }

    [Then(@"""(.*)""'s details page does not list ""(.*)"" as a sibling")]
    public async Task DetailsPageDoesNotListAsSiblingAsync(string name, string siblingName)
    {
        var content = await GetDetailsPageAsync(name);
        var start = content.IndexOf("<h2>Siblings</h2>", StringComparison.Ordinal);
        var end = content.IndexOf("<h2>Spouses</h2>", StringComparison.Ordinal);
        Assert.IsFalse(content[start..end].Contains(siblingName, StringComparison.Ordinal), $"Expected {name}'s Siblings section to NOT list {siblingName}.");
    }

    [Then(@"""(.*)""'s details page shows ""(.*)"" as a spouse")]
    public async Task DetailsPageShowsSpouseAsync(string name, string spouseName)
    {
        var content = await GetDetailsPageAsync(name);
        Assert.IsTrue(content.Contains(spouseName, StringComparison.Ordinal), $"Expected {name}'s Details page to show {spouseName} as a spouse.\n{content}");
    }

    [Then(@"""(.*)""'s details page does not show ""(.*)"" as a spouse")]
    public async Task DetailsPageDoesNotShowSpouseAsync(string name, string spouseName)
    {
        var content = await GetDetailsPageAsync(name);
        Assert.IsFalse(content.Contains(spouseName, StringComparison.Ordinal), $"Expected {name}'s Details page to NOT show {spouseName} as a spouse.\n{content}");
    }

    [Then(@"""(.*)""'s details page shows the notes ""(.*)""")]
    public async Task DetailsPageShowsNotesAsync(string name, string notes)
    {
        var content = await GetDetailsPageAsync(name);
        Assert.IsTrue(content.Contains(notes, StringComparison.Ordinal), $"Expected {name}'s Details page to show notes '{notes}'.\n{content}");
    }

    [Then(@"""(.*)""'s notes are empty again")]
    public async Task NotesAreEmptyAgainAsync(string name)
    {
        var repository = fixture.Factory.Services.GetRequiredService<IPersonRepository>();
        var person = await repository.GetByIdAsync(state.IdFor(name));
        Assert.AreEqual("", person!.Notes);
    }

    [Then(@"""(.*)"" has exactly (\d+) commits?")]
    public void HasExactlyCommits(string name, int expectedCount)
    {
        var gitService = fixture.Factory.Services.GetRequiredService<IGitRepositoryService>();
        var history = gitService.GetHistory($"people/{state.IdFor(name)}.md");
        Assert.AreEqual(expectedCount, history.Count);
    }

    [Then("the deletion is blocked")]
    public void TheDeletionIsBlocked()
    {
        Assert.IsFalse(state.LastRedirectLocation!.Equals("/people", StringComparison.OrdinalIgnoreCase),
            "Expected the delete to be blocked (redirected back to Details), but it redirected to the People index.");
    }

    [Then(@"""(.*)"" (?:still exists|exists)")]
    public async Task StillExistsAsync(string name)
    {
        var repository = fixture.Factory.Services.GetRequiredService<IPersonRepository>();
        var person = await repository.GetByIdAsync(state.IdFor(name));
        Assert.IsNotNull(person, $"Expected '{name}' to still exist.");
    }

    [Then(@"""(.*)"" no longer exists")]
    public async Task NoLongerExistsAsync(string name)
    {
        var repository = fixture.Factory.Services.GetRequiredService<IPersonRepository>();
        var person = await repository.GetByIdAsync(state.IdFor(name));
        Assert.IsNull(person, $"Expected '{name}' to no longer exist.");
    }

    [Then(@"the data directory is a git repository with exactly (\d+) commits?")]
    public void DataDirectoryIsAGitRepositoryWithExactlyCommits(int expectedCount)
    {
        var gitService = fixture.Factory.Services.GetRequiredService<IGitRepositoryService>();
        var history = gitService.GetHistory("README.md");
        Assert.AreEqual(expectedCount, history.Count);
    }

    private async Task<string> GetDetailsPageAsync(string name)
    {
        var response = await fixture.Client.GetAsync($"/people/{state.IdFor(name)}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> AssertRedirectAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Found)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Expected a 302 redirect but got {(int)response.StatusCode} {response.StatusCode}.\n{body}");
        }

        return response.Headers.Location!.ToString();
    }

    private static string ExtractPersonId(string location)
    {
        var segments = location.Trim('/').Split('/');
        return segments is ["people", var id] ? id : throw new InvalidOperationException($"Could not parse a person id out of '{location}'.");
    }

    private static (string First, string Last) SplitName(string fullName)
    {
        var lastSpace = fullName.LastIndexOf(' ');
        return lastSpace < 0
            ? (fullName, "")
            : (fullName[..lastSpace], fullName[(lastSpace + 1)..]);
    }

    private static FormUrlEncodedContent FormContent(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));
}
