using Il2CppModdingCodegen.Config;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    public class DllData : DefaultAssemblyResolver
    {
        public HashSet<TypeDefinition> Types { get; } = new HashSet<TypeDefinition>(new TypeDefinitionComparer());
        public HashSet<MethodDefinition> Methods { get; } = new HashSet<MethodDefinition>();
        private readonly DllConfig _config;

        internal DllData(string dir, DllConfig config)
        {
            _config = config;
            AddSearchDirectory(dir);
            var _metadataResolver = new MetadataResolver(this);
            var _readerParams = new ReaderParameters(ReadingMode.Immediate)
            {
                AssemblyResolver = this,
                MetadataResolver = _metadataResolver
            };
            Queue<TypeDefinition> frontier = new Queue<TypeDefinition>();

            foreach (var file in Directory.GetFiles(dir))
            {
                if (file.EndsWith(".dll") && !_config.BlacklistDlls.Contains(file))
                {
                    var assemb = AssemblyDefinition.ReadAssembly(file, _readerParams);
                    foreach (var module in assemb.Modules)
                    {
                        foreach (var t in module.Types)
                        {
                            if (_config.ParseTypes && !_config.BlacklistTypes.Contains(t.Name))
                            {
                                frontier.Enqueue(t);
                            }
                        }
                    }
                }
            }

            while (frontier.Count > 0)
            {
                var t = frontier.Dequeue();
                if (t.Name.StartsWith("<") && t.Namespace.Length == 0 && t.DeclaringType is null)
                {
                    if (!t.Name.StartsWith("<Module>") && !t.Name.StartsWith("<PrivateImplementationDetails>"))
                        Console.Error.WriteLine($"Skipping TypeDefinition {t}");
                    continue;
                }
                foreach (var nested in t.NestedTypes)
                    frontier.Enqueue(nested);
                if (!Types.Add(t))
                {
                    Console.Error.WriteLine($"{t} already in {nameof(Types)}!");
                }
                foreach (var m in t.Methods)
                {
                    if (!Methods.Add(m))
                        Console.Error.WriteLine($"{m} already in {nameof(Methods)}!");
                }
            }
            // Ignore images for now.
        }

        public override string ToString() => $"Types: {Types.Count}";
    }
}