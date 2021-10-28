using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization.Interfaces
{
    public interface ISerializer<T, TWriter>
    {
        void Resolve(CppContext context, T t);

        void Write(TWriter writer, T t);
    }
}