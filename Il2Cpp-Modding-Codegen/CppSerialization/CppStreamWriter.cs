using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppModdingCodegen.CppSerialization
{
    /// <summary>
    /// A C++ valid syntax writer.
    /// Overall todo:
    /// - This type should also be harder to write invalid semantic information in (template writes would be ideal)
    /// - Should be easier to write members on opened C++ types
    /// - Should be easier to keep track of all known C++ members/extract references to them
    /// - Should be easier to apply large scale changes without entire type rewrites
    /// - Method Serializer should not exist in its current form at all
    /// - Parsing can be rewritten to accept dlls specifically
    /// - Handling singular members should be far easier-- we should be able to handle each method in isolation
    /// - if we see a method that we want to skip, it should not be challenging, likewise, field ops should be easy too
    /// Ideally setup for header only approach? includes after the fact, assuming non-generic.
    /// Also consider using Cecil to determine type sizes since they should match.
    /// </summary>
    public partial class CppStreamWriter : IndentedTextWriter
    {
        internal CppStreamWriter(StreamWriter writer) : base(writer)
        {
        }

        internal CppStreamWriter(StreamWriter writer, string tabString) : base(writer, tabString)
        {
        }

        /// <summary>
        /// Write a single line comment
        /// </summary>
        /// <param name="commentString"></param>
        public void WriteComment(string commentString) => WriteLine("// " + commentString);

        public void WriteInclude(string include) => WriteLine("#include \"" + include + "\"");

        /// <summary>
        /// Write a single ;-terminated line (or a declaration)
        /// </summary>
        /// <param name="declString"></param>
        public void WriteDeclaration(string declString) => WriteLine(declString + ";");

        internal void WriteFieldDeclaration(string fieldType, string fieldName) => WriteDeclaration(fieldType + ' ' + fieldName);

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

        public void WriteTemplate(string innards) => WriteLine($"template<{innards}>");

        // TODO: Maybe write some methods for ensuring methods can't be called with incorrect parameter counts?
        // Perhaps macro level checks?
        // Perhaps also check generics for generic definitions, map to generic args
        // Perhaps also check to make sure all identifiers are validly mapped/exist

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

        public CppDefinitionWriter OpenDefinition(string def)
        {
            return new CppDefinitionWriter(this, def);
        }
    }
}