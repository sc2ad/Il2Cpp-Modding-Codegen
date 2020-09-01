using Il2CppModdingCodegen.Data.DllHandling;
using Il2CppModdingCodegen.Data.DumpHandling;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Data
{
    public class Parameter
    {
        internal TypeRef Type { get; }
        internal string Name { get; } = "";
        internal ParameterFlags Flags { get; } = ParameterFlags.None;

        internal Parameter(string innard)
        {
            var spl = innard.Split(' ');
            int typeIndex = 1;
            if (Enum.TryParse<ParameterFlags>(spl[0], true, out var flags))
                Flags = flags;
            else
                typeIndex = 0;

            Type = new DumpTypeRef(DumpTypeRef.FromMultiple(spl, typeIndex, out int res, 1, " "));
            if (res + 1 < spl.Length)
                Name = spl[res + 1];
        }

        internal Parameter(ParameterDefinition def)
        {
            Type = DllTypeRef.From(def.ParameterType);
            Name = def.Name;
            Flags |= def.IsIn ? ParameterFlags.In : ParameterFlags.None;
            Flags |= def.IsOut ? ParameterFlags.Out : ParameterFlags.None;
            Flags |= def.ParameterType.IsByReference ? ParameterFlags.Ref : ParameterFlags.None;
            Flags |= def.CustomAttributes.Any(a => a.AttributeType.FullName == typeof(ParamArrayAttribute).FullName)
                ? ParameterFlags.Params : ParameterFlags.None;
        }

        public override string ToString() => ToString(true);

        internal string ToString(bool name)
        {
            string s = "";
            if (Flags != ParameterFlags.None)
                s = $"{Flags.GetFlagsString().ToLower()} ";
            s += $"{Type}";
            if (name && Name != null)
                s += $" {Name}";
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
        internal static string PrintParameter(this (MethodTypeContainer container, ParameterFlags flags) param,
            bool header, bool csharp = false)
        {
            var s = param.container.TypeName(header);
            if (csharp)
            {
                if (param.flags != ParameterFlags.None)
                    s = $"{param.flags.GetFlagsString().ToLower()} {s}";
            }
            else if (param.flags != ParameterFlags.None && !param.flags.HasFlag(ParameterFlags.Params))
                s += "&";
            return s;
        }

        internal static string FormatParameters(this List<Parameter> parameters, HashSet<string>? illegalNames = null,
            List<(MethodTypeContainer, ParameterFlags)>? resolvedNames = null, FormatParameterMode mode = FormatParameterMode.Normal,
            bool header = false, bool csharp = false)
        {
            var s = "";
            for (int i = 0; i < parameters.Count; i++)
            {
                if (resolvedNames != null && resolvedNames[i].Item1.Skip)
                    continue;
                string nameStr = "";
                if (mode != FormatParameterMode.Types)
                {
                    nameStr = parameters[i].Name;
                    if (mode.HasFlag(FormatParameterMode.Names) && string.IsNullOrWhiteSpace(nameStr))
                        nameStr = $"param_{i}";
                    while (illegalNames?.Contains(nameStr) ?? false)
                        nameStr = "_" + nameStr;
                }
                nameStr = nameStr.Replace('<', '$').Replace('>', '$');
                if (mode == FormatParameterMode.Names)
                {
                    if (resolvedNames != null && resolvedNames[i].Item1.UnPointered)
                        nameStr = "&" + nameStr;
                    // Only names
                    s += $"{nameStr}";
                }
                else if (mode == FormatParameterMode.Types)
                {
                    // Only types
                    if (resolvedNames != null)
                        s += $"{resolvedNames[i].PrintParameter(header, csharp)}";
                    else
                        // Includes modifiers
                        s += $"{parameters[i].ToString(false)}";
                }
                else
                {
                    // Types and names
                    if (resolvedNames != null)
                        s += $"{resolvedNames[i].PrintParameter(header, csharp)} {nameStr}";
                    else
                        // Includes modifiers
                        s += $"{parameters[i]}";
                }
                if (i != parameters.Count - 1)
                    s += ", ";
            }
            return s;
        }
    }
}
