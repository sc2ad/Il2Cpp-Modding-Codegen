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

        private HashSet<TypeDefinition> cache = new HashSet<TypeDefinition>();

        public DllData(string dirname, DllConfig config)
        {
            _config = config;
            var root = Path.GetDirectoryName(dirname);
            root = Path.Combine(root, "DummyDll");
            foreach (var file in Directory.GetFiles(root))
            {
                if (!file.EndsWith(".dll"))
                    continue;
                if (!_config.BlacklistDlls.Contains(file))
                {
                    var assemb = AssemblyDefinition.ReadAssembly(Path.Combine(root, file));
                    //assemb.Modules.ForEach(m => m.Types.ToList().ForEach(t =>
                    //{
                    //    if (_config.ParseTypes && !_config.BlacklistTypes.Contains(t.Name))
                    //        Types.Add(new DllTypeData(t, _config));
                    //}));
                    RegisterAssembly(assemb);
                    foreach (var module in assemb.Modules)
                    {
                        foreach (var t in module.Types)
                        {
                            if (_config.ParseTypes && !_config.BlacklistTypes.Contains(t.Name))
                            {
                                if (cache.Contains(t))
                                {
                                    Console.WriteLine($"Prevented repeat parsing of {t} from {module}");
                                    continue;
                                }
                                Types.Add(new DllTypeData(t, _config));
                                cache.Add(t);
                            }
                        }
                    }
                }
            }
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

            int i = 1;

            var tn = new TypeName(def);
            while (_resolvedTypeNames.ContainsValue(tn))
            {
                // The type we are trying to add a reference to is already resolved, but is not referenced.
                // This means we have a duplicate typename. We will unique-ify this one by suffixing _{i} to the original typename
                // until the typename is unique.
                tn = new TypeName(def, i);
                i++;
            }
            _resolvedTypeNames.Add(def, tn);
            return tn;
        }
    }
}