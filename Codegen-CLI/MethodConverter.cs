using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class MethodConverter : JsonConverter<IMethod>
    {
        public override IMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        private void WriteSimple(Utf8JsonWriter writer, IMethod value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value.DeclaringType));
            JsonSerializer.Serialize(writer, value.DeclaringType, options);
            writer.WriteString(nameof(value.Name), value.Name);
            writer.WriteBoolean(nameof(value.IsSpecialName), value.IsSpecialName);
            writer.WriteEndObject();
        }

        public override void Write(Utf8JsonWriter writer, IMethod value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value.Attributes));
            JsonSerializer.Serialize(writer, value.Attributes, options);
            writer.WritePropertyName(nameof(value.DeclaringType));
            JsonSerializer.Serialize(writer, value.DeclaringType, options);
            writer.WritePropertyName(nameof(value.Generic));
            JsonSerializer.Serialize(writer, value.Generic, options);
            writer.WritePropertyName(nameof(value.GenericParameters));
            JsonSerializer.Serialize(writer, value.GenericParameters, options);
            writer.WritePropertyName(nameof(value.HidesBase));
            JsonSerializer.Serialize(writer, value.HidesBase, options);
            writer.WritePropertyName(nameof(value.Il2CppName));
            JsonSerializer.Serialize(writer, value.Il2CppName, options);
            writer.WritePropertyName(nameof(value.ImplementedFrom));
            JsonSerializer.Serialize(writer, value.ImplementedFrom, options);
            writer.WritePropertyName(nameof(value.IsSpecialName));
            JsonSerializer.Serialize(writer, value.IsSpecialName, options);
            writer.WritePropertyName(nameof(value.IsVirtual));
            JsonSerializer.Serialize(writer, value.IsVirtual, options);
            writer.WritePropertyName(nameof(value.Name));
            JsonSerializer.Serialize(writer, value.Name, options);
            writer.WritePropertyName(nameof(value.Offset));
            JsonSerializer.Serialize(writer, value.Offset, options);
            writer.WritePropertyName(nameof(value.Parameters));
            JsonSerializer.Serialize(writer, value.Parameters, options);
            writer.WritePropertyName(nameof(value.ReturnType));
            JsonSerializer.Serialize(writer, value.ReturnType, options);
            writer.WritePropertyName(nameof(value.RVA));
            JsonSerializer.Serialize(writer, value.RVA, options);
            writer.WritePropertyName(nameof(value.Slot));
            JsonSerializer.Serialize(writer, value.Slot, options);
            writer.WritePropertyName(nameof(value.Specifiers));
            JsonSerializer.Serialize(writer, value.Specifiers, options);
            writer.WritePropertyName(nameof(value.VA));
            JsonSerializer.Serialize(writer, value.VA, options);

            writer.WritePropertyName(nameof(value.BaseMethods));
            writer.WriteStartArray();
            foreach (var bm in value.BaseMethods)
                WriteSimple(writer, bm, options);
            writer.WriteEndArray();
            writer.WritePropertyName(nameof(value.ImplementingMethods));
            writer.WriteStartArray();
            foreach (var im in value.ImplementingMethods)
                WriteSimple(writer, im, options);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}