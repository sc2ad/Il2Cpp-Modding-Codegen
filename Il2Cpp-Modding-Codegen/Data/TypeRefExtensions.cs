using System;
using System.Collections.Generic;
using System.IO;
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
    }
}