using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
{
    public static class TypeNameExtensions
    {
        private const string NoNamespace = "GlobalNamespace";

        public static string ConvertTypeToNamespace(this TypeName def)
        {
            if (def.Namespace == string.Empty)
                return NoNamespace;
            return def.Namespace;
        }

        public static string ConvertTypeToIl2CppMetadata(this TypeName def)
        {
            var s = "";
            var tmp = def;
            while (tmp.DeclaringType != null)
            {
                // TODO: compare the generics as if they were Sets instead?
                if (tmp.IsGenericTemplate && !tmp.Generics.SequenceEqual(tmp.DeclaringType.Generics))
                    throw new InvalidOperationException($"{tmp.DeclaringType} is generic, but nested class {tmp} does not have the same generic parameters!");
                var genericStr = "";
                if (tmp.DeclaringType.IsGeneric && (tmp.DeclaringType.DeclaringType == null || !tmp.DeclaringType.DeclaringType.IsGeneric))
                    genericStr = "`" + tmp.DeclaringType.Generics.Count;
                s = tmp.DeclaringType.Name + genericStr + "/" + s;
                tmp = new TypeName(tmp.DeclaringType);
            }
            return s;
        }
    }
}