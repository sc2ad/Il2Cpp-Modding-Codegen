using System.Collections.Generic;

namespace Il2Cpp_Modding_Codegen.Data
{
    public interface IMethod
    {
        List<IAttribute> Attributes { get; }
        List<ISpecifier> Specifiers { get; }
        int RVA { get; }
        int Offset { get; }
        int VA { get; }
        int Slot { get; }
        TypeDefinition ReturnType { get; }
        string Name { get; }
        List<Parameter> Parameters { get; }
    }
}