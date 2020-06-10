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
    public class TypeName : IEquatable<TypeName>
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

        // null @namespace is reserved for Il2Cpp typedefs
        public TypeName(string @namespace, string name)
        {
            Namespace = @namespace;
            Name = name;
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Namespace))
                return $"{Namespace}::{Name}";
            if (!Generic)
                return $"{Name}";
            var s = Name + "<";
            bool first = true;
            var generics = GenericArguments ?? GenericParameters;
            foreach (var param in generics)
            {
                if (!first) s += ", ";
                s += param.ToString();
                first = false;
            }
            s += ">";
            return s;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TypeName);
        }

        private static FastTypeNameComparer fastComparer = new FastTypeNameComparer();

        public bool Equals(TypeName other)
        {
            return fastComparer.Equals(this, other) &&
                (DeclaringType?.Equals(other.DeclaringType) ?? other.DeclaringType == null);
        }

        public override int GetHashCode()
        {
            int hashCode = fastComparer.GetHashCode(this);
            hashCode = hashCode * -1521134295 + DeclaringType?.GetHashCode() ?? 0;
            return hashCode;
        }
    }
}