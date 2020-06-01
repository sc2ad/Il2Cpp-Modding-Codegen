using Il2Cpp_Modding_Codegen.Parsers;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    internal class DllProperty : IProperty
    {
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public TypeRef Type { get; }
        public TypeRef DeclaringType { get; }
        public string Name { get; }
        public bool GetMethod { get; }
        public bool SetMethod { get; }

        public DllProperty(PropertyDefinition p)
        {
            DeclaringType = TypeRef.From(p.DeclaringType);
            Type = TypeRef.From(p.PropertyType);
            Name = p.Name;
            GetMethod = p.GetMethod != null;
            SetMethod = p.SetMethod != null;
            if (p.HasCustomAttributes)
                Attributes.AddRange(p.CustomAttributes.Select(ca => new DllAttribute(ca)));
            Specifiers.AddRange(DllSpecifierHelpers.From(p));
        }

        public override string ToString()
        {
            var s = $"{Type} {Name}";
            s += " { ";
            if (GetMethod)
            {
                s += "get; ";
            }
            if (SetMethod)
            {
                s += "set; ";
            }
            s += "}";
            return s;
        }
    }
}