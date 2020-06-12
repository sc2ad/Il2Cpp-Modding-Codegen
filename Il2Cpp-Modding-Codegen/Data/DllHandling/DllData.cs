using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Parsers;
using Il2Cpp_Modding_Codegen.Serialization;
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
        public IEnumerable<ITypeData> Types { get { return _types.Values; } }
        private Dictionary<TypeRef, TypeName> _resolvedTypeNames { get; } = new Dictionary<TypeRef, TypeName>();
        private DllConfig _config;
        private string _dir;
        private ReaderParameters _readerParams;
        private IMetadataResolver _metadataResolver;

        private Dictionary<TypeRef, ITypeData> _types = new Dictionary<TypeRef, ITypeData>();

        public DllData(string dir, DllConfig config)
        {
            _config = config;
            _dir = dir;
            AddSearchDirectory(dir);
            _metadataResolver = new MetadataResolver(this);
            _readerParams = new ReaderParameters(ReadingMode.Immediate)
            {
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

            Queue<TypeDefinition> frontier = new Queue<TypeDefinition>();
            modules.ForEach(m => m.Types.ToList().ForEach(t =>
            {
                if (_config.ParseTypes && !_config.BlacklistTypes.Contains(t.Name))
                    frontier.Enqueue(t);
            }));

            while (frontier.Count > 0)
            {
                var t = frontier.Dequeue();
                if (t.Name.StartsWith("<") && t.Namespace.Length == 0 && t.DeclaringType is null)
                {
                    if (!t.Name.StartsWith("<Module>") && !t.Name.StartsWith("<PrivateImplementationDetails>"))
                        Console.Error.WriteLine($"Skipping TypeDefinition {t}");
                    continue;
                }

                var dllRef = DllTypeRef.From(t);
                if (!_types.ContainsKey(dllRef))
                {
                    var type = new DllTypeData(t, _config);
                    foreach (var nested in t.NestedTypes)
                        frontier.Enqueue(nested);
                    _types.Add(dllRef, type);
                }
                else
                    Console.Error.WriteLine($"{dllRef} already in _types! Matching item: {_types[dllRef].This}");
            }

            int total = DllTypeRef.hits + DllTypeRef.misses;
            Console.WriteLine($"{nameof(DllTypeRef)} cache hits: {DllTypeRef.hits} / {total} = {100.0f * DllTypeRef.hits / total}");
            // Ignore images for now.
        }

        public override string ToString()
        {
            return $"Types: {Types.Count()}";
        }

        public ITypeData Resolve(TypeRef typeRef)
        {
            // TODO: Resolve only among our types that we actually plan on serializing
            // Basically, check it against our whitelist/blacklist
            ITypeData ret;
            if (typeRef.IsGenericInstance)
            {
                // This is a generic instance. We want to convert this instance to a generic type that we have already created in _types
                var def = (typeRef as DllTypeRef).This.Resolve();
                var check = DllTypeRef.From(def);
                // Try to get our Generic Definition out of _types
                _types.TryGetValue(check, out ret);
                if (ret is null)
                    // This should never happen. All generic definitions should already be resolved.
                    throw new InvalidOperationException($"Generic instance: {typeRef} (definition: {check}) cannot map to any type in _types!");
                return ret;
            }

            if (!_types.TryGetValue(typeRef, out ret))
            {
                var def = (typeRef as DllTypeRef).This.Resolve();
                if (def != null)
                {
                    ret = new DllTypeData(def, _config);
                    if (!_types.ContainsKey(ret.This))
                        Console.Error.WriteLine($"Too late to add {def} to Types!");
                    else
                        Console.Error.WriteLine($"{def} already existed in _types! Matching item: {_types[ret.This]}");
                }
                // else likely a T, which can never "resolve"
            }
            return ret;
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
                foreach (var pair in _resolvedTypeNames.Where(x => x.Value.Equals(tn))) {
                    if (def.Equals(pair.Key))
                    {
                        Console.Error.WriteLine($"_resolvedTypeNames should have found {pair.Key}!");
                        return pair.Value;
                    }
                    else if (TypeRef.PrintEqual(pair.Key, def))
                        throw new Exception($"PrintEqual is wrong for {pair.Key}, {def}!");
                }
                // The type we are trying to add a reference to is already resolved, but is not referenced.
                // This means that tn is a duplicate typename. We will unique-ify this one by suffixing _{i} to the original typename
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