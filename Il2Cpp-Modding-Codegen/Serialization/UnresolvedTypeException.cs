using Il2CppModdingCodegen.Data;
using Mono.Cecil;
using System;

namespace Il2CppModdingCodegen.Serialization
{
    public class UnresolvedTypeException : Exception
    {
        internal UnresolvedTypeException(TypeReference declaringType, TypeReference typeFailed) : this($"{declaringType} could not find reference to type: {typeFailed}")
        {
        }

        private UnresolvedTypeException()
        {
        }

        private UnresolvedTypeException(string message) : base(message)
        {
        }

        private UnresolvedTypeException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}