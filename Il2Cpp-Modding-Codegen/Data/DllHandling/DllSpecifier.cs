using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    internal class DllSpecifier : ISpecifier
    {
        public string Value { get; }
        public bool Static => Value == "static";
        public bool Private => Value == "private";
        public bool Internal => Value == "internal";
        public bool Public => Value == "public";
        public bool Sealed => Value == "sealed";
        // TODO: should apply to methods, properties, indexers and events
        // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override
        public bool Override => Value == "override";

        // TODO: should apply to field/properties "IsInitOnly"?
        // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/readonly
        // How to determine readonly struct?
        public bool Readonly => Value == "readonly";
        // TODO: should apply to only fields or variables
        // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/const
        public bool Const => Value == "const";

        public DllSpecifier(string specifier)
        {
            Value = specifier;
        }

        public override string ToString()
        {
            return Value;
        }

        internal static IEnumerable<ISpecifier> From(TypeDefinition def)
        {
            List<DllSpecifier> list = new List<DllSpecifier>();
            if (def.IsSealed)
            {
                // https://groups.google.com/forum/#!topic/mono-cecil/MtRTwHjPNu4
                if (def.IsAbstract) list.Add(new DllSpecifier("static"));
                else list.Add(new DllSpecifier("sealed"));
            }
            if (def.IsPublic || def.IsNestedPublic) list.Add(new DllSpecifier("public"));
            else if (!def.IsNested) list.Add(new DllSpecifier("internal"));
            else if (def.IsNestedFamily || def.IsNestedFamilyOrAssembly)
            {
                list.Add(new DllSpecifier("protected"));
                if (def.IsNestedFamilyOrAssembly) list.Add(new DllSpecifier("internal"));
            }
            else if (def.IsNestedPrivate) list.Add(new DllSpecifier("private"));

            // TODO: readonly struct?
            return list;
        }

        internal static IEnumerable<ISpecifier> From(FieldDefinition def)
        {
            List<DllSpecifier> list = new List<DllSpecifier>();
            if (def.IsStatic) list.Add(new DllSpecifier("static"));
            if (def.IsPublic) list.Add(new DllSpecifier("public"));
            else if (def.IsFamily || def.IsFamilyOrAssembly)
            {
                list.Add(new DllSpecifier("protected"));
                if (def.IsFamilyOrAssembly) list.Add(new DllSpecifier("internal"));
            }
            else if (def.IsPrivate) list.Add(new DllSpecifier("private"));

            if (def.IsInitOnly)
                // https://stackoverflow.com/questions/56179043/how-to-get-initial-value-of-field-by-mono-cecil ?
                if (def.HasConstant) list.Add(new DllSpecifier("const"));
                else list.Add(new DllSpecifier("readonly"));
            return list;
        }
    }
}