using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    public class TypeDefinitionComparer : IEqualityComparer<TypeDefinition>
    {
        public bool Equals(TypeDefinition x, TypeDefinition y)
        {
            return x.FullName == y.FullName;
        }

        public int GetHashCode(TypeDefinition obj)
        {
            return obj.FullName.GetHashCode();
        }
    }
}