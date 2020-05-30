using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
{
    public class Parameter
    {
        public TypeRef Type { get; }

        public string Name { get; } = null;
        public ParameterFlags Flags { get; } = ParameterFlags.None;

        public Parameter(string innard)
        {
            var spl = innard.Split(' ');
            int typeIndex = 1;
            if (spl[0] == "ref")
            {
                Flags = ParameterFlags.Ref;
            }
            else if (spl[0] == "out")
            {
                Flags = ParameterFlags.Out;
            }
            else if (spl[0] == "in")
            {
                Flags = ParameterFlags.In;
            }
            else
            {
                Flags = ParameterFlags.None;
                typeIndex = 0;
            }

            Type = new TypeRef(TypeRef.FromMultiple(spl, typeIndex, out int res, 1, " "), false);
            if (res + 1 < spl.Length)
            {
                Name = spl[res + 1];
            }
        }

        public Parameter(ParameterDefinition def)
        {
            Type = new TypeRef(def.ParameterType);
            Name = def.Name;
            Flags |= def.IsIn ? ParameterFlags.In : ParameterFlags.None;
            Flags |= def.IsOut ? ParameterFlags.Out : ParameterFlags.None;
            Flags |= def.ParameterType.IsByReference ? ParameterFlags.Ref : ParameterFlags.None;
        }

        public override string ToString()
        {
            string s = "";
            if (Flags != ParameterFlags.None)
            {
                s = $"{Flags.ToString().ToLower()} ";
            }
            s += $"{Type}";
            if (Name != null)
            {
                s += $" {Name}";
            }
            return s;
        }
    }

    public enum FormatParameterMode
    {
        Normal = 0,
        Types = 1,
        Names = 2
    }

    public static class ParameterExtensions
    {
        public static string FormatParameters(this List<Parameter> parameters, List<string> resolvedNames = null, FormatParameterMode mode = FormatParameterMode.Normal)
        {
            var s = "";
            for (int i = 0; i < parameters.Count; i++)
            {
                var nameStr = "";
                if (mode != FormatParameterMode.Types)
                {
                    nameStr = $"{parameters[i].Name}";
                    if (mode.HasFlag(FormatParameterMode.Names) && string.IsNullOrWhiteSpace(nameStr))
                    {
                        nameStr = $"param_{i}";
                    }
                }
                if (mode == FormatParameterMode.Names)
                {
                    // Only names
                    s += $"{nameStr}";
                }
                else if (mode == FormatParameterMode.Types)
                {
                    // Only types
                    if (resolvedNames != null)
                    {
                        s += $"{resolvedNames[i]}";
                    }
                    else
                    {
                        // Includes ref modifier
                        s += $"{parameters[i]}";
                    }
                }
                else
                {
                    // Types and names
                    if (resolvedNames != null)
                    {
                        s += $"{resolvedNames[i]} {nameStr}";
                    }
                    else
                    {
                        // Does not include ref modifier
                        s += $"{parameters[i].Type} {nameStr}";
                    }
                }
                if (i != parameters.Count - 1)
                {
                    s += ", ";
                }
            }
            return s;
        }
    }
}