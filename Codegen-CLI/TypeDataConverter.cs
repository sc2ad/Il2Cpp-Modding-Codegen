using Il2CppModdingCodegen;
using Il2CppModdingCodegen.Data;
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
    internal class TypeDataConverter : JsonConverter<ITypeData>
    {
        public override ITypeData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        private readonly JsonConverter<TypeRef> simpleConv;
        private readonly ITypeCollection types;

        public TypeDataConverter(ITypeCollection types, JsonConverter<TypeRef> simpleConv)
        {
            this.types = types;
            this.simpleConv = simpleConv;
        }

        private void WriteThis(Utf8JsonWriter writer, TypeRef value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            // Write simple here, only relevant details and a TID
            writer.WriteString(nameof(value.Namespace), value.Namespace);
            writer.WriteString(nameof(value.Name), value.Name);
            writer.WriteString("QualifiedCppName", value.GetQualifiedCppName());
            writer.WriteBoolean(nameof(value.IsGenericTemplate), value.IsGenericTemplate);
            writer.WriteBoolean("IsNested", value.DeclaringType != null);
            if (value.DeclaringType != null)
            {
                // Declaring types can't have a cycle, but could be weird with generics
                writer.WritePropertyName(nameof(value.DeclaringType));
                simpleConv.Write(writer, value.OriginalDeclaringType, options);
            }
            if (value.ElementType != null)
            {
                // Element types can have a cycle
                writer.WritePropertyName(nameof(value.ElementType));
                simpleConv.Write(writer, value.ElementType, options);
            }
            else
            {
                writer.WriteNull(nameof(value.ElementType));
            }
            writer.WritePropertyName(nameof(value.GenericParameterConstraints));
            writer.WriteStartArray();
            foreach (var gpc in value.GenericParameterConstraints)
            {
                simpleConv.Write(writer, gpc, options);
            }
            writer.WriteEndArray();
            writer.WritePropertyName(nameof(value.Generics));
            writer.WriteStartArray();
            foreach (var gp in value.Generics)
            {
                simpleConv.Write(writer, gp, options);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        public override void Write(Utf8JsonWriter writer, ITypeData value, JsonSerializerOptions options)
        {
            // We basically want to verbosely write out our this typeref, everything else should use the simple type ref converter.
            // Everything else in this type should also just be serialized normally.
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value.This));
            WriteThis(writer, value.This, options);
            // Write each of the other properties explicitly by converting
            writer.WritePropertyName(nameof(value.Attributes));
            JsonSerializer.Serialize(writer, value.Attributes, options);
            writer.WritePropertyName(nameof(value.ImplementingInterfaces));
            JsonSerializer.Serialize(writer, value.ImplementingInterfaces, options);
            writer.WritePropertyName(nameof(value.InstanceFields));
            JsonSerializer.Serialize(writer, value.InstanceFields, options);
            writer.WritePropertyName(nameof(value.Layout));
            JsonSerializer.Serialize(writer, value.Layout, options);
            writer.WritePropertyName(nameof(value.Methods));
            JsonSerializer.Serialize(writer, value.Methods, options);
            writer.WritePropertyName(nameof(value.NestedTypes));
            JsonSerializer.Serialize(writer, value.NestedTypes, options);
            writer.WritePropertyName(nameof(value.Parent));
            JsonSerializer.Serialize(writer, value.Parent, options);
            writer.WritePropertyName(nameof(value.Properties));
            JsonSerializer.Serialize(writer, value.Properties, options);
            writer.WritePropertyName(nameof(value.Specifiers));
            JsonSerializer.Serialize(writer, value.Specifiers, options);
            writer.WritePropertyName(nameof(value.StaticFields));
            JsonSerializer.Serialize(writer, value.StaticFields, options);
            writer.WritePropertyName(nameof(value.Type));
            JsonSerializer.Serialize(writer, value.Type, options);
            writer.WritePropertyName(nameof(value.TypeDefIndex));
            JsonSerializer.Serialize(writer, value.TypeDefIndex, options);
            writer.WriteNumber("Size", SizeTracker.GetSize(value));
            writer.WriteEndObject();
        }
    }
}