using Mono.Cecil;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;

namespace Il2Cpp_Modding_Codegen.Data
{
    public abstract class TypeRef : IEquatable<TypeRef>
    {
        public abstract string Namespace { get; }
        public abstract string Name { get; }

        public abstract bool Generic { get; }
        public abstract IEnumerable<TypeRef> GenericParameters { get; }
        public abstract IEnumerable<TypeRef> GenericArguments { get; }
        public abstract TypeRef DeclaringType { get; }
        public abstract TypeRef ElementType { get; }

        private ITypeData _resolvedType;

        /// <summary>
        /// Resolves the type in the given context
        /// </summary>
        public ITypeData Resolve(ITypeContext context)
        {
            if (_resolvedType == null)
            {
                _resolvedType = context.Resolve(this);
            }
            return _resolvedType;
        }

        public bool IsVoid()
        {
            return Name.Equals("void", StringComparison.OrdinalIgnoreCase);
        }

        public virtual bool IsPointer(ITypeContext context)
        {
            // Resolve type, if type is not a value type, it is a pointer
            Resolve(context);
            return _resolvedType?.Info.TypeFlags == TypeFlags.ReferenceType;
        }

        public abstract bool IsArray();

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

        public string SafeName()
        {
            // TODO: make nested types actually nested by replacing / with :: instead
            return Name.Replace('<', '_').Replace('>', '_').Replace('`', '_').Replace('/', '_');
        }

        public string SafeNamespace()
        {
            return Namespace?.Replace('<', '_').Replace('>', '_').Replace('`', '_').Replace('/', '_').Replace(".", "::");
        }

        public string SafeFullName()
        {
            return SafeNamespace() + "::" + SafeName();
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TypeRef);
        }

        private static FastTypeRefComparer fastComparer;

        public bool Equals(TypeRef other)
        {
            if (fastComparer == null)
                fastComparer = new FastTypeRefComparer();
            bool eq = fastComparer.Equals(this, other);
            if (!eq)
                return false;
            if (DeclaringType != null)
                // Check to ensure the declaring type is not ourselves (short circuit to true)
                // If it isn't, instead of performing a deep check on our declaring type, perform a fast check.
                eq = eq && DeclaringType != this ? fastComparer.Equals(DeclaringType, other.DeclaringType) : true;
            else
                eq = eq && other.DeclaringType == null;
            if (ElementType != null)
                // Perform a fast check instead of a deep check for the same reasons as above.
                eq = eq && ElementType != this ? fastComparer.Equals(ElementType, other.ElementType) : true;
            else
                eq = eq && other.ElementType == null;
            return eq;
        }

        public override int GetHashCode()
        {
            if (fastComparer == null)
                fastComparer = new FastTypeRefComparer();
            int hashCode = fastComparer.GetHashCode(this);
            hashCode = hashCode * -1521134295 + fastComparer.GetHashCode(DeclaringType);
            hashCode = hashCode * -1521134295 + fastComparer.GetHashCode(ElementType);
            return hashCode;
        }
    }
}