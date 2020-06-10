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

        public bool Equals(TypeRef other)
        {
            return other != null &&
                   Namespace == other.Namespace &&
                   Name == other.Name &&
                   Generic == other.Generic &&
                   EqualityComparer<IEnumerable<TypeRef>>.Default.Equals(GenericParameters, other.GenericParameters) &&
                   EqualityComparer<IEnumerable<TypeRef>>.Default.Equals(GenericArguments, other.GenericArguments) &&
                   EqualityComparer<TypeRef>.Default.Equals(DeclaringType, other.DeclaringType) &&
                   EqualityComparer<TypeRef>.Default.Equals(ElementType, other.ElementType);
        }

        public override int GetHashCode()
        {
            int hashCode = 611187721;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Namespace);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
            hashCode = hashCode * -1521134295 + Generic.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<IEnumerable<TypeRef>>.Default.GetHashCode(GenericParameters);
            hashCode = hashCode * -1521134295 + EqualityComparer<IEnumerable<TypeRef>>.Default.GetHashCode(GenericArguments);
            hashCode = hashCode * -1521134295 + EqualityComparer<TypeRef>.Default.GetHashCode(DeclaringType);
            hashCode = hashCode * -1521134295 + EqualityComparer<TypeRef>.Default.GetHashCode(ElementType);
            return hashCode;
        }
    }
}