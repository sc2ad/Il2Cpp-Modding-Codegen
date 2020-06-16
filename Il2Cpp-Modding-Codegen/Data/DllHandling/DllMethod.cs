using Il2Cpp_Modding_Codegen.Parsers;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    internal class DllMethod : IMethod
    {
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public int RVA { get; }
        public int Offset { get; }
        public int VA { get; }
        public int Slot { get; }
        public TypeRef ReturnType { get; }
        public TypeRef DeclaringType { get; }
        public TypeRef ImplementedFrom { get; }
        public string Name { get; }
        public List<Parameter> Parameters { get; } = new List<Parameter>();
        public bool Generic { get; }

        public DllMethod(MethodDefinition m)
        {
            ReturnType = DllTypeRef.From(m.ReturnType);
            DeclaringType = DllTypeRef.From(m.DeclaringType);
            var baseMethod = m.GetBaseMethod();
            if (baseMethod != null)
                ImplementedFrom = DllTypeRef.From(baseMethod.DeclaringType);
            Name = m.Name;
            RVA = -1;
            Offset = -1;
            VA = -1;
            Slot = -1;
            if (m.HasCustomAttributes)
            {
                foreach (var ca in m.CustomAttributes)
                {
                    if (ca.AttributeType.Name == "AddressAttribute")
                    {
                        if (ca.Fields.Count >= 3)
                        {
                            for (int i = 0; i < ca.Fields.Count; i++)
                            {
                                var f = ca.Fields[i];
                                if (f.Name == "RVA" || f.Name == "Offset" || f.Name == "VA")
                                {
                                    var val = Convert.ToInt32(f.Argument.Value as string, 16);
                                    if (f.Name == "RVA") RVA = val;
                                    else if (f.Name == "Offset") Offset = val;
                                    else if (f.Name == "VA") VA = val;
                                }
                                else if (f.Name == "Slot")
                                    Slot = Convert.ToInt32(f.Argument.Value as string);
                            }
                        }
                    }
                    else
                    {
                        // Ignore the DummyDll attributes
                        Attributes.Add(new DllAttribute(ca));
                    }
                }
            }
            Parameters.AddRange(m.Parameters.Select(p => new Parameter(p)));
            Specifiers.AddRange(DllSpecifierHelpers.From(m));
            // This is not necessary: m.GenericParameters.Any(param => !m.DeclaringType.GenericParameters.Contains(param));
            Generic = m.HasGenericParameters;
        }

        public override string ToString()
        {
            var s = $"// Offset: 0x{Offset:X}\n\t";
            s += $"{ReturnType} {Name}({Parameters.FormatParameters()}) ";
            s += "{}";
            return s;
        }
    }
}