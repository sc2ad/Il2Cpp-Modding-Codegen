using Il2CppModdingCodegen.Config;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    public class DllData : DefaultAssemblyResolver, IParsedData
    {
        public string Name => "Dll Data";
        public List<IImage> Images { get; } = new List<IImage>();
        public IEnumerable<ITypeData> Types => _types.Values;
        private readonly DllConfig _config;
        private readonly string _dir;
        private readonly ReaderParameters _readerParams;
        private readonly IMetadataResolver _metadataResolver;

        private readonly Dictionary<TypeRef, ITypeData> _types = new Dictionary<TypeRef, ITypeData>();

        internal DllData(string dir, DllConfig config)
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
                if (file.EndsWith(".dll") && !_config.BlacklistDlls.Contains(file))
                {
                    var assemb = AssemblyDefinition.ReadAssembly(file, _readerParams);
                    foreach (var module in assemb.Modules)
                        modules.Add(module);
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
                    if (dllRef.DeclaringType != null)
                        _types[dllRef.DeclaringType].NestedTypes.AddOrThrow(type);
                    foreach (var nested in t.NestedTypes)
                        frontier.Enqueue(nested);
                    _types.Add(dllRef, type);
                }
                else
                    Console.Error.WriteLine($"{dllRef} already in _types! Matching item: {_types[dllRef].This}");
            }

            int total = DllTypeRef.Hits + DllTypeRef.Misses;
            Console.WriteLine($"{nameof(DllTypeRef)} cache hits: {DllTypeRef.Hits} / {total} = {100.0f * DllTypeRef.Hits / total}");
            // Ignore images for now.
        }

        public override string ToString() => $"Types: {Types.Count()}";

        public ITypeData? Resolve(TypeRef? typeRef)
        {
            if (typeRef is null) throw new ArgumentNullException(nameof(typeRef));
            return Resolve(typeRef.AsDllTypeRef);
        }

        private ITypeData? Resolve(DllTypeRef typeRef)
        {
            // Generic parameters can never "Resolve"
            if (typeRef.IsGenericParameter) return null;
            // TODO: Resolve only among our types that we actually plan on serializing
            // Basically, check it against our whitelist/blacklist
            ITypeData ret;
            if (typeRef.IsGenericInstance)
            {
                // This is a generic instance. We want to convert this instance to a generic type that we have already created in _types
                var def = typeRef.This.Resolve();
                var check = DllTypeRef.From(def);
                // Try to get our Generic Definition out of _types
                if (!_types.TryGetValue(check, out ret))
                    // This should never happen. All generic definitions should already be resolved.
                    throw new InvalidOperationException($"Generic instance: {typeRef} (definition: {check}) cannot map to any type in _types!");
                return ret;
            }

            if (!_types.TryGetValue(typeRef, out ret))
            {
                var def = typeRef.This.Resolve();
                if (def != null)
                {
                    ret = new DllTypeData(def, _config);
                    if (!_types.ContainsKey(ret.This))
                        Console.Error.WriteLine($"Too late to add {def} to Types!");
                    else
                    {
                        Console.Error.WriteLine($"{typeRef} already existed in _types?! Matching item: {_types[ret.This].This}");
                        ret = _types[ret.This];
                    }
                }
                else
                    throw new InvalidOperationException($"Non-generic-parameter {typeRef} cannot be resolved!");
            }
            return ret;
        }
    }
}