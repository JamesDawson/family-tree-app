using FamilyTree.Data.Models;

namespace FamilyTree.Data.Parsing;

public interface IPersonFileSerializer
{
    Person Parse(string id, string fileContents);
    string Serialize(Person person);
}
