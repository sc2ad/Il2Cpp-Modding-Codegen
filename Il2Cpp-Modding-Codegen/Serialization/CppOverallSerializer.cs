using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data.DllHandling;
using Mono.Cecil;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppOverallSerializer
    {
        private readonly SerializationConfig config;
        private readonly string includeDir;
        private readonly string srcDir;

        public CppOverallSerializer(SerializationConfig config)
        {
            this.config = config;
            includeDir = Path.Combine(config.OutputDirectory, config.OutputHeaderDirectory);
            srcDir = Path.Combine(config.OutputDirectory, config.OutputSourceDirectory);
        }

        private readonly ConcurrentDictionary<TypeDefinition, CppTypeHeaderContext> headerContexts = new ConcurrentDictionary<TypeDefinition, CppTypeHeaderContext>();
        private readonly ConcurrentDictionary<TypeDefinition, CppTypeSourceContext> srcContexts = new ConcurrentDictionary<TypeDefinition, CppTypeSourceContext>();

        public void Begin(DllData data)
        {
            if (config.OneSourceFile)
            {
                srcCtx = new CppTypeSourceContext();
            }
            data.Types.AsParallel().ForAll(t =>
            {
                if (t.HasGenericParameters && config.GenericHandling == GenericHandling.Skip)
                {
                    // Skip the generic type, ensure it doesn't get serialized.
                    return;
                }
                if (t.DeclaringType is null)
                {
                    // Create a Cpp context around this type
                    headerContexts.TryAdd(t, new CppTypeHeaderContext(t));
                    // Create a writer for the header
                    // Conditionally add the context to the particular src, which might be ONE src, or MANY srcs.
                    // If we are in ONE src, have ONE writer for it, with ONE parent src context that serves as a a holder for all includes
                    // Deduction of includes is still most likely a two step process (?) but overall we can write out just fine.
                    if (config.OneSourceFile)
                    {
                        srcCtx.Add(new CppTypeSourceContext(t));
                    }
                    else
                    {
                        srcContexts.TryAdd(t, new CppTypeSourceContext(t));
                    }
                }
            });
        }

        public void Write(DllData data)
        {
            data.Types.AsParallel().ForAll(t =>
            {
            });
        }
    }
}