using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Data.DllHandling;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppStaticFieldSerializer : Serializer<IField>
    {
        private string _declaringFullyQualified;
        private string _thisTypeName;
        private readonly Dictionary<IField, string> _resolvedTypes = new Dictionary<IField, string>();
        private bool _asHeader;
        private readonly SerializationConfig _config;

        struct Constant
        {
            public string type;
            public string value;
            public Constant(string type_, string value_)
            {
                type = type_;
                value = value_;
            }
        };
        private readonly Dictionary<IField, Constant> _constants = new Dictionary<IField, Constant>();

        internal CppStaticFieldSerializer(SerializationConfig config)
        {
            _config = config;
        }

        static string EncodeAtypicalCharacters(string value)
        {
            // This should replace any characters not in the typical ASCII printable range.
            return Regex.Replace(value, @"[^ -~]", match => $"\\u{(int)match.Value[0]:x4}");
        }

        static TypeRef GetEnumUnderlyingType(ITypeData self)
        {
            var fields = self.Fields;
            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (!field.Specifiers.IsStatic())
                    return field.Type;
            }
            throw new ArgumentException("", nameof(self));
        }

    public override void PreSerialize(CppTypeContext context, IField field)
        {
            Contract.Requires(context != null && field != null);
            _declaringFullyQualified = context.QualifiedTypeName.TrimStart(':');
            _thisTypeName = context.GetCppName(field.DeclaringType, false, needAs: CppTypeContext.NeedAs.Definition);
            var resolvedName = context.GetCppName(field.Type, true);
            if (resolvedName != null)
                // Add static field to forward declares, since it is used by the static _get and _set methods
                Resolved(field);
            _resolvedTypes.Add(field, resolvedName);

            if (field is DllField)
            {
                var dllField = field as DllField;
                if (dllField.This.Constant != null)
                {
                    var type = "";
                    var value = "";
                    var resolved = context.ResolveAndStore(field.Type, forceAs: CppTypeContext.ForceAsType.None);
                    if (resolved.Type == TypeEnum.Enum || !resolvedName.Any(char.IsUpper))
                    {
                        var val = $"{dllField.This.Constant}";
                        if (val == "True" || val == "False" || Regex.IsMatch(val, @"-?(?:[\d\.]|E[\+\-])+"))
                        {
                            TypeRef typeRef = (resolved.Type == TypeEnum.Enum) ? GetEnumUnderlyingType(resolved) : resolved.This;
                            type = context.GetCppName(typeRef, true);
                            value = val.ToLower();
                        }
                        else
                            Console.WriteLine($"{field.DeclaringType}'s {resolvedName} {field.Name} has constant that is not valid C++: {val}");
                    }
                    else if (resolvedName.StartsWith("::Il2CppString"))
                    {
                        var str = dllField.This.Constant as string;
                        var encodedStr = EncodeAtypicalCharacters(str);
                        type = "char*";
                        value = $"\"{encodedStr}\"";
                    }
                    else if (resolvedName.StartsWith("::Il2CppChar"))
                    {
                        char c = (char)dllField.This.Constant;
                        var encodedStr = EncodeAtypicalCharacters(c.ToString());
                        type = "char";
                        value = $"'{encodedStr}'";
                    }
                    else
                        throw new Exception($"Unhandled constant type {resolvedName}!");

                    if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(value))
                        _constants.Add(field, new Constant(type, value));

                } else if (dllField.This.HasDefault)
                    Console.WriteLine($"TODO for {field.DeclaringType}'s {resolvedName} {field.Name}: figure out how to get default values??");
            }
        }

        private static string SafeName(IField field) => field.Name.Replace('<', '$').Replace('>', '$');

        private string GetGetter(string fieldType, IField field, bool namespaceQualified)
        {
            var retStr = fieldType;
            if (_config.OutputStyle == OutputStyle.Normal)
                retStr = "std::optional<" + retStr + ">";
            var staticStr = string.Empty;
            var ns = string.Empty;
            if (namespaceQualified)
                ns = _declaringFullyQualified + "::";
            if (_asHeader)
                staticStr = "static ";
            // Collisions with this name are incredibly unlikely.
            return $"{staticStr + retStr} {ns}_get_{SafeName(field)}()";
        }

        private string GetSetter(string fieldType, IField field, bool namespaceQualified)
        {
            var ns = string.Empty;
            var staticStr = string.Empty;
            if (namespaceQualified)
                ns = _declaringFullyQualified + "::";
            if (_asHeader)
                staticStr = "static ";
            return $"{staticStr}void {ns}_set_{SafeName(field)}({fieldType} value)";
        }

        public override void Serialize(CppStreamWriter writer, IField field, bool asHeader)
        {
            Contract.Requires(writer != null && field != null);
            _asHeader = asHeader;
            if (_resolvedTypes[field] == null)
                throw new UnresolvedTypeException(field.DeclaringType, field.Type);
            var fieldCommentString = "";
            foreach (var spec in field.Specifiers)
                fieldCommentString += $"{spec} ";
            fieldCommentString += $"{field.Type} {field.Name}";
            var resolvedType = _resolvedTypes[field];
            if (_asHeader && !field.DeclaringType.IsGenericTemplate)
            {
                if (_constants.TryGetValue(field, out var constant))
                {
                    writer.WriteComment("static field const value: " + fieldCommentString);
                    string declaration;
                    if (constant.type.EndsWith("*"))
                        declaration = $"static constexpr const {constant.type} {SafeName(field)} = {constant.value}";
                    else
                        declaration = $"static const {constant.type} {SafeName(field)} = {constant.value}";
                    writer.WriteDeclaration(declaration);
                }
                // Create two method declarations:
                // static FIELDTYPE _get_FIELDNAME();
                // static void _set_FIELDNAME(FIELDTYPE value);
                writer.WriteComment("Get static field: " + fieldCommentString);
                writer.WriteDeclaration(GetGetter(resolvedType, field, !_asHeader));
                writer.WriteComment("Set static field: " + fieldCommentString);
                writer.WriteDeclaration(GetSetter(resolvedType, field, !_asHeader));
            }
            else
            {
                var (@namespace, @class) = field.DeclaringType.GetIl2CppName();
                var classArgs = $"\"{@namespace}\", \"{@class}\"";
                if (field.DeclaringType.IsGeneric)
                    classArgs = $"il2cpp_utils::il2cpp_type_check::il2cpp_no_arg_class<{_thisTypeName}>::get()";

                // Write getter
                writer.WriteComment("Autogenerated static field getter");
                writer.WriteComment("Get static field: " + fieldCommentString);
                writer.WriteDefinition(GetGetter(resolvedType, field, !_asHeader));

                var s = "return ";
                var innard = $"<{resolvedType}>";
                var macro = "CRASH_UNLESS((";
                if (_config.OutputStyle != OutputStyle.CrashUnless)
                    macro = "";

                s += $"{macro}il2cpp_utils::GetFieldValue{innard}(";
                s += $"{classArgs}, \"{field.Name}\")";
                if (!string.IsNullOrEmpty(macro)) s += "))";
                s += ";";
                writer.WriteLine(s);
                writer.CloseDefinition();

                // Write setter
                writer.WriteComment("Autogenerated static field setter");
                writer.WriteComment("Set static field: " + fieldCommentString);
                writer.WriteDefinition(GetSetter(resolvedType, field, !_asHeader));
                s = "";
                if (_config.OutputStyle == OutputStyle.CrashUnless)
                    macro = "CRASH_UNLESS(";
                else
                    macro = "RET_V_UNLESS(";

                s += $"{macro}il2cpp_utils::SetFieldValue(";
                s += $"{classArgs}, \"{field.Name}\", value));";
                writer.WriteLine(s);
                writer.CloseDefinition();
            }
            writer.Flush();
            Serialized(field);
        }
    }
}
