using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Data.DllHandling;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class MethodConverter : JsonConverter<DllMethod>
    {
        public override DllMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        private void WriteSimple(Utf8JsonWriter writer, MethodDefinition m, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(m.DeclaringType));
            JsonSerializer.Serialize(writer, m.DeclaringType, options);
            writer.WriteString(nameof(m.Name), m.Name);
            writer.WriteBoolean(nameof(m.IsSpecialName), m.IsSpecialName);
            writer.WriteEndObject();
        }

        public override void Write(Utf8JsonWriter writer, DllMethod value, JsonSerializerOptions options)
        {
            var m = value.Method;
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(m.Attributes));
            JsonSerializer.Serialize(writer, m.Attributes, options);
            writer.WritePropertyName(nameof(m.HasGenericParameters));
            JsonSerializer.Serialize(writer, m.HasGenericParameters, options);
            writer.WritePropertyName(nameof(m.GenericParameters));
            JsonSerializer.Serialize(writer, m.GenericParameters, options);
            writer.WritePropertyName(nameof(value.Il2CppName));
            JsonSerializer.Serialize(writer, value.Il2CppName, options);
            writer.WritePropertyName(nameof(m.IsSpecialName));
            JsonSerializer.Serialize(writer, m.IsSpecialName, options);
            writer.WritePropertyName(nameof(value.IsVirtual));
            JsonSerializer.Serialize(writer, value.IsVirtual, options);
            writer.WritePropertyName(nameof(value.Name));
            JsonSerializer.Serialize(writer, value.Name, options);
            writer.WritePropertyName(nameof(value.Offset));
            JsonSerializer.Serialize(writer, value.Offset, options);
            writer.WritePropertyName(nameof(m.Parameters));
            JsonSerializer.Serialize(writer, m.Parameters, options);
            writer.WritePropertyName(nameof(m.ReturnType));
            JsonSerializer.Serialize(writer, m.ReturnType, options);
            writer.WritePropertyName(nameof(value.RVA));
            JsonSerializer.Serialize(writer, value.RVA, options);
            writer.WritePropertyName(nameof(value.Slot));
            JsonSerializer.Serialize(writer, value.Slot, options);
            writer.WritePropertyName(nameof(value.VA));
            JsonSerializer.Serialize(writer, value.VA, options);

            //writer.WritePropertyName(nameof(value.BaseMethods));
            //writer.WriteStartArray();
            //foreach (var bm in value.BaseMethods)
            //    WriteSimple(writer, bm, options);
            //writer.WriteEndArray();
            //writer.WritePropertyName(nameof(value.ImplementingMethods));
            //writer.WriteStartArray();
            //foreach (var im in value.ImplementingMethods)
            //    WriteSimple(writer, im, options);
            //writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}