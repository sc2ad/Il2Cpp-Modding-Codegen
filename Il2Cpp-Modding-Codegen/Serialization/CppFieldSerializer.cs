using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.CppSerialization;
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
        private readonly List<IMethod> methods = new();
        private readonly List<IField> hiddenFields = new();
        private int localSize;

        private bool isInUnion = false;

        internal CppFieldSerializer(SerializationConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Initializes the list of fields to use for offset resolution
        /// </summary>
        /// <param name="fieldCollection"></param>
        internal void Initialize(List<IField> fieldCollection, List<IMethod> methods)
        {
            isInUnion = false;
            fields.AddRange(fieldCollection);
            this.methods.AddRange(methods);
            // Here we need to determine if we need to make nested structures with internals for use in unions
            // Essentially, if we are making a union, yet we find that our field is not the same size as our other fields in our union, we need to struct-ify
            // In order to struct-ify, we need to combine all fields that when offset + size is applied, they are still within the size of the largest union member
            // This will only happen if there are TWO fields with DIFFERENT sizes at the SAME offset
        }

        /// <summary>
        /// Returns true if the provided field is the first field in a union or is not in a union.
        /// </summary>
        /// <param name="f"></param>
        /// <returns></returns>
        internal bool FirstOrNotInUnion(IField field)
        {
            if (localSize > 0)
            {
                var fInd = fields.FindIndex(f => f.Equals(field));
                // If this field is not the first field, and it matches the previous field's offset, return false.
                if (fInd != 0 && fields[fInd - 1].Offset == field.Offset && fields[fInd - 1].HasSize())
                    // A field with the same offset as a field immediately before it is in a union.
                    return false;
            }
            // Fields on types with unknown sizes are never in unions
            return true;
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
            localSize = context.GetLocalSize();
            var fSize = context.GetSize(field.Type);
            if (fSize == 0)
                // If fSize is 0, that means that the field would be 0 size. However, that's not allowed, so make it a 1.
                fSize = 1;
            // TODO: Determine if and what fields to put in union here
            // TODO: If we need to create a wrapper structure, do that here, possibly even earlier.
            ResolvedFieldSizes.Add(field, fSize);
            // First, check to see if we have an existing field at the same offset.
            // If we do, and it is a different size, we need to create an internal struct.
            // The fields then need to be written within this struct, and this struct itself needs to be initializable from any of the fields.
            if (!string.IsNullOrEmpty(resolvedName))
                Resolved(field);

            // In order to ensure we get an UnresolvedTypeException when we serialize
            ResolvedTypeNames.Add(field, resolvedName);
            var fName = Utils.SafeFieldName(field);
            // Suffix _ for field names with same name as methods
            while (methods.Any(m => m.Name == fName))
                fName += "_";
            SafeFieldNames.Add(field, fName);
        }

        private void WriteField(CppStreamWriter writer, IField field)
        {
            var fieldString = "";
            foreach (var spec in field.Specifiers)
                fieldString += $"{spec} ";
            writer.WriteComment(fieldString + field.Type + " " + field.Name);
            writer.WriteComment($"Size: 0x{ResolvedFieldSizes[field]:X}");
            writer.WriteComment($"Offset: 0x{field.Offset:X}");
            if (field.Constant != null)
                writer.WriteComment($"Constant: {field.Constant}");
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

            // Only write padding if we have a known size, otherwise our unpacked state is good enough for us.
            if (!field.Specifiers.IsStatic() && !field.Equals(fields.Last()) && localSize >= 0 && !isInUnion)
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
                        writer.WriteDeclaration($"char __padding{fInd}[0x{nextField.Offset - field.Offset - size:X}] = {{}}");
                    }
                }
                else
                    writer.WriteComment($"WARNING Could not write padding for field: {SafeFieldNames[field]}! Ignoring it instead (and assuming correct layout regardless)...");
            }
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

            if (localSize > 0)
            {
                var nextF = fields.FindIndex(f => f.Equals(field));
                if (nextF == -1)
                    throw new InvalidOperationException("All fields must exist!");
                nextF++;
                if (nextF < fields.Count && fields[nextF].Offset == field.Offset && !isInUnion)
                {
                    // If this field has the same offset as our next field offset, we create a union with it.
                    writer.WriteComment($"Creating union for fields at offset: 0x{field.Offset:X}");
                    writer.WriteDefinition($"union");
                    isInUnion = true;
                }
                else if (isInUnion && (nextF >= fields.Count || fields[nextF].Offset != field.Offset))
                {
                    // If we are in a union, but our next field isn't at the same offset, exit the union, but only after we write our CURRENT field.
                    WriteField(writer, field);
                    writer.CloseDefinition(";");
                    isInUnion = false;
                    writer.Flush();
                    Serialized(field);
                    return;
                }
            }
            WriteField(writer, field);

            writer.Flush();
            Serialized(field);
        }
    }
}