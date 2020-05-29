using Il2Cpp_Modding_Codegen.Parsers;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    internal class DllField : IField
    {
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public TypeRef Type { get; }
        public TypeRef DeclaringType { get; }
        public string Name { get; }
        public int Offset { get; }

        public DllField(FieldDefinition f, TypeDefinition def)
        {
            DeclaringType = new TypeRef(def);
            Attributes.AddRange(DllAttribute.From(f));

            Offset = f.Offset;
            Name = f.Name;
            Type = new TypeRef(f.FieldType);
            Specifiers.AddRange(DllSpecifier.From(f));
        }

        public override string ToString()
        {
            var s = "";
            foreach (var atr in Attributes)
            {
                s += $"{atr}\n\t";
            }
            foreach (var spec in Specifiers)
            {
                s += $"{spec} ";
            }
            s += $"{Type} {Name}; // 0x{Offset:X}";
            return s;
        }
    }
}