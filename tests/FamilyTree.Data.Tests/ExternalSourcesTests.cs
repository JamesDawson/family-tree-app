using FamilyTree.Data.ExternalSources;
using FamilyTree.Data.ExternalSources.Providers;
using FamilyTree.Data.Models;

namespace FamilyTree.Data.Tests;

[TestClass]
public sealed class ExternalSourcesTests
{
    private static PersonSearchContext Context(
        string? bornPlace = null,
        string? born = "1880",
        string? died = null,
        PersonName? name = null,
        Sex sex = Sex.Male) =>
        new("p1", name ?? new PersonName("John", null, "Murphy", null), sex,
            PartialDate.Parse(born), bornPlace, PartialDate.Parse(died), null, [], []);

    [TestMethod]
    public void YearRange_widens_by_qualifier()
    {
        Assert.AreEqual(new YearRange(1880, 1880), YearRange.Around(PartialDate.Parse("1880")!));
        Assert.AreEqual(new YearRange(1878, 1882), YearRange.Around(PartialDate.Parse("abt 1880")!));
        Assert.AreEqual(new YearRange(1870, 1880), YearRange.Around(PartialDate.Parse("bef 1880")!));
        Assert.AreEqual(new YearRange(1880, 1890), YearRange.Around(PartialDate.Parse("aft 1880")!));
    }

    [TestMethod]
    public void Factory_maps_person_and_relatives()
    {
        var person = new Person { Id = "p1", Name = new PersonName("Mary", null, "Walsh", "Byrne"), BornPlace = "  " };
        var parent = new Person { Id = "p2", Name = new PersonName("Tom", null, "Byrne", null) };
        var resolved = new PersonWithRelationships
        {
            Person = person,
            Parents = [parent],
            Children = [],
            Siblings = [],
            Spouses = [],
        };

        var context = PersonSearchContextFactory.Create(resolved);

        Assert.AreEqual("p1", context.PersonId);
        Assert.IsNull(context.BornPlace);
        Assert.AreEqual("Byrne", context.Name.MaidenName);
        Assert.AreEqual("Tom Byrne", context.Parents[0].DisplayName);
    }

    [TestMethod]
    public void NaiCensus_is_available_for_irish_or_unknown_places_only()
    {
        var source = new NationalArchivesIrelandCensusSource();

        Assert.IsTrue(source.IsAvailableFor(Context(bornPlace: "Cork, Ireland")));
        Assert.IsTrue(source.IsAvailableFor(Context(bornPlace: null)));
        Assert.IsFalse(source.IsAvailableFor(Context(bornPlace: "Leeds, Yorkshire")));
    }

    [TestMethod]
    public void NaiCensus_is_unavailable_when_person_was_born_after_all_censuses()
    {
        Assert.IsFalse(new NationalArchivesIrelandCensusSource().IsAvailableFor(Context(born: "1940")));
    }

    [TestMethod]
    public async Task NaiCensus_suggests_county_age_and_years_alive()
    {
        var source = new NationalArchivesIrelandCensusSource();

        var response = await source.SearchAsync(Context(bornPlace: "Ballina, County Mayo", born: "1880", died: "1905"), CancellationToken.None);

        var result = response.Results.Single();
        Assert.AreEqual(ExternalResultKind.SearchLink, result.Kind);
        Assert.AreEqual("1901", result.Details!["Census year"]);
        Assert.AreEqual("21 in 1901", result.Details["Age"]);
        Assert.AreEqual("Mayo", result.Details["County"]);
        Assert.AreEqual("Murphy", result.Details["Surname"]);
    }

    [TestMethod]
    public async Task FreeCen_offers_maiden_name_and_excludes_irish_places()
    {
        var source = new FreeCenSource();
        var name = new PersonName("Mary", null, "Walsh", "Byrne");

        Assert.IsFalse(source.IsAvailableFor(Context(bornPlace: "Dublin")));
        Assert.IsTrue(source.IsAvailableFor(Context(bornPlace: "Leeds")));

        var response = await source.SearchAsync(Context(bornPlace: "Leeds", name: name, sex: Sex.Female), CancellationToken.None);
        var details = response.Results.Single().Details!;
        Assert.AreEqual("Walsh", details["Surname"]);
        Assert.AreEqual("Byrne", details["Maiden name (for records before marriage)"]);
        Assert.AreEqual("Leeds", details["Birth place"]);
    }

    [TestMethod]
    public void FreeBmd_is_unavailable_for_irish_places_and_deaths_before_registration()
    {
        var source = new FreeBmdSource();

        Assert.IsTrue(source.IsAvailableFor(Context(bornPlace: "Leeds", born: "1850")));
        Assert.IsFalse(source.IsAvailableFor(Context(bornPlace: "Cork, Ireland")));
        Assert.IsFalse(source.IsAvailableFor(Context(born: "1790", died: "1830")));
    }

    [TestMethod]
    public async Task FreeBmd_suggests_event_years_and_spouse_forenames()
    {
        var spouse = new PersonName("Ann", null, "Walsh", null);
        var context = Context(bornPlace: "Leeds", born: "abt 1850", died: "1920") with { Spouses = [spouse] };

        var response = await new FreeBmdSource().SearchAsync(context, CancellationToken.None);

        var details = response.Results.Single().Details!;
        Assert.AreEqual("1848–1852", details["Birth years"]);
        Assert.AreEqual("1920", details["Death years"]);
        Assert.AreEqual("Ann", details["Spouse forename (for marriages)"]);
        Assert.AreEqual("Murphy", details["Surname"]);
    }

    [TestMethod]
    public async Task FamilySearch_returns_records_and_tree_links_with_escaped_parameters()
    {
        var name = new PersonName("Mary", "Ann", "O'Neill", "Byrne");
        var context = Context(bornPlace: "Ballina, County Mayo", born: "1880", died: "1950", name: name, sex: Sex.Female) with { DiedPlace = "Leeds" };

        var response = await new FamilySearchSource().SearchAsync(context, CancellationToken.None);

        Assert.HasCount(2, response.Results);
        var records = response.Results[0];
        var tree = response.Results[1];
        Assert.AreEqual(ExternalResultKind.SearchLink, records.Kind);
        Assert.StartsWith("https://www.familysearch.org/search/record/results?", records.Url!.ToString());
        Assert.StartsWith("https://www.familysearch.org/search/tree/results?", tree.Url!.ToString());

        var query = records.Url.Query;
        StringAssert.Contains(query, "q.givenName=Mary%20Ann");
        StringAssert.Contains(query, "q.surname=O%27Neill");
        StringAssert.Contains(query, "q.birthLikeDate.from=1880");
        StringAssert.Contains(query, "q.birthLikeDate.to=1880");
        StringAssert.Contains(query, "q.birthLikePlace=Ballina%2C%20County%20Mayo");
        StringAssert.Contains(query, "q.deathLikeDate.from=1950");
        StringAssert.Contains(query, "q.deathLikePlace=Leeds");
        Assert.AreEqual(query, tree.Url.Query);
    }

    [TestMethod]
    public async Task FamilySearch_omits_unknown_values_and_widens_approximate_dates()
    {
        var response = await new FamilySearchSource().SearchAsync(Context(born: "abt 1880", died: null), CancellationToken.None);

        var query = response.Results[0].Url!.Query;
        StringAssert.Contains(query, "q.birthLikeDate.from=1878");
        StringAssert.Contains(query, "q.birthLikeDate.to=1882");
        Assert.DoesNotContain("birthLikePlace", query);
        Assert.DoesNotContain("deathLike", query);
        Assert.AreEqual("1878–1882", response.Results[0].Details!["Birth years"]);
    }

    [TestMethod]
    public async Task FamilySearch_lists_the_maiden_name_without_putting_it_in_the_url()
    {
        var name = new PersonName("Mary", null, "Walsh", "Byrne");

        var response = await new FamilySearchSource().SearchAsync(Context(name: name, sex: Sex.Female), CancellationToken.None);

        var result = response.Results[0];
        Assert.AreEqual("Byrne", result.Details!["Maiden name (for records before marriage)"]);
        Assert.DoesNotContain("Byrne", result.Url!.Query);
        Assert.IsTrue(new FamilySearchSource().IsAvailableFor(Context(bornPlace: "Anywhere")));
    }

    [TestMethod]
    public void Registry_returns_only_enabled_and_applicable_sources()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ExternalSourcesOptions
        {
            Sources = { ["freecen"] = new ExternalSourceOptions { Enabled = false } },
        });
        var registry = new ExternalDataSourceRegistry(
            [new NationalArchivesIrelandCensusSource(), new FreeCenSource(), new FreeBmdSource(), new FakeSource("fake", available: false)],
            options);

        var available = registry.GetAvailable(Context(bornPlace: null));

        CollectionAssert.AreEqual(new[] { "nai-census", "freebmd" }, available.Select(s => s.Id).ToArray());
        Assert.IsNull(registry.Find("freecen"));
        Assert.IsNotNull(registry.Find("NAI-CENSUS"));
    }

    [TestMethod]
    public void Registry_includes_additional_registered_sources()
    {
        var registry = new ExternalDataSourceRegistry([new FakeSource("fake", available: true)], Microsoft.Extensions.Options.Options.Create(new ExternalSourcesOptions()));

        Assert.AreEqual("fake", registry.GetAvailable(Context()).Single().Id);
    }

    private sealed class FakeSource(string id, bool available) : IExternalDataSource
    {
        public string Id => id;
        public string DisplayName => id;
        public string? Description => null;
        public bool IsAvailableFor(PersonSearchContext context) => available;

        public Task<ExternalSearchResponse> SearchAsync(PersonSearchContext context, CancellationToken ct) =>
            Task.FromResult(ExternalSearchResponse.Failed("fake"));
    }
}
