using System;

namespace Il2CppModdingCodegen.Serialization
{
    public abstract class Serializer<T>
    {
        internal event Action<T>? OnResolved;
        protected virtual void Resolved(T obj) => OnResolved?.Invoke(obj);

        internal event Action<T>? OnSerialized;
        protected virtual void Serialized(T obj) => OnSerialized?.Invoke(obj);

        public abstract void PreSerialize(CppTypeContext context, T obj);

        public abstract void Serialize(CppStreamWriter writer, T obj, bool asHeader);
    }
}
