using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppDataSerializer : Serializer<IParsedData>
    {
        // This class is responsible for creating the contexts and passing them to each of the types
        // It then needs to create a header and a non-header for each class, with reasonable file structuring
        // Images, fortunately, don't have to be created at all
        private readonly ITypeCollection _collection;

        private readonly SerializationConfig _config;

        private CppContextSerializer? _contextSerializer;
        private static readonly Dictionary<ITypeData, CppTypeContext> _map = new Dictionary<ITypeData, CppTypeContext>();
        internal static IReadOnlyDictionary<ITypeData, CppTypeContext> TypeToContext { get => _map; }

        /// <summary>
        /// This event is called after all types are PreSerialized, but BEFORE any definitions or declarations are resolved to includes.
        /// This allows for any delegates of this type to modify each type's context's definitions or declarations before they are considered complete.
        /// This is called for each type that is registered in <see cref="_map"/>, which is each type that is not skipped due to config.
        /// </summary>
        internal event Action<CppDataSerializer, ITypeData, CppTypeContext>? ContextsComplete;

        /// <summary>
        /// Creates a C++ Serializer with the given type context, which is a wrapper for a list of all types to serialize
        /// </summary>
        /// <param name="types"></param>
        public CppDataSerializer(SerializationConfig config, ITypeCollection types)
        {
            if (config is null) throw new ArgumentNullException(nameof(config));
            _collection = types;
            _config = config;
            CppStreamWriter.PopulateExistingFiles(Path.Combine(config.OutputDirectory, config.OutputHeaderDirectory));
            CppStreamWriter.PopulateExistingFiles(Path.Combine(config.OutputDirectory, config.OutputSourceDirectory));

            //if (Directory.Exists(Path.Combine(config.OutputDirectory, config.OutputHeaderDirectory)))
            //    Directory.Delete(Path.Combine(config.OutputDirectory, config.OutputHeaderDirectory), true);
            //if (Directory.Exists(Path.Combine(config.OutputDirectory, config.OutputSourceDirectory)))
            //    Directory.Delete(Path.Combine(config.OutputDirectory, config.OutputSourceDirectory), true);

            ContextsComplete += CppTypeContext.CreateConversionOperator;
        }

        private CppTypeContext CreateContext(ITypeData t, CppTypeContext? declaring)
        {
            if (!_map.TryGetValue(t, out var typeContext))
            {
                // Each type is always added to _map (with a non-null header and cpp context)
                _map.Add(t, typeContext = new CppTypeContext(_collection, t, declaring));
                foreach (var nt in t.NestedTypes)
                {
                    // For each nested type, we create a context for it
                    CreateContext(nt, typeContext);
                }
            }
            return typeContext;
        }

        public override void PreSerialize(CppTypeContext _unused_, IParsedData data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
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
                    CreateContext(t, null);

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
            if (_contextSerializer is null) throw new InvalidOperationException("Must call PreSerialize first!");
            if (_config.Id is null) throw new InvalidOperationException("Must supply config.Id!");
            int i = -1;
            int count = _map.Count;
            var mkSerializer = new AndroidMkSerializer(_config);
            mkSerializer.WriteHeader(Path.Combine(_config.OutputDirectory, "Android.mk"));
            var names = new List<string>();
            var libs = new List<AndroidMkSerializer.Library>();
            int currentPathLength = 0;

            var serializer = new CppSourceCreator(_config, _contextSerializer);
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
                if (/*t.Type == TypeEnum.Interface || */t.This.IsGeneric || (!t.Methods.Any() && !t.StaticFields.Any()))
                    // Don't create C++ for types with no methods (including static fields), or if it is an interface, or if it is generic
                    continue;

                // We need to split up the files into multiple pieces, which all build to static libraries and then build to a single shared library
                if (_config.MultipleLibraries)
                {
                    var name = _config.OutputSourceDirectory + "/" + pair.Value.CppFileName;
                    if (currentPathLength + name.Length >= _config.SourceFileCharacterLimit)
                    {
                        // If we are about to go over, use the names list to create a library and add it to libs.
                        var newLib = new AndroidMkSerializer.Library(_config.Id + "_" + i, true, names);
                        mkSerializer.WriteStaticLibrary(newLib);
                        libs.Add(newLib);
                        currentPathLength = 0;
                        names.Clear();
                    }
                    currentPathLength += name.Length;
                    names.Add(name);
                }
                if (i % _config.ChunkFrequency == 0 && _config.OneSourceFile)
                {
                    serializer.Close();
                    serializer.SetupChunkedSerialization(i);
                }

                serializer.Serialize(pair.Value);
            }
            serializer.Close();
            if (!_config.OneSourceFile)
                CppStreamWriter.DeleteUnwrittenFiles();
            Console.WriteLine($"Copy constructor count: {CppMethodSerializer.CopyConstructorCount}.");

            // After all static libraries are created, aggregate them all and collpase them into a single Android.mk file.
            // As a double check, doing a ctrl-f for any given id in the Android.mk should net two results: Where it is created and where it is aggregated.
            // Add one last lib for the final set of names to be built
            if (_config.MultipleLibraries)
            {
                if (names.Count > 0)
                {
                    var newLib = new AndroidMkSerializer.Library(_config.Id + "_" + i, true, names);
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
                mkSerializer.WritePrebuiltSharedLibrary("beatsaber-hook", "./extern/libbeatsaber-hook_0_7_4.so", "./extern/beatsaber-hook/shared/");
                mkSerializer.WriteSingleFile(new AndroidMkSerializer.Library(_config.Id, false, new List<string> { "beatsaber-hook" }));
            }
            mkSerializer.Close();
            // Write the Application.mk after
            var appMkSerializer = new ApplicationMkSerializer();
            appMkSerializer.Write(Path.Combine(_config.OutputDirectory, "Application.mk"));
            appMkSerializer.Close();
        }
    }
}