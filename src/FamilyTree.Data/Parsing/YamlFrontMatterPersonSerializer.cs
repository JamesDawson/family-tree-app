using FamilyTree.Data.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FamilyTree.Data.Parsing;

public sealed class YamlFrontMatterPersonSerializer : IPersonFileSerializer
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public Person Parse(string id, string fileContents)
    {
        var (frontMatterYaml, body) = FrontMatterSplitter.Split(fileContents);
        var dto = _deserializer.Deserialize<PersonFrontMatterDto>(frontMatterYaml) ?? new PersonFrontMatterDto();

        return new Person
        {
            Id = dto.Id is { Length: > 0 } dtoId ? dtoId : id,
            Name = new PersonName(dto.FirstName ?? "", dto.MiddleNames, dto.LastName ?? "", dto.MaidenName),
            Sex = ParseSex(dto.Sex),
            BornOn = PartialDate.Parse(dto.BornOn),
            BornPlace = dto.BornPlace,
            DiedOn = PartialDate.Parse(dto.DiedOn),
            DiedPlace = dto.DiedPlace,
            ParentIds = dto.Parents ?? [],
            Spouses = (dto.Spouses ?? []).Select(ToSpouseRelationship).ToList(),
            Notes = body.Trim('\n'),
        };
    }

    public string Serialize(Person person)
    {
        var dto = new PersonFrontMatterDto
        {
            Id = person.Id,
            FirstName = person.Name.First,
            MiddleNames = person.Name.Middle,
            LastName = person.Name.Last,
            MaidenName = person.Name.MaidenName,
            Sex = person.Sex.ToString().ToLowerInvariant(),
            BornOn = person.BornOn?.ToString(),
            BornPlace = person.BornPlace,
            DiedOn = person.DiedOn?.ToString(),
            DiedPlace = person.DiedPlace,
            Parents = [.. person.ParentIds],
            Spouses = person.Spouses.Select(ToSpouseDto).ToList(),
        };

        var yaml = _serializer.Serialize(dto);
        return FrontMatterSplitter.Combine(yaml, person.Notes);
    }

    private static SpouseRelationship ToSpouseRelationship(SpouseFrontMatterDto dto) => new(
        SpouseId: dto.Id ?? throw new FormatException("A spouse entry is missing its 'id'."),
        MarriedOn: PartialDate.Parse(dto.MarriedOn),
        DivorcedOn: PartialDate.Parse(dto.DivorcedOn),
        Current: dto.Current ?? true);

    private static SpouseFrontMatterDto ToSpouseDto(SpouseRelationship relationship) => new()
    {
        Id = relationship.SpouseId,
        MarriedOn = relationship.MarriedOn?.ToString(),
        DivorcedOn = relationship.DivorcedOn?.ToString(),
        Current = relationship.Current,
    };

    private static Sex ParseSex(string? value) => value?.ToLowerInvariant() switch
    {
        "female" => Sex.Female,
        "male" => Sex.Male,
        _ => Sex.Unknown,
    };
}
