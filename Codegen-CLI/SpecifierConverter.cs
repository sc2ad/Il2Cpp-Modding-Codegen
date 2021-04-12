using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class SpecifierConverter : JsonConverter<ISpecifier>
    {
        public override ISpecifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, ISpecifier value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }
}