using Il2CppModdingCodegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppTypeSourceContext : CppContext
    {
        private readonly IEnumerable<ISerializer<TypeDefinition>> serializers;

        public CppTypeSourceContext(TypeDefinition def, CppTypeHeaderContext header, IEnumerable<ISerializer<TypeDefinition>> serializers)
        {
            this.serializers = serializers;
            Type = def;
            Header = header;
        }

        public CppTypeHeaderContext Header { get; }

        public void Resolve()
        {
            // We want to have something where we resolve all of the things we need
            // This means we need to FD and include everything that isn't already FD'd or included
            // Ultimately, the decision to FD or include something new is determined by the serialization approach
            // As such, we will need to ask all of our serializers how we want to handle this.

            foreach (var ser in serializers)
            {
                ser.Resolve(Type);
            }
        }
    }
}