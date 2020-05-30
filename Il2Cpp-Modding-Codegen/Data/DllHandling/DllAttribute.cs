using Il2Cpp_Modding_Codegen.Parsers;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    internal class DllAttribute : IAttribute
    {
        public string Name { get; }
        public int RVA { get; }
        public int Offset { get; }
        public int VA { get; }

        public DllAttribute(CustomAttribute attribute)
        {
            // These parameters are unknown for attributes besides the attribute that has all three in its constructor
            // Name is the name field of the attribute
            Name = string.Empty;
            RVA = -1;
            Offset = -1;
            VA = -1;
            if (attribute.Fields.Count == 3)
            {
                foreach (var f in attribute.Fields)
                {
                    if (f.Name == "Name")
                        Name = f.Argument.Value as string;
                    if (f.Name == "RVA" || f.Name == "Offset" || f.Name == "VA")
                        RVA = Convert.ToInt32(f.Argument.Value as string, 16);
                }
            }
        }

        public override string ToString()
        {
            return $"[{Name}] // Offset: 0x{Offset:X}";
        }
    }
}