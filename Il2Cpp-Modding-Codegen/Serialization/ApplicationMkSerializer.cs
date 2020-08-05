using System.IO;

namespace Il2CppModdingCodegen.Serialization
{
    public sealed class ApplicationMkSerializer : System.IDisposable
    {
        private const string ApplicationMk = @"APP_ABI := arm64-v8a
APP_PLATFORM := android-24
APP_PIE := true
APP_STL := c++_static
APP_CFLAGS := -std=gnu18
APP_CPPFLAGS := -std=gnu++2a
APP_SHORT_COMMANDS := true";

        private StreamWriter? _stream;

        internal void Write(string filename)
        {
            _stream = new StreamWriter(new MemoryStream());
            _stream.WriteLine(ApplicationMk);
            _stream.Flush();
            CppStreamWriter.WriteIfDifferent(filename, _stream.BaseStream);
        }

        public void Close() => _stream?.Close();
        public void Dispose() => Close();
    }
}
