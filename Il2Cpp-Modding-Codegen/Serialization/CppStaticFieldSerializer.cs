using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Data.DllHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppStaticFieldSerializer : Serializer<IField>
    {
        private string? _declaringFullyQualified;
        private string? _thisTypeName;
        private string? _declaringLiteral;
        private readonly Dictionary<IField, string?> _resolvedTypes = new Dictionary<IField, string?>();
        private bool _asHeader;
        private readonly SerializationConfig _config;

        private struct Constant
        {
            public string name;
            public string type;
            public string value;

            public Constant(string name_, string type_, string value_)
            {
                name = name_;
                type = type_;
                value = value_;
            }
        };

        private readonly Dictionary<IField, Constant> _constants = new Dictionary<IField, Constant>();

        internal CppStaticFieldSerializer(SerializationConfig config)
        {
            _config = config;
        }

        private static string Encode(string value)
        {
            // This should replace any characters not in the typical ASCII printable range.
            static ushort FirstChar(Match match) => match.Value[0];
            return Regex.Replace(value.Replace(@"\", @"\\"), @"[^ -~]", match => $"\\u{FirstChar(match):x4}");
        }

        private static TypeRef GetEnumUnderlyingType(ITypeData self)
            => self.InstanceFields.FirstOrDefault()?.Type ?? throw new ArgumentException("should be an Enum type!", nameof(self));

        public override void PreSerialize(CppTypeContext context, IField field)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));
            if (field is null) throw new ArgumentNullException(nameof(field));
            _declaringFullyQualified = context.QualifiedTypeName.TrimStart(':');
            _declaringLiteral = context.GetCppName(field.DeclaringType, false, false, forceAsType: CppTypeContext.ForceAsType.Literal);
            _thisTypeName = context.GetCppName(field.DeclaringType, false, needAs: CppTypeContext.NeedAs.Definition);
            var resolvedName = context.GetCppName(field.Type, true);
            _resolvedTypes.Add(field, resolvedName);
            if (resolvedName != null)
            {
                // Add static field to forward declares, since it is used by the static _get and _set methods
                Resolved(field);

                if (field is DllField dllField)
                    if (dllField.This.Constant != null)
                    {
                        string name = SafeName(field);
                        string type = "";
                        string value = "";
                        var resolved = context.ResolveAndStore(field.Type, forceAs: CppTypeContext.ForceAsType.None);
                        if (resolved?.Type == TypeEnum.Enum || !resolvedName.Any(char.IsUpper))
                        {
                            if (resolved?.Type == TypeEnum.Enum && name == "value")
                                name = "Value";
                            var val = $"{dllField.This.Constant}";
                            if (val == "True" || val == "False" || Regex.IsMatch(val, @"-?(?:[\d\.]|E[\+\-])+"))
                            {
                                var temp = (resolved?.Type == TypeEnum.Enum) ? context.GetCppName(GetEnumUnderlyingType(resolved), true) : resolvedName;
                                if (temp is null) throw new Exception($"Failed to get C++ type for {field.Type}");
                                type = temp;
                                if (type.Contains("uint")) val += "u";
                                value = val.ToLower();
                                if (value == long.MinValue.ToString())
                                    value = (long.MinValue + 1).ToString() + " - 1";
                            }
                            else
                                Console.WriteLine($"{field.DeclaringType}'s {resolvedName} {field.Name} has constant that is not valid C++: {val}");
                        }
                        else if (resolvedName.StartsWith($"::{Constants.StringCppName}"))
                        {
                            var str = (string)dllField.This.Constant;
                            var encodedStr = Encode(str);
                            type = "char*";
                            value = $"\"{encodedStr}\"";
                        }
                        else if (resolvedName.StartsWith("::Il2CppChar"))
                        {
                            char c = (char)dllField.This.Constant;
                            var encodedStr = Encode(c.ToString());
                            type = resolvedName;
                            value = $"u'{encodedStr}'";
                        }
                        else
                            throw new Exception($"Unhandled constant type {resolvedName}!");

                        if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(value))
                            _constants.Add(field, new Constant(name, type, value));
                    }
                    else if (dllField.This.HasDefault)
                        Console.WriteLine($"TODO for {field.DeclaringType}'s {resolvedName} {field.Name}: figure out how to get default values??");
            }
        }

        private string SafeConfigName(string name) => _config.SafeName(name.Replace('<', '$').Replace('>', '$'));

        private string SafeName(IField field)
        {
            string name = field.Name;
            if (name == _declaringLiteral)
                name = "_" + name;
            return SafeConfigName(name);
        }

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
            return $"{staticStr + retStr} {ns}{SafeConfigName($"_get_{field.Name}")}()";
        }

        private string GetSetter(string fieldType, IField field, bool namespaceQualified)
        {
            var ns = string.Empty;
            var staticStr = string.Empty;
            if (namespaceQualified)
                ns = _declaringFullyQualified + "::";
            if (_asHeader)
                staticStr = "static ";
            return $"{staticStr}void {ns}{SafeConfigName($"_set_{field.Name}")}({fieldType} value)";
        }

        public override void Serialize(CppStreamWriter writer, IField field, bool asHeader)
        {
            if (writer is null) throw new ArgumentNullException(nameof(writer));
            if (field is null) throw new ArgumentNullException(nameof(field));
            _asHeader = asHeader;
            if (_resolvedTypes[field] is null)
                throw new UnresolvedTypeException(field.DeclaringType, field.Type);
            string resolvedType = _resolvedTypes[field]!;
            var fieldCommentString = "";
            foreach (var spec in field.Specifiers)
                fieldCommentString += $"{spec} ";
            fieldCommentString += $"{field.Type} {field.Name}";
            if (_asHeader && !field.DeclaringType.IsGenericTemplate)
            {
                if (_constants.TryGetValue(field, out var constant))
                {
                    writer.WriteComment("static field const value: " + fieldCommentString);
                    writer.WriteDeclaration($"static constexpr const {constant.type} {constant.name} = {constant.value}");
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

                // TODO: Check invalid name
                var loggerId = "___internal__logger";

                writer.WriteDeclaration($"static auto {loggerId} = Logger::get().WithContext(\"codegen\")" +
                    $".WithContext(\"{field.DeclaringType.CppNamespace()}\")" +
                    $".WithContext(\"{field.DeclaringType.CppName()}\")" +
                    $".WithContext(\"{SafeConfigName($"_get_{field.Name}")}\")");

                var innard = $"<{resolvedType}>";
                var call = $"il2cpp_utils::GetFieldValue{innard}(";
                call += $"{classArgs}, \"{field.Name}\")";
                writer.WriteDeclaration("return " + _config.MacroWrap(loggerId, call, true));
                writer.CloseDefinition();

                // Write setter
                writer.WriteComment("Autogenerated static field setter");
                writer.WriteComment("Set static field: " + fieldCommentString);
                writer.WriteDefinition(GetSetter(resolvedType, field, !_asHeader));

                writer.WriteDeclaration($"static auto {loggerId} = Logger::get().WithContext(\"codegen\")" +
                    $".WithContext(\"{field.DeclaringType.CppNamespace()}\")" +
                    $".WithContext(\"{field.DeclaringType.CppName()}\")" +
                    $".WithContext(\"{SafeConfigName($"_set_{field.Name}")}\")");

                call = $"il2cpp_utils::SetFieldValue(";
                call += $"{classArgs}, \"{field.Name}\", value)";
                writer.WriteDeclaration(_config.MacroWrap(loggerId, call, false));
                writer.CloseDefinition();
            }
            writer.Flush();
            Serialized(field);
        }
    }
}