using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Data.DllHandling;
using Il2CppModdingCodegen.Parsers;
using System;
using System.IO;

namespace Il2CppModdingCodegen
{
    public class DllParser : IParser
    {
        private readonly DllConfig _config;

        public DllParser(DllConfig config)
        {
            _config = config;
        }

        public IParsedData Parse(string dirname) => new DllData(dirname, _config);

        public IParsedData Parse(Stream stream) => throw new InvalidOperationException("Cannot DllParse a stream! Must DllParse a directory!");

        public bool ValidFile(string filename) => Directory.Exists(filename);
    }
}
