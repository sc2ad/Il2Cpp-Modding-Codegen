using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization.Interfaces
{
    public interface ISerializer<T>
    {
        void Resolve(T t);
    }
}