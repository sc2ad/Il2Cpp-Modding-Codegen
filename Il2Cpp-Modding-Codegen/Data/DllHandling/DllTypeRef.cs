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
            get { return This.GenericParameters.Select(t => DllTypeRef.From(t)); }
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

        public string ResolvePrimitive(CppSerializerContext context, ForceAsType force)
        {
            var name = Name.ToLower();
            if (name == "void")
                return "void";
            else if (name == "void*")
                return "void*";

            string s = null;
            if (name == "object")
                s = "Il2CppObject";
            else if (name == "string")
                s = "Il2CppString";
            else if (name == "int")
                s = "int";
            else if (name == "single")
                s = "float";
            else if (name == "double")
                s = "double";
            else if (name == "uint")
                s = "uint";
            else if (name == "char")
                s = "uint16_t";
            else if (name == "byte")
                s = "int8_t";
            else if (name == "sbyte")
                s = "uint8_t";
            else if (name == "bool")
                s = "bool";
            else if (name == "short")
                s = "int16_t";
            else if (name == "ushort")
                s = "uint16_t";
            else if (name == "long")
                s = "int64_t";
            else if (name == "ulong")
                s = "uint64_t";
            else if (This.IsArray)
            {
                // Array
                // TODO: Make this use Array<ElementType> instead of Il2CppArray
                s = $"Array<{context.GetNameFromReference(ElementType, ForceAsType.None, true, true)}>";
            }
            switch (force)
            {
                case ForceAsType.Pointer:
                    return s != null ? s + "*" : null;

                case ForceAsType.Reference:
                    return s != null ? s + "&" : null;

                case ForceAsType.Literal:
                    // Special cases for Il2Cpp types, need to forward declare/include typedefs.h iff force valuetype
                    if (s != null && (s.StartsWith("Il2Cpp") || s.StartsWith("Array<")))
                    {
                        context.Includes.Add("utils/typedefs.h");
                        return s;
                    }
                    return s;

                default:
                    // Pointer type for Il2Cpp types on default
                    if (s != null && (s.StartsWith("Il2Cpp") || s.StartsWith("Array<")))
                    {
                        context.ForwardDeclares.Add(new TypeName("", s));
                        return s + "*";
                    }
                    return s;
            }
        }
    }
}