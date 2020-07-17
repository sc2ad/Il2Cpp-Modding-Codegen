using Il2CppModdingCodegen.Parsers;
using System;

namespace Il2CppModdingCodegen.Data.DumpHandling
{
    internal class DumpAttribute : IAttribute
    {
        public string Name { get; }
        public int RVA { get; } = 0;
        public int Offset { get; } = 0;
        public int VA { get; } = 0;

        public DumpAttribute(PeekableStreamReader fs)
        {
            var line = fs.ReadLine().Trim();
            // Line must start with a [ after being trimmed
            if (!line.StartsWith("["))
                throw new InvalidOperationException($"Line {fs.CurrentLineIndex}: Could not create attribute from: \"{line.Trim()}\"");

            var parsed = line.Substring(1);
            Name = parsed.Substring(0, line.LastIndexOf(']') - 1);
            var split = parsed.Split(new string[] { " " }, StringSplitOptions.None);
            if (split.Length != 8)
                return;
            RVA = Convert.ToInt32(split[3], 16);
            Offset = Convert.ToInt32(split[5], 16);
            VA = Convert.ToInt32(split[7], 16);
        }

        public override string ToString() => $"[{Name}] // Offset: 0x{Offset:X}";
    }
}
