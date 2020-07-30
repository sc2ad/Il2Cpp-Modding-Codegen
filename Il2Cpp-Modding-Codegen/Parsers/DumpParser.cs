using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Data.DumpHandling;
using Il2CppModdingCodegen.Parsers;
using System.IO;

namespace Il2CppModdingCodegen
{
    public class DumpParser : IParser
    {
        private readonly DumpConfig _config;

        public DumpParser(DumpConfig config) => _config = config;

        public IParsedData Parse(string fileName) => new DumpData(fileName, _config);

        public IParsedData Parse(Stream stream) => new DumpData(stream, _config);

        public bool ValidFile(string filename) => Path.GetFileName(filename) == "dump.cs";
    }
}
