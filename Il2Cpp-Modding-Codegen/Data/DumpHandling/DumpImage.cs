using Il2CppModdingCodegen.Parsers;
using System;

namespace Il2CppModdingCodegen.Data.DumpHandling
{
    internal class DumpImage : IImage
    {
        public string Name { get; }
        public int Start { get; }

        internal DumpImage(PeekableStreamReader fs)
        {
            var line = fs.ReadLine() ?? "";
            var split = line.Split(' ');
            if (split.Length < 6)
                throw new InvalidOperationException($"Could not create Image out of: \"{line.Trim()}\"");

            Start = int.Parse(split[^1]);
            Name = split[^3];
        }

        public override string ToString() => $"{Name} - {Start}";
    }
}
