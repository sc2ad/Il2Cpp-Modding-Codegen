using Il2CppModdingCodegen.CppSerialization;
using Il2CppModdingCodegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppTypeSourceContext : CppContext
    {
        private readonly IEnumerable<ISerializer<TypeDefinition, CppStreamWriter>> serializers;

        public CppTypeSourceContext(TypeDefinition def, CppTypeHeaderContext header, IEnumerable<ISerializer<TypeDefinition, CppStreamWriter>> serializers) : base(def)
        {
            this.serializers = serializers;
            Header = header;
        }

        public CppTypeHeaderContext Header { get; }

        public override void NeedIl2CppUtils()
        {
            ExplicitIncludes.Add("beatsaber-hook/shared/utils/il2cpp-utils.hpp");
        }

        public void Resolve()
        {
            // We want to have something where we resolve all of the things we need
            // This means we need to FD and include everything that isn't already FD'd or included
            // Ultimately, the decision to FD or include something new is determined by the serialization approach
            // As such, we will need to ask all of our serializers how we want to handle this.

            foreach (var ser in serializers)
            {
                ser.Resolve(this, Type);
            }
        }
    }
}