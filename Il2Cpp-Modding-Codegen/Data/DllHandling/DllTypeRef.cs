using Mono.Cecil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    public class DllTypeRef : TypeRef
    {
        internal TypeReference This;

        private readonly string _namespace;
        public override string Namespace { get => _namespace; }
        private readonly string _name;
        public override string Name { get => _name; }

        public override bool IsGenericParameter { get => This.IsGenericParameter; }
        public override bool IsCovariant { get; }
        public override IReadOnlyList<TypeRef> GenericParameterConstraints { get; } = new List<TypeRef>();
        public override bool IsGenericInstance { get => This.IsGenericInstance; }
        public override bool IsGenericTemplate { get => This.HasGenericParameters; }

        public override IReadOnlyList<TypeRef> Generics { get; }

        public override TypeRef? DeclaringType { get => From(This.DeclaringType); }
        public override TypeRef? ElementType
        {
            get
            {
                if (!(This is TypeSpecification typeSpec)) return null;
                if (typeSpec.MetadataType == MetadataType.GenericInstance) return null;
                return From(typeSpec.ElementType);
            }
        }

        public override bool IsVoid() => This.MetadataType == MetadataType.Void;
        public override bool IsPointer() => This.IsPointer;
        public override bool IsArray() => This.IsArray;

        public override TypeRef MakePointer() => From(This.MakePointerType());

        private static readonly Dictionary<TypeReference, DllTypeRef> cache = new Dictionary<TypeReference, DllTypeRef>();

        internal static int Hits { get; private set; } = 0;
        internal static int Misses { get; private set; } = 0;

        // Should use DllTypeRef.From instead!
        private DllTypeRef(TypeReference reference)
        {
            cache.Add(reference, this);
            This = reference;

            if (This.IsByReference)
            {
                // TODO: Set as ByReference? For method params, the ref keyword is handled by Parameter.cs
                This = ((ByReferenceType)This).ElementType;
            }
            _name = This.Name;

            if (IsGenericInstance)
                Generics = ((GenericInstanceType)This).GenericArguments.Select(g => From(g)).ToList();
            else if (IsGenericTemplate)
                Generics = This.GenericParameters.Select(g => From(g)).ToList();
            else
                Generics = new List<TypeRef>();

            if (IsGeneric && Generics.Count == 0)
                throw new InvalidDataException($"Wtf? In DllTypeRef constructor, a generic with no generics: {this}, IsGenInst: {this.IsGenericInstance}");

            DllTypeRef? refDeclaring = null;
            if (!This.IsGenericParameter && This.IsNested)
                refDeclaring = From(This.DeclaringType);

            // Remove *, [] from end of variable name
            _name = Regex.Replace(_name, @"\W+$", "");

            _namespace = (refDeclaring?.Namespace ?? This.Namespace) ?? "";

            if (IsGenericParameter && (This is GenericParameter genParam))
            {
                IsCovariant = genParam.IsCovariant;
                if (genParam.HasConstraints)
                {
                    GenericParameterConstraints = genParam.Constraints.Select(c => From(c.ConstraintType)).ToList();
                    if (genParam.Constraints.Any(c => c.HasCustomAttributes))
                    {
                        var declaring = genParam.DeclaringType is null ? $"method {genParam.DeclaringMethod}" : $"{genParam.DeclaringType}";
                        Console.WriteLine($"Constraints on {genParam} (in {declaring}): ");
                        Console.WriteLine(string.Join(", ",
                            genParam.Constraints.Select(c => $"({c.ConstraintType}, {{{string.Join(", ", c.CustomAttributes)}}})")));
                    }
                }
            }
        }

        [return: NotNullIfNotNull("type")]
        internal static DllTypeRef? From(TypeReference? type)
        {
            if (type is null) return null;
            if (cache.TryGetValue(type, out var value))
            {
                Hits++;
                return value;
            }
            Misses++;

            // Creates new TypeRef and add it to map
            value = new DllTypeRef(type);
            return value;
        }

        // For better comments
        public override string ToString() => This.ToString();
    }
}
