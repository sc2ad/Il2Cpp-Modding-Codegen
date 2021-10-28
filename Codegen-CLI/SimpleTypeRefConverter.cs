using Il2CppModdingCodegen.Data;
using Mono.Cecil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class SimpleTypeRefConverter : JsonConverter<TypeReference>
    {
        private readonly Collection<TypeDefinition> types;

        public SimpleTypeRefConverter(Collection<TypeDefinition> types)
        {
            this.types = types;
        }

        public override TypeReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, TypeReference value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            // Write simple here, only relevant details and a TID
            writer.WriteString(nameof(value.Namespace), value.Namespace);
            writer.WriteString(nameof(value.Name), value.Name);
            writer.WriteBoolean(nameof(value.IsGenericParameter), value.IsGenericParameter);
            writer.WriteBoolean(nameof(value.IsArray), value.IsArray);
            writer.WriteBoolean(nameof(value.IsPointer), value.IsPointer);
            var genericsTypedef = value;
            if (value.GetElementType() != null)
            {
                // If we have an element type, skip ourselves and go to our elements directly.
                genericsTypedef = value.GetElementType();
            }
            while (genericsTypedef.GetElementType() != null)
            {
                // Continue checking our element types until we have extinguished all of our element types, then write those generics.
                genericsTypedef = genericsTypedef.GetElementType();
            }
            writer.WritePropertyName(nameof(value.GenericParameters));
            writer.WriteStartArray();
            foreach (var gp in genericsTypedef.GenericParameters)
            {
                Write(writer, gp, options);
            }
            writer.WriteEndArray();
            //// Write constraints of our bottom type
            //writer.WritePropertyName(nameof(value.GenericParameterConstraints));
            //writer.WriteStartArray();
            //foreach (var constraint in genericsTypedef.GenericParameterConstraints)
            //{
            //    Write(writer, constraint, options);
            //}
            //writer.WriteEndArray();
            var ind = types.IndexOf(genericsTypedef.Resolve());
            bool genericParam = value.IsGenericParameter;
            while (ind < 0 && !genericParam)
            {
                if (value.GetElementType() != null)
                {
                    genericParam = value.GetElementType().IsGenericParameter;
                    if (genericParam)
                        break;
                    ind = types.IndexOf(value.GetElementType().Resolve());
                    value = value.GetElementType();
                }
            }
            if (ind < 0 && !genericParam)
            {
                // If index is STILL -1 (and we aren't a generic param)
                // We couldn't find it even after searching our element types for as long as we could!
                throw new InvalidOperationException("TypeRef could not be found in types! Is this a generic parameter/array/pointer?");
            }
            if (genericParam)
                ind--; // VERY IMPORTANT DETAIL
            writer.WriteNumber("TypeId", ind);
            writer.WriteEndObject();
        }
    }
}