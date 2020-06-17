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
        private ITypeCollection _context;

        private SerializationConfig _config;

        private Dictionary<ITypeData, (CppTypeDataSerializer, CppTypeDataSerializer)> _map = new Dictionary<ITypeData, (CppTypeDataSerializer, CppTypeDataSerializer)>();

        /// <summary>
        /// Creates a C++ Serializer with the given type context, which is a wrapper for a list of all types to serialize
        /// </summary>
        /// <param name="context"></param>
        public CppDataSerializer(SerializationConfig config, ITypeCollection context)
        {
            _context = context;
            _config = config;
        }

        public override void PreSerialize(CppSerializerContext _unused_, IParsedData data)
        {
            // We create a CppContextSerializer for both headers and .cpp files
            // We create a mapping (either here or in the serializer) of ITypeData --> CppSerializerContext
            // For each type, we create their contexts, preserialize them.
            // Then we iterate over all the types again, creating header and source creators for each, using our context serializers.
            // We then serialize the header and source creators, actually creating the data.

            var headerContextSerializer = new CppContextSerializer(true);
            var cppContextSerializer = new CppContextSerializer(false);

            foreach (var t in data.Types)
            {
                // We need to create both a header and a non-header serializer (and a context for each)
                // Cache all of these
                // and ofc call PreSerialize on each of the types
                // This could be simplified to actually SHARE a context... Not really sure how, atm
                if (t.This.IsGeneric && _config.GenericHandling == GenericHandling.Skip)
                    // Skip the generic type, ensure it doesn't get serialized.
                    continue;
                // TODO: give nested types their own cpp files?
                var header = new CppTypeDataSerializer(_config, headerContextSerializer, true);
                var cpp = new CppTypeDataSerializer(_config, cppContextSerializer, false);
                var headerContext = new CppSerializerContext(_context, t);
                var cppContext = new CppSerializerContext(_context, t, true);
                header.PreSerialize(headerContext, t);
                cpp.PreSerialize(cppContext, t);
                _map.Add(t, (header, cpp));
            }
        }

        public override void Serialize(CppStreamWriter writer, IParsedData data)
        {
            foreach (var pair in _map)
            {
                // Ensure that we are going to write everything in this context:
                // Global context should have everything now, all names are also resolved!
                // Now, we create the folders/files for the particular type we would like to create
                // Then, we write the includes
                // Then, we write the forward declares
                // Then, we write the actual file data (call header or cpp .Serialize on the stream)
                // That's it!
                // Now we serialize
                new CppHeaderCreator(_config, pair.Value.Item1.serializer).Serialize(pair.Value.Item1, pair.Key);
                new CppSourceCreator(_config, pair.Value.Item2.serializer).Serialize(pair.Value.Item2, pair.Key);
            }
        }
    }
}