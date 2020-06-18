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
        private Dictionary<ITypeData, (CppTypeDataSerializer, CppTypeDataSerializer)> _map = new Dictionary<ITypeData, (CppTypeDataSerializer, CppTypeDataSerializer)>();
        private Dictionary<ITypeData, CppSerializerContext> _headerOnlyMap = new Dictionary<ITypeData, CppSerializerContext>();

        /// <summary>
        /// Creates a C++ Serializer with the given type context, which is a wrapper for a list of all types to serialize
        /// </summary>
        /// <param name="context"></param>
        public CppDataSerializer(SerializationConfig config, ITypeCollection context)
        {
            _collection = context;
            _config = config;
        }

        public override void PreSerialize(CppSerializerContext _unused_, IParsedData data)
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
                // So, nested types are currently double counted.
                // That is, we hit them once (or more) in the declaring type when it preserializes the nested types
                // And we hit them again while we iterate over them.
                // Nested types can have their own .cpp and .hpp, but truly nested types should only have their own .cpp
                // If you need a definition of a truly nested type, you have to include the declaring type.
                // Therefore, when we resolve include locations, we must ensure we are not a nested type before returning our include path
                // (otherwise returning our declaring type's include path)
                var header = new CppTypeDataSerializer(_config, _contextSerializer, true);
                var cpp = new CppTypeDataSerializer(_config, _contextSerializer, false);
                var headerContext = new CppSerializerContext(_collection, t, true);
                var cppContext = new CppSerializerContext(_collection, t, false);
                header.PreSerialize(headerContext, t);
                cpp.PreSerialize(cppContext, t);
                _map.Add(t, (header, cpp));
                _headerOnlyMap.Add(t, headerContext);
            }
        }

        public override void Serialize(CppStreamWriter writer, IParsedData data)
        {
            int i = 0;
            int count = _map.Count;
            foreach (var pair in _map)
            {
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
                // We need to ensure that all of our definitions and declarations are resolved for our given type in both contexts.
                // We do this by calling CppContextSerializer.Resolve(pair.Key, _map)
                _contextSerializer.Resolve(pair.Value.Item1.Context, _headerOnlyMap);
                _contextSerializer.Resolve(pair.Value.Item2.Context, _headerOnlyMap);

                // If we have a type that is nested in place, we only create the .cpp for it
                if (!pair.Key.IsNestedInPlace)
                    new CppHeaderCreator(_config, _contextSerializer, pair.Value.Item1).Serialize(pair.Value.Item1.Context);
                new CppSourceCreator(_config, _contextSerializer, pair.Value.Item2).Serialize(pair.Value.Item2.Context);
                i++;
            }
        }
    }
}