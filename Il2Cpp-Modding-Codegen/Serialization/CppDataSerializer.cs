using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppDataSerializer : Serializer<IParsedData>
    {
        // This class is responsible for creating the contexts and passing them to each of the types
        // It then needs to create a header and a non-header for each class, with reasonable file structuring
        // Images, fortunately, don't have to be created at all
        private readonly ITypeCollection _collection;

        private readonly SerializationConfig _config;

        private CppContextSerializer _contextSerializer;
        private static readonly Dictionary<ITypeData, CppTypeContext> _map = new Dictionary<ITypeData, CppTypeContext>();
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
                // For each nested type, we create a context for it
                var nestedContexts = CreateContext(nt);
                // Order of these two functions matter, since after we set the declaring context, we can then call AddNestedContext and resolve InPlaces
                nestedContexts.SetDeclaringContext(typeContext);
                typeContext.AddNestedContext(nt, nestedContexts);
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
                ContextsComplete?.Invoke(this, pair.Key, pair.Value);

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
            int i = -1;
            int count = _map.Count;
            var mkSerializer = new AndroidMkSerializer(_config);
            mkSerializer.WriteHeader(Path.Combine(_config.OutputDirectory, "Android.mk"));
            var names = new List<string>();
            var libs = new List<AndroidMkSerializer.Library>();
            int currentPathLength = 0;
            foreach (var pair in _map)
            {
                i++;
                // We iterate over every type.
                // Then, we check InPlace for each context object, and if it is InPlace, we don't write a header for it
                // (we attempt to write a .cpp for it, if it is a template, this won't do anything)
                // Also, types that have no declaring context are always written (otherwise we would have 0 types!)

                if (_config.PrintSerializationProgress)
                    if (i % _config.PrintSerializationProgressFrequency == 0)
                        Console.WriteLine($"{i} / {count}");

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
                var t = pair.Value.LocalType;
                if (/*t.Type == TypeEnum.Interface || */t.This.IsGeneric || (t.Methods.Count == 0 && t.Fields.Where(f => f.Specifiers.IsStatic()).Count() == 0))
                    // Don't create C++ for types with no methods (including static fields), or if it is an interface, or if it is generic
                    continue;

                // We need to split up the files into multiple pieces, which all build to static libraries and then build to a single shared library
                if (_config.MultipleLibraries)
                {
                    var name = _config.OutputSourceDirectory + "/" + pair.Value.CppFileName;
                    if (currentPathLength + name.Length >= _config.SourceFileCharacterLimit)
                    {
                        // If we are about to go over, use the names list to create a library and add it to libs.
                        var newLib = new AndroidMkSerializer.Library { id = _config.Id + "_" + i, isSource = true, toBuild = names };
                        mkSerializer.WriteStaticLibrary(newLib);
                        libs.Add(newLib);
                        currentPathLength = 0;
                        names.Clear();
                    }
                    currentPathLength += name.Length;
                    names.Add(name);
                }
                new CppSourceCreator(_config, _contextSerializer).Serialize(pair.Value);
            }

            // After all static libraries are created, aggregate them all and collpase them into a single Android.mk file.
            // As a double check, doing a ctrl-f for any given id in the Android.mk should net two results: Where it is created and where it is aggregated.
            // Add one last lib for the final set of names to be built
            if (_config.MultipleLibraries)
            {
                if (names.Count > 0)
                {
                    var newLib = new AndroidMkSerializer.Library { id = _config.Id + "_" + i, isSource = true, toBuild = names };
                    libs.Add(newLib);
                    mkSerializer.WriteStaticLibrary(newLib);
                }
                Console.WriteLine("Beginning aggregation of libraries: " + libs.Count);
                mkSerializer.AggregateStaticLibraries(libs);
            }
            else
            {
                // Don't need to use modloader since this library is not a mod, it has no ModInfo that it uses!
                // TODO: Configurable bs-hook version
                mkSerializer.WritePrebuiltSharedLibrary("beatsaber-hook", "./extern/libbeatsaber-hook_0_2_1.so", "./extern/beatsaber-hook/shared/");
                mkSerializer.WriteSingleFile(new AndroidMkSerializer.Library { id = _config.Id, isSource = false, toBuild = new List<string> { "beatsaber-hook" } });
            }
            mkSerializer.Close();
            // Write the Application.mk after
            var appMkSerializer = new ApplicationMkSerializer();
            appMkSerializer.Write(Path.Combine(_config.OutputDirectory, "Application.mk"));
            appMkSerializer.Close();
        }
    }
}
