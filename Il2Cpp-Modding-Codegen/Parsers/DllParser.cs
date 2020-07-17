using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Data.DllHandling;
using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.IO;

namespace Il2Cpp_Modding_Codegen
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
