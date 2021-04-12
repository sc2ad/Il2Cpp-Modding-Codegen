using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class PropertyConverter : JsonConverter<IProperty>
    {
        public override IProperty Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, IProperty value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value.Attributes));
            JsonSerializer.Serialize(writer, value.Attributes, options);
            writer.WritePropertyName(nameof(value.Specifiers));
            JsonSerializer.Serialize(writer, value.Specifiers, options);
            writer.WriteBoolean(nameof(value.GetMethod), value.GetMethod);
            writer.WriteBoolean(nameof(value.SetMethod), value.SetMethod);
            writer.WriteString(nameof(value.Name), value.Name);
            writer.WritePropertyName(nameof(value.Type));
            JsonSerializer.Serialize(writer, value.Type, options);
            writer.WriteEndObject();
        }
    }
}