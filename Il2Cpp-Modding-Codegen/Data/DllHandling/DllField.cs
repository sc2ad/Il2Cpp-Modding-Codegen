using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;

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
            DeclaringType = DllTypeRef.From(f.DeclaringType);
            Type = DllTypeRef.From(f.FieldType);
            Name = f.Name;
            Offset = -1;
            if (f.HasCustomAttributes)
                foreach (var ca in f.CustomAttributes)
                    if (ca.AttributeType.Name == "FieldOffsetAttribute")
                    {
                        if (ca.Fields.Count > 0)
                            Offset = Convert.ToInt32(ca.Fields.FirstOrDefault().Argument.Value as string, 16);
                    }
                    else
                        // Ignore the DummyDll attributes
                        Attributes.Add(new DllAttribute(ca));

            Specifiers.AddRange(DllSpecifierHelpers.From(f));
        }

        public override string ToString() => $"{Type} {DeclaringType}.{Name}; // Offset: 0x{Offset:X}";
    }
}
