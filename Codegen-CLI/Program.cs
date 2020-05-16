using Il2Cpp_Modding_Codegen;
using Il2Cpp_Modding_Codegen.Config;
using System;
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
            var parsed = parser.Parse(stream);
            Console.WriteLine(parsed);
            Console.WriteLine("============================================");
            Console.ReadLine();
        }
    }
}