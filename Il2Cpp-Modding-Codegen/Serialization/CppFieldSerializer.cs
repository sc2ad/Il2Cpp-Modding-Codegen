using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppFieldSerializer : Serializer<IField>
    {
        // When we construct this class, we resolve the field by placing everything it needs in the context object
        // When serialize is called, we simply write the field we have.

        internal readonly Dictionary<IField, string?> ResolvedTypeNames = new Dictionary<IField, string?>();
        internal readonly Dictionary<IField, string> SafeFieldNames = new Dictionary<IField, string>();

        private readonly SerializationConfig _config;

        internal CppFieldSerializer(SerializationConfig config)
        {
            _config = config;
        }

        private readonly char[] angleBrackets = { '<', '>' };

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
            var resolvedName = context.GetCppName(field.Type, true);
            if (!string.IsNullOrEmpty(resolvedName))
                Resolved(field);
            // In order to ensure we get an UnresolvedTypeException when we serialize
            ResolvedTypeNames.Add(field, resolvedName);

            string SafeFieldName()
            {
                var name = field.Name;
                if (name.EndsWith("k__BackingField"))
                    name = name.Split(angleBrackets, StringSplitOptions.RemoveEmptyEntries)[0];
                name = string.Join("$", name.Split(angleBrackets)).Trim('_');
                if (char.IsDigit(name[0])) name = "_" + name;
                return _config.SafeName(name);
            }
            SafeFieldNames.Add(field, SafeFieldName());
        }

        // Write the field here
        public override void Serialize(CppStreamWriter writer, IField field, bool asHeader)
        {
            if (writer is null) throw new ArgumentNullException(nameof(writer));
            if (field is null) throw new ArgumentNullException(nameof(field));
            // If we could not resolve the type name, don't serialize the field (this should cause a critical failure in the type)
            if (ResolvedTypeNames[field] == null)
                throw new UnresolvedTypeException(field.DeclaringType, field.Type);

            var fieldString = "";
            foreach (var spec in field.Specifiers)
                fieldString += $"{spec} ";
            writer.WriteComment(fieldString + field.Type + " " + field.Name);

            writer.WriteComment($"Offset: 0x{field.Offset:X}");
            if (!field.Specifiers.IsStatic() && !field.Specifiers.IsConst())
                writer.WriteFieldDeclaration(ResolvedTypeNames[field]!, SafeFieldNames[field]);
            writer.Flush();
            Serialized(field);
        }
    }
}
