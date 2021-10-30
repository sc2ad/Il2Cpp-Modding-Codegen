using Il2CppModdingCodegen.CppSerialization;
using Il2CppModdingCodegen.Data.DllHandling;
using Il2CppModdingCodegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppTopTypeSerializer : ISerializer<TypeDefinition, CppStreamWriter>
    {
        private readonly IEnumerable<ISerializer<DllField, CppTypeWriter>> fieldSerializers;
        private readonly IEnumerable<ISerializer<DllMethod, CppTypeWriter>> methodSerializers;
        private readonly IEnumerable<ISerializer<InterfaceImplementation, CppTypeWriter>> interfaceSerializers;
        private readonly IEnumerable<ISerializer<TypeDefinition, CppTypeWriter>> nestedSerializers;

        private readonly Dictionary<TypeDefinition, State> typeParentMap = new();

        private readonly struct State
        {
            public readonly List<string> parentNames;
            public readonly string declaring;
            public readonly string type;
            public readonly string il2cppName;

            public State(List<string> p, string d, string t, string i)
            {
                parentNames = p;
                declaring = d;
                type = t;
                il2cppName = i;
            }
        }

        public CppTopTypeSerializer(IEnumerable<ISerializer<DllField, CppTypeWriter>> fs,
            IEnumerable<ISerializer<DllMethod, CppTypeWriter>> ms,
            IEnumerable<ISerializer<InterfaceImplementation, CppTypeWriter>> @is,
            IEnumerable<ISerializer<TypeDefinition, CppTypeWriter>> ns)
        {
            fieldSerializers = fs;
            methodSerializers = ms;
            interfaceSerializers = @is;
            nestedSerializers = ns;
        }

        public void Resolve(CppContext context, TypeDefinition t)
        {
            if (t is null)
                throw new ArgumentNullException(nameof(t));
            if (context is null)
                throw new ArgumentNullException(nameof(context));
            var parentName = "System::Object";
            if ((t.Namespace != "System" || t.Name != "ValueType") && t.BaseType is not null)
                parentName = context.GetCppName(t.BaseType, true, true, CppContext.NeedAs.Definition, CppContext.ForceAsType.Literal)!;
            List<string> lst;
            string declaring;
            if (t.DeclaringType is not null && !context.UnNested)
                // TODO: This is ONLY true if we do NOT handle unnesting, so this will have to change!
                throw new InvalidOperationException("Cannot top level resolve a nested type!");
            if (t.DeclaringType is not null && t.DeclaringType.HasGenericParameters)
            {
                lst = new List<string>
                {
                    parentName,
                    "::il2cpp_utils::il2cpp_type_check::NestedType"
                };
                context.NeedIl2CppUtils();
                declaring = context.GetCppName(t.DeclaringType, false, true, CppContext.NeedAs.Definition)!;
            }
            else
            {
                lst = new List<string>
                {
                    parentName
                };
                declaring = "";
            }
            State state = new(lst, declaring, context.GetCppName(t, false, false, CppContext.NeedAs.Definition, CppContext.ForceAsType.Literal)!, t.Name);
            lock (typeParentMap)
            {
                typeParentMap.Add(t, state);
            }

            foreach (var i in t.Interfaces)
            {
                foreach (var s in interfaceSerializers)
                {
                    s.Resolve(context, i);
                }
            }
            foreach (var n in t.NestedTypes)
            {
                foreach (var s in nestedSerializers)
                {
                    // We need to resolve with our correct nested context (just in case we out-place it later)
                    CppContext nestedCtx;
                    lock (CppContext.TypesToContexts)
                    {
                        CppContext.TypesToContexts.TryGetValue(n, out nestedCtx);
                    }
                    if (nestedCtx is null)
                        throw new InvalidOperationException("Somehow we have no context for our nested type?");
                    s.Resolve(nestedCtx, n);
                }
            }
            foreach (var f in t.Fields)
            {
                foreach (var s in fieldSerializers)
                {
                    s.Resolve(context, new DllField(f));
                }
            }
            foreach (var m in t.Methods)
            {
                foreach (var s in methodSerializers)
                {
                    s.Resolve(context, new DllMethod(m));
                }
            }
        }

        public void Write(CppStreamWriter writer, TypeDefinition t)
        {
            using var nsw = writer.OpenNamespace(CppContext.CppNamespace(t));
            nsw.WriteComment($"Autogenerated type: {t}");
            int token = -1;
            foreach (var ca in t.CustomAttributes)
            {
                var name = ca.AttributeType.Name;
                if (ca.AttributeType.Name == "TokenAttribute")
                {
                    token = Convert.ToInt32(ca.Fields.First().Argument.Value as string, 16);
                }
                else
                {
                    var loc = new DllCustomAttributeData(ca);
                    nsw.WriteComment($"[{loc.Name}] Offset: {loc.Offset}");
                }
            }
            nsw.WriteComment($"Token: 0x{token:X}");
            // Get the state
            if (!typeParentMap.TryGetValue(t, out var st))
                throw new InvalidOperationException("Cannot get parent name because it hasn't been resolved!");

            // Write the type definition
            string suffix = "";
            if (t.BaseType is not null)
            {
                suffix = ": " + string.Join("public ", st.parentNames.Where(s => !string.IsNullOrEmpty(s)));
            }
            // TODO: Write generic template here, along with correct nested name if UnNested = true, etc, etc.
            if (t.HasGenericParameters)
            {
                writer.WriteTemplate(t.GetTemplateLine());
            }
            // We have to check for UnNested here, since UnNested may have been changed prior
            var tName = st.type;
            bool inPlace;
            lock (CppContext.TypesToContexts)
            {
                inPlace = CppContext.TypesToContexts[t].InPlace;
            }
            if (inPlace)
            {
                int idx = tName.LastIndexOf("::");
                if (idx >= 0)
                    tName = tName[(idx + 2)..];
            }
            using var typeWriter = nsw.OpenType("struct", st.type, suffix);

            if (!string.IsNullOrEmpty(st.declaring))
            {
                typeWriter.WriteDeclaration($"using declaring_type = {st.declaring}");
                typeWriter.WriteDeclaration($"static constexpr std::string_view NESTED_NAME = \"{st.il2cppName}\"");
                typeWriter.WriteDeclaration($"static constexpr bool IS_VALUE_TYPE = {(t.IsValueType || t.IsEnum).ToString().ToLower()}");
            }

            foreach (var n in t.NestedTypes)
            {
                // We want to start by FD'ing all NON UnNested types.
                // UnNested types need to be handled separately, however.
                // UnNested types should be handled by their OWN passes!
                // They should be COMPLETELY IGNORED HERE!

                foreach (var s in nestedSerializers)
                {
                    s.Write(typeWriter, n);
                }
            }
            foreach (var i in t.Interfaces)
            {
                foreach (var s in interfaceSerializers)
                {
                    s.Write(typeWriter, i);
                }
            }
            foreach (var f in t.Fields)
            {
                foreach (var s in fieldSerializers)
                {
                    s.Write(typeWriter, new DllField(f));
                }
            }
            foreach (var m in t.Methods)
            {
                foreach (var s in methodSerializers)
                {
                    s.Write(typeWriter, new DllMethod(m));
                }
            }
        }
    }
}