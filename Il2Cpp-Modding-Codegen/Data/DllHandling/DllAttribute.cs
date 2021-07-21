using Mono.Cecil;
using System;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    internal class DllAttribute : IAttribute
    {
        // Name is the name field of the attribute
        public string Name { get; } = "";

        public int RVA { get; } = -1;
        public int Offset { get; } = -1;
        public int VA { get; } = -1;

        internal DllAttribute(CustomAttribute attribute)
        {
            // These parameters are unknown for attributes besides the attribute that has all three in its constructor
            if (attribute.Fields.Count == 3)
                foreach (var f in attribute.Fields)
                {
                    if (f.Name == "Name")
                        Name = (string)f.Argument.Value;
                    else if (f.Name == "RVA" || f.Name == "Offset" || f.Name == "VA")
                    {
                        var val = Convert.ToInt32(f.Argument.Value as string, 16);
                        if (f.Name == "RVA") RVA = val;
                        else if (f.Name == "Offset") Offset = val;
                        else if (f.Name == "VA") VA = val;
                    }
                }
            else if (attribute.AttributeType.FullName != "Il2CppInspector.Dll.TokenAttribute")
                // Ignore TokenAttribute
                Name = attribute.AttributeType.Name;
        }

        public override string ToString() => $"[{Name}] // Offset: 0x{Offset:X}";
    }
}