using Il2Cpp_Modding_Codegen.Serialization;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    public class DllTypeRef : TypeRef
    {
        private TypeReference This;
        public override string Namespace {
            get { return This.Namespace; }
        }
        public override string Name {
            get { return This.Name; }
        }
        public override bool Generic {
            get { return This.IsGenericInstance; }
        }

        public override IEnumerable<TypeRef> GenericParameters {
            get { return This.GenericParameters.Select(DllTypeRef.From); }
        }

        public override TypeRef DeclaringType {
            get { return From(This.DeclaringType); }
        }
        public override TypeRef ElementType {
            get { return From(This.GetElementType()); }
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
    }
}