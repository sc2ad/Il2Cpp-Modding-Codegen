﻿using Il2CppModdingCodegen.Data.DumpHandling;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using System.Collections.Generic;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    internal class DllSpecifierHelpers
    {
        // TODO: These methods are, put simply, horrible. They should not exist, instead the interface should be more generic.
        // As of now, because I am far too lazy, I am simply going to do this.
        // But in order to ensure that we can migrate away from it easier, this class literally does not exist.
        // Because we are doing this, we are using DumpSpecifiers for a DllSpecifier. Why? Because there are no DllSpecifiers
        // and there shouldn't be any.
        internal static IEnumerable<ISpecifier> From(TypeDefinition def)
        {
            var list = new List<DumpSpecifier>();
            if (def.IsSealed)
                // https://groups.google.com/forum/#!topic/mono-cecil/MtRTwHjPNu4
                if (def.IsAbstract)
                    list.Add(new DumpSpecifier("static"));
                else
                    list.Add(new DumpSpecifier("sealed"));

            // http://www.programmersought.com/article/6494173120/
            if (def.IsPublic || def.IsNestedPublic)
                list.Add(new DumpSpecifier("public"));
            else if (!def.IsNested)
                list.Add(new DumpSpecifier("internal"));
            else if (def.IsNestedFamily || def.IsNestedFamilyOrAssembly)
            {
                list.Add(new DumpSpecifier("protected"));
                if (def.IsNestedFamilyOrAssembly)
                    list.Add(new DumpSpecifier("internal"));
            }
            else if (def.IsNestedPrivate)
                list.Add(new DumpSpecifier("private"));

            // TODO: readonly struct?
            return list;
        }

        internal static IEnumerable<ISpecifier> From(FieldDefinition def)
        {
            var list = new List<DumpSpecifier>();
            if (def.IsStatic)
                list.Add(new DumpSpecifier("static"));
            if (def.IsPublic)
                list.Add(new DumpSpecifier("public"));
            else if (def.IsFamily || def.IsFamilyOrAssembly)
            {
                list.Add(new DumpSpecifier("protected"));
                if (def.IsFamilyOrAssembly)
                    list.Add(new DumpSpecifier("internal"));
            }
            else if (def.IsPrivate)
                list.Add(new DumpSpecifier("private"));

            if (def.IsInitOnly)
                // https://stackoverflow.com/questions/56179043/how-to-get-initial-value-of-field-by-mono-cecil ?
                if (def.HasConstant)
                    list.Add(new DumpSpecifier("const"));
                else
                    list.Add(new DumpSpecifier("readonly"));
            return list;
        }

        internal static IEnumerable<ISpecifier> From(MethodDefinition def)
        {
            var list = new List<DumpSpecifier>();
            if (def.IsStatic)
                list.Add(new DumpSpecifier("static"));
            if (def.IsPublic)
                list.Add(new DumpSpecifier("public"));
            else if (def.IsFamily || def.IsFamilyOrAssembly)
            {
                list.Add(new DumpSpecifier("protected"));
                if (def.IsFamilyOrAssembly)
                    list.Add(new DumpSpecifier("internal"));
            }
            else if (def.IsPrivate)
                list.Add(new DumpSpecifier("private"));

            if (def.GetBaseMethod() != def)
                list.Add(new DumpSpecifier("override"));
            return list;
        }

        internal static IEnumerable<ISpecifier> From(PropertyDefinition def)
        {
            if (def.GetMethod != null)
                return From(def.GetMethod);
            else
                return From(def.SetMethod);
        }
    }
}
