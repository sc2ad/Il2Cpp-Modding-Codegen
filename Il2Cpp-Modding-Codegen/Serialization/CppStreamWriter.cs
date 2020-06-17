using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    /// <summary>
    /// A C++ valid syntax writer
    /// </summary>
    public class CppStreamWriter : IndentedTextWriter
    {
        public CppStreamWriter(TextWriter writer) : base(writer)
        {
        }

        public CppStreamWriter(TextWriter writer, string tabString) : base(writer, tabString)
        {
        }

        /// <summary>
        /// Write a single line comment
        /// </summary>
        /// <param name="commentString"></param>
        public void WriteComment(string commentString)
        {
            WriteLine("// " + commentString);
        }

        public void WriteInclude(string include)
        {
            WriteLine("#include \"" + include + "\"");
        }

        /// <summary>
        /// Write a single syntax ; terminated line (or a declaration)
        /// </summary>
        /// <param name="declString"></param>
        public void WriteDeclaration(string declString)
        {
            WriteLine(declString + ";");
        }

        public void WriteFieldDeclaration(string fieldType, string fieldName)
        {
            WriteLine(fieldType + ' ' + fieldName + ';');
        }

        /// <summary>
        /// Write a single syntax ; terminated line with a comment on the same line (or a declaration)
        /// </summary>
        /// <param name="declString"></param>
        /// <param name="commentString">If null or empty, will call <see cref="WriteDeclaration(string)"/></param>
        public void WriteDeclaration(string declString, string commentString)
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
        public void WriteDefinition(string defString)
        {
            WriteLine(defString + " {");
            Indent++;
        }

        /// <summary>
        /// Close a body with }
        /// </summary>
        public void CloseDefinition(string suffix = "")
        {
            Indent--;
            WriteLine("}" + suffix);
        }
    }
}