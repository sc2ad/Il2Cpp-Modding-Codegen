using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.CppSerialization
{
    public partial class CppStreamWriter
    {
        public class CppNestedDefinitionWriter : IDisposable
        {
            protected readonly CppStreamWriter writer;
            private readonly string suffix;

            internal CppNestedDefinitionWriter(CppStreamWriter writer, string def, string suffix = "")
            {
                this.writer = writer;
                this.suffix = suffix;
                writer.WriteDefinition(def);
            }

            public void WriteComment(string comment) => writer.WriteComment(comment);

            public void WriteLine(string line) => writer.WriteLine(line);

            public void WriteDeclaration(string declString, string commentString = "") => writer.WriteDeclaration(declString, commentString);

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected void WriteDefinition(string line) => writer.WriteDefinition(line);

            protected void CloseDefinition(string suffix = "") => writer.CloseDefinition(suffix);

            protected virtual void Dispose(bool managed)
            {
                writer.CloseDefinition(suffix);
            }
        }
    }
}