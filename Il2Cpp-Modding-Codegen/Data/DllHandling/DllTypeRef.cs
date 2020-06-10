using Il2Cpp_Modding_Codegen.Serialization;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    public class DllTypeRef : TypeRef
    {
        private TypeReference This;
        readonly string _namespace;
        public override string Namespace {
            get { return _namespace; }
        }
        readonly string _name;
        public override string Name {
            get { return _name; }
        }
        public override bool Generic {
            get { return This.IsGenericInstance || This.HasGenericParameters; }
        }

        public override IEnumerable<TypeRef> GenericParameters { get; } = new List<TypeRef>();
        public override IEnumerable<TypeRef> GenericArguments { get; } = null;

        public override TypeRef DeclaringType {
            get { return From(This.DeclaringType); }
        }
        public override TypeRef ElementType {
            get { return From((This as TypeSpecification)?.ElementType); }
        }

        public override bool IsPointer(ITypeContext context)
        {
            return This.IsPointer;
        }

        public override bool IsArray()
        {
            return This.IsArray;
        }

        private static readonly Dictionary<TypeReference, DllTypeRef> cache = new Dictionary<TypeReference, DllTypeRef>();

        public static int hits = 0;
        public static int misses = 0;

        // Should use DllTypeRef.From instead!
        private DllTypeRef(TypeReference reference)
        {
            This = reference;

            if (This.IsByReference)
            {
                // TODO: Set as ByReference? For method params, the ref keyword is handled by Parameter.cs
                This = (This as ByReferenceType).ElementType;
            }
            _name = This.Name;
            //if (!This.IsGenericParameter && !(This.DeclaringType is null))
            //    _name = DllTypeRef.From(This.DeclaringType).Name + "/" + _name;

            // Remove *, [] from end of variable name
            _name = Regex.Replace(_name, @"\W+$", "");
            // if (!char.IsLetterOrDigit(_name.Last())) Console.WriteLine(reference);

            _namespace = (This.DeclaringType is null) ? (This.Namespace ?? "") : null;

            if (This.IsGenericInstance)
                GenericArguments = (This as GenericInstanceType).GenericArguments.Select(DllTypeRef.From).ToList();
            if (This.HasGenericParameters)
                GenericParameters = This.GenericParameters.Select(DllTypeRef.From).ToList();
        }

        public static DllTypeRef From(TypeReference type)
        {
            if (type is null) return null;
            if (cache.TryGetValue(type, out var value))
            {
                hits++;
                return value;
            }
            misses++;

            // Creates new TypeRef and add it to map
            value = new DllTypeRef(type);
            cache.Add(type, value);
            return value;
        }

        // For better comments
        public override string ToString()
        {
            return This.ToString();
        }
    }
}