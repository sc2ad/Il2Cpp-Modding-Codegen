using System;
using System.Linq;

namespace Il2Cpp_Modding_Codegen.Data
{
    public class MethodTypeContainer
    {
        private string typeName;
        private string _suffix;
        private string templatedName;
        public bool Skip = false;
        public bool UnPointered { get; private set; }
        public bool IsPointer { get => typeName.EndsWith("*"); }
        // Contains a class or struct
        public bool IsClassType { get => typeName.Any(char.IsUpper); }

        internal MethodTypeContainer(string t)
        {
            typeName = t;
        }

        internal void Prefix(string prefix)
        {
            typeName = prefix + typeName;
        }

        internal void Suffix(string suffix)
        {
            _suffix += suffix;
        }

        // Make this parameter no longer a pointer, and use its value as `&val` from now on
        internal bool UnPointer()
        {
            if (!IsPointer) return false;
            typeName = typeName.Substring(0, typeName.Length - 1);
            UnPointered = true;
            return true;
        }

        internal string TypeName(bool header)
        {
            // If we are a header, return a templated typename.
            // Otherwise, we should never return a templated typename.
            if (!string.IsNullOrEmpty(templatedName) && header)
                return templatedName;
            return typeName + _suffix;
        }

        internal void Template(string newName)
        {
            templatedName = newName;
        }

        public override string ToString() => throw new NotSupportedException("Not implemented! Did you mean to use TypeName( ?");
    }
}