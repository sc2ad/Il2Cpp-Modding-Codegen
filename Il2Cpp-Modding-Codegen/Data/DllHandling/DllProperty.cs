using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    public class DllProperty
    {
        public PropertyDefinition Property { get; }

        public DllProperty(PropertyDefinition p)
        {
            Property = p;
        }
    }
}