using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codegen_CLI
{
    internal class SimpleTypeRefConverter : JsonConverter<TypeRef>
    {
        private class ClearTypeRefConverter
        {
            internal bool Equals(TypeRef x, [AllowNull] TypeRef y)
            {
                if (y is null) return false;
                if (x.Namespace != y.Namespace || x.Name != y.Name)
                    return false;
                if (x.DeclaringType != null && !Equals(x.DeclaringType, y.DeclaringType))
                    return false;
                return true;
            }

            // Determine if the type ref on the left is a reference to the right
            // This INCLUDES generic instances x that are of the generic template y
            internal bool Equals([AllowNull] TypeRef x, ITypeData y)
            {
                if (x is null || y is null) return false;
                // First a simple name match
                if (x.Namespace != y.This.Namespace || x.Name != y.This.Name)
                    return false;
                // Then, check to see if the declaring types match
                if (x.DeclaringType != null && !Equals(x.DeclaringType, y.This.DeclaringType))
                    return false;
                // Otherwise, we return true
                // TODO: (this may short circuit fail on some interesting cases where generic args match?)
                return true;
            }
        }

        private readonly List<ITypeData> types;
        private readonly ClearTypeRefConverter comparer = new();

        public SimpleTypeRefConverter(IEnumerable<ITypeData> types)
        {
            this.types = new List<ITypeData>(types);
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
            writer.WriteBoolean(nameof(value.IsGenericParameter), value.IsGenericParameter);
            writer.WriteBoolean(nameof(value.IsArray), value.IsArray());
            writer.WriteBoolean(nameof(value.IsPointer), value.IsPointer());
            writer.WritePropertyName(nameof(value.Generics));
            writer.WriteStartArray();
            var genericsTypedef = value;
            if (value.ElementType != null)
            {
                // If we have an element type, skip ourselves and go to our elements directly.
                genericsTypedef = value.ElementType;
            }
            while (genericsTypedef.ElementType != null)
            {
                // Continue checking our element types until we have extinguished all of our element types, then write those generics.
                genericsTypedef = genericsTypedef.ElementType;
            }
            foreach (var gp in genericsTypedef.Generics)
            {
                Write(writer, gp, options);
            }
            writer.WriteEndArray();
            var ind = types.FindIndex(d => comparer.Equals(genericsTypedef, d));
            bool genericParam = value.IsGenericParameter;
            while (ind < 0 && !genericParam)
            {
                if (value.ElementType != null)
                {
                    genericParam = value.ElementType.IsGenericParameter;
                    if (genericParam)
                        break;
                    ind = types.FindIndex(d => comparer.Equals(value.ElementType, d));
                    value = value.ElementType;
                }
            }
            if (ind < 0 && !genericParam)
            {
                // If index is STILL -1 (and we aren't a generic param)
                // We couldn't find it even after searching our element types for as long as we could!
                throw new InvalidOperationException("TypeRef could not be found in types! Is this a generic parameter/array/pointer?");
            }
            if (genericParam)
                ind--; // VERY IMPORTANT DETAIL
            writer.WriteNumber("TypeId", ind);
            writer.WriteEndObject();
        }
    }
}