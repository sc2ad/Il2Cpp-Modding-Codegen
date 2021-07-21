using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    internal class DllField : IField
    {
        internal FieldDefinition This;
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public TypeRef Type { get; }
        public TypeRef DeclaringType { get; }
        public string Name { get; }
        public int Offset { get; }
        public int LayoutOffset { get; } = -1;
        public object Constant { get; }

        internal DllField(FieldDefinition f, TypeDefinition info)
        {
            LayoutOffset = f.Offset;
            DeclaringType = DllTypeRef.From(f.DeclaringType);
            Type = DllTypeRef.From(f.FieldType);
            Name = f.Name;
            Offset = -1;
            if (f.HasCustomAttributes)
                foreach (var ca in f.CustomAttributes)
                    if (ca.AttributeType.Name == "FieldOffsetAttribute" || ca.AttributeType.Name == "StaticFieldOffsetAttribute")
                    {
                        if (ca.Fields.Count > 0)
                            Offset = Convert.ToInt32(ca.Fields.FirstOrDefault().Argument.Value as string, 16);
                        if (info.IsEnum)
                            // Because Il2CppInspector is bad and emits 0x10 for fields on enums. I seriously don't know why.
                            Offset -= 0x10;
                    }
                    else
                    {
                        // Ignore the DummyDll attributes
                        var atr = new DllAttribute(ca);
                        if (!string.IsNullOrEmpty(atr.Name))
                            Attributes.Add(atr);
                    }

            Specifiers.AddRange(DllSpecifierHelpers.From(f));

            This = f;
            Constant = f.Constant;
        }

        public override string ToString() => $"{Type} {DeclaringType}.{Name}; // Offset: 0x{Offset:X}";
    }
}