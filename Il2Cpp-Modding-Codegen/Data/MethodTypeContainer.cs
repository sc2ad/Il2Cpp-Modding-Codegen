using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
{
    public class MethodTypeContainer
    {
        private string typeName;
        private string templatedName;

        internal MethodTypeContainer(string t)
        {
            typeName = t;
        }

        internal string TypeName(bool header)
        {
            // If we are a header, return a templated typename.
            // Otherwise, we should never return a templated typename.
            if (!string.IsNullOrEmpty(templatedName) && header)
                return templatedName;
            return typeName;
        }

        internal void Template(string newName)
        {
            templatedName = newName;
        }

        public override string ToString() => throw new NotSupportedException("Not implemented! Did you mean to use TypeName( ?");
    }
}