using Il2CppModdingCodegen.Data;
using System.IO;

namespace Il2CppModdingCodegen.Parsers
{
    public interface IParser
    {
        bool ValidFile(string fileName);

        IParsedData Parse(string fileName);

        IParsedData Parse(Stream stream);
    }
}
