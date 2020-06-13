using Il2Cpp_Modding_Codegen.Data;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppFieldSerializer : Serializer<IField>
    {
        // When we construct this class, we resolve the field by placing everything it needs in the context object
        // When serialize is called, we simply write the field we have.

        private Dictionary<IField, ResolvedType> _resolvedTypeNames = new Dictionary<IField, ResolvedType>();

        public CppFieldSerializer()
        {
        }

        // Resolve the field into context here
        public override void PreSerialize(CppSerializerContext context, IField field)
        {
            // In this situation, if the type is a pointer, we can simply forward declare.
            // Otherwise, we need to include the corresponding file. This must be resolved via context
            // If the resolved type name is null, we won't serialize this field
            // First, resolve the field type to see if it exists
            // If it does, because it is a field, we can FD it if it is a pointer
            // If it is not a pointer, then we need to include it
            // If it is a nested class, we need to deal with some stuff (maybe)
            var resolvedType = context.ResolveType(field.Type);
            if (resolvedType is null)
                goto end;
            if (resolvedType.Info.TypeFlags == TypeFlags.ReferenceType)
                context.AddForwardDeclare(resolvedType);
            else if (resolvedType.Primitive)
                context.AddPrimitive(resolvedType);
            else
                context.AddInclude(resolvedType.GetIncludeLocation());
            Resolved(field);
        end:
            // In order to ensure we get an UnresolvedTypeException when we serialize
            _resolvedTypeNames.Add(field, resolvedType);
        }

        // Write the field here
        public override void Serialize(CppStreamWriter writer, IField field)
        {
            // If we could not resolve the type name, don't serialize the field (this should cause a critical failure in the type)
            if (_resolvedTypeNames[field] == null)
                throw new UnresolvedTypeException(field.DeclaringType, field.Type);

            var fieldString = "";
            foreach (var spec in field.Specifiers)
            {
                fieldString += $"{spec} ";
            }
            fieldString += $"{field.Type} {field.Name} // Offset: 0x{field.Offset:X}";
            writer.WriteComment(fieldString);
            if (!field.Specifiers.IsStatic() && !field.Specifiers.IsConst())
                writer.WriteFieldDeclaration(_resolvedTypeNames[field].GetTypeName(), string.Join("$", field.Name.Split('<', '>')));
            writer.Flush();
            Serialized(field);
        }
    }
}