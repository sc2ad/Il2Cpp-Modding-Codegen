using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class FieldConverter : JsonConverter<IField>
    {
        public override IField Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, IField value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value.Attributes));
            JsonSerializer.Serialize(writer, value.Attributes, options);
            writer.WriteString(nameof(value.Name), value.Name);
            writer.WriteNumber(nameof(value.Offset), value.Offset);
            writer.WriteNumber(nameof(value.LayoutOffset), value.LayoutOffset);
            writer.WritePropertyName(nameof(value.Specifiers));
            JsonSerializer.Serialize(writer, value.Specifiers, options);
            writer.WritePropertyName(nameof(value.Type));
            JsonSerializer.Serialize(writer, value.Type, options);
            writer.WriteEndObject();
        }
    }
}