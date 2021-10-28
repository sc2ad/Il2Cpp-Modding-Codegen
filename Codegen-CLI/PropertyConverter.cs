using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Data.DllHandling;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class PropertyConverter : JsonConverter<DllProperty>
    {
        public override DllProperty Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, DllProperty prop, JsonSerializerOptions options)
        {
            var value = prop.Property;
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value.Attributes));
            JsonSerializer.Serialize(writer, value.Attributes, options);
            writer.WriteBoolean(nameof(value.GetMethod), value.GetMethod != null);
            writer.WriteBoolean(nameof(value.SetMethod), value.SetMethod != null);
            writer.WriteString(nameof(value.Name), value.Name);
            writer.WritePropertyName(nameof(value.PropertyType));
            JsonSerializer.Serialize(writer, value.PropertyType, options);
            writer.WriteEndObject();
        }
    }
}