using Il2CppModdingCodegen;
using Il2CppModdingCodegen.Data;
using Mono.Cecil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    /// <summary>
    /// CAN ONLY BE CALLED AFTER TYPE REGISTRATION HAS TAKEN PLACE!
    /// </summary>
    internal class TypeDefinitionConverter : JsonConverter<TypeDefinition>
    {
        public override TypeDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        private readonly JsonConverter<TypeReference> simpleConv;
        private readonly SizeTracker sz;

        private static int typeDefIndex = 0;

        public TypeDefinitionConverter(SizeTracker sz, JsonConverter<TypeReference> simpleConv)
        {
            this.sz = sz;
            this.simpleConv = simpleConv;
        }

        private void WriteThis(Utf8JsonWriter writer, TypeReference value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            // Write simple here, only relevant details and a TID
            writer.WriteString(nameof(value.Namespace), value.Namespace);
            writer.WriteString(nameof(value.Name), value.Name);
            //writer.WriteString("QualifiedCppName", value.GetQualifiedCppName());
            writer.WriteBoolean(nameof(value.HasGenericParameters), value.HasGenericParameters);
            writer.WriteBoolean("IsNested", value.DeclaringType != null);
            if (value.DeclaringType != null)
            {
                // Declaring types can't have a cycle, but could be weird with generics
                writer.WritePropertyName(nameof(value.DeclaringType));
                simpleConv.Write(writer, value.DeclaringType, options);
            }
            if (value.GetElementType() != null)
            {
                // Element types can have a cycle
                writer.WritePropertyName("ElementType");
                simpleConv.Write(writer, value.GetElementType(), options);
            }
            else
            {
                writer.WriteNull("ElementType");
            }
            //writer.WritePropertyName(nameof(value.GenericParameterConstraints));
            //writer.WriteStartArray();
            //foreach (var gpc in value.GenericParameterConstraints)
            //{
            //    simpleConv.Write(writer, gpc, options);
            //}
            //writer.WriteEndArray();
            writer.WritePropertyName(nameof(value.GenericParameters));
            writer.WriteStartArray();
            foreach (var gp in value.GenericParameters)
            {
                simpleConv.Write(writer, gp, options);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        public override void Write(Utf8JsonWriter writer, TypeDefinition value, JsonSerializerOptions options)
        {
            // We basically want to verbosely write out our this typeref, everything else should use the simple type ref converter.
            // Everything else in this type should also just be serialized normally.
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value));
            WriteThis(writer, value, options);
            // Write each of the other properties explicitly by converting
            writer.WritePropertyName(nameof(value.Attributes));
            JsonSerializer.Serialize(writer, value.Attributes, options);
            writer.WritePropertyName(nameof(value.Interfaces));
            JsonSerializer.Serialize(writer, value.Interfaces, options);
            writer.WritePropertyName("InstanceFields");
            JsonSerializer.Serialize(writer, value.Fields.Where(f => !f.IsStatic), options);
            writer.WritePropertyName(nameof(value.IsAutoLayout));
            JsonSerializer.Serialize(writer, value.IsAutoLayout, options);
            writer.WritePropertyName(nameof(value.IsExplicitLayout));
            JsonSerializer.Serialize(writer, value.IsExplicitLayout, options);
            writer.WritePropertyName(nameof(value.IsSequentialLayout));
            JsonSerializer.Serialize(writer, value.IsSequentialLayout, options);
            writer.WritePropertyName(nameof(value.Methods));
            JsonSerializer.Serialize(writer, value.Methods, options);
            writer.WritePropertyName(nameof(value.NestedTypes));
            JsonSerializer.Serialize(writer, value.NestedTypes, options);
            writer.WritePropertyName(nameof(value.BaseType));
            JsonSerializer.Serialize(writer, value.BaseType, options);
            writer.WritePropertyName(nameof(value.Properties));
            JsonSerializer.Serialize(writer, value.Properties, options);
            writer.WritePropertyName("StaticFields");
            JsonSerializer.Serialize(writer, value.Fields.Where(f => f.IsStatic), options);
            writer.WritePropertyName(nameof(typeDefIndex));
            JsonSerializer.Serialize(writer, typeDefIndex++, options);
            writer.WriteNumber("Size", sz.GetSize(value));
            writer.WriteEndObject();
        }
    }
}