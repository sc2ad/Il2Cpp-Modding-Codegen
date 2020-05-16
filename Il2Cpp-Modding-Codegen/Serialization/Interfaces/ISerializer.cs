using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization.Interfaces
{
    public interface ISerializer<T>
    {
        void PreSerialize(ISerializerContext context, T obj);

        void Serialize(Stream stream, T obj);
    }
}