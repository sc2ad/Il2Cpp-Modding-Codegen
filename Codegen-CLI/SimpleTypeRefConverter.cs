using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class SimpleTypeRefConverter : JsonConverter<TypeRef>
    {
        private readonly List<ITypeData> types;
        private readonly FastTypeRefComparer comparer;

        public SimpleTypeRefConverter(IEnumerable<ITypeData> types)
        {
            this.types = new List<ITypeData>(types);
            comparer = new FastTypeRefComparer();
        }

        public override TypeRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, TypeRef value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            // Write simple here, only relevant details and a TID
            writer.WriteString(nameof(value.Namespace), value.Namespace);
            writer.WriteString(nameof(value.Name), value.Name);
            writer.WriteString("QualifiedCppName", value.GetQualifiedCppName());
            writer.WriteBoolean(nameof(value.IsGenericInstance), value.IsGenericInstance);
            writer.WriteBoolean(nameof(value.IsGenericTemplate), value.IsGenericTemplate);
            writer.WriteBoolean(nameof(value.IsGenericParameter), value.IsGenericParameter);
            writer.WriteBoolean(nameof(value.IsCovariant), value.IsCovariant);
            var ind = types.FindIndex(d => comparer.Equals(value, d.This));
            if (ind < 0 && !value.IsGenericParameter && !value.IsArray() && !value.IsPointer())
                throw new InvalidOperationException("TypeRef could not be found in types! Is this a generic parameter?");
            writer.WriteNumber("TypeId", ind);
            writer.WritePropertyName(nameof(value.Generics));
            writer.WriteStartArray();
            foreach (var gp in value.Generics)
            {
                Write(writer, gp, options);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}