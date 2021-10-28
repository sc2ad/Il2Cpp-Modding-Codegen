using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Data.DllHandling;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class FieldConverter : JsonConverter<DllField>
    {
        public override DllField Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, DllField value, JsonSerializerOptions options)
        {
            var f = value.Field;
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(f.Attributes));
            JsonSerializer.Serialize(writer, f.Attributes, options);
            writer.WriteString(nameof(f.Name), f.Name);
            writer.WriteNumber("LayoutOffset", f.Offset);
            writer.WriteNumber(nameof(value.Offset), value.Offset);
            writer.WritePropertyName(nameof(f.FieldType));
            JsonSerializer.Serialize(writer, f.FieldType, options);
            writer.WriteString(nameof(f.Constant), f.Constant?.ToString());
            writer.WriteEndObject();
        }
    }
}