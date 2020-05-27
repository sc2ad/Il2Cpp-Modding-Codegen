using Il2Cpp_Modding_Codegen;
using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Serialization;
using System;
using System.Diagnostics;
using System.IO;

namespace Codegen_CLI
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Drag and drop your dump.cs file (or a partial of it of the correct format) then press enter...");
            string path;
            if (File.Exists(@"C:\Users\Sc2ad\Desktop\Code\Android Modding\BeatSaber\1.8.0\partial.cs"))
                path = @"C:\Users\Sc2ad\Desktop\Code\Android Modding\BeatSaber\1.8.0\partial.cs";
            else
                path = Console.ReadLine();
            var parser = new DumpParser(new DumpConfig());

            using var stream = File.OpenRead(path);
            Console.WriteLine("Parsing...");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            var parsed = parser.Parse(stream);
            watch.Stop();
            //Console.WriteLine(parsed);
            Console.WriteLine($"Parsing took: {watch.ElapsedMilliseconds}ms");
            Console.WriteLine("============================================");
            Console.WriteLine("Type the name of an output style (or don't for Normal) then press enter to serialize:");
            var input = Console.ReadLine();
            OutputStyle style = OutputStyle.Normal;
            if (Enum.TryParse(input, false, out style))
            {
                Console.WriteLine($"Parsed style '{style}'");
            }

            Console.WriteLine("Creating serializer...");
            var config = new SerializationConfig
            {
                OutputDirectory = Path.Combine(Environment.CurrentDirectory, "output"),
                OutputHeaderDirectory = "include",
                OutputSourceDirectory = "src",
                GenericHandling = GenericHandling.Do,
                OutputStyle = style,
                UnresolvedTypeExceptionHandling = new ExceptionHandling
                {
                    FieldHandling = UnresolvedTypeExceptionHandling.DisplayInFile,
                    MethodHandling = UnresolvedTypeExceptionHandling.DisplayInFile,
                    TypeHandling = UnresolvedTypeExceptionHandling.DisplayInFile
                }
            };

            if (Directory.Exists(Path.Combine(config.OutputDirectory, config.OutputHeaderDirectory)))
                Directory.Delete(Path.Combine(config.OutputDirectory, config.OutputHeaderDirectory), true);
            if (Directory.Exists(Path.Combine(config.OutputDirectory, config.OutputSourceDirectory)))
                Directory.Delete(Path.Combine(config.OutputDirectory, config.OutputSourceDirectory), true);

            var serializer = new CppDataSerializer(config, parsed);
            Console.WriteLine("Serializing...");
            watch.Restart();
            // context unused
            serializer.PreSerialize(null, parsed);
            watch.Stop();
            Console.WriteLine($"Serialization Complete, took: {watch.Elapsed}!");
            Console.ReadLine();
        }
    }
}