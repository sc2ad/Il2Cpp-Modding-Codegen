using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization.Interfaces
{
    public interface ISerializerContext
    {
        string TypeNamespace { get; }
        string TypeName { get; }
        string QualifiedTypeName { get; }

        string GetNameFromReference(TypeDefinition def, ForceAsType force = ForceAsType.None, bool qualified = true, bool genericParams = true);
    }

    public enum ForceAsType
    {
        None,
        Literal,
        Pointer,
        Reference
    }
}