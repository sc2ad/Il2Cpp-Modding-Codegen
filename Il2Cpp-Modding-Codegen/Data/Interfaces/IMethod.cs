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
        // Does this method hide (by signature or override) an existing method in a base class or interface?
        bool HidesBase { get; }
        // If the method overrides (in C# terms) another, OR iff HidesBase and only 1 such method is hidden, this gives that method's DeclaringType.
        TypeRef OverriddenFrom { get; }
        string Name { get; }
        List<Parameter> Parameters { get; }
    }
}