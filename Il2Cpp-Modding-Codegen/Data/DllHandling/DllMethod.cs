using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    public class DllMethod
    {
        public MethodDefinition Method { get; }
        public string Name { get; }
        public string Il2CppName { get; }
        public bool IsVirtual { get; }
        public int RVA { get; } = -1;
        public int Offset { get; } = -1;
        public int VA { get; } = -1;
        public int Slot { get; } = -1;

        public DllMethod(MethodDefinition m)
        {
            if (m is null)
                throw new ArgumentNullException(nameof(m));
            Method = m;
            Il2CppName = m.Name;
            Name = m.Name.Replace('(', '_').Replace(')', '_');

            IsVirtual = m.IsVirtual || m.IsAbstract;
            foreach (var ca in m.CustomAttributes)
            {
                if (ca.AttributeType.Name == "AddressAttribute")
                {
                    foreach (var f in ca.Fields)
                    {
                        var val = Convert.ToInt32(f.Argument.Value as string, 16);
                        switch (f.Name)
                        {
                            case "RVA":
                                RVA = val;
                                break;

                            case "Offset":
                                Offset = val;
                                break;

                            case "VA":
                                VA = val;
                                break;

                            case "Slot":
                                Slot = val;
                                break;

                            default:
                                throw new InvalidOperationException("Unknown field name for AddressAttribute on method!");
                        }
                    }
                }
            }
        }
    }
}