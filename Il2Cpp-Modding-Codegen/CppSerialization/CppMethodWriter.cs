using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.CppSerialization
{
    public partial class CppStreamWriter
    {
        /// <summary>
        /// Represents a type that wraps a method definition in C++
        /// </summary>
        public class CppMethodWriter : IDisposable
        {
            private readonly CppStreamWriter writer;

            internal CppMethodWriter(CppStreamWriter writer, string prefix, string def)
            {
                this.writer = writer;
                writer.WriteDefinition(prefix + def);
            }

            public void WriteComment(string comment) => writer.WriteComment(comment);

            public void WriteLine(string line) => writer.WriteLine(line);

            public void WriteDeclaration(string declString, string commentString = "") => writer.WriteDeclaration(declString, commentString);

            public void StartBody(string prefix) => writer.WriteDefinition(prefix);

            public void EndBody(string suffix = "") => writer.CloseDefinition(suffix);

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
}