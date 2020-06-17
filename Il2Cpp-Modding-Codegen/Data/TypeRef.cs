using Mono.Cecil;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace Il2Cpp_Modding_Codegen.Data
{
    public abstract class TypeRef : IEquatable<TypeRef>
    {
        private const string NoNamespace = "GlobalNamespace";
        public abstract string Namespace { get; }
        public abstract string Name { get; }

        public bool IsGeneric { get => IsGenericInstance || IsGenericTemplate; }
        public abstract bool IsGenericInstance { get; }
        public abstract bool IsGenericTemplate { get; }
        public abstract IReadOnlyList<TypeRef> Generics { get; }
        public abstract TypeRef DeclaringType { get; }
        public abstract TypeRef ElementType { get; }

        private ITypeData _resolvedType;

        /// <summary>
        /// Resolves the type from the given type collection
        /// </summary>
        internal ITypeData Resolve(ITypeCollection context)
        {
            if (_resolvedType == null)
            {
                _resolvedType = context.Resolve(this);
            }
            return _resolvedType;
        }

        public virtual bool IsVoid()
        {
            return Name.Equals("void", StringComparison.OrdinalIgnoreCase);
        }

        public virtual bool IsPointer()
        {
            // If type is not a value type, it is a pointer
            return _resolvedType?.Info.TypeFlags == TypeFlags.ReferenceType;
        }

        public abstract bool IsPrimitive();

        public abstract bool IsArray();

        public string GetNamespace() => !string.IsNullOrEmpty(Namespace) ? Namespace.Replace(".", "::") : NoNamespace;

        public string GetName() => Name.Replace('`', '_').Replace('<', '$').Replace('>', '$');

        internal string GetIncludeLocation()
        {
            var fileName = string.Join("-", GetName().Split(Path.GetInvalidFileNameChars()));
            // Splits multiple namespaces into nested directories
            var directory = string.Join("-", string.Join("/", GetNamespace().Split(new string[] { "::" }, StringSplitOptions.None)).Split(Path.GetInvalidPathChars()));
            return directory + "/" + fileName;
        }

        public override string ToString()
        {
            return GetNamespace() + "::" + GetName();
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TypeRef);
        }

        internal static FastTypeRefComparer fastComparer = new FastTypeRefComparer();

        public bool Equals(TypeRef other)
        {
            return fastComparer.Equals(this, other) &&
                (DeclaringType?.Equals(other.DeclaringType) ?? other.DeclaringType == null) &&
                (ElementType?.Equals(other.ElementType) ?? other.ElementType == null);
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