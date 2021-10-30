using Il2CppModdingCodegen.CppSerialization;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppNestedHeaderContext : CppContext
    {
        public CppNestedHeaderContext(TypeDefinition t, SizeTracker sz, CppContext declaring) : base(t, sz, declaring)
        {
            if (t.DeclaringType is null)
                throw new ArgumentException($"{t} must be a nested type!");
        }

        public override void NeedIl2CppUtils()
        {
            DeclaringContext!.NeedIl2CppUtils();
        }

        internal void Resolve(HashSet<CppContext> resolved)
        {
        }

        public void Write(CppTypeWriter writer)
        {
            // Here we would write out ourselves within our type.
            // This is kinda weird logically since it's contexts --> types --> contexts --> types
        }
    }
}