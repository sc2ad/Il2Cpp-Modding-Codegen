using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
{
    public interface IParsedData : ITypeContext
    {
        string Name { get; }
        List<IImage> Images { get; }
    }
}