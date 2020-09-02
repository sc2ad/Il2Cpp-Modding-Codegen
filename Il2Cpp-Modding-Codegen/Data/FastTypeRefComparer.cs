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
            if (x.IsGeneric != y.IsGeneric)
                return false;
            // if their counts match, consider it good enough.
            return x.Generics is null ? y.Generics is null : x.Generics.Count == y.Generics.Count;
        }

        public int GetHashCode(TypeRef obj)
        {
            if (obj is null)
                return 0;
            // TODO: Ensure this function is correct
            int hashCode = 611187721;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(obj.Namespace);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(obj.Name);
            hashCode = hashCode * -1521134295 + obj.Generics.Count;
            // Generics are not included in the hash code.
            return hashCode;
        }
    }
}
