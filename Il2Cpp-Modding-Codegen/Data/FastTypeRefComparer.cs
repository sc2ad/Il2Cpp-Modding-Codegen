using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Data
{
    /// <summary>
    /// Compares <see cref="TypeRef"/> objects WITHOUT comparing their DeclaringTypes.
    /// This allows for faster comparison and avoids <see cref="StackOverflowException"/>
    /// </summary>
    public class FastTypeRefComparer : IEqualityComparer<TypeRef>
    {
        // TODO: Ensure this behaves as intended for recursive DeclaringTypes (it probably does not)
        public bool Equals(TypeRef? x, TypeRef? y)
        {
            if (x is null || y is null)
                return x is null == y is null;
            if (x.Namespace != y.Namespace || x.Name != y.Name)
                return false;
            if (x.KnownOffsetTypeName != y.KnownOffsetTypeName)
                return false;
            if (x.IsGeneric && y.IsGeneric)
            {
                // If both x and y are generic
                if (x.IsGenericInstance == y.IsGenericInstance || x.IsGenericTemplate == y.IsGenericTemplate)
                    // If they are both an instance or both a template, return sequence equal
                    return x.Generics is null ? y.Generics is null : x.Generics.SequenceEqual(y.Generics, this);
                // Otherwise, if one is a template and the other is an instance, if their counts match, consider it good enough.
                return x.Generics is null ? y.Generics is null : x.Generics.Count == y.Generics.Count;
            }
            return true;
        }

        public int GetHashCode(TypeRef obj)
        {
            if (obj is null)
                return 0;
            return (obj.Namespace, obj.Name, obj.KnownOffsetTypeName).GetHashCode();
        }
    }
}
