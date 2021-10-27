using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.CppSerialization;
using Il2CppModdingCodegen.Data.DllHandling;
using Il2CppModdingCodegen.Serialization.Interfaces;
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
        private readonly IEnumerable<ISerializer<TypeDefinition, CppStreamWriter>> serializers;

        public CppOverallSerializer(SerializationConfig config, IEnumerable<ISerializer<TypeDefinition, CppStreamWriter>> ser)
        {
            if (config is null)
                throw new ArgumentNullException(nameof(config));
            this.config = config;
            includeDir = Path.Combine(config.OutputDirectory, config.OutputHeaderDirectory);
            srcDir = Path.Combine(config.OutputDirectory, config.OutputSourceDirectory);
            CppTypeHeaderContext.CyclicDefinition += CppTypeHeaderContext_CyclicDefinition;
            CppTypeHeaderContext.NestedDefinitionTwice += CppTypeHeaderContext_NestedDefinitionTwice;
            serializers = ser;
        }

        private readonly ConcurrentDictionary<TypeDefinition, CppTypeHeaderContext> headerContexts = new ConcurrentDictionary<TypeDefinition, CppTypeHeaderContext>();
        private readonly ConcurrentDictionary<TypeDefinition, CppTypeSourceContext> srcContexts = new ConcurrentDictionary<TypeDefinition, CppTypeSourceContext>();

        public void Begin(DllData data)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (config.OneSourceFile)
            {
                //srcCtx = new CppTypeSourceContext();
            }
            var sz = new SizeTracker(config.PointerSize);
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
                    var header = new CppTypeHeaderContext(t, sz, serializers, null);
                    headerContexts.TryAdd(t, header);
                    // Create a writer for the header
                    // Conditionally add the context to the particular src, which might be ONE src, or MANY srcs.
                    // If we are in ONE src, have ONE writer for it, with ONE parent src context that serves as a a holder for all includes
                    // Deduction of includes is still most likely a two step process (?) but overall we can write out just fine.

                    //var cppCtx = new CppTypeSourceContext(t, header, cppSer);
                    //if (config.OneSourceFile)
                    //{
                    //    srcCtx.Add(cppCtx);
                    //}
                    //else
                    //{
                    //    srcContexts.TryAdd(t, cppCtx);
                    //}
                }
            });
            // Resolve all types after top-level pass
            data.Types.AsParallel().ForAll(t =>
            {
                if (headerContexts.TryGetValue(t, out var h))
                {
                    h.Resolve();
                }
                if (srcContexts.TryGetValue(t, out var c))
                {
                    c.Resolve();
                }
            });
        }

        private void CppTypeHeaderContext_NestedDefinitionTwice(CppTypeHeaderContext ctx, CppTypeHeaderContext offending, TypeDefinition offendingType)
        {
            Console.Error.WriteLine($"Nested definition is attempted to be included even though it's already defined? Initial context: {ctx.Type} to: {offending.Type} with type: {offendingType}");
        }

        private static void CppTypeHeaderContext_CyclicDefinition(CppTypeHeaderContext ctx, CppTypeHeaderContext offending, TypeDefinition offendingType)
        {
            Console.Error.WriteLine($"Cyclic definition from initial context: {ctx.Type} to: {offending.Type} which attempts to include type: {offending}");
        }

        public void Write(DllData data)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            data.Types.AsParallel().ForAll(t =>
            {
                if (headerContexts.TryGetValue(t, out var h))
                {
                    h.Write(includeDir);
                }
                //if (srcContexts.TryGetValue(t, out var c))
                //{
                //    c.Write();
                //}
            });
        }
    }
}