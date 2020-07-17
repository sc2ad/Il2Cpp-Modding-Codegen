using System.Collections.Generic;

namespace Il2Cpp_Modding_Codegen.Data
{
    public interface IParsedData : ITypeCollection
    {
        string Name { get; }
        List<IImage> Images { get; }
    }
}