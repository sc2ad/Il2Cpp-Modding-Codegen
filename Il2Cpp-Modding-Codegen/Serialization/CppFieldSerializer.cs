using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppFieldSerializer : ISerializer<IField>
    {
        // When we construct this class, we resolve the field by placing everything it needs in the context object
        // When serialize is called, we simply write the field we have.

        private Dictionary<IField, string> _resolvedTypeNames = new Dictionary<IField, string>();

        public CppFieldSerializer()
        {
        }

        // Resolve the field into context here
        public void PreSerialize(ISerializerContext context, IField field)
        {
            // In this situation, if the type is a pointer, we can simply forward declare.
            // Otherwise, we need to include the corresponding file. This must be resolved via context
            // If the resolved type name is null, we won't serialize this field
            _resolvedTypeNames.Add(field, context.GetNameFromReference(field.Type, mayNeedComplete: true));
        }

        // Write the field here
        public void Serialize(IndentedTextWriter writer, IField field)
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
            writer.WriteLine($"// {fieldString}");
            if (!field.Specifiers.IsStatic() && !field.Specifiers.IsConst())
                writer.WriteLine($"{_resolvedTypeNames[field]} {field.Name.Replace('<', '$').Replace('>', '$')};");
            writer.Flush();
        }
    }
}