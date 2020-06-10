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

        public static string ConvertTypeToName(this TypeName def)
        {
            return def.Name;
        }

        public static string ConvertTypeToNamespace(this TypeName def)
        {
            if (def.Namespace == string.Empty)
                return NoNamespace;
            return def.Namespace;
        }

        public static string ConvertTypeToQualifiedName(this TypeName def, ITypeContext context)
        {
            string space;
            if (def.DeclaringType is null)
                space = ConvertTypeToNamespace(def);
            else
                space = context.ResolvedTypeRef(def.DeclaringType).ConvertTypeToQualifiedName(context);
            return space + "::" + ConvertTypeToName(def);
        }

        public static string ConvertTypeToInclude(this TypeName def, ITypeContext context)
        {
            if (def.DeclaringType != null)
                return context.ResolvedTypeRef(def.DeclaringType).ConvertTypeToInclude(context);
            // TODO: instead split on :: and Path.Combine?
            var fileName = string.Join("-", ConvertTypeToName(def).Replace("::", "_").Split(Path.GetInvalidFileNameChars()));
            var directory = string.Join("-", ConvertTypeToNamespace(def).Replace("::", "_").Split(Path.GetInvalidPathChars()));
            return Path.Combine(directory, fileName);
        }

        public static string ConvertTypeToIl2CppMetadata(this TypeName def)
        {
            var s = "";
            var tmp = def;
            while (tmp.DeclaringType != null)
            {
                if (!tmp.GenericParameters.SequenceEqual(tmp.DeclaringType.GenericParameters))
                    throw new InvalidOperationException($"{tmp.DeclaringType} is generic, but nested class {tmp} does not have the same generic parameters!");
                var genericStr = "";
                if (tmp.DeclaringType.Generic && (tmp.DeclaringType.DeclaringType == null || !tmp.DeclaringType.DeclaringType.Generic))
                    genericStr = "`" + tmp.DeclaringType.GenericParameters.Count();
                s = tmp.DeclaringType.Name + genericStr + "/" + s;
                tmp = new TypeName(tmp.DeclaringType);
            }
            return s;
        }
    }
}
