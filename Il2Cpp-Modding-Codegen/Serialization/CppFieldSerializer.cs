using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppFieldSerializer : ISerializer<IField>
    {
        // When we construct this class, we resolve the field by placing everything it needs in the context object
        // When serialize is called, we simply write the field we have.
        private string _prefix;

        public CppFieldSerializer(string prefix = "  ")
        {
            _prefix = prefix;
        }

        // Resolve the field into context here
        public void PreSerialize(ISerializerContext context, IField field)
        {
            // In this situation, if the type is a pointer, we can simply forward declare.
            // Otherwise, we need to include the corresponding file. This must be resolved via context
            context.AddReference(field.Type);
        }

        // Write the field here
        public void Serialize(Stream stream, IField field)
        {
            using (var writer = new StreamWriter(stream))
            {
                var fieldString = "";
                foreach (var spec in field.Specifiers)
                {
                    fieldString += $"{spec} ";
                }
                fieldString += $"{field.Type} {field.Name} // 0x{field.Offset:X}";
                writer.WriteLine($"{_prefix}// {fieldString}");
                if (field.Type.IsPointer(_context.TypeContext))
                {
                    if (_context.ContainsType(field.Type.SafeFullName()))
                    {
                    }
                }
            }
        }
    }