using System;
using System.CodeDom.Compiler;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public abstract class Serializer<T>
    {
        public event Action<T> OnResolved;

        public event Action<T> OnSerialized;

        public abstract void PreSerialize(CppSerializerContext context, T obj);

        public abstract void Serialize(CppStreamWriter writer, T obj);

        protected virtual void Resolved(T obj)
        {
            OnResolved?.Invoke(obj);
        }

        protected virtual void Serialized(T obj)
        {
            OnSerialized?.Invoke(obj);
        }
    }
}