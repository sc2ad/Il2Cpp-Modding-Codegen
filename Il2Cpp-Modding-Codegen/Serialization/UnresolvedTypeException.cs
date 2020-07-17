using Il2Cpp_Modding_Codegen.Data;
using System;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class UnresolvedTypeException : Exception
    {
        public UnresolvedTypeException(TypeRef declaringType, TypeRef typeFailed) : base($"{declaringType} could not find reference to type: {typeFailed}") { }
    }
}
