using System.CodeDom.Compiler;

namespace Il2Cpp_Modding_Codegen.Serialization.Interfaces
{
    public interface ISerializer<T>
    {
        void PreSerialize(ISerializerContext context, T obj);

        void Serialize(IndentedTextWriter writer, T obj);
    }
}