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
        private Dictionary<TypeDefinition, ITypeData> cache = new Dictionary<TypeDefinition, ITypeData>();
        public List<ITypeData> Types { get; } = new List<ITypeData>();
        private Dictionary<TypeRef, TypeName> _resolvedTypeNames { get; } = new Dictionary<TypeRef, TypeName>();
        private DllConfig _config;
        private string _dir;
        private ReaderParameters _readerParams;
        private IMetadataResolver _metadataResolver;

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
                    cache.Add(t, new DllTypeData(t, _config));
            }));
            Types = cache.Values.ToList();

            int total = DllTypeRef.hits + DllTypeRef.misses;
            Console.WriteLine($"{nameof(DllTypeRef)} cache hits: {DllTypeRef.hits} / {total} = {100.0f * DllTypeRef.hits / total}");
            // Ignore images for now.
        }

        public override string ToString()
        {
            return $"Types: {Types.Count}";
        }

        public ITypeData Resolve(TypeRef typeRef)
        {
            var dllRef = typeRef as DllTypeRef;
            // TODO: Resolve only among our types that we actually plan on serializing
            // Basically, check it against our whitelist/blacklist
            var reference = dllRef.This;
            var def = reference.Resolve();
            if (def is null)
            {
                Console.WriteLine($"Failed to resolve {reference.DeclaringType}'s {reference}");
                return null;
            }
            ITypeData te;
            if (cache.TryGetValue(def, out te))
                return te;
            te = new DllTypeData(def, _config);
            cache.Add(def, te);
            Types.Add(te);
            Console.WriteLine($"Late resolved {def}");
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