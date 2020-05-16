using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DumpHandling
{
    public class DumpData : IParsedData
    {
        public string Name => "Dump Data";
        public List<IImage> Images { get; } = new List<IImage>();
        public List<ITypeData> Types { get; } = new List<ITypeData>();
        private DumpConfig _config;

        private void ParseImages(PeekableStreamReader fs)
        {
            var line = fs.PeekLine();
            while (line.StartsWith("// Image"))
            {
                if (_config.ParseImages)
                    Images.Add(new DumpImage(fs));
                line = fs.PeekLine();
            }
        }

        private void ParseTypes(PeekableStreamReader fs)
        {
            var line = fs.PeekLine();
            while (line != null)
            {
                while (!line.StartsWith("// Namespace: "))
                {
                    // Read empty lines
                    fs.ReadLine();
                    line = fs.PeekLine();
                    if (line == null)
                    {
                        return;
                    }
                }
                if (_config.ParseTypes)
                    Types.Add(new DumpTypeData(fs, _config));
                line = fs.PeekLine();
            }
        }

        public void Parse(PeekableStreamReader fs)
        {
            ParseImages(fs);
            ParseTypes(fs);
        }

        public DumpData(string fileName, DumpConfig config)
        {
            _config = config;
            using (var fs = new PeekableStreamReader(fileName))
            {
                Parse(fs);
            }
        }

        public DumpData(Stream stream, DumpConfig config)
        {
            _config = config;
            using (var fs = new PeekableStreamReader(stream))
            {
                Parse(fs);
            }
        }

        public override string ToString()
        {
            var s = "";
            for (int i = 0; i < Images.Count; i++)
            {
                s += $"// Image {i}: {Images[i]}\n";
            }
            s += "\n";
            foreach (var t in Types)
            {
                s += $"{t}\n";
            }
            return s;
        }
    }
}