using System.IO;

namespace Il2Cpp_Modding_Codegen.Parsers
{
    public class PeekableStreamReader : StreamReader
    {


        // Only buffer a maximum of one line
        private string bufferedLine = null;

        //private Queue<string> _bufferedLines = new Queue<string>();

        public ulong CurrentLineIndex { get; private set; } = 0;

        public PeekableStreamReader(string path) : base(path)
        {
        }

        public PeekableStreamReader(Stream stream) : base(stream)
        {
        }

        public string PeekLine()
        {
            if (bufferedLine != null)
                return bufferedLine;
            string line = base.ReadLine();
            if (line == null)
                return null;
            bufferedLine = line;
            //_bufferedLines.Enqueue(line);
            return line;
        }

        public override string ReadLine()
        {
            CurrentLineIndex++;
            //if (_bufferedLines.Count > 0)
            //    return _bufferedLines.Dequeue();
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