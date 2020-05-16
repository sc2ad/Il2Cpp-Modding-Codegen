using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Data.DumpHandling;
using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen
{
    public class DumpParser : IParser
    {
        private DumpConfig _config;

        public DumpParser(DumpConfig config)
        {
            _config = config;
        }

        public IParsedData Parse(string fileName)
        {
            return new DumpData(fileName, _config);
        }

        public IParsedData Parse(Stream stream)
        {
            return new DumpData(stream, _config);
        }

        public bool ValidFile(string filename)
        {
            return Path.GetFileName(filename) == "dump.cs";
        }
    }
}