using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
{
    public static class TypeRefExtensions
    {
        private const string NoNamespace = "GlobalNamespace";

        public static string ConvertTypeToName(this TypeRef def)
        {
            return def.Name;
        }

        public static string ConvertTypeToNamespace(this TypeRef def)
        {
            if (string.IsNullOrWhiteSpace(def.Namespace))
                return NoNamespace;
            return def.Namespace;
        }

        public static string ConvertTypeToQualifiedName(this TypeRef def)
        {
            return ConvertTypeToNamespace(def) + "::" + ConvertTypeToName(def);
        }

        public static string ConvertTypeToInclude(this TypeRef def)
        {
            // TODO: instead split on :: and Path.Combine?
            var fileName = string.Join("-", ConvertTypeToName(def).Replace("::", "_").Split(Path.GetInvalidFileNameChars()));
            var directory = string.Join("-", ConvertTypeToNamespace(def).Replace("::", "_").Split(Path.GetInvalidPathChars()));
            return Path.Combine(directory, fileName);
        }

        public static string ConvertTypeToIl2CppMetadata(this TypeRef def)
        {
            var s = "";
            var tmp = def;
            while (tmp.DeclaringType != null)
            {
                if (!tmp.GenericParameters.SequenceEqual(tmp.DeclaringType.GenericParameters))
                    throw new InvalidOperationException($"{tmp.DeclaringType} is generic, but nested class {tmp} does not have the same generic parameters!");
                var genericStr = "";
                if (tmp.DeclaringType.Generic && (tmp.DeclaringType.DeclaringType == null || !tmp.DeclaringType.DeclaringType.Generic))
                    genericStr = "`" + tmp.DeclaringType.GenericParameters.Count;
                s = tmp.DeclaringType.Name + genericStr + "/" + s;
                tmp = tmp.DeclaringType;
            }
            return s;
        }
    }
}