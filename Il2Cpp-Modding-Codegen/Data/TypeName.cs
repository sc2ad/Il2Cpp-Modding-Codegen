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
        public bool IsGeneric { get => IsGenericInstance || IsGenericTemplate; }
        public bool IsGenericInstance { get; }
        public bool IsGenericTemplate { get; }
        public IReadOnlyList<TypeRef> Generics { get; }
        public TypeRef DeclaringType { get; }
        public int IncludeCount { get; set; } = 0;
        private bool _getsOwnHeader = true;
        public bool GetsOwnHeader
        {
            get => _getsOwnHeader;
            set
            {
                //if (_getsOwnHeader == true && value == false && IncludeCount > 0)
                //    Console.WriteLine($"In-place nesting requested on {this} which has been included {IncludeCount} times!");
                _getsOwnHeader = value;
            }
        }

        public TypeName(TypeRef tr, int dupCount = 0)
        {
            Namespace = tr.SafeNamespace();
            Name = dupCount == 0 ? tr.SafeName() : tr.SafeName() + "_" + dupCount;
            IsGenericInstance = tr.IsGenericInstance;
            IsGenericTemplate = tr.IsGenericTemplate;
            Generics = tr.Generics?.ToList();
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
            var s = string.IsNullOrWhiteSpace(Namespace) ? Name : $"{Namespace}::{Name}";
            if (!IsGeneric)
                return s;

            s += "<";
            bool first = true;
            foreach (var param in Generics)
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