using Il2CppModdingCodegen.Data;
using System;

namespace Il2CppModdingCodegen.Serialization
{
    public sealed class UnresolvedTypeException : Exception
    {
        public UnresolvedTypeException(TypeRef declaringType, TypeRef typeFailed) : base($"{declaringType} could not find reference to type: {typeFailed}") { }
        public UnresolvedTypeException(TypeRef declaringType, TypeRef typeFailed, Exception innerException) : base($"{declaringType} could not find reference to type: {typeFailed}", innerException) { }
    }
}
