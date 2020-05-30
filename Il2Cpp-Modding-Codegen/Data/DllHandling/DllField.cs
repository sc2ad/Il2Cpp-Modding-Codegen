using Il2Cpp_Modding_Codegen.Parsers;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public DllField(FieldDefinition f)
        {
            DeclaringType = new TypeRef(f.DeclaringType);
            Type = new TypeRef(f.FieldType);
            Name = f.Name;
            Offset = -1;
            if (f.HasCustomAttributes)
            {
                foreach (var ca in f.CustomAttributes)
                {
                    if (ca.AttributeType.Name == "FieldOffsetAttribute")
                    {
                        if (ca.Fields.Count > 0)
                            Offset = Convert.ToInt32(ca.Fields.FirstOrDefault().Argument.Value as string, 16);
                    }
                    else
                    {
                        // Ignore the DummyDll attributes
                        Attributes.Add(new DllAttribute(ca));
                    }
                }
            }
            // We can safely ignore Specifiers... They shouldn't exist for DLL data at all.
        }

        public override string ToString()
        {
            return $"{Type} {DeclaringType}.{Name}; // Offset: 0x{Offset:X}";
        }
    }
}