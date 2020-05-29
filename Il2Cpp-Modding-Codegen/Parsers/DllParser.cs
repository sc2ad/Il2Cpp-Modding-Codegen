using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Data.DllHandling;
using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen
{
    public class DllParser : IParser
    {
        private DllConfig _config;

        public DllParser(DllConfig config)
        {
            _config = config;
        }

        public IParsedData Parse(string fileName)
        {
            return new DllData(fileName, _config);
        }

        public IParsedData Parse(Stream stream)
        {
            return new DllData(stream, _config);
        }

        public bool ValidFile(string filename)
        {
            return Path.GetFileName(filename) == "dump.cs";
        }
    }
}