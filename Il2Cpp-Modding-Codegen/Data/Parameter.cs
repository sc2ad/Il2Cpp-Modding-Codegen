using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Data
{
    public class Parameter
    {
        public TypeReference Type { get; }
        public string Name { get; } = "";
        public ParameterModifier Modifier { get; } = ParameterModifier.None;

        internal Parameter(ParameterDefinition def)
        {
            Type = def.ParameterType;
            Name = def.Name;
            if (def.IsIn)
                Modifier = ParameterModifier.In;
            else if (def.IsOut)
                Modifier = ParameterModifier.Out;
            else if (def.ParameterType.IsByReference)
                Modifier = ParameterModifier.Ref;
            else if (def.CustomAttributes.Any(a => a.AttributeType.FullName == typeof(ParamArrayAttribute).FullName))
                Modifier = ParameterModifier.Params;
            // TODO: capture and print default argument values?
        }

        public override string ToString() => ToString(true);

        internal string ToString(bool name)
        {
            string s = "";
            if (Modifier != ParameterModifier.None)
                s = $"{Modifier.ToString().ToLower()} ";
            s += $"{Type}";
            if (name && Name != null)
                s += $" {Name}";
            return s;
        }
    }

    [Flags]
    public enum ParameterFormatFlags
    {
        Types = 1,
        Names = 2,
        Normal = Types | Names,
    }

    public static class ParameterExtensions
    {
        internal static string PrintParameter(this (MethodTypeContainer container, ParameterModifier modifier) param, bool header, bool wantWrappers = false)
        {
            var s = param.container.TypeName(header);
            if (param.modifier != ParameterModifier.None && param.modifier != ParameterModifier.Params)
                if (!wantWrappers)
                    s += "&";
                else
                    s = "ByRef<" + s + ">";
            return s;
        }

        internal static string FormatParameters(this List<Parameter> parameters, HashSet<string>? illegalNames = null,
            List<(MethodTypeContainer, ParameterModifier)>? resolvedNames = null, ParameterFormatFlags mode = ParameterFormatFlags.Normal, bool header = false,
            Func<(MethodTypeContainer, ParameterModifier), string, string>? nameOverride = null, bool wantWrappers = false)
        {
            var s = "";
            for (int i = 0; i < parameters.Count; i++)
            {
                if (resolvedNames != null && resolvedNames[i].Item1.Skip)
                    continue;
                string nameStr = "";
                if (mode.HasFlag(ParameterFormatFlags.Names))
                {
                    nameStr = parameters[i].Name;
                    if (string.IsNullOrWhiteSpace(nameStr))
                        nameStr = $"param_{i}";
                    while (illegalNames?.Contains(nameStr) ?? false)
                        nameStr = "_" + nameStr;
                }
                nameStr = nameStr.Replace('<', '$').Replace('>', '$');
                if (mode == ParameterFormatFlags.Names)
                {
                    if (resolvedNames != null)
                    {
                        var container = resolvedNames[i].Item1;
                        if (container.ExpandParams)
                        {
                            if (!container.HasTemplate)
                                nameStr = $"::ArrayW<{container.ElementType}>({nameStr})";
                            else
                            {
                                if (!container.TypeName(true).Contains("..."))
                                    throw new ArgumentException($"resolvedNames[{i}]'s {nameof(MethodTypeContainer)} has ExpandParams " +
                                        "and a Template name that is NOT a ...TArgs style name!");
                                nameStr = $"{{{nameStr}...}}";
                            }
                        }
                        else if (container.UnPointered)
                            nameStr = "&" + nameStr;
                    }
                    // Only names
                    if (resolvedNames != null && nameOverride != null)
                        s += nameOverride(resolvedNames[i], nameStr);
                    else
                        s += nameStr;
                }
                else if (mode == ParameterFormatFlags.Types)
                {
                    // Only types
                    if (resolvedNames != null)
                        s += $"{resolvedNames[i].PrintParameter(header, wantWrappers)}";
                    else
                        // Includes modifiers
                        s += $"{parameters[i].ToString(false)}";
                }
                else
                {
                    // Types and names
                    if (resolvedNames != null)
                        s += $"{resolvedNames[i].PrintParameter(header, wantWrappers)} {nameStr}";
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