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
            if (x is null || y is null)
                return false;
            return x.Namespace == y.Namespace &&
                x.Name == y.Name &&
                x.Generic == y.Generic &&
                (x.GenericParameters != null ? x.GenericParameters.SequenceEqual(y.GenericParameters) : y.GenericParameters == null) &&
                (x.GenericArguments != null ? x.GenericArguments.SequenceEqual(y.GenericArguments) : y.GenericArguments == null);
        }

        public int GetHashCode(TypeName obj)
        {
            if (obj is null)
                return 0;
            // TODO: Ensure this function is correct
            int hashCode = 611187721;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(obj.Namespace);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(obj.Name);
            hashCode = hashCode * -1521134295 + obj.Generic.GetHashCode();
            if (obj.GenericParameters != null)
            {
                foreach (var gp in obj.GenericParameters)
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
            if (obj.GenericArguments != null)
            {
                foreach (var gp in obj.GenericArguments)
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