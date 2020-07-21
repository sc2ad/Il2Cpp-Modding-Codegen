using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppModdingCodegen.Data
{
    public abstract class TypeRef : IEquatable<TypeRef>
    {
        private const string NoNamespace = "GlobalNamespace";

        public abstract string Namespace { get; }
        public abstract string Name { get; }

        public abstract bool IsGenericParameter { get; }
        public abstract bool IsCovariant { get; set; }
        internal bool IsGeneric { get => IsGenericInstance || IsGenericTemplate; }
        public abstract bool IsGenericInstance { get; }
        public abstract bool IsGenericTemplate { get; }
        public abstract IReadOnlyList<TypeRef> Generics { get; }

        public abstract TypeRef DeclaringType { get; }
        public abstract TypeRef? ElementType { get; }

        private ITypeData _resolvedType;

        public abstract TypeRef MakePointer();

        /// <summary>
        /// Resolves the type from the given type collection
        /// </summary>
        internal ITypeData Resolve(ITypeCollection types)
        {
#pragma warning disable 612, 618
            // TODO: if we upgrade to C# 8.0, change this to `_resolvedType ??= types.Resolve(this);`
            if (_resolvedType == null)
                _resolvedType = types.Resolve(this);
#pragma warning restore 612, 618
            return _resolvedType;
        }

        public virtual bool IsVoid() => Name.Equals("void", StringComparison.OrdinalIgnoreCase);

        public virtual bool IsPointer()
        {
            // If type is not a value type, it is a pointer
            return _resolvedType?.Info.Refness == Refness.ReferenceType;
        }

        public abstract bool IsArray();

        public string GetNamespace() => string.IsNullOrEmpty(Namespace) ? NoNamespace : Namespace.Replace(".", "::");

        public string GetName()
        {
            if (Name.StartsWith("!"))
                throw new InvalidOperationException("Tried to get the name of a copied generic parameter!");
            return Name.Replace('`', '_').Replace('<', '$').Replace('>', '$');
        }

        public string GetQualifiedName()
        {
            var name = GetName();
            var dt = this;
            while (dt.DeclaringType != null)
            {
                name = dt.DeclaringType.GetName() + "::" + name;
                dt = dt.DeclaringType;
            }
            // Namespace obtained from final declaring type
            return dt.GetNamespace() + "::" + name;
        }

        public (string, string) GetIl2CppName()
        {
            var name = Name;
            var dt = this;
            while (dt.DeclaringType != null)
            {
                name = dt.DeclaringType.Name + "/" + name;
                dt = dt.DeclaringType;
            }
            // Namespace obtained from final declaring type
            return (dt.Namespace.Replace("::", "."), name);
        }

        // TODO: new method/param to easily allow for getting only the new generic templates that this TypeRef brings to the table?
        public IEnumerable<TypeRef> GetDeclaredGenerics(bool includeSelf)
        {
            var genericsDefined = new List<TypeRef>();
            // Populate genericsDefined with all TypeRefs that are used in a declaring type
            var dt = includeSelf ? this : DeclaringType;
            var lastGenerics = !includeSelf ? Generics : null;
            while (dt != null)
            {
                if (dt.IsGeneric)
                {
                    foreach (var g in dt.Generics.Reverse())
                    {
                        if (IsGenericInstance && g.Name.StartsWith("!"))
                        {
                            // If we are a generic instance, and we see that the name of our generic parameter starts with a !
                            var idx = int.Parse(g.Name.Substring(1));
                            genericsDefined.Insert(0, lastGenerics[idx]);
                            // Replace g with our lastGenerics[i]
                        }
                        else
                            // We want the highest level declaring type's first generic parameter (template or argument) to be first in our genericsDefined list
                            genericsDefined.Insert(0, g);
                    }
                    lastGenerics = dt.Generics;
                }
                dt = dt.DeclaringType;
            }
            // Return only the first occurance of each of the generic parameters (template or argument)
            // Do not compare the generic types' declaring types (use the fastCompararer)
            return genericsDefined.Distinct(fastComparer);
        }

        internal bool ContainsOrEquals(TypeRef offendingType)
        {
            if (Equals(offendingType)) return true;
            if (ElementType != null && ElementType.ContainsOrEquals(offendingType)) return true;
            if (DeclaringType != null && DeclaringType.ContainsOrEquals(offendingType)) return true;
            return false;
        }

        /// <summary>
        /// Returns a mapping of <see cref="TypeRef"/> to generics explicitly defined by that <see cref="TypeRef"/>
        /// </summary>
        /// <param name="includeSelf"></param>
        /// <returns></returns>
        public Dictionary<TypeRef, List<TypeRef>> GetGenericMap(bool includeSelf)
        {
            // Use fastComparers here to avoid checking DeclaringType (and to be fast)
            var genericMap = new Dictionary<TypeRef, List<TypeRef>>(fastComparer);
            var genericParamToDeclaring = new Dictionary<TypeRef, TypeRef>(fastComparer);
            var dt = includeSelf ? this : DeclaringType;
            while (dt != null)
            {
                if (dt.IsGeneric)
                    foreach (var g in dt.Generics)
                        // Overwrite existing declaring type and add it to genericParamToDeclaring
                        genericParamToDeclaring[g] = dt;
                dt = dt.DeclaringType;
            }
            // Iterate over each generic param to declaring type and convert it to a mapping of declaring type to generic parameters
            foreach (var pair in genericParamToDeclaring)
            {
                if (genericMap.TryGetValue(pair.Value, out var lst))
                    lst.Add(pair.Key);
                else
                    genericMap.Add(pair.Value, new List<TypeRef> { pair.Key });
            }
            return genericMap;
        }

        internal string GetIncludeLocation()
        {
            var fileName = string.Join("-", GetName().Split(Path.GetInvalidFileNameChars())).Replace('$', '-');
            if (DeclaringType != null)
                return DeclaringType.GetIncludeLocation() + "_" + fileName;
            // Splits multiple namespaces into nested directories
            var directory = string.Join("-", string.Join("/", GetNamespace().Split(new string[] { "::" }, StringSplitOptions.RemoveEmptyEntries)).Split(Path.GetInvalidPathChars()));
            return directory + "/" + fileName;
        }

        public override string ToString()
        {
            var ret = GetNamespace() + "::" + GetName();
            if (IsGeneric)
                ret += "<" + string.Join(", ", Generics) + ">";
            return ret;
        }

        [ObsoleteAttribute("The argument should be a TypeRef!")]
#pragma warning disable 809  // "obsolete method extends non-obsolete mehtod object.Equals(object)
        public override bool Equals(object obj) => Equals(obj as TypeRef);
#pragma warning restore 809

        internal static FastTypeRefComparer fastComparer = new FastTypeRefComparer();

        public bool Equals(TypeRef other)
        {
            return fastComparer.Equals(this, other) &&
                (DeclaringType?.Equals(other?.DeclaringType) ?? other?.DeclaringType == null) &&
                (ElementType?.Equals(other?.ElementType) ?? other?.ElementType == null);
        }

        public override int GetHashCode()
        {
            int hashCode = fastComparer.GetHashCode(this);
            hashCode = hashCode * -1521134295 + DeclaringType?.GetHashCode() ?? 0;
            hashCode = hashCode * -1521134295 + ElementType?.GetHashCode() ?? 0;
            return hashCode;
        }

        internal static bool SetsEqualOrPrint(IEnumerable<TypeRef> a, IEnumerable<TypeRef> b, bool fast = false)
        {
            bool equal = (fast ? a.Intersect(b, fastComparer) : a.Intersect(b)).Count() == a.Count();
            if (!equal)
            {
                Console.WriteLine($"Sets: {{{String.Join(", ", a)}}} == {{{String.Join(", ", b)}}}? {equal}");
                if (fast)
                {
                    var aSet = new HashSet<TypeRef>(a, fastComparer);
                    var bSet = new HashSet<TypeRef>(b, fastComparer);
                    Console.Error.WriteLine($"in a but not in b: {{{String.Join(", ", aSet.Except(bSet, TypeRef.fastComparer))}}}");
                    Console.Error.WriteLine($"in b but not in a: {{{String.Join(", ", bSet.Except(aSet, TypeRef.fastComparer))}}}");
                }
                else
                {
                    var aSet = new HashSet<TypeRef>(a);
                    var bSet = new HashSet<TypeRef>(b);
                    Console.Error.WriteLine($"in a but not in b: {{{String.Join(", ", aSet.Except(bSet))}}}");
                    Console.Error.WriteLine($"in b but not in a: {{{String.Join(", ", bSet.Except(aSet))}}}");
                }
            }
            return equal;
        }

        internal static bool SequenceEqualOrPrint(IEnumerable<TypeRef> a, IEnumerable<TypeRef> b, bool fast = false)
        {
            bool equal = fast ? a.SequenceEqual(b, fastComparer) : (a?.SequenceEqual(b) ?? b is null);
            if (!equal)
            {
                Console.WriteLine($"Generics: {{{String.Join(", ", a)}}} == {{{String.Join(", ", b)}}}? {equal}");
                if (a is null) return equal;
                var aList = a.ToList();
                var bList = b.ToList();
                if (aList.Count == bList.Count)
                    for (int i = 0; i < aList.Count; i++)
                    {
                        var one = aList[i];
                        var two = bList[i];
                        PrintEqual(one, two, fast);
                    }
            }
            return equal;
        }

        internal static bool PrintEqual(TypeRef a, TypeRef b, bool fast = false)
        {
            bool equal = fast ? fastComparer.Equals(a, b) : (a?.Equals(b) ?? b is null);
            if (!equal)
            {
                Console.WriteLine($"{a} == {b}? {equal}");
                if (a is null) return equal;
                equal = a.Namespace?.Equals(b.Namespace) ?? (b.Namespace == null);
                if (!equal)
                {
                    Console.WriteLine($"Namespace: {a.Namespace} != {b.Namespace}");
                    return equal;
                }
                equal = a.Name.Equals(b.Name);
                if (!equal)
                {
                    Console.WriteLine($"Name: {a.Name} != {b.Name}");
                    return equal;
                }
                equal = a.IsGenericInstance == b.IsGenericInstance;
                if (!equal)
                {
                    Console.WriteLine($"IsGenericInstance: {a.IsGenericInstance} != {b.IsGenericInstance}");
                    return equal;
                }
                equal = a.IsGenericTemplate == b.IsGenericTemplate;
                if (!equal)
                {
                    Console.WriteLine($"IsGenericTemplate: {a.IsGenericTemplate} != {b.IsGenericTemplate}");
                    return equal;
                }
                equal = SequenceEqualOrPrint(a.Generics, b.Generics, fast);
                if (!equal) return equal;
                if (!fast)
                {
                    Console.WriteLine($"DeclaringType: ");
                    equal = PrintEqual(a.DeclaringType, b.DeclaringType, fast);
                    if (!equal) return equal;
                    Console.WriteLine($"ElementType: ");
                    equal = PrintEqual(a.ElementType, b.ElementType, fast);
                }
            }
            return equal;
        }
    }
}
