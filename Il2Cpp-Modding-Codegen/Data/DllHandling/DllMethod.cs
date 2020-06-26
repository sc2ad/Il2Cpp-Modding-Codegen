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
        public bool HidesBase { get; }
        public TypeRef OverriddenFrom { get; }
        public string Name { get; private set; }
        public string Il2CppName { get; }
        public List<Parameter> Parameters { get; } = new List<Parameter>();
        public bool Generic { get; }

        private static Dictionary<MethodDefinition, DllMethod> cache = new Dictionary<MethodDefinition, DllMethod>();
        private static Dictionary<MethodDefinition, string> toRename = new Dictionary<MethodDefinition, string>();

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
            cache.Add(m, this);
            This = m;
            // Il2CppName is the MethodDefinition Name (hopefully we don't need to convert it for il2cpp, but we might)
            Il2CppName = m.Name;
            Name = m.Name;
            int idxDot = Name.LastIndexOf(".");
            if (idxDot >= 2)  // ".ctor" doesn't count
            {
                var typeStr = Name.Substring(0, idxDot);
                var iface = FindInterface(m.DeclaringType, typeStr);
                if (iface is null)
                    throw new Exception($"For method {m}: failed to get TypeReference for ImplementedFrom {typeStr}");

                ImplementedFrom = DllTypeRef.From(iface);
                // Set tName to method name only
                var tName = Name.Substring(idxDot + 1);
                var implementedMethod = iface.Resolve().Methods.Where(im => im.Name == tName).Single();
                // Set Name to safe Il2CppName
                Name = DllTypeRef.From(implementedMethod.DeclaringType).GetQualifiedName().Replace("::", "_") + "_" + tName;
                // We need to ensure that the implementedMethod is aware that its Il2CppName should be set to our Il2CppName (all methods should match!)
                if (cache.TryGetValue(implementedMethod, out var implementedDllMethod))
                    // TODO: We want to grab the safe name from our cached method if it has been set, otherwise we set it to our method's Name
                    implementedDllMethod.Name = Name;
                else if (!toRename.ContainsKey(implementedMethod))
                    // If we don't have it in our list to rename, add it
                    toRename.Add(implementedMethod, Name);
                else
                {
                    // Otherwise, assume we have already renamed it. In such a case, we may need to set our Name to our implementing Il2CppName
                    // This will be the case if we have an implementing method that sometimes does not have the specialname flag.
                    Console.WriteLine($"Already renamed method: {implementedMethod} to Name: {Name}");
                }
                // In all cases, Name should not have any generic parameters. If it does, we need to change that (either here or on serialization side?)
                if (Name.Contains("<"))
                    Console.WriteLine($"Method: {m} on type: {DeclaringType} has Name: {Name} which has a generic parameter!");
            }

            if (toRename.TryGetValue(m, out var nameStr))
                Name = nameStr;

            var baseMethods = m.GetBaseMethods();
            if (baseMethods.Count > 0)
                HidesBase = true;

            MethodDefinition baseMethod = m.GetBaseMethod();
            if ((baseMethod == m) && baseMethods.Count == 1)
                baseMethod = baseMethods.Single();
            if (baseMethod != m)
                OverriddenFrom = DllTypeRef.From(baseMethod.DeclaringType);

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