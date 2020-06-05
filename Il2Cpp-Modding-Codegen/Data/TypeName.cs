using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
{
    /// <summary>
    /// The goal of TypeName is to literally only be enough information to name the type.
    /// This means we should be able to write this type in any way shape or form without causing migraines.
    /// </summary>
    public class TypeName
    {
        public string Namespace { get; }
        public string Name { get; }
        public bool Generic { get; }
        public List<TypeRef> GenericParameters { get; } = new List<TypeRef>();
        public List<TypeRef> GenericArguments { get; } = null;
        public TypeRef DeclaringType { get; }

        public TypeName(TypeRef tr, int dupCount = 0)
        {
            Namespace = tr.SafeNamespace();
            Name = dupCount == 0 ? tr.SafeName() : tr.SafeName() + "_" + dupCount;
            Generic = tr.Generic;
            GenericParameters.AddRange(tr.GenericParameters);
            GenericArguments = tr.GenericArguments?.ToList();
            DeclaringType = tr.DeclaringType;
        }

        public TypeName(string @namespace, string name)
        {
            Namespace = @namespace;
            Name = name;
        }

        public override int GetHashCode()
        {
            return (Namespace + Name).GetHashCode();
        }

        // Namespace is actually NOT useful for comparisons!
        public override bool Equals(object obj)
        {
            var o = obj as TypeRef;
            if (o is null) return false;
            return o.Namespace + o.Name == Namespace + Name
                && o.Generic == Generic
                && ((GenericArguments is null) == (o.GenericArguments is null))
                && (GenericArguments?.SequenceEqual(o.GenericArguments)
                ?? GenericParameters.SequenceEqual(o.GenericParameters));
        }
    }
}