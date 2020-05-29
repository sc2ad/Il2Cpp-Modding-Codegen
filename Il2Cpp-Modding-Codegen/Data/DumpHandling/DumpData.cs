using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DumpHandling
{
    public class DumpData : IParsedData
    {
        public string Name => "Dump Data";
        public List<IImage> Images { get; } = new List<IImage>();
        public List<ITypeData> Types { get; } = new List<ITypeData>();
        private Dictionary<TypeRef, TypeRef> _resolvedTypeNames { get; } = new Dictionary<TypeRef, TypeRef>();
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

        public ITypeData Resolve(TypeRef TypeRef)
        {
            // TODO: Resolve only among our types that we actually plan on serializing
            // Basically, check it against our whitelist/blacklist
            var te = Types.FirstOrDefault(t => t.This.Equals(TypeRef) || t.This.Name == TypeRef.Name);
            return te;
        }

        // Resolves the TypeRef def in the current context and returns a TypeRef that is guaranteed unique
        public TypeRef ResolvedTypeRef(TypeRef def)
        {
            // If the type we are looking for exactly matches a type we have resolved
            if (_resolvedTypeNames.TryGetValue(def, out TypeRef v))
            {
                return v;
            }
            // Otherwise, check our set of created names (values) until we are unique

            var safeNamespace = def.SafeNamespace();
            var safeName = def.SafeName();
            int i = 1;

            TypeRef td = new TypeRef { Name = safeName, Namespace = safeNamespace };
            while (_resolvedTypeNames.ContainsValue(td))
            {
                // The type we are trying to add a reference to is already resolved, but is not referenced.
                // This means we have a duplicate typename. We will unique-ify this one by suffixing _{i} to the original typename
                // until the typename is unique.
                td.Name = safeName + $"_{i}";
                i++;
            }
            _resolvedTypeNames.Add(def, td);
            return td;
        }
    }
}