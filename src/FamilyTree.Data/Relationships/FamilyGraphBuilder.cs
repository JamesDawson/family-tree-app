using FamilyTree.Data.Models;

namespace FamilyTree.Data.Relationships;

public sealed class FamilyGraphBuilder : IFamilyGraphBuilder
{
    // The index is O(people) to build but immutable, and the repository hands out the same list instance
    // until the data changes, so keying on list identity means it's rebuilt only when the data is.
    private volatile CachedIndex? _cached;

    private sealed record CachedIndex(IReadOnlyList<Person> Source, PersonIndex Index);

    public FamilyGraph? Build(string focusId, int ancestorGenerations, int descendantGenerations, IReadOnlyList<Person> allPeople)
    {
        var index = GetIndex(allPeople);
        if (!index.ById.ContainsKey(focusId))
        {
            return null;
        }

        var included = new HashSet<string> { focusId };

        Walk(focusId, ancestorGenerations, id => index.ById[id].ParentIds, index.ById, included);
        Walk(focusId, descendantGenerations, id => index.ChildrenOf.GetValueOrDefault(id) ?? [], index.ById, included);

        // Spouses are one hop only: a spouse's own ancestors/descendants are not pulled in.
        foreach (var id in included.ToList())
        {
            foreach (var spouseId in index.SpousesOf.GetValueOrDefault(id) ?? [])
            {
                included.Add(spouseId);
            }
        }

        var nodes = included
            .Select(id => ToNode(index.ById[id], index, included))
            .OrderBy(n => n.Person.Id, StringComparer.Ordinal)
            .ToList();

        return new FamilyGraph(focusId, nodes);
    }

    private static void Walk(
        string start,
        int generations,
        Func<string, IEnumerable<string>> next,
        IReadOnlyDictionary<string, Person> byId,
        HashSet<string> included)
    {
        // Breadth-first; `included` doubles as the visited set, which is what terminates a parent cycle.
        var frontier = new List<string> { start };
        for (var depth = 0; depth < generations && frontier.Count > 0; depth++)
        {
            var following = new List<string>();
            foreach (var id in frontier)
            {
                foreach (var nextId in next(id))
                {
                    if (byId.ContainsKey(nextId) && included.Add(nextId))
                    {
                        following.Add(nextId);
                    }
                }
            }

            frontier = following;
        }
    }

    private static FamilyGraph.Node ToNode(Person person, PersonIndex index, HashSet<string> included)
    {
        var parents = person.ParentIds.Where(id => index.ById.ContainsKey(id)).Distinct().ToList();
        var children = index.ChildrenOf.GetValueOrDefault(person.Id) ?? [];
        var spouses = index.SpousesOf.GetValueOrDefault(person.Id) ?? [];

        return new FamilyGraph.Node(
            person,
            [.. parents.Where(included.Contains)],
            [.. spouses.Where(included.Contains)],
            [.. children.Where(included.Contains)],
            HiddenParentCount: parents.Count(id => !included.Contains(id)),
            HiddenChildCount: children.Count(id => !included.Contains(id)));
    }

    private PersonIndex GetIndex(IReadOnlyList<Person> allPeople)
    {
        var cached = _cached;
        if (cached is not null && ReferenceEquals(cached.Source, allPeople))
        {
            return cached.Index;
        }

        var index = PersonIndex.Create(allPeople);
        _cached = new CachedIndex(allPeople, index);
        return index;
    }

    private sealed class PersonIndex
    {
        public required Dictionary<string, Person> ById { get; init; }
        public required Dictionary<string, List<string>> ChildrenOf { get; init; }

        /// <summary>Symmetric: a link recorded on only one side (hand-edited data) still counts for both people.</summary>
        public required Dictionary<string, List<string>> SpousesOf { get; init; }

        public static PersonIndex Create(IReadOnlyList<Person> people)
        {
            var byId = new Dictionary<string, Person>(people.Count);
            foreach (var person in people)
            {
                byId[person.Id] = person;
            }

            var childrenOf = new Dictionary<string, List<string>>();
            var spousesOf = new Dictionary<string, List<string>>();

            foreach (var person in byId.Values)
            {
                foreach (var parentId in person.ParentIds.Distinct())
                {
                    Add(childrenOf, parentId, person.Id);
                }

                foreach (var spouse in person.Spouses)
                {
                    if (spouse.SpouseId != person.Id && byId.ContainsKey(spouse.SpouseId))
                    {
                        Add(spousesOf, person.Id, spouse.SpouseId);
                        Add(spousesOf, spouse.SpouseId, person.Id);
                    }
                }
            }

            return new PersonIndex { ById = byId, ChildrenOf = childrenOf, SpousesOf = spousesOf };
        }

        private static void Add(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var list))
            {
                map[key] = list = [];
            }

            if (!list.Contains(value))
            {
                list.Add(value);
            }
        }
    }
}
