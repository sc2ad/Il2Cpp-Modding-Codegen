using Mono.Cecil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public IMethod BaseMethod { get; private set; }
        public List<IMethod> ImplementingMethods { get; } = new List<IMethod>();
        public bool HidesBase { get; }
        public TypeRef OverriddenFrom { get; }
        public string Name { get; private set; }
        public string Il2CppName { get; }
        public List<Parameter> Parameters { get; } = new List<Parameter>();
        public bool Generic { get; }

        private static Dictionary<MethodDefinition, DllMethod> cache = new Dictionary<MethodDefinition, DllMethod>();

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

        public static DllMethod From(MethodDefinition def)
        {
            if (cache.TryGetValue(def, out var m))
                return m;
            return new DllMethod(def);
        }

        private DllMethod(MethodDefinition m)
        {
            cache.Add(m, this);
            This = m;
            // Il2CppName is the MethodDefinition Name (hopefully we don't need to convert it for il2cpp, but we might)
            Il2CppName = m.Name;
            Name = m.Name;

            var baseMethods = m.GetBaseMethods();
            if (baseMethods.Count > 0)
                HidesBase = true;
            MethodDefinition baseMethod = m.GetBaseMethod();
            if (baseMethod == m && baseMethods.Count == 1)
                baseMethod = baseMethods.Single();
            if (baseMethod != m)
                BaseMethod = From(baseMethod);

            // This may not always be the case, we could have a special name in which case we have to do some sorcery
            // Grab the special name, grab the type from the special name
            int idxDot = Name.LastIndexOf('.');
            if (idxDot >= 2)
            {
                var typeStr = Name.Substring(0, idxDot);
                var iface = FindInterface(m.DeclaringType, typeStr);
                ImplementedFrom = DllTypeRef.From(iface);
                var tName = Name.Substring(idxDot + 1);
                baseMethod = iface.Resolve().Methods.Where(im => im.Name == tName).Single();
                BaseMethod = From(baseMethod);
            }

            if (BaseMethod != null)
            {
                // TODO: This may not be true for generic methods. Should ensure validity for IEnumerator<T> methods
                // This method is an overriden method.
                OverriddenFrom = BaseMethod.DeclaringType;
                ImplementedFrom = BaseMethod.DeclaringType;
                // Add ourselves to our BaseMethod's ImplementingMethods
                BaseMethod.ImplementingMethods.Add(this);
            }

            ReturnType = DllTypeRef.From(m.ReturnType);
            DeclaringType = DllTypeRef.From(m.DeclaringType);
            // This is a very rare condition that we need to handle if it ever happens, but for now just log it
            if (m.HasOverrides)
                Console.WriteLine($"{m}, Overrides: {string.Join(", ", m.Overrides)}");

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