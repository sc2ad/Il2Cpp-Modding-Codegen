using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppDataSerializer : ISerializer<IParsedData>
    {
        // This class is responsible for creating the contexts and passing them to each of the types
        // It then needs to create a header and a non-header for each class, with reasonable file structuring
        // Images, fortunately, don't have to be created at all
        private ITypeContext _context;

        private SerializationConfig _config;

        /// <summary>
        /// Creates a C++ Serializer with the given type context, which is a wrapper for a list of all types to serialize
        /// </summary>
        /// <param name="context"></param>
        public CppDataSerializer(SerializationConfig config, ITypeContext context)
        {
            _context = context;
            _config = config;
        }

        public void PreSerialize(ISerializerContext context, IParsedData data)
        {
            foreach (var t in data.Types)
            {
                // We need to create both a header and a non-header serializer (and a context for each)
                // Cache all of these
                // and ofc call PreSerialize on each of the types
                // This could be simplified to actually SHARE a context... Not really sure how, atm
                var header = new CppTypeDataSerializer("  ", true);
                var cpp = new CppTypeDataSerializer("", false);
                var headerContext = new CppSerializerContext(_context, t);
                var cppContext = new CppSerializerContext(_context, t);
                header.PreSerialize(headerContext, t);
                cpp.PreSerialize(cppContext, t);
                // Ensure that we are going to write everything in this context:
                // Global context should have everything now, all names are also resolved!
                // Now, we create the folders/files for the particular type we would like to create
                // Then, we write the includes
                // Then, we write the forward declares
                // Then, we write the actual file data (call header or cpp .Serialize on the stream)
                // That's it!
                // Now we serialize
                new CppHeaderCreator(_config, headerContext).Serialize(header, t);
                new CppSourceCreator(_config, cppContext).Serialize(cpp, t);
            }
        }

        public void Serialize(Stream stream, IParsedData data)
        {
            throw new InvalidOperationException($"Cannot serialize a {nameof(CppDataSerializer)}, since it serializes in {nameof(PreSerialize)}!");
        }
    }
}