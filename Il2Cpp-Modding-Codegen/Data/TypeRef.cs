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
        public abstract string Namespace { get; protected set; }
        public abstract string Name { get; protected set; }

        public abstract bool Generic { get; protected set; }
        public abstract List<TypeRef> GenericParameters { get; }
        public abstract TypeRef DeclaringType { get; protected set; }
        public abstract TypeRef ElementType { get; protected set; }

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

        public bool IsPointer(ITypeContext context)
        {
            if (Name.EndsWith("*"))
            {
                return true;
            }
            // Resolve type, if type is not a value type, it is a pointer
            Resolve(context);
            return _resolvedType?.Info.TypeFlags == TypeFlags.ReferenceType;
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Namespace))
                return $"{Namespace}::{Name}";
            if (!Generic)
                return $"{Name}";
            var s = Name + "<";
            for (int i = 0; i < GenericParameters.Count; i++)
            {
                s += GenericParameters[i].ToString();
                if (i != GenericParameters.Count - 1)
                    s += ", ";
            }
            s += ">";
            return s;
        }

        public string SafeName()
        {
            return Name.Replace('<', '_').Replace('>', '_').Replace(".", "::");
        }

        public string SafeNamespace()
        {
            return Namespace.Replace('<', '_').Replace('>', '_').Replace(".", "::");
        }

        public string SafeFullName()
        {
            return SafeNamespace() + "_" + SafeName();
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
            return o?.Namespace + o?.Name == Namespace + Name
                && o?.Generic == Generic
                && GenericParameters.SequenceEqual(o?.GenericParameters);
        }
    }
}