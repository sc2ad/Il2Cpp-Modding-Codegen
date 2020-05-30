using Il2Cpp_Modding_Codegen.Data.DumpHandling;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    internal class DllSpecifierHelpers
    {
        // TODO: These methods are, put simply, horrible. They should not exist, instead the interface should be more generic.
        // As of now, because I am far too lazy, I am simply going to do this.
        // But in order to ensure that we can migrate away from it easier, this class literally does not exist.
        // Because we are doing this, we are using DumpSpecifiers for a DllSpecifier. Why? Because there are no DllSpecifiers
        // and there shouldn't be any.
        public static IEnumerable<ISpecifier> From(TypeDefinition def)
        {
            var list = new List<DumpSpecifier>();
            if (def.IsSealed) list.Add(new DumpSpecifier("sealed"));
            if (def.IsPublic) list.Add(new DumpSpecifier("public"));
            // Assume that private classes are nested classes ONLY.
            else list.Add(new DumpSpecifier("internal"));
            return list;
        }

        public static IEnumerable<ISpecifier> From(FieldDefinition def)
        {
            var list = new List<DumpSpecifier>();
            if (def.IsStatic) list.Add(new DumpSpecifier("static"));
            if (def.IsPublic) list.Add(new DumpSpecifier("public"));
            else if (def.IsFamily || def.IsFamilyOrAssembly)
            {
                list.Add(new DumpSpecifier("protected"));
                if (def.IsFamilyOrAssembly) list.Add(new DumpSpecifier("internal"));
            }
            else if (def.IsPrivate) list.Add(new DumpSpecifier("private"));

            if (def.IsInitOnly)
                // https://stackoverflow.com/questions/56179043/how-to-get-initial-value-of-field-by-mono-cecil ?
                if (def.HasConstant) list.Add(new DumpSpecifier("const"));
                else list.Add(new DumpSpecifier("readonly"));
            return list;
        }

        public static IEnumerable<ISpecifier> From(MethodDefinition def)
        {
            var list = new List<DumpSpecifier>();
            if (def.IsStatic) list.Add(new DumpSpecifier("static"));
            if (def.IsPublic) list.Add(new DumpSpecifier("public"));
            else if (def.IsFamily || def.IsFamilyOrAssembly)
            {
                list.Add(new DumpSpecifier("protected"));
                if (def.IsFamilyOrAssembly) list.Add(new DumpSpecifier("internal"));
            }
            else if (def.IsPrivate) list.Add(new DumpSpecifier("private"));

            if (def.GetBaseMethod() != null) list.Add(new DumpSpecifier("override"));
            return list;
        }

        public static IEnumerable<ISpecifier> From(PropertyDefinition def)
        {
            var list = new List<DumpSpecifier>();
            if (!def.HasThis) list.Add(new DumpSpecifier("static"));
            if (def.HasConstant) list.Add(new DumpSpecifier("const"));
            return list;
        }
    }
}