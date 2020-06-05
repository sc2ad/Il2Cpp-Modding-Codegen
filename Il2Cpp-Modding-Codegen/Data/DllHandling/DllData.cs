using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Parsers;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    public class DllData : DefaultAssemblyResolver, IParsedData
    {
        public string Name => "Dll Data";
        public List<IImage> Images { get; } = new List<IImage>();
        public List<ITypeData> Types { get; } = new List<ITypeData>();
        private Dictionary<TypeRef, TypeName> _resolvedTypeNames { get; } = new Dictionary<TypeRef, TypeName>();
        private DllConfig _config;
        private string _dir;
        private ReaderParameters _readerParams;
        private IMetadataResolver _metadataResolver;

        Queue<TypeDefinition> frontier = new Queue<TypeDefinition>();

        public DllData(string dir, DllConfig config)
        {
            _config = config;
            _dir = dir;
            AddSearchDirectory(dir);
            _metadataResolver = new MetadataResolver(this);
            _readerParams = new ReaderParameters(ReadingMode.Immediate) {
                AssemblyResolver = this,
                MetadataResolver = _metadataResolver
            };

            var modules = new List<ModuleDefinition>();
            foreach (var file in Directory.GetFiles(dir))
            {
                if (!file.EndsWith(".dll"))
                    continue;
                if (!_config.BlacklistDlls.Contains(file))
                {
                    var assemb = AssemblyDefinition.ReadAssembly(file, _readerParams);
                    foreach (var module in assemb.Modules)
                    {
                        modules.Add(module);
                    }
                }
            }
            modules.ForEach(m => m.Types.ToList().ForEach(t =>
            {
                if (_config.ParseTypes && !_config.BlacklistTypes.Contains(t.Name))
                    frontier.Enqueue(t);
            }));
            while (frontier.Count > 0)
            {
                var t = frontier.Dequeue();
                if (t.Name.StartsWith("<") && t.Namespace.Length == 0 && t.DeclaringType is null) {
                    if (!t.Name.StartsWith("<Module>") && !t.Name.StartsWith("<PrivateImplementationDetails>"))
                        Console.Error.WriteLine($"Skipping TypeDefinition {t}");
                    continue;
                }
                Types.Add(new DllTypeData(t, _config));
                foreach (var nt in t.NestedTypes) frontier.Enqueue(nt);
            }

            int total = DllTypeRef.hits + DllTypeRef.misses;
            Console.WriteLine($"{nameof(DllTypeRef)} cache hits: {DllTypeRef.hits} / {total} = {100.0f * DllTypeRef.hits / total}");
            // Ignore images for now.
        }

        public override string ToString()
        {
            return $"Types: {Types.Count}";
        }

        public ITypeData Resolve(TypeRef TypeRef)
        {
            // TODO: Resolve only among our types that we actually plan on serializing
            // Basically, check it against our whitelist/blacklist
            var te = Types.FirstOrDefault(t => t.This.Equals(TypeRef) || t.This.Name == TypeRef.Name);
            return te;
        }

        // Resolves the TypeRef def in the current context and returns a TypeRef that is guaranteed unique
        public TypeName ResolvedTypeRef(TypeRef def)
        {
            // If the type we are looking for exactly matches a type we have resolved
            if (_resolvedTypeNames.TryGetValue(def, out TypeName v))
            {
                return v;
            }
            // Otherwise, check our set of created names (values) until we are unique
            var tn = new TypeName(def);

            int i = 0;
            while (_resolvedTypeNames.ContainsValue(tn))
            {
                // The type we are trying to add a reference to is already resolved, but is not referenced.
                // This means we have a duplicate typename. We will unique-ify this one by suffixing _{i} to the original typename
                // until the typename is unique.
                i++;
                tn = new TypeName(def, i);
            }
            if (i > 0) Console.WriteLine($"Unique-ified to {tn}");
            _resolvedTypeNames.Add(def, tn);
            return tn;
        }
    }
}