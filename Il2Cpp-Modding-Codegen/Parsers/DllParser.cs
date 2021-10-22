using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Data.DllHandling;
using System;
using System.IO;

namespace Il2CppModdingCodegen
{
    public class DllParser
    {
        private readonly DllConfig _config;

        public DllParser(DllConfig config) => _config = config;

        public DllData Parse(string dirname) => new(dirname, _config);
    }
}