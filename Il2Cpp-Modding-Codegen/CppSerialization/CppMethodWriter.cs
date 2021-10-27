using System;
using System.Collections.Generic;
using System.Text;
using static Il2CppModdingCodegen.CppSerialization.CppStreamWriter;

namespace Il2CppModdingCodegen.CppSerialization
{
    /// <summary>
    /// Represents a type that wraps a method definition in C++
    /// </summary>
    public class CppMethodWriter : CppNestedDefinitionWriter
    {
        internal CppMethodWriter(CppStreamWriter writer, string prefix, string def) : base(writer, prefix + def)
        {
        }

        public void StartBody(string prefix) => WriteDefinition(prefix);

        public void EndBody(string suffix = "") => CloseDefinition(suffix);
    }
}