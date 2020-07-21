using System.Collections.Generic;

namespace Il2CppModdingCodegen.Data
{
    public interface IParsedData : ITypeCollection
    {
        string Name { get; }
        List<IImage> Images { get; }
    }
}
