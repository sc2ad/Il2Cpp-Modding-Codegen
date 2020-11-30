using Il2CppModdingCodegen;
using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Parsers;
using Il2CppModdingCodegen.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Codegen_CLI
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine(DateTime.UtcNow.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'"));
            Console.WriteLine("Drag and drop your dump.cs file (or a partial of it of the correct format) then press enter...");
            string path = @"C:\Users\Sc2ad\Desktop\Code\Android Modding\BeatSaber\1.13.0\DummyDll";
            if (!Directory.Exists(path))
                path = Console.ReadLine().Replace("\"", string.Empty);
            bool parseDlls = false;
            IParser parser;
            if (Directory.Exists(path))
            {
                var parseConfig = new DllConfig() { };
                parser = new DllParser(parseConfig);
                parseDlls = true;
            }
            else
            {
                var parseConfig = new DumpConfig() { };
                parser = new DumpParser(parseConfig);
            }

            Console.WriteLine("Parsing...");
            Stopwatch watch = new();
            watch.Start();
            IParsedData parsed;
            if (parseDlls)
                parsed = parser.Parse(path);
            else
            {
                using var stream = File.OpenRead(path);
                parsed = parser.Parse(stream);
            }
            watch.Stop();
            //Console.WriteLine(parsed);
            Console.WriteLine($"Parsing took: {watch.Elapsed}!");
            Console.WriteLine("============================================");
            Console.WriteLine("Type the name of an output style (or don't for Normal) then press enter to serialize:");
            var input = "ThrowUnless";
            //var input = Console.ReadLine();
            // TODO: strip non-alphabetic characters out of input before parsing it
            if (Enum.TryParse(input, true, out OutputStyle style))
                Console.WriteLine($"Parsed style '{style}'");

            var libIl2cpp = @"C:\Program Files\Unity\Hub\Editor\2019.3.15f1\Editor\Data\il2cpp\libil2cpp";
            if (!Directory.Exists(libIl2cpp))
            {
                Console.WriteLine("Drag and drop your libil2cpp folder into this window then press enter:");
                libIl2cpp = Console.ReadLine();
            }

            Console.WriteLine("Creating serializer...");
            var config = new SerializationConfig
            {
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
                    "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq", "INT_MAX", "INT_MIN"
                },
                IllegalMethodNames = new HashSet<string> {
                    "bzero", "Assert"
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
                Libil2cpp = libIl2cpp
            };

            var serializer = new CppDataSerializer(config, parsed);
            Console.WriteLine("Resolving types...");
            try
            {
                watch.Restart();
                // context unused
                serializer.PreSerialize(null, parsed);
                watch.Stop();
                Console.WriteLine($"Resolution Complete, took: {watch.Elapsed}!");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            Console.WriteLine("Serializing...");
            try
            {
                watch.Restart();
                serializer.Serialize(null, parsed, true);
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