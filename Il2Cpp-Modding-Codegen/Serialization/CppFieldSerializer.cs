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
        // from https://en.cppreference.com/w/cpp/keyword
        private static readonly HashSet<string> IllegalFieldNames = new HashSet<string> {
            "alignas", "alignof", "and", "and_eq", "asm", "atomic_cancel", "atomic_commit", "atomic_noexcept", "auto",
            "bitand", "bitor", "bool", "break", "case", "catch", "char", "char8_t", "char16_t", "char32_t", "class",
            "compl", "concept", "const", "consteval", "constexpr", "constinit", "const_cast", "continue", "co_await",
            "co_return", "co_yield", "decltype", "default", "delete", "do", "double", "dynamic_cast", "else", "enum",
            "explicit", "export", "extern", "false", "float", "for", "friend", "goto", "if", "inline", "int", "long",
            "mutable", "namespace", "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq",
            "private", "protected", "public", "reflexpr", "register", "reinterpret_cast", "requires", "return",
            "short", "signed", "sizeof", "static", "static_assert", "static_cast", "struct", "switch", "synchronized",
            "template", "this", "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union",
            "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq"
        };
        // When we construct this class, we resolve the field by placing everything it needs in the context object
        // When serialize is called, we simply write the field we have.

        private Dictionary<IField, string> _resolvedTypeNames = new Dictionary<IField, string>();

        public CppFieldSerializer() { }

        // Resolve the field into context here
        public override void PreSerialize(CppTypeContext context, IField field)
        {
            // In this situation, if the type is a pointer, we can simply forward declare.
            // Otherwise, we need to include the corresponding file. This must be resolved via context
            // If the resolved type name is null, we won't serialize this field
            // First, resolve the field type to see if it exists
            // If it does, because it is a field, we can FD it if it is a pointer
            // If it is not a pointer, then we need to include it
            // If it is a nested class, we need to deal with some stuff (maybe)
            var resolvedType = context.GetCppName(field.Type, true);
            if (!string.IsNullOrEmpty(resolvedType))
                Resolved(field);
            // In order to ensure we get an UnresolvedTypeException when we serialize
            _resolvedTypeNames.Add(field, resolvedType);
        }

        private string SafeFieldName(IField field)
        {
            var name = string.Join("$", field.Name.Split('<', '>'));
            if (IllegalFieldNames.Contains(name))
                name = "_" + name;
            return name;
        }

        // Write the field here
        public override void Serialize(CppStreamWriter writer, IField field, bool asHeader)
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
                writer.WriteFieldDeclaration(_resolvedTypeNames[field], SafeFieldName(field));
            writer.Flush();
            Serialized(field);
        }
    }
}