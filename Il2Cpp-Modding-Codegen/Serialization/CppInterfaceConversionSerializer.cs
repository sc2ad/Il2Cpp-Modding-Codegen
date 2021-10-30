using Il2CppModdingCodegen.CppSerialization;
using Il2CppModdingCodegen.Data.DllHandling;
using Il2CppModdingCodegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppInterfaceConversionSerializer : ISerializer<InterfaceImplementation, CppTypeWriter>
    {
        private readonly ConcurrentDictionary<InterfaceImplementation, State> stateMap = new();

        private readonly struct State
        {
            public readonly string name;

            public State(string n)
            {
                name = n;
            }
        }

        public void Resolve(CppContext context, InterfaceImplementation t)
        {
            var iName = context.GetCppName(t.InterfaceType, true, true, CppContext.NeedAs.BestMatch);
            stateMap.TryAdd(t, new State(iName));
        }

        public void Write(CppTypeWriter writer, InterfaceImplementation t)
        {
            if (!stateMap.TryGetValue(t, out var st))
            {
                throw new InvalidOperationException($"Interface: {t} must be resolved first!");
            }
            foreach (var ca in t.CustomAttributes)
            {
                var loc = new DllCustomAttributeData(ca);
                writer.WriteComment($"[{loc.Name}] Offset: {loc.Offset}");
            }
            writer.WriteComment($"Interface: {t.InterfaceType.FullName}");
            // Conversion operator now!
            using var w = writer.OpenMethod("operator auto const", "");
            w.WriteDeclaration($"return *reinterpret_cast<{st.name}>(this)");
        }
    }
}