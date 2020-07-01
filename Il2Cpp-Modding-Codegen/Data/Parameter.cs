using Il2Cpp_Modding_Codegen.Data.DllHandling;
using Il2Cpp_Modding_Codegen.Data.DumpHandling;
using Mono.Cecil;
using System.Collections.Generic;

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

            Type = new DumpTypeRef(DumpTypeRef.FromMultiple(spl, typeIndex, out int res, 1, " "));
            if (res + 1 < spl.Length)
            {
                Name = spl[res + 1];
            }
        }

        public Parameter(ParameterDefinition def)
        {
            Type = DllTypeRef.From(def.ParameterType);
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
        public static string PrintParameter(this (string, ParameterFlags) param, bool csharp = false)
        {
            var s = param.Item1;
            if (csharp)
            {
                if (param.Item2.HasFlag(ParameterFlags.Out))
                    s = "out " + s;
                if (param.Item2.HasFlag(ParameterFlags.Ref))
                    s = "ref " + s;
                if (param.Item2.HasFlag(ParameterFlags.In))
                    s = "in " + s;
            }
            else if (param.Item2 != ParameterFlags.None)
                s += "&";
            return s;
        }

        public static string FormatParameters(this List<Parameter> parameters, HashSet<string> illegalNames = null, List<(string, ParameterFlags)> resolvedNames = null, FormatParameterMode mode = FormatParameterMode.Normal, bool csharp = false)
        {
            var s = "";
            for (int i = 0; i < parameters.Count; i++)
            {
                var nameStr = "";
                if (mode != FormatParameterMode.Types)
                {
                    nameStr = parameters[i].Name;
                    if (mode.HasFlag(FormatParameterMode.Names) && string.IsNullOrWhiteSpace(nameStr))
                        nameStr = $"param_{i}";
                    while (illegalNames?.Contains(nameStr) is true)
                        nameStr = "_" + nameStr;
                }
                nameStr = nameStr.Replace('<', '$').Replace('>', '$');
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
                        s += $"{resolvedNames[i].PrintParameter(csharp)}";
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
                        s += $"{resolvedNames[i].PrintParameter(csharp)} {nameStr}";
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