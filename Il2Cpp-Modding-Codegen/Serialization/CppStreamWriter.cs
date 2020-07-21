using System.CodeDom.Compiler;
using System.IO;

namespace Il2CppModdingCodegen.Serialization
{
    /// <summary>
    /// A C++ valid syntax writer
    /// </summary>
    public class CppStreamWriter : IndentedTextWriter
    {
        internal CppStreamWriter(TextWriter writer) : base(writer) { }

        internal CppStreamWriter(TextWriter writer, string tabString) : base(writer, tabString) { }

        /// <summary>
        /// Write a single line comment
        /// </summary>
        /// <param name="commentString"></param>
        internal void WriteComment(string commentString) => WriteLine("// " + commentString);

        internal void WriteInclude(string include) => WriteLine("#include \"" + include + "\"");

        /// <summary>
        /// Write a single ;-terminated line (or a declaration)
        /// </summary>
        /// <param name="declString"></param>
        internal void WriteDeclaration(string declString) => WriteLine(declString + ";");

        internal void WriteFieldDeclaration(string fieldType, string fieldName) => WriteDeclaration(fieldType + ' ' + fieldName);

        /// <summary>
        /// Write a single syntax ; terminated line with a comment on the same line (or a declaration)
        /// </summary>
        /// <param name="declString"></param>
        /// <param name="commentString">If null or empty, will call <see cref="WriteDeclaration(string)"/></param>
        internal void WriteDeclaration(string declString, string commentString)
        {
            if (string.IsNullOrEmpty(commentString))
                WriteDeclaration(declString);
            else
                WriteLine(declString + "; // " + commentString);
        }

        /// <summary>
        /// Write a definition and open a body with {
        /// </summary>
        /// <param name="defString"></param>
        internal void WriteDefinition(string defString)
        {
            WriteLine(defString + " {");
            Indent++;
        }

        /// <summary>
        /// Close a body with }
        /// </summary>
        internal void CloseDefinition(string suffix = "")
        {
            Indent--;
            WriteLine("}" + suffix);
        }
    }
}
