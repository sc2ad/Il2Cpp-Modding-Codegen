using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppModdingCodegen
{
    internal static class Utils
    {
        internal static string ReplaceFirst(this string text, string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0)
                return text;
            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }

        internal static TypeDefinition ResolvedBaseType(this TypeDefinition self)
        {
            var base_type = self?.BaseType;
            if (base_type is null) return null;
            return base_type.Resolve();
        }

        private static Dictionary<string, TypeReference> GetGenerics(this TypeReference self, TypeDefinition templateType)
        {
            var map = new Dictionary<string, TypeReference>();
            if (!self.IsGenericInstance)
                return map;
            if (!(self is GenericInstanceType instance) || instance.GenericArguments.Count != templateType.GenericParameters.Count)
                // Mismatch of generic parameters. Presumably, resolved has some inherited generic parameters that it is not listing, although this should not happen.
                // Since !0 and !1 will occur in resolved.GenericParameters instead.
                throw new InvalidOperationException("instance.GenericArguments is either null or of a mismatching count compared to resolved.GenericParameters!");
            for (int i = 0; i < templateType.GenericParameters.Count; i++)
                // Map from resolved generic parameter to self generic parameter
                map.Add(templateType.GenericParameters[i].Name, (self as GenericInstanceType).GenericArguments[i]);
            return map;
        }

        class QuickComparer : IEqualityComparer<TypeReference>
        {
            public bool Equals(TypeReference r1, TypeReference r2) => r1?.FullName == r2?.FullName;
            public int GetHashCode(TypeReference r) => r?.FullName.GetHashCode() ?? 0;
        };
        static readonly QuickComparer quickCompare = new QuickComparer();

        // Returns all methods with the same name and parameters as `self` in any base type or interface of `type`.
        private static HashSet<MethodDefinition> FindIn(this MethodDefinition self, TypeDefinition type, Dictionary<string, TypeReference> genericMapping)
        {
            HashSet<MethodDefinition> matches = new HashSet<MethodDefinition>();
            if (type == null) return matches;
            if (type != self.DeclaringType)
            {
                var sName = self.Name.Substring(self.Name.LastIndexOf('.') + 1);
                foreach (var m in type.Methods)
                {
                    // We don't want to actually check the equivalence of these, we want to check to see if they mean the same thing.
                    // For example, if we have a T, we want to ensure that the Ts would match
                    // We need to ensure the name of both self and m are fixed to not have any ., use the last . and ignore generic parameters
                    if (m.Name.Substring(m.Name.LastIndexOf('.') + 1) != sName)
                        continue;
                    if (!genericMapping.TryGetValue(m.ReturnType.Name, out var ret))
                        ret = m.ReturnType;
                    // Only if ret == self.ReturnType, can we have a match
                    if (!quickCompare.Equals(ret, self.ReturnType))
                        continue;
                    if (m.Parameters.Count != self.Parameters.Count)
                        continue;

                    var mParams = m.Parameters.Select(
                        p => genericMapping.TryGetValue(p.ParameterType.Name, out var arg) ? arg : p.ParameterType);
                    if (mParams.SequenceEqual(self.Parameters.Select(p => p.ParameterType), quickCompare))
                        matches.Add(m);
                }
            }

            var bType = type.ResolvedBaseType();
            matches.UnionWith(self.FindIn(bType, type.GetGenerics(bType)));
            foreach (var @interface in type.Interfaces)
            {
                var resolved = @interface.InterfaceType.Resolve();
                matches.UnionWith(self.FindIn(resolved, @interface.InterfaceType.GetGenerics(resolved)));
            }
            return matches;
        }

        private static TypeReference FindInterface(TypeReference type, string find)
        {
            if (type is null) return null;
            var typeStr = Regex.Replace(type.ToString(), @"`\d+", "").Replace('/', '.');
            if (typeStr == find)
                return type;

            var def = type.Resolve();
            if (def is null) return null;
            foreach (var iface in def.Interfaces)
            {
                var ret = FindInterface(iface.InterfaceType, find);
                if (ret != null) return ret;
            }
            return FindInterface(def.BaseType, find);
        }

        internal static MethodDefinition GetSpecialNameBaseMethod(this MethodDefinition self, out TypeReference iface, int idxDot = -1)
        {
            if (idxDot == -1)
                idxDot = self.Name.LastIndexOf('.');
            if (idxDot < 2)
            {
                iface = null;
                return null;
            }
            var typeStr = self.Name.Substring(0, idxDot);
            iface = FindInterface(self.DeclaringType, typeStr);
            var tName = self.Name.Substring(idxDot + 1);
            return iface.Resolve().Methods.Where(im => im.Name == tName && self.Parameters.Count == im.Parameters.Count).Single();
        }

        /// <summary>
        /// Returns all methods with the same name, parameters, and return type as <paramref name="self"/> in any base type or interface of <see cref="MethodDefinition.DeclaringType"/>
        /// Returns an empty set if no matching methods are found. Does not include <paramref name="self"/> in the search.
        /// </summary>
        /// <param name="self"></param>
        /// <returns></returns>
        internal static HashSet<MethodDefinition> GetBaseMethods(this MethodDefinition self)
        {
            Contract.Requires(self != null);
            // Whenever we call GetBaseMethods, we should explicitly exclude all base methods that are specifically defined by self.DeclaringType via special names already.
            // We would ideally do this by compiling a list of special named methods, and for each of those, explicitly excluding them from our matches.
            // However, this means that we need to be able to convert a special name to a base method, which means we need an extension method for it here.
            // TODO: This list should be generated once and then cached.
            var specialBaseMethods = self.DeclaringType.Methods.Select(m => m.GetSpecialNameBaseMethod(out var iface)).Where(md => md != null);
            var matches = self.FindIn(self.DeclaringType, self.DeclaringType.GetGenerics(self.DeclaringType.Resolve()));
            foreach (var sbm in specialBaseMethods)
                matches.Remove(sbm);
            return matches;
        }
    }
}
