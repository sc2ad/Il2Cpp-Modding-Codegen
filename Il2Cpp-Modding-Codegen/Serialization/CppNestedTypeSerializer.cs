using Il2CppModdingCodegen.CppSerialization;
using Il2CppModdingCodegen.Data.DllHandling;
using Il2CppModdingCodegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppNestedTypeSerializer : ISerializer<TypeDefinition, CppTypeWriter>
    {
        private readonly IEnumerable<ISerializer<DllField, CppTypeWriter>> fieldSerializers;
        private readonly IEnumerable<ISerializer<DllMethod, CppTypeWriter>> methodSerializers;
        private readonly IEnumerable<ISerializer<InterfaceImplementation, CppTypeWriter>> interfaceSerializers;
        private readonly IEnumerable<ISerializer<TypeDefinition, CppTypeWriter>> nestedSerializers;

        public CppNestedTypeSerializer(IEnumerable<ISerializer<DllField, CppTypeWriter>> fs,
            IEnumerable<ISerializer<DllMethod, CppTypeWriter>> ms,
            IEnumerable<ISerializer<InterfaceImplementation, CppTypeWriter>> @is,
            IEnumerable<ISerializer<TypeDefinition, CppTypeWriter>> ns)
        {
            fieldSerializers = fs;
            methodSerializers = ms;
            interfaceSerializers = @is;
            nestedSerializers = ns;
        }

        public void Resolve(CppContext context, TypeDefinition t)
        {
            // For now, just always add the type as a definition to our declaring context
            //context.AddNestedDefinition(t);
        }

        public void Write(CppTypeWriter writer, TypeDefinition t)
        {
            // Do nothing for now
            writer.WriteComment($"Nested type: {t.FullName}");
        }
    }
}