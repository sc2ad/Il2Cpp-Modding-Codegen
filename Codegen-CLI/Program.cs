using Il2CppModdingCodegen;
using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.CppSerialization;
using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Data.DllHandling;
using Il2CppModdingCodegen.Serialization;
using Il2CppModdingCodegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine(DateTime.UtcNow.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'"));
            Console.WriteLine("Drag and drop your dump.cs file (or a partial of it of the correct format) then press enter...");
            string path = @"D:\AndroidModding\BeatSaber\1.18.1\DummyDll";
            //string path = @"C:\Users\Sc2ad\Desktop\Code\Android Modding\GorillaTag\DummyDll";
            if (!Directory.Exists(path))
                path = Console.ReadLine().Replace("\"", string.Empty);
            while (!Directory.Exists(path))
            {
                Console.WriteLine("Enter a valid directory!");
                path = Console.ReadLine().Replace("\"", string.Empty);
            }
            var parseConfig = new DllConfig() { };
            var parser = new DllParser(parseConfig);

            Console.WriteLine("Parsing...");
            Stopwatch watch = new();
            watch.Start();
            var parsed = parser.Parse(path);
            watch.Stop();
            //Console.WriteLine(parsed);
            Console.WriteLine($"Parsing took: {watch.Elapsed}!");
            Console.WriteLine("============================================");
            Console.WriteLine("Type the name of an output style (or don't for Normal) then press enter to serialize:");
            var input = "ThrowUnless";
            //var input = Console.ReadLine();
            // TODO: strip non-alphabetic characters out of input before parsing it
            OutputStyle style;
            while (!Enum.TryParse(input, true, out style))
            {
                Console.WriteLine($"Failed to parse valid {nameof(OutputStyle)} from: {input}");
                input = Console.ReadLine();
            }
            Console.WriteLine($"Parsed style '{style}'");

            Console.WriteLine("Creating serializer...");
            var config = new SerializationConfig
            {
                OneSourceFile = true,
                ChunkFrequency = 100,
                // from https://en.cppreference.com/w/cpp/keyword
                IllegalNames = new HashSet<string> {
                    "alignas", "alignof", "and", "and_eq", "asm", "atomic_cancel", "atomic_commit", "atomic_noexcept", "auto",
                    "bitand", "bitor", "bool", "break", "case", "catch", "char", "char8_t", "char16_t", "char32_t", "class",
                    "compl", "concept", "const", "consteval", "constexpr", "constinit", "const_cast", "continue", "co_await",
                    "co_return", "co_yield", "decltype", "default", "delete", "do", "double", "dynamic_cast", "else", "enum",
                    "explicit", "export", "extern", "false", "float", "for", "friend", "goto", "if", "inline", "int", "long",
                    "mutable", "namespace", "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq",
                    "private", "protected", "public", "reflexpr", "register", "reinterpret_cast", "requires", "return",
                    "short", "signed", "sizeof", "static", "static_assert", "static_cast", "struct", "switch", "synchronized",
                    "template", "this", "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union",
                    "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq", "INT_MAX", "INT_MIN",
                    "Assert", "bzero", "ID", "VERSION", "NULL"
                },
                IllegalMethodNames = new HashSet<string> {
                    "bzero", "Assert"
                },
                QualifiedBlacklistMethods = new HashSet<(string @namespace, string typeName, string methodName)>
                {
                    ("UnityEngine.ResourceManagement.AsyncOperations", "AsyncOperationHandle", "Convert")
                },
                OutputDirectory = Path.Combine(Environment.CurrentDirectory, "output"),
                OutputHeaderDirectory = "include",
                OutputSourceDirectory = "src",
                GenericHandling = GenericHandling.Do,
                OutputStyle = style,
                UnresolvedTypeExceptionHandling = new UnresolvedTypeExceptionHandlingWrapper
                {
                    FieldHandling = UnresolvedTypeExceptionHandling.DisplayInFile,
                    MethodHandling = UnresolvedTypeExceptionHandling.DisplayInFile,
                    TypeHandling = UnresolvedTypeExceptionHandling.DisplayInFile
                },
                PrintSerializationProgress = true,
                PrintSerializationProgressFrequency = 1000,
                Id = "codegen",
                Version = "0.2.5",
            };

            if (config.OneSourceFile)
            {
                // If we have one source file, yeet our destination src
                if (Directory.Exists(Path.Combine(config.OutputDirectory, config.OutputSourceDirectory)))
                    Directory.Delete(Path.Combine(config.OutputDirectory, config.OutputSourceDirectory), true);
            }
            Utils.Init(config);
            var serializers = new List<ISerializer<TypeDefinition, CppStreamWriter>>
            {
            };

            var serializer = new CppOverallSerializer(config, serializers);
            Console.WriteLine("Resolving types...");
            try
            {
                watch.Restart();
                // context unused
                serializer.Begin(parsed);
                watch.Stop();
                Console.WriteLine($"Resolution Complete, took: {watch.Elapsed}!");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            //Console.WriteLine("Performing JSON dump...");
            //watch.Restart();
            //var jsonOutput = Path.Combine(Environment.CurrentDirectory, "json_output");
            //Directory.CreateDirectory(jsonOutput);
            //var outp = Path.Combine(jsonOutput, "parsed.json");
            //if (File.Exists(outp))
            //    File.Delete(outp);
            //var conf = new JsonSerializerOptions
            //{
            //    WriteIndented = true,
            //    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            //};
            //var strc = new SimpleTypeRefConverter(parsed.Types);
            //conf.Converters.Add(strc);
            //conf.Converters.Add(new TypeDataConverter(parsed, strc));
            //conf.Converters.Add(new MethodConverter());
            //conf.Converters.Add(new JsonStringEnumConverter());
            //conf.Converters.Add(new FieldConverter());
            //conf.Converters.Add(new SpecifierConverter());
            //conf.Converters.Add(new PropertyConverter());
            //using (var fs = File.OpenWrite(outp))
            //{
            //    JsonSerializer.SerializeAsync(fs, parsed as DllData, conf).Wait();
            //}
            //watch.Stop();
            //Console.WriteLine($"Json Dump took: {watch.Elapsed}!");
            //Console.WriteLine("============================================");

            Console.WriteLine("Serializing...");
            try
            {
                watch.Restart();
                serializer.Write(parsed);
                watch.Stop();
                Console.WriteLine($"Serialization Complete, took: {watch.Elapsed}!");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            Console.WriteLine(string.Join(", ", SerializationConfig.SpecialMethodNames));
            Console.ReadLine();
        }
    }
}