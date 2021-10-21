using System;

namespace Il2CppModdingCodegen.CppSerialization
{
    /// <summary>
    /// Represents a type that wraps a definition in C++
    /// </summary>
    public class CppDefinitionWriter : IDisposable
    {
        private readonly CppStreamWriter writer;

        internal CppDefinitionWriter(CppStreamWriter writer, string def)
        {
            this.writer = writer;
            writer.WriteDefinition(def);
        }

        public void WriteComment(string comment) => writer.WriteComment(comment);
        public void WriteLine(string line) => writer.WriteLine(line);
        public void WriteDeclaration(string declString, string commentString) => writer.WriteDeclaration(declString, commentString);

        public CppDefinitionWriter OpenDefinition(string def)
        {
            return new CppDefinitionWriter(writer, def);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool managed)
        {
            writer.CloseDefinition();
        }
    }
}