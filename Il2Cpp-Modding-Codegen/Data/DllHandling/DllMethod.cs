using Il2Cpp_Modding_Codegen.Parsers;
using Il2Cpp_Modding_Codegen.Serialization;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    internal class DllMethod : IMethod
    {
        private MethodDefinition This;  // just to aid with debugging
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public int RVA { get; }
        public int Offset { get; }
        public int VA { get; }
        public int Slot { get; }
        public TypeRef ReturnType { get; }
        public TypeRef DeclaringType { get; }
        public TypeRef ImplementedFrom { get; } = null;
        public bool IsOverride { get; }
        public TypeRef OverriddenFrom { get; }
        public string Name { get; }
        public List<Parameter> Parameters { get; } = new List<Parameter>();
        public bool Generic { get; }

        TypeReference FindInterface(TypeReference type, string find)
        {
            if (type is null) return null;
            var typeStr = Regex.Replace(type.ToString(), @"`\d+", "").Replace('/', '.');
            if (typeStr == find)
                return type;

            var def = type.Resolve();
            if (def is null) return null;
            foreach (var iface in def.Interfaces)
            {
                var ret = FindInterface(iface.InterfaceType, find);
                if (ret != null) return ret;
            }
            return FindInterface(def.BaseType, find);
        }

        public DllMethod(MethodDefinition m)
        {
            This = m;
            IsOverride = m.IsVirtual && !m.IsNewSlot;
            var baseMethod = m.GetBaseMethod();
            if (baseMethod != m)
                OverriddenFrom = DllTypeRef.From(baseMethod.DeclaringType);

            ReturnType = DllTypeRef.From(m.ReturnType);
            DeclaringType = DllTypeRef.From(m.DeclaringType);
            // This is a very rare condition that we need to handle if it ever happens, but for now just log it
            if (m.HasOverrides)
                Console.WriteLine($"{m}, Overrides: {String.Join(", ", m.Overrides)}");

            Name = m.Name;
            int idxDot = Name.LastIndexOf(".");
            if (idxDot >= 2)  // ".ctor" doesn't count
            {
                var typeStr = Name.Substring(0, idxDot);
                var iface = FindInterface(m.DeclaringType, typeStr);
                if (iface is null)
                    throw new Exception($"For method {m}: failed to get TypeReference for ImplementedFrom {typeStr}");

                ImplementedFrom = DllTypeRef.From(iface);
                Name = Name.Substring(idxDot + 1);
            }

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