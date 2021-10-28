using System;
using System.Collections.Generic;
using static Il2CppModdingCodegen.CppSerialization.CppStreamWriter;

namespace Il2CppModdingCodegen.CppSerialization
{
    /// <summary>
    /// Represents a type that wraps a type definition in C++
    /// </summary>
    public class CppTypeWriter : CppNestedDefinitionWriter
    {
        public HashSet<string> Types { get; } = new HashSet<string>();
        public HashSet<string> Methods { get; } = new HashSet<string>();

        internal CppTypeWriter(CppStreamWriter writer, string prefix, string def, string suffix) : base(writer, prefix + " " + def + " " + suffix, ";")
        {
        }

        public CppTypeWriter OpenType(string prefix, string def, string suffix = "")
        {
            Types.Add(def);
            return new CppTypeWriter(Writer, prefix, def, suffix);
        }

        public CppMethodWriter OpenMethod(string prefix, string def)
        {
            Methods.Add(def);
            return new CppMethodWriter(Writer, prefix, def);
        }
    }
}