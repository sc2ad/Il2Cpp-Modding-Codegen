using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Data.DumpHandling;
using Il2Cpp_Modding_Codegen.Parsers;
using System.IO;

namespace Il2Cpp_Modding_Codegen
{
    public class DumpParser : IParser
    {
        private readonly DumpConfig _config;

        public DumpParser(DumpConfig config)
        {
            _config = config;
        }

        public IParsedData Parse(string fileName) => new DumpData(fileName, _config);

        public IParsedData Parse(Stream stream) => new DumpData(stream, _config);

        public bool ValidFile(string filename) => Path.GetFileName(filename) == "dump.cs";
    }
}
