using Mono.Cecil;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;

namespace Il2Cpp_Modding_Codegen.Data
{
    public abstract class TypeRef
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
            return Name.Replace('<', '_').Replace('>', '_').Replace('`', '_').Replace(".", "::");
        }

        public string SafeNamespace()
        {
            return Namespace.Replace('<', '_').Replace('>', '_').Replace('`', '_').Replace(".", "::");
        }

        public string SafeFullName()
        {
            return SafeNamespace() + "::" + SafeName();
        }

        // Namespace is actually NOT useful for comparisons!
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