namespace FamilyTree.Data.Models;

public sealed record SpouseRelationship(string SpouseId, PartialDate? MarriedOn, PartialDate? DivorcedOn, bool Current);
