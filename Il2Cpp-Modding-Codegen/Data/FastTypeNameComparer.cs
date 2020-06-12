using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
{
    /// <summary>
    /// Compares <see cref="TypeName"/> objects WITHOUT comparing their DeclaringTypes.
    /// This allows for faster comparison and avoids <see cref="StackOverflowException"/>
    /// </summary>
    public class FastTypeNameComparer : IEqualityComparer<TypeName>
    {
        // TODO: Ensure this behaves as intended for recursive DeclaringTypes (it probably does not)
        public bool Equals(TypeName x, TypeName y)
        {
            if (x is null != y is null)
                return false;
            return x.Namespace == y.Namespace &&
                x.Name == y.Name &&
                x.IsGenericInstance == y.IsGenericInstance &&
                x.IsGenericTemplate == y.IsGenericTemplate &&
                (x.Generics is null ? y.Generics is null : x.Generics.SequenceEqual(y.Generics));
        }

        public int GetHashCode(TypeName obj)
        {
            if (obj is null)
                return 0;
            // TODO: Ensure this function is correct
            int hashCode = 611187721;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(obj.Namespace);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(obj.Name);
            hashCode = hashCode * -1521134295 + obj.IsGenericInstance.GetHashCode();
            hashCode = hashCode * -1521134295 + obj.IsGenericTemplate.GetHashCode();
            if (obj.Generics != null)
            {
                foreach (var gp in obj.Generics)
                {
                    try
                    {
                        hashCode = hashCode * 31 + gp.GetHashCode();
                    }
                    catch (OverflowException)
                    {
                        // Ignore it
                    }
                }
            }
            return hashCode;
        }
    }
}