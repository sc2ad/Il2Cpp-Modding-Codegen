using System.IO;

namespace Il2CppModdingCodegen.Parsers
{
    public class PeekableStreamReader : StreamReader
    {
        // Only buffer a maximum of one line
        private string bufferedLine = null;

        internal ulong CurrentLineIndex { get; private set; } = 0;

        internal PeekableStreamReader(string path) : base(path) { }

        internal PeekableStreamReader(Stream stream) : base(stream) { }

        internal string PeekLine()
        {
            if (bufferedLine != null)
                return bufferedLine;
            string line = base.ReadLine();
            if (line != null)
                bufferedLine = line;
            return line;
        }

        public override string ReadLine()
        {
            CurrentLineIndex++;
            if (bufferedLine != null)
            {
                var tmp = bufferedLine;
                bufferedLine = null;
                return tmp;
            }
            return base.ReadLine();
        }
    }
}
