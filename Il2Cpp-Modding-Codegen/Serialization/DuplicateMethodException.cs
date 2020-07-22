using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class DuplicateMethodException : Exception
    {
        internal DuplicateMethodException(IMethod method, string sigMatching) : this($"Method: {method} has matching signature: {sigMatching} in type: {method.DeclaringType}!")
        {
        }

        private DuplicateMethodException(string message) : base(message)
        {
        }

        private DuplicateMethodException(string message, Exception innerException) : base(message, innerException)
        {
        }

        private DuplicateMethodException()
        {
        }
    }
}