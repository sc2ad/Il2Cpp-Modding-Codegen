using Il2CppModdingCodegen.Data;
using System;

namespace Il2CppModdingCodegen.Serialization
{
    public class DuplicateMethodException : Exception
    {
        private DuplicateMethodException() { }
        private DuplicateMethodException(string message) : base(message) { }
        internal DuplicateMethodException(IMethod method, string sigMatching)
            : this($"Method: {method} has matching signature: {sigMatching} in type: {method.DeclaringType}!") { }
        internal DuplicateMethodException(string message, Exception innerException) : base(message, innerException) { }
    }
}