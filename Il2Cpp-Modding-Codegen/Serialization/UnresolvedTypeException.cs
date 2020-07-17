using Il2CppModdingCodegen.Data;
using System;

namespace Il2CppModdingCodegen.Serialization
{
#pragma warning disable CA1032 // Implement standard exception constructors
    public sealed class UnresolvedTypeException : Exception
#pragma warning restore CA1032 // Implement standard exception constructors
    {
        public UnresolvedTypeException(TypeRef declaringType, TypeRef typeFailed) : base($"{declaringType} could not find reference to type: {typeFailed}") { }
        public UnresolvedTypeException(TypeRef declaringType, TypeRef typeFailed, Exception innerException) : base($"{declaringType} could not find reference to type: {typeFailed}", innerException) { }
    }
}
