using Il2CppModdingCodegen.CppSerialization;
using Il2CppModdingCodegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;
using static Il2CppModdingCodegen.CppSerialization.CppStreamWriter;

namespace Il2CppModdingCodegen.Serialization
{
    internal class CppTypeSerializer : ISerializer<TypeDefinition>
    {
        private readonly IEnumerable<ISerializer<FieldDefinition>> fieldSerializers;
        private readonly IEnumerable<ISerializer<MethodDefinition>> methodSerializers;
        private readonly IEnumerable<ISerializer<InterfaceImplementation>> interfaceSerializers;
        private readonly IEnumerable<ISerializer<TypeDefinition>> nestedSerializers;

        public CppTypeSerializer(IEnumerable<ISerializer<FieldDefinition>> fs,
            IEnumerable<ISerializer<MethodDefinition>> ms,
            IEnumerable<ISerializer<InterfaceImplementation>> @is,
            IEnumerable<ISerializer<TypeDefinition>> ns)
        {
            fieldSerializers = fs;
            methodSerializers = ms;
            interfaceSerializers = @is;
            nestedSerializers = ns;
        }

        public void Resolve(TypeDefinition t)
        {
            foreach (var i in t.Interfaces)
            {
                foreach (var s in interfaceSerializers)
                {
                    s.Resolve(i);
                }
            }
            foreach (var n in t.NestedTypes)
            {
                foreach (var s in nestedSerializers)
                {
                    s.Resolve(n);
                }
            }
            foreach (var f in t.Fields)
            {
                foreach (var s in fieldSerializers)
                {
                    s.Resolve(f);
                }
            }
            foreach (var m in t.Methods)
            {
                foreach (var s in methodSerializers)
                {
                    s.Resolve(m);
                }
            }
        }

        public void Write(CppNamespaceWriter writer, TypeDefinition t, bool header = false)
        {
            foreach (var m in t.Methods)
            {
                writer.OpenMethod()
            }
        }
    }
}