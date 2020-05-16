using Il2Cpp_Modding_Codegen.Data;
using System.IO;

namespace Il2Cpp_Modding_Codegen.Parsers
{
    internal interface IParser
    {
        bool ValidFile(string fileName);

        IParsedData Parse(string fileName);

        IParsedData Parse(Stream stream);
    }
}