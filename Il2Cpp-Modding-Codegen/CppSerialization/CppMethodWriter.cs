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
        public class CppMethodWriter : CppNestedDefinitionWriter
        {
            internal CppMethodWriter(CppStreamWriter writer, string prefix, string def) : base(writer, prefix + def)
            {
            }

            public void StartBody(string prefix) => writer.WriteDefinition(prefix);

            public void EndBody(string suffix = "") => writer.CloseDefinition(suffix);
        }
    }
}