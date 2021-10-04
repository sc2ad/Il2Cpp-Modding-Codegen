using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppModdingCodegen.Data
{
    public class MethodTypeContainer
    {
        private string? _typeName;
        private string _suffix = "";
        private string? _templatedName;

        // getter-only properties
        internal bool IsPointer => _typeName?.EndsWith("*") ?? throw new InvalidOperationException("typeName is null!");

        // Contains a class or struct
        internal bool IsClassType => _typeName.Any(char.IsUpper);

        internal bool HasTemplate => !string.IsNullOrEmpty(_templatedName);
        internal string ElementType => Regex.Match(_typeName, @"ArrayW<(.*)>[^>]*").Groups[1].ToString();

        // other properties
        internal bool Skip { get; set; } = false;

        internal bool UnPointered { get; private set; } = false;
        internal bool ExpandParams { get; set; } = false;
        internal TypeRef Type { get; }

        // methods
        internal MethodTypeContainer(string? t, TypeRef typ)
        {
            _typeName = t;
            Type = typ;
        }

        internal void Prefix(string prefix) => _typeName = prefix + _typeName;

        internal void Suffix(string suffix) => _suffix += suffix;

        // Make this parameter no longer a pointer, and use its value as `&val` from now on
        internal bool UnPointer()
        {
            if (_typeName == null) throw new InvalidOperationException("typeName is null!");
            if (!IsPointer) return false;
            _typeName = _typeName[0..^1];
            return UnPointered = true;
        }

        internal string TypeName(bool header)
        {
            // If we are a header, return a templated typename.
            // Otherwise, we should never return a templated typename.
            if (HasTemplate && header)
                return _templatedName!;
            var typeName = ExpandParams ? $"std::initializer_list<{ElementType}>" : _typeName;
            if (_typeName != null && (string.IsNullOrEmpty(typeName) || typeName.Contains(_typeName) && !typeName.Equals(_typeName)))
                throw new FormatException($"Got '{typeName}' for type name '{_typeName}'!");
            return typeName + _suffix;
        }

        internal void Template(string? newName) => _templatedName = newName;

        [Obsolete("TypeName should be used instead!", true)]
#pragma warning disable CS0809 // Obsolete member 'MethodTypeContainer.ToString()' overrides non-obsolete member 'object.ToString()'
        public override string ToString() => "";

#pragma warning restore CS0809 // Obsolete member 'MethodTypeContainer.ToString()' overrides non-obsolete member 'object.ToString()'
    }
}