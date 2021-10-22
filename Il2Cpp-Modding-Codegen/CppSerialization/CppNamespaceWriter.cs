using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.CppSerialization
{
    public partial class CppStreamWriter
    {
        /// <summary>
        /// Represents a type that wraps a namespace definition in C++
        /// </summary>
        public class CppNamespaceWriter : IDisposable
        {
            private readonly CppStreamWriter writer;
            public HashSet<string> Types { get; } = new HashSet<string>();
            public HashSet<string> Methods { get; } = new HashSet<string>();

            internal CppNamespaceWriter(CppStreamWriter writer, string def)
            {
                this.writer = writer;
                writer.WriteDefinition("namespace " + def);
            }

            public void WriteComment(string comment) => writer.WriteComment(comment);

            public void WriteLine(string line) => writer.WriteLine(line);

            public void WriteDeclaration(string declString, string commentString = "") => writer.WriteDeclaration(declString, commentString);

            public CppTypeWriter OpenType(string prefix, string def)
            {
                Types.Add(def);
                return new CppTypeWriter(writer, prefix + def);
            }

            public CppMethodWriter OpenMethod(string prefix, string def)
            {
                Methods.Add(def);
                return new CppMethodWriter(writer, prefix + def);
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
}