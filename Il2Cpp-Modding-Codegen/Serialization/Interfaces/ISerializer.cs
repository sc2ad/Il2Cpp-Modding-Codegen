using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization.Interfaces
{
    public interface ISerializer<T, TWriter>
    {
        void Resolve(T t);

        void Write(TWriter writer);
    }
}