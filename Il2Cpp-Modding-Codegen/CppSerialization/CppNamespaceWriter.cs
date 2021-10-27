using System;
using System.Collections.Generic;
using System.Text;
using static Il2CppModdingCodegen.CppSerialization.CppStreamWriter;

namespace Il2CppModdingCodegen.CppSerialization
{
    /// <summary>
    /// Represents a type that wraps a namespace definition in C++
    /// </summary>
    public class CppNamespaceWriter : CppNestedDefinitionWriter
    {
        public HashSet<string> Types { get; } = new HashSet<string>();
        public HashSet<string> Methods { get; } = new HashSet<string>();

        internal CppNamespaceWriter(CppStreamWriter writer, string def) : base(writer, "namespace " + def)
        {
        }

        public CppTypeWriter OpenType(string prefix, string def, string suffix = "")
        {
            Types.Add(def);
            return new CppTypeWriter(writer, prefix, def, suffix);
        }

        public CppMethodWriter OpenMethod(string prefix, string def)
        {
            Methods.Add(def);
            return new CppMethodWriter(writer, prefix, def);
        }
    }
}