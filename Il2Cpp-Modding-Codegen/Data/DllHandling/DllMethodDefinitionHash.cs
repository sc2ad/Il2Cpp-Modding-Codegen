using Mono.Cecil;
using System.Collections.Generic;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    /// <summary>
    /// Provides a way of determining whether two method definitions are the same or not.
    /// This is important because of the cache in DllMethod, which will otherwise fail to properly map base methods.
    /// </summary>
    internal class DllMethodDefinitionHash : IEqualityComparer<MethodDefinition>
    {
        /// <summary>
        /// Here, we check some common fields.
        /// This is by no means entirely complete, but if we see a duplicate, we should double hit the cache and subsequently throw when adding a key that exists to DllMethod.cache.
        /// This currently checks attributes, declaring type, return type, parameters, and generic parameters.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public bool Equals(MethodDefinition x, MethodDefinition y)
        {
            if (x.Attributes != y.Attributes
                || x.FullName != y.FullName
                || x.ReturnType.FullName != y.ReturnType.FullName
                || x.Parameters.Count != y.Parameters.Count
                || x.DeclaringType.FullName != y.DeclaringType.FullName
                || x.HasGenericParameters != y.HasGenericParameters
                || x.GenericParameters.Count != y.GenericParameters.Count)
                return false;
            for (int i = 0; i < x.Parameters.Count; i++)
                if (x.Parameters[i].ParameterType.FullName != y.Parameters[i].ParameterType.FullName)
                    return false;
            for (int i = 0; i < x.GenericParameters.Count; i++)
                if (x.GenericParameters[i].FullName != y.GenericParameters[i].FullName)
                    return false;
            return true;
        }

        // This currently collides quite frequently and should probably be adjusted.
        public int GetHashCode(MethodDefinition obj)
        {
            var val = obj.FullName.GetHashCode() ^ (19 * obj.DeclaringType.FullName.GetHashCode() << 1);
            foreach (var p in obj.Parameters)
                val ^= p.ParameterType.FullName.GetHashCode();
            return val;
        }
    }
}