using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppModdingCodegen.Data.DumpHandling
{
    public class DumpData : IParsedData
    {
        public string Name => "Dump Data";
        public List<IImage> Images { get; } = new List<IImage>();
        public IEnumerable<ITypeData> Types { get => _types; }
        private readonly DumpConfig _config;
        private readonly List<ITypeData> _types = new List<ITypeData>();

        private void ParseImages(PeekableStreamReader fs)
        {
            var line = fs.PeekLine();
            while (line != null && line.StartsWith("// Image"))
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
                    if (line is null) return;
                }
                if (_config.ParseTypes)
                {
                    var typeData = new DumpTypeData(fs, _config);
                    if (typeData.This.DeclaringType != null)
                    {
                        var declaringTypeData = Resolve(typeData.This.DeclaringType);
                        if (declaringTypeData is null)
                            throw new Exception("Failed to get declaring type ITypeData for newly parsed nested type!");
                        declaringTypeData.NestedTypes.AddOrThrow(typeData);
                    }
                    _types.Add(typeData);
                }
                line = fs.PeekLine();
            }
        }

        internal void Parse(PeekableStreamReader fs)
        {
            ParseImages(fs);
            ParseTypes(fs);
        }

        internal DumpData(string fileName, DumpConfig config)
        {
            _config = config;
            using var fs = new PeekableStreamReader(fileName);
            Parse(fs);
        }

        internal DumpData(Stream stream, DumpConfig config)
        {
            _config = config;
            using var fs = new PeekableStreamReader(stream);
            Parse(fs);
        }

        public override string ToString()
        {
            var s = "";
            for (int i = 0; i < Images.Count; i++)
                s += $"// Image {i}: {Images[i]}\n";
            s += "\n";
            foreach (var t in Types)
                s += $"{t}\n";
            return s;
        }

        public ITypeData? Resolve(TypeRef? typeRef)
        {
            if (typeRef is null) throw new ArgumentNullException(nameof(typeRef));
            // TODO: Resolve only among our types that we actually plan on serializing
            // Basically, check it against our whitelist/blacklist
            var te = Types.LastOrDefault(t => t.This.Equals(typeRef) || t.This.Name == typeRef.Name);
            return te;
        }
    }
}
