using Il2CppModdingCodegen.CppSerialization;
using Il2CppModdingCodegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppNestedHeaderContext : CppContext, IHeaderContext
    {
        private readonly IEnumerable<ISerializer<TypeDefinition, CppStreamWriter>> serializers;

        public string HeaderFileName => (RootContext as IHeaderContext)!.HeaderFileName;

        public HashSet<IHeaderContext> Includes => (RootContext as IHeaderContext)!.Includes;

        public CppNestedHeaderContext(TypeDefinition t, SizeTracker sz, CppContext declaring, IEnumerable<ISerializer<TypeDefinition, CppStreamWriter>> sers) : base(t, sz, declaring)
        {
            if (t.DeclaringType is null)
                throw new ArgumentException($"{t} must be a nested type!");
            if (declaring is not IHeaderContext)
                throw new ArgumentException($"Must be {nameof(IHeaderContext)}", nameof(declaring));
            serializers = sers;
        }

        public override void NeedIl2CppUtils()
        {
            DeclaringContext!.NeedIl2CppUtils();
        }

        internal override void Resolve(HashSet<CppContext> resolved)
        {
            // If we are InPlace, then we can easily just add ourselves as a definition
            // If we are UnNested, then we need to force ourselves to be resolved later.
            resolved.AddOrThrow(this);
        }

        public void Write(CppTypeWriter writer)
        {
            // Here we would write out ourselves within our type.
            // This is kinda weird logically since it's contexts --> types --> contexts --> types
        }
    }
}