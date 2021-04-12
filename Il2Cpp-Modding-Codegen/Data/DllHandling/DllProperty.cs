using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Data.DllHandling
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

        internal DllProperty(PropertyDefinition p)
        {
            DeclaringType = DllTypeRef.From(p.DeclaringType);
            Type = DllTypeRef.From(p.PropertyType);
            Name = p.Name;
            GetMethod = p.GetMethod != null;
            SetMethod = p.SetMethod != null;
            if (p.HasCustomAttributes)
                Attributes.AddRange(p.CustomAttributes.Select(ca => new DllAttribute(ca)).Where(a => !string.IsNullOrEmpty(a.Name)));
            Specifiers.AddRange(DllSpecifierHelpers.From(p));
        }

        public override string ToString()
        {
            var s = $"{Type} {Name}";
            s += " { ";
            if (GetMethod)
                s += "get; ";
            if (SetMethod)
                s += "set; ";
            s += "}";
            return s;
        }
    }
}