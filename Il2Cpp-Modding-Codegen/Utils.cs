using Mono.Cecil;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Il2Cpp_Modding_Codegen
{
    static class Utils
    {
        public static string ReplaceFirst(this string text, string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0)
                return text;
            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }

        public static TypeDefinition ResolvedBaseType(this TypeDefinition self)
        {
            var base_type = self?.BaseType;
            if (base_type is null) return null;
            return base_type.Resolve();
        }

        // Returns all methods with the same name and parameters as `self` in any base type or interface of `type`.
        private static HashSet<MethodDefinition> FindIn(this MethodDefinition self, TypeDefinition type)
        {
            HashSet<MethodDefinition> matches = new HashSet<MethodDefinition>();
            if (type == null) return matches;
            if (type != self.DeclaringType)
                foreach (var m in type.Methods)
                    if (m.Name == self.Name && m.Parameters.SequenceEqual(self.Parameters))
                        matches.Add(m);
            matches.UnionWith(self.FindIn(type.ResolvedBaseType()));
            foreach (var @interface in type.Interfaces)
                matches.UnionWith(self.FindIn(@interface.InterfaceType.Resolve()));
            return matches;
        }

        // Returns all methods with the same name and parameters as `self` in any base type or interface of `self.DeclaringType`.
        // Unlike Mono.Cecil.Rocks.MethodDefinitionRocks.GetBaseMethod, will never return `self`.
        public static HashSet<MethodDefinition> GetBaseMethods(this MethodDefinition self)
        {
            Contract.Requires(self != null);
            return self.FindIn(self.DeclaringType);
        }
    }
}
