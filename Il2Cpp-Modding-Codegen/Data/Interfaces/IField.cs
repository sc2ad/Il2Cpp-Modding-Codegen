using System.Collections.Generic;

namespace Il2CppModdingCodegen.Data
{
    public interface IField
    {
        List<IAttribute> Attributes { get; }
        List<ISpecifier> Specifiers { get; }
        TypeRef Type { get; }
        TypeRef DeclaringType { get; }
        string Name { get; }
        int Offset { get; }
        int LayoutOffset { get; }
    }
}