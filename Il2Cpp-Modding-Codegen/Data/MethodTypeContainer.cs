using System;
using System.Linq;

namespace Il2CppModdingCodegen.Data
{
    public class MethodTypeContainer
    {
        private string typeName;
        private string _suffix;
        private string templatedName;

        public bool Skip { get; set; } = false;
        public bool UnPointered { get; private set; }
        public bool IsPointer { get => typeName.EndsWith("*"); }
        // Contains a class or struct
        public bool IsClassType { get => typeName.Any(char.IsUpper); }

        internal MethodTypeContainer(string t) => typeName = t;

        internal void Prefix(string prefix) => typeName = prefix + typeName;

        internal void Suffix(string suffix) => _suffix += suffix;

        // Make this parameter no longer a pointer, and use its value as `&val` from now on
        internal bool UnPointer()
        {
            if (!IsPointer) return false;
            typeName = typeName.Substring(0, typeName.Length - 1);
            return UnPointered = true;
        }

        internal string TypeName(bool header)
        {
            // If we are a header, return a templated typename.
            // Otherwise, we should never return a templated typename.
            if (!string.IsNullOrEmpty(templatedName) && header)
                return templatedName;
            return typeName + _suffix;
        }

        internal void Template(string newName) => templatedName = newName;

        [Obsolete("TypeName should be used instead!", true)]
#pragma warning disable CS0809 // Obsolete member 'MethodTypeContainer.ToString()' overrides non-obsolete member 'object.ToString()'
        public override string ToString() => null;
#pragma warning restore CS0809 // Obsolete member 'MethodTypeContainer.ToString()' overrides non-obsolete member 'object.ToString()'
    }
}
