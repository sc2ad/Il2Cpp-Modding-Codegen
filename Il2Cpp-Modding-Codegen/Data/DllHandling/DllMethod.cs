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
        public string Name { get; private set; }
        public string Il2CppName { get; }
        public List<Parameter> Parameters { get; } = new List<Parameter>();
        public bool Generic { get; }
        public IReadOnlyList<TypeRef> GenericParameters { get; }

        // Use the specific hash comparer to ensure validity!
        private static readonly DllMethodDefinitionHash comparer = new DllMethodDefinitionHash();

        private static readonly Dictionary<MethodDefinition, DllMethod> cache = new Dictionary<MethodDefinition, DllMethod>(comparer);

        public static DllMethod From(MethodDefinition def, ref HashSet<MethodDefinition> mappedBaseMethods)
        {
            // Note that TryGetValue is now significantly slower due to hash collisions and equality checks being expensive.
            // Before, it was simply pointers.
            if (cache.TryGetValue(def, out var m))
                return m;
            return new DllMethod(def, ref mappedBaseMethods);
        }

        private DllMethod(MethodDefinition m, ref HashSet<MethodDefinition> mappedBaseMethods)
        {
            cache.Add(m, this);
            This = m;
            // Il2CppName is the MethodDefinition Name (hopefully we don't need to convert it for il2cpp, but we might)
            Il2CppName = m.Name;
            Name = m.Name;
            Parameters.AddRange(m.Parameters.Select(p => new Parameter(p)));
            Specifiers.AddRange(DllSpecifierHelpers.From(m));
            // This is not necessary: m.GenericParameters.Any(param => !m.DeclaringType.GenericParameters.Contains(param));
            Generic = m.HasGenericParameters;
            GenericParameters = m.GenericParameters?.Select(DllTypeRef.From).ToList();

            // This may not always be the case, we could have a special name in which case we have to do some sorcery
            // Grab the special name, grab the type from the special name
            int idxDot = Name.LastIndexOf('.');
            if (idxDot >= 2)
            {
                // Call a utilities function for converting a special name method to a proper base method
                var baseMethod = m.GetSpecialNameBaseMethod(out var iface, idxDot);
                if (!mappedBaseMethods.Add(baseMethod))
                    throw new InvalidOperationException($"Base method: {baseMethod} has already been overriden!");
                BaseMethod = From(baseMethod, ref mappedBaseMethods);
                ImplementedFrom = DllTypeRef.From(iface);
            }
            else
            {
                var baseMethod = m.GetBaseMethod();
                if (baseMethod == m)
                {
                    var baseMethods = m.GetBaseMethods();
                    if (baseMethods.Count > 0)
                        HidesBase = true;
                    // We need to check here SPECIFICALLY for a method in our declaring type that shares the same name as us, since we could have the same BaseMethod as it.
                    // If either ourselves or a method of the same safe name (after . prefixes) exists, we need to ensure that only the one with the dots gets the base method
                    // It correctly describes.
                    // Basically, we need to take all our specially named methods on our type that have already been defined and remove them from our current list of baseMethods.
                    // We should only ever have baseMethods of methods that are of methods that we haven't already used yet.
                    if (baseMethods.Count > 0)
                        foreach (var baseM in mappedBaseMethods)
                            baseMethods.Remove(baseM);
                    if (baseMethods.Count > 0)
                        baseMethod = baseMethods.First();
                }
                if (baseMethod != m)
                {
                    if (!mappedBaseMethods.Add(baseMethod))
                        throw new InvalidOperationException($"Base method: {baseMethod} has already been overriden!");
                    BaseMethod = From(baseMethod, ref mappedBaseMethods);
                }
            }
            if (BaseMethod != null)
            {
                // TODO: This may not be true for generic methods. Should ensure validity for IEnumerator<T> methods
                // This method is an implemented/overriden method.
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
        }

        public override string ToString()
        {
            return $"{ReturnType} {Name}({Parameters.FormatParameters()})";
        }
    }
}