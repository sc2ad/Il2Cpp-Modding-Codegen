using System.Collections.Generic;

namespace Il2Cpp_Modding_Codegen.Data
{
    public interface IMethod
    {
        bool Generic { get; }
        List<IAttribute> Attributes { get; }
        List<ISpecifier> Specifiers { get; }
        int RVA { get; }
        int Offset { get; }
        int VA { get; }
        int Slot { get; }
        TypeRef ReturnType { get; }
        TypeRef DeclaringType { get; }
        TypeRef ImplementedFrom { get; }
        TypeRef OverriddenFrom { get; }
        string Name { get; }
        List<Parameter> Parameters { get; }
    }
}