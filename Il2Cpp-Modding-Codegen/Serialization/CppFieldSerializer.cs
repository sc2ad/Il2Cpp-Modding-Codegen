using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppFieldSerializer : Serializer<IField>
    {
        // When we construct this class, we resolve the field by placing everything it needs in the context object
        // When serialize is called, we simply write the field we have.

        internal readonly Dictionary<IField, string?> ResolvedTypeNames = new Dictionary<IField, string?>();

        /// <summary>
        /// Contains a mapping of fields to sizes, where size is either -1 or a positive value.
        /// -1 Implies an invalid/unknown size.
        /// </summary>
        internal readonly Dictionary<IField, int> ResolvedFieldSizes = new Dictionary<IField, int>();

        internal readonly Dictionary<IField, string> SafeFieldNames = new Dictionary<IField, string>();

        private readonly SerializationConfig _config;
        private readonly List<IField> fields = new();
        private readonly List<IField> hiddenFields = new();

        internal CppFieldSerializer(SerializationConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Initializes the list of fields to use for offset resolution
        /// </summary>
        /// <param name="fieldCollection"></param>
        internal void InitializeFields(List<IField> fieldCollection)
        {
            fields.AddRange(fieldCollection);
        }

        // Resolve the field into context here
        public override void PreSerialize(CppTypeContext context, IField field)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));
            if (field is null) throw new ArgumentNullException(nameof(field));
            // In this situation, if the type is a pointer, we can simply forward declare.
            // Otherwise, we need to include the corresponding file. This must be resolved via context
            // If the resolved type name is null, we won't serialize this field
            // First, resolve the field type to see if it exists
            // If it does, because it is a field, we can FD it if it is a pointer
            // If it is not a pointer, then we need to include it
            // If it is a nested class, we need to deal with some stuff (maybe)
            if (!field.HasSize())
            {
                // If we have an ignore attribute, we may choose to avoid writing it. This depends on our NEXT field's offset.
                // If it is the same as our current offset, we can ignore this field.
                var idx = fields.FindIndex(f => f.Equals(field));
                if (fields.Count > idx + 1 && fields[idx + 1].Offset == field.Offset)
                {
                    // Skip this field
                    hiddenFields.Add(field);
                    return;
                }
            }
            var resolvedName = context.GetCppName(field.Type, true);
            ResolvedFieldSizes.Add(field, context.GetSize(field.Type));
            if (!string.IsNullOrEmpty(resolvedName))
                Resolved(field);
            // In order to ensure we get an UnresolvedTypeException when we serialize
            ResolvedTypeNames.Add(field, resolvedName);

            SafeFieldNames.Add(field, field.SafeFieldName(_config));
        }

        // Write the field here
        public override void Serialize(CppStreamWriter writer, IField field, bool asHeader)
        {
            if (writer is null) throw new ArgumentNullException(nameof(writer));
            if (field is null) throw new ArgumentNullException(nameof(field));
            foreach (var attr in field.Attributes)
            {
                if (attr.Offset != -1)
                    writer.WriteComment($"[{attr.Name}] Offset: 0x{attr.Offset:X}");
            }
            if (hiddenFields.Contains(field))
            {
                writer.WriteComment($"Ignoring hidden field: {string.Join(' ', field.Specifiers)} {field.Type} {field.Name}");
                writer.WriteComment($"Offset: 0x{field.Offset:X}");
                if (field.LayoutOffset >= 0)
                    writer.WriteComment($"Layout Offset: 0x{field.LayoutOffset:X}");
                writer.Flush();
                Serialized(field);
                return;
            }
            // If we could not resolve the type name, don't serialize the field (this should cause a critical failure in the type)
            if (ResolvedTypeNames[field] == null)
                throw new UnresolvedTypeException(field.DeclaringType, field.Type);

            var fieldString = "";
            foreach (var spec in field.Specifiers)
                fieldString += $"{spec} ";
            writer.WriteComment(fieldString + field.Type + " " + field.Name);
            writer.WriteComment($"Size: 0x{ResolvedFieldSizes[field]:X}");
            writer.WriteComment($"Offset: 0x{field.Offset:X}");
            if (field.LayoutOffset >= 0)
                writer.WriteComment($"Layout Offset: 0x{field.LayoutOffset:X}");
            if (!field.Specifiers.IsStatic() && !field.Specifiers.IsConst())
                writer.WriteFieldDeclaration(ResolvedTypeNames[field]!, SafeFieldNames[field]);
            // If the field has a size, we can write a quick static_assert check to ensure the sizeof this field matches our computed size.

            if (ResolvedFieldSizes[field] > 0)
            {
                writer.WriteComment("Field size check");
                writer.WriteDeclaration($"static_assert(sizeof({ResolvedTypeNames[field]!}) == 0x{ResolvedFieldSizes[field]:X})");
            }
            // If we aren't the last field, add the padding necessary to ensure our next field is in the correct location.
            // This is actually quite tricky, as we need to account for quite a few things (specifically when we don't HAVE an offset to take advantage of)
            // If our fields do NOT have offsets, we need to make some assumptions, namely that we are okay with standard #pragma pack(push, 8)
            // Otherwise, we actually want a #pragma pack(push, 1) and to place all of the fields within that block
            // Ideally, in all cases where we have offset checks, we have padding fields as well.
            // In cases where we do NOT have offset checks, we hope for the best. ex: generics.

            if (!field.Specifiers.IsStatic() && !field.Equals(fields.Last()))
            {
                // fields must contain field. If not, we throw here.
                var fInd = fields.FindIndex(f => f.Equals(field));
                int size = ResolvedFieldSizes[field];
                var nextField = fields[fInd + 1];
                if (field.Offset >= 0 && nextField.Offset > field.Offset && size > 0)
                {
                    if (nextField.Offset - field.Offset > size)
                    {
                        // If our next field's offset is more than the size of our current field, we need to write some padding.
                        writer.WriteComment($"Padding between fields: {SafeFieldNames[field]} and: {SafeFieldNames[nextField]}");
                        writer.WriteDeclaration($"private: char __padding{fInd}[0x{nextField.Offset - field.Offset - size:X}] = {{}}");
                        writer.WriteLine("public:");
                    }
                }
                else
                    writer.WriteComment($"WARNING Could not write padding for field: {SafeFieldNames[field]}! Ignoring it instead (and assuming correct layout regardless)...");
            }
            writer.Flush();
            Serialized(field);
        }
    }
}