using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.CppSerialization
{
    public class CppNestedDefinitionWriter : IDisposable
    {
        protected CppStreamWriter Writer { get; }
        private readonly string suffix;

        internal CppNestedDefinitionWriter(CppStreamWriter writer, string def, string suffix = "")
        {
            Writer = writer;
            this.suffix = suffix;
            WriteDefinition(def);
        }

        public void WriteComment(string comment) => Writer.WriteComment(comment);

        public void WriteLine(string line) => Writer.WriteLine(line);

        public void WriteDeclaration(string declString, string commentString = "") => Writer.WriteDeclaration(declString, commentString);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Write a definition and open a body with {
        /// </summary>
        /// <param name="defString"></param>
        protected void WriteDefinition(string defString)
        {
            Writer.WriteLine(defString + " {");
            Writer.Indent++;
        }

        /// <summary>
        /// Close a body with }
        /// </summary>
        protected void CloseDefinition(string suffix = "")
        {
            Writer.Indent--;
            Writer.WriteLine("}" + suffix);
        }

        protected virtual void Dispose(bool managed)
        {
            CloseDefinition(suffix);
        }
    }
}