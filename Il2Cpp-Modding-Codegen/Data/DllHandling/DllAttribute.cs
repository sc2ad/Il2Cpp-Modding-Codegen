using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    internal class DllAttribute : IAttribute
    {
        public string Name { get; }
        public int RVA { get; }
        public int Offset { get; }
        public int VA { get; }

        public DllAttribute(PeekableStreamReader fs)
        {
            var line = fs.ReadLine().Trim();
            // Line must start with a [ after being trimmed
            if (!line.StartsWith("["))
            {
                throw new InvalidOperationException($"Line {fs.CurrentLineIndex}: Could not create attribute from: \"{line.Trim()}\"");
            }
            var parsed = line.Substring(1);
            Name = parsed.Substring(0, line.LastIndexOf(']') - 1);
            var split = parsed.Split(new string[] { " " }, StringSplitOptions.None);
            if (split.Length != 8)
            {
                RVA = 0;
                Offset = 0;
                VA = 0;
                return;
            }
            RVA = Convert.ToInt32(split[3], 16);
            Offset = Convert.ToInt32(split[5], 16);
            VA = Convert.ToInt32(split[7], 16);
        }

        public override string ToString()
        {
            return $"[{Name}] // Offset: 0x{Offset:X}";
        }
    }
}