using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
{
    public interface ITypeContext
    {
        List<ITypeData> Types { get; }

        ITypeData Resolve(TypeDefinition typeDefinition);

        TypeDefinition ResolvedTypeDefinition(TypeDefinition def);
    }
}