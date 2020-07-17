using System.Collections.Generic;

namespace Il2Cpp_Modding_Codegen.Data
{
    public interface ITypeData
    {
        TypeRef This { get; }
        TypeEnum Type { get; }
        TypeInfo Info { get; }
        TypeRef Parent { get; }
        HashSet<ITypeData> NestedTypes { get; }
        List<TypeRef> ImplementingInterfaces { get; }
        int TypeDefIndex { get; }
        List<IAttribute> Attributes { get; }
        List<ISpecifier> Specifiers { get; }
        List<IField> Fields { get; }
        List<IProperty> Properties { get; }
        List<IMethod> Methods { get; }
    }
}