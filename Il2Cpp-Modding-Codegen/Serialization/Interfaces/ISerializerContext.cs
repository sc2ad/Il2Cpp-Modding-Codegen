using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization.Interfaces
{
    public interface ISerializerContext
    {
        ITypeContext TypeContext { get; }
    }
}