using System;

namespace Il2CppModdingCodegen.Data
{
    public enum TypeEnum
    {
        Struct,
        Class,
        Enum,
        Interface
    }

    public static class TypeEnumExtensions
    {
        public static string TypeName(this TypeEnum type)
        {
            switch (type)
            {
                case TypeEnum.Class:
                case TypeEnum.Interface:
                    return "class";

                case TypeEnum.Struct:
                    return "struct";

                case TypeEnum.Enum:
                    // For now, serialize enums as structs
                    return "struct";

                default:
                    throw new InvalidOperationException($"Cannot get C++ type name of type: {type}!");
            }
        }
    }
}