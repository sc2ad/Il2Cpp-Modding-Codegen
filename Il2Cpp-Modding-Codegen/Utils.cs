using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppModdingCodegen
{
    public static class Utils
    {
        private static HashSet<string>? illegalNames;

        public static void Init(SerializationConfig cfg)
        {
            illegalNames = cfg.IllegalNames;
        }

        /// <summary>
        /// Returns a name that is definitely not within IllegalNames
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        [return: NotNullIfNotNull("name")]
        internal static string? SafeName(string? name)
        {
            if (name is null) return null;
            while (illegalNames?.Contains(name) is true)
                name = "_" + name;
            return name;
        }

        internal static string ReplaceFirst(this string text, string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0)
                return text;
            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }

        internal static string TrimStart(this string str, string prefix)
        {
            if (str.StartsWith(prefix))
                return str.Substring(prefix.Length);
            return str;
        }

        internal static IEnumerable<T> GetEnumValues<T>() => Enum.GetValues(typeof(T)).Cast<T>();

        internal static IEnumerable<string> GetFlagStrings<T>(this T flags) where T : struct, Enum =>
            // the Convert.ToBoolean filters out None (or whatever has the value 0)
            GetEnumValues<T>().Where(f => Convert.ToBoolean(f) && flags.HasFlag(f)).Select(f => f.ToString());

        internal static string GetFlagsString<T>(this T flags) where T : struct, Enum => string.Join(" ", flags.GetFlagStrings());

        internal static void AddOrThrow<T>(this ISet<T> set, T item)
        {
            if (!set.Add(item))
                throw new ArgumentException("The item was already in the set!");
        }

        internal static void RemoveOrThrow<T>(this ISet<T> set, T item)
        {
            if (!set.Remove(item))
                throw new ArgumentException("The item was not in the set!");
        }

        internal static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue> constructor)
        {
            if (!dict.TryGetValue(key, out var ret))
            {
                ret = constructor();
                dict.Add(key, ret);
            }
            return ret;
        }

        internal static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) where TValue : new()
            => dict.GetOrAdd(key, () => new TValue());

        internal static void AddOrUnionWith<TKey, TValue>(this IDictionary<TKey, SortedSet<TValue>> dict, TKey key, SortedSet<TValue> values)
        {
            if (dict.TryGetValue(key, out var existingValues))
                existingValues.UnionWith(values);
            else
                dict.Add(key, values);
        }

        private static readonly char[] angleBrackets = new char[] { '<', '>' };

        internal static string SafeFieldName(this IField field)
        {
            var name = field.Name;
            if (name.EndsWith("k__BackingField"))
                name = name.Split(angleBrackets, StringSplitOptions.RemoveEmptyEntries)[0];
            name = string.Join("$", name.Split(angleBrackets)).Trim('_');
            if (char.IsDigit(name[0])) name = "_" + name;
            var sstr = name.LastIndexOf('.');
            if (sstr != -1 && sstr < name.Length)
                name = name.Substring(sstr + 1);
            var tmp = SafeName(name);
            return tmp != "base" ? tmp : "_base";
        }

        internal static bool HasSize(this IField field)
        {
            return field.Attributes.Find(a => a.Name.Equals("IgnoreAttribute")) is null;
        }

        // Mostly for unobtrusive break lines
        internal static void Noop() { }

        internal static TypeDefinition? ResolvedBaseType(this TypeDefinition self)
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
            var instance = (GenericInstanceType)self;
            if (instance.GenericArguments.Count != templateType.GenericParameters.Count)
                // Mismatch of generic parameters. Presumably, resolved has some inherited generic parameters that it is not listing, although this should not happen.
                // Since !0 and !1 will occur in resolved.GenericParameters instead.
                throw new InvalidOperationException("instance.GenericArguments is either null or of a mismatching count compared to resolved.GenericParameters!");
            for (int i = 0; i < templateType.GenericParameters.Count; i++)
                // Map from resolved generic parameter to self generic parameter
                map.Add(templateType.GenericParameters[i].Name, instance.GenericArguments[i]);
            return map;
        }

        private class QuickComparer : IEqualityComparer<TypeReference>
        {
            public bool Equals(TypeReference r1, TypeReference r2) => r1?.FullName == r2?.FullName;

            public int GetHashCode(TypeReference r) => r?.FullName.GetHashCode() ?? 0;
        };

        private static readonly QuickComparer quickCompare = new QuickComparer();

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
                    var ret = genericMapping.GetValueOrDefault(m.ReturnType.Name, m.ReturnType);
                    // Only if ret == self.ReturnType, can we have a match
                    if (!quickCompare.Equals(ret, self.ReturnType))
                        continue;
                    if (m.Parameters.Count != self.Parameters.Count)
                        continue;

                    var mParams = m.Parameters.Select(
                        p => genericMapping.GetValueOrDefault(p.ParameterType.Name, p.ParameterType));
                    if (mParams.SequenceEqual(self.Parameters.Select(p => p.ParameterType), quickCompare))
                        matches.Add(m);
                }
            }

            var bType = type.ResolvedBaseType();
            if (bType != null)
                matches.UnionWith(self.FindIn(bType, type.GetGenerics(bType)));
            foreach (var @interface in type.Interfaces)
            {
                var resolved = @interface.InterfaceType.Resolve();
                matches.UnionWith(self.FindIn(resolved, @interface.InterfaceType.GetGenerics(resolved)));
            }
            return matches;
        }

        private static TypeReference? FindInterface(TypeReference? type, string find)
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

        internal static MethodDefinition? GetSpecialNameBaseMethod(this MethodDefinition self, out TypeReference? iface, int idxDot = -1)
        {
            iface = null;
            if (idxDot == -1)
                idxDot = self.Name.LastIndexOf('.');
            if (idxDot < 2)
                return null;
            var typeStr = self.Name.Substring(0, idxDot);
            iface = FindInterface(self.DeclaringType, typeStr);
            if (iface is null)
                return null;
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
            if (self is null) throw new ArgumentNullException(nameof(self));
            // Whenever we call GetBaseMethods, we should explicitly exclude all base methods that are specifically defined by self.DeclaringType via special names already.
            // We would ideally do this by compiling a list of special named methods, and for each of those, explicitly excluding them from our matches.
            // However, this means that we need to be able to convert a special name to a base method, which means we need an extension method for it here.
            // TODO: This list should be generated once and then cached.
            var specialBaseMethods = self.DeclaringType.Methods.Select(m => m.GetSpecialNameBaseMethod(out var iface)).Where(md => md != null);
            var matches = self.FindIn(self.DeclaringType, self.DeclaringType.GetGenerics(self.DeclaringType.Resolve()));
            foreach (var sbm in specialBaseMethods)
                matches.Remove(sbm!);
            return matches;
        }
    }
}