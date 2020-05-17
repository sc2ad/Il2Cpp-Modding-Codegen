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
            Console.WriteLine("Hello World!");
            var parser = new DumpParser(new DumpConfig());

            using var stream = File.OpenRead(@"C:\Users\Sc2ad\Desktop\Code\Android Modding\BeatSaber\1.8.0\partial.cs");
            Console.WriteLine("Parsing...");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            var parsed = parser.Parse(stream);
            watch.Stop();
            //Console.WriteLine(parsed);
            Console.WriteLine($"Parsing took: {watch.ElapsedMilliseconds}ms");
            Console.WriteLine("============================================");
            Console.ReadLine();

            Console.WriteLine("Creating serializer...");
            var config = new SerializationConfig
            {
                OutputDirectory = Path.Combine(Environment.CurrentDirectory, "output"),
                OutputHeaderDirectory = "include",
                OutputSourceDirectory = "src"
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
            Console.WriteLine($"Serialization Complete, took: {watch.ElapsedMilliseconds}ms!");
            Console.ReadLine();
        }
    }
}