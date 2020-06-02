using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    public class DllTypeRef : TypeRef
    {
        public override string Namespace { get; protected set; }
        public override string Name { get; protected set; }
        public override bool Generic { get; protected set; }

        public override List<TypeRef> GenericParameters { get; } = new List<TypeRef>();

        public override TypeRef DeclaringType { get; protected set; }
        public override TypeRef ElementType { get; protected set; }

        private static readonly Dictionary<TypeReference, DllTypeRef> cache = new Dictionary<TypeReference, DllTypeRef>();

        // Should ONLY have contents during TypeRef functions!
        private static readonly Stack<DllTypeRef> toPopulate = new Stack<DllTypeRef>();

        public static int hits = 0;
        public static int misses = 0;

        // Should use DllTypeRef.From instead!
        private DllTypeRef()
        {
        }

        private static DllTypeRef GenericT(TypeReference type)
        {
            return new DllTypeRef
            {
                // Hopefully this doesn't cause stuff to recurse forever...
                DeclaringType = FromInternal(type.DeclaringType),
                // The generic parameter itself isn't generic
                Generic = false,
                Namespace = string.Empty,
                Name = type.Name
            };
        }

        private static DllTypeRef FromInternal(TypeReference type)
        {
            if (cache.TryGetValue(type, out var value))
            {
                hits++;
                return value;
            }
            misses++;

            // Creates TypeRef to be populated later
            value = new DllTypeRef();
            // Ensures the placeholder TypeRef will be resolved as THE TypeRef for this TypeReference
            cache.Add(type, value);
            // Queues the TypeRef for population as the new First
            toPopulate.Push(value);
            return value;
        }

        public static DllTypeRef From(TypeReference type)
        {
            // Initiates and queues only the requested TypeRef
            var ret = FromInternal(type);
            // We must populate ALL un-populated TypeRefs before leaving TypeRef execution!
            while (toPopulate.Count > 0)
            {
                var t = toPopulate.Pop();
                // Populates t and queues any uncached TypeRefs among its fields
                t.SetFieldsFromTypeReference(type);
            }
            return ret;
        }

        private void SetFieldsFromTypeReference(TypeReference reference)
        {
            Namespace = reference.Namespace;
            Name = reference.Name;
            Generic = reference.IsGenericInstance;
            if (Generic)
                GenericParameters.AddRange(reference.GenericParameters.Select(gp => FromInternal(gp)));
            else if (reference.HasGenericParameters)
                GenericParameters.AddRange(reference.GenericParameters.Select(gp => GenericT(gp)));
            if (reference.DeclaringType != null && !reference.DeclaringType.Equals(reference))
                DeclaringType = FromInternal(reference.DeclaringType);
            var etype = reference.GetElementType();
            if (etype != null && !etype.Equals(reference))
                ElementType = FromInternal(etype);
        }
    }
}