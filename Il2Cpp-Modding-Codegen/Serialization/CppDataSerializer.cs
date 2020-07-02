using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppDataSerializer : Serializer<IParsedData>
    {
        // This class is responsible for creating the contexts and passing them to each of the types
        // It then needs to create a header and a non-header for each class, with reasonable file structuring
        // Images, fortunately, don't have to be created at all
        private ITypeCollection _collection;

        private SerializationConfig _config;

        private CppContextSerializer _contextSerializer;
        private static Dictionary<ITypeData, CppTypeContext> _map = new Dictionary<ITypeData, CppTypeContext>();
        public static IReadOnlyDictionary<ITypeData, CppTypeContext> TypeToContext { get => _map; }

        /// <summary>
        /// This event is called after all types are PreSerialized, but BEFORE any definitions or declarations are resolved to includes.
        /// This allows for any delegates of this type to modify each type's context's definitions or declarations before they are considered complete.
        /// This is called for each type that is registered in <see cref="_map"/>, which is each type that is not skipped due to config.
        /// </summary>
        public event Action<CppDataSerializer, ITypeData, CppTypeContext> ContextsComplete;

        /// <summary>
        /// Creates a C++ Serializer with the given type context, which is a wrapper for a list of all types to serialize
        /// </summary>
        /// <param name="types"></param>
        public CppDataSerializer(SerializationConfig config, ITypeCollection types)
        {
            _collection = types;
            _config = config;
        }

        private CppTypeContext CreateContext(ITypeData t)
        {
            var typeContext = new CppTypeContext(_collection, t);
            foreach (var nt in t.NestedTypes)
            {
                // For each nested type, we create a context for it, and we add it to our current context.
                var nestedContexts = CreateContext(nt);
                typeContext.AddNestedContext(nt, nestedContexts);
                // In addition, we set the nested context's declaring context to headerContext
                nestedContexts.SetDeclaringContext(typeContext);
            }
            // Each type is always added to _map (with a non-null header and cpp context)
            _map.Add(t, typeContext);
            return typeContext;
        }

        public override void PreSerialize(CppTypeContext _unused_, IParsedData data)
        {
            // We create a CppContextSerializer for both headers and .cpp files
            // We create a mapping (either here or in the serializer) of ITypeData --> CppSerializerContext
            // For each type, we create their contexts, preserialize them.
            // Then we iterate over all the types again, creating header and source creators for each, using our context serializers.
            // We then serialize the header and source creators, actually creating the data.

            _contextSerializer = new CppContextSerializer(_config, data);

            foreach (var t in data.Types)
            {
                // We need to create both a header and a non-header serializer (and a context for each)
                // Cache all of these
                // and ofc call PreSerialize on each of the types
                // This could be simplified to actually SHARE a context... Not really sure how, atm
                if (t.This.IsGeneric && _config.GenericHandling == GenericHandling.Skip)
                    // Skip the generic type, ensure it doesn't get serialized.
                    continue;

                // Alright, so. We create only top level types, all nested types are recursively created.
                if (t.This.DeclaringType is null)
                    CreateContext(t);

                // So, nested types are currently double counted.
                // That is, we hit them once (or more) in the declaring type when it preserializes the nested types
                // And we hit them again while we iterate over them.
                // Nested types can have their own .cpp and .hpp, but truly nested types should only have their own .cpp
                // If you need a definition of a truly nested type, you have to include the declaring type.
                // Therefore, when we resolve include locations, we must ensure we are not a nested type before returning our include path
                // (otherwise returning our declaring type's include path)
            }
            // Perform any post context creation for all pairs in _map
            foreach (var pair in _map)
            {
                ContextsComplete?.Invoke(this, pair.Key, pair.Value);
            }
            // Resolve all definitions and declarations for each context in _map
            foreach (var context in _map.Values)
            {
                // We need to ensure that all of our definitions and declarations are resolved for our given type in both contexts.
                // We do this by calling CppContextSerializer.Resolve(pair.Key, _map)
                _contextSerializer.Resolve(context, _map, true);
                _contextSerializer.Resolve(context, _map, false);
            }
        }

        public override void Serialize(CppStreamWriter writer, IParsedData data, bool _unused_)
        {
            int i = 0;
            int count = _map.Count;
            foreach (var pair in _map)
            {
                // We iterate over every type.
                // Then, we check InPlace for each context object, and if it is InPlace, we don't write a header for it
                // (we attempt to write a .cpp for it, if it is a template, this won't do anything)
                // Also, types that have no declaring context are always written (otherwise we would have 0 types!)

                if (_config.PrintSerializationProgress)
                    if (i % _config.PrintSerializationProgressFrequency == 0)
                    {
                        Console.WriteLine($"{i} / {count}");
                    }
                // Ensure that we are going to write everything in this context:
                // Global context should have everything now, all names are also resolved!
                // Now, we create the folders/files for the particular type we would like to create
                // Then, we write the includes
                // Then, we write the forward declares
                // Then, we write the actual file data (call header or cpp .Serialize on the stream)
                // That's it!
                // Now we serialize

                if (!pair.Value.InPlace || pair.Value.DeclaringContext == null)
                    new CppHeaderCreator(_config, _contextSerializer).Serialize(pair.Value);
                new CppSourceCreator(_config, _contextSerializer).Serialize(pair.Value);
                i++;
            }
        }
    }
}