using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Data.DllHandling;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppMethodSerializer : ISerializer<IMethod>
    {
        private static readonly HashSet<string> IgnoredMethods = new HashSet<string>() { "op_Implicit", "op_Explicit" };
        private bool _asHeader;
        private SerializationConfig _config;

        private Dictionary<IMethod, string> _resolvedTypeNames = new Dictionary<IMethod, string>();
        private Dictionary<IMethod, string> _declaringTypeNames = new Dictionary<IMethod, string>();
        private Dictionary<IMethod, List<string>> _parameterMaps = new Dictionary<IMethod, List<string>>();
        private Dictionary<TypeRef, string> _declaringFullyQualified = new Dictionary<TypeRef, string>();
        private Dictionary<TypeRef, bool> _isInterface = new Dictionary<TypeRef, bool>();

        public CppMethodSerializer(SerializationConfig config, bool asHeader = true)
        {
            _config = config;
            _asHeader = asHeader;
        }

        public void PreSerialize(ISerializerContext context, IMethod method)
        {
            if (method.DeclaringType.IsGenericTemplate && !_asHeader)
                // Need to create the method ENTIRELY in the header, instead of split between the C++ and the header
                return;

            // Get the fully qualified name of the context
            if (!_declaringFullyQualified.ContainsKey(method.DeclaringType))
            {
                _declaringFullyQualified.Add(method.DeclaringType, context.GetNameFromReference(method.DeclaringType, ForceAsType.Literal));
                _isInterface.Add(method.DeclaringType, context.Types.Resolve(method.DeclaringType)?.Type == TypeEnum.Interface);
            }
            if (method.Generic)
                // Skip generic methods
                return;
            // We need to forward declare/include all types that are either returned from the method or are parameters
            _resolvedTypeNames.Add(method, context.GetNameFromReference(method.ReturnType));
            // The declaringTypeName needs to be a reference, even if the type itself is a value type.
            _declaringTypeNames.Add(method, context.GetNameFromReference(method.DeclaringType, ForceAsType.Pointer));
            var parameterMap = new List<string>();
            foreach (var p in method.Parameters)
            {
                string s;
                if (p.Flags != ParameterFlags.None)
                    // TODO: ParameterFlags.In can be const&
                    s = context.GetNameFromReference(p.Type, ForceAsType.Reference, mayNeedComplete: method.DeclaringType.IsGenericTemplate);
                else
                    s = context.GetNameFromReference(p.Type, mayNeedComplete: method.DeclaringType.IsGenericTemplate);
                parameterMap.Add(s);
            }
            _parameterMaps.Add(method, parameterMap);
        }

        private bool IsOverride(IMethod method)
        {
            return (method.ImplementedFrom != null) && !method.ImplementedFrom.Equals(method.DeclaringType);
        }

        private string WriteMethod(bool staticFunc, IMethod method, bool namespaceQualified)
        {
            // If the method is an instance method, first parameter should be a pointer to the declaringType.
            string paramString = "";
            var ns = "";
            var preRetStr = "";
            var overrideStr = "";
            if (namespaceQualified)
                ns = _declaringFullyQualified[method.DeclaringType] + "::";
            else
            {
                if (staticFunc)
                    preRetStr += "static ";
                if (_isInterface[method.DeclaringType] || IsOverride(method))
                {
                    preRetStr += "virtual ";
                    if (IsOverride(method))
                        overrideStr += " override";
                    else
                        overrideStr += " = 0";
                }
            }
            // Returns an optional
            // TODO: Should be configurable
            var retStr = _resolvedTypeNames[method];
            if (!method.ReturnType.IsVoid())
            {
                if (_config.OutputStyle == OutputStyle.Normal)
                    retStr = "std::optional<" + retStr + ">";
            }
            // Handles i.e. ".ctor"
            var nameStr = method.Name;
            if (nameStr.StartsWith(".")) nameStr = nameStr.ReplaceFirst(".", "_");
            if (nameStr.StartsWith("<")) nameStr = nameStr.ReplaceFirst("<", "$").ReplaceFirst(">", "$");
            var copy = nameStr;
            nameStr = nameStr.Replace('<', '$').Replace('>', '$').Replace('.', '_');
            if (nameStr != copy)
                overrideStr = "";
            return $"{preRetStr}{retStr} {ns}{nameStr}({paramString + method.Parameters.FormatParameters(_parameterMaps[method], FormatParameterMode.Names | FormatParameterMode.Types)}){overrideStr}";
        }

        // Write the method here
        public void Serialize(IndentedTextWriter writer, IMethod method)
        {
            if (method.DeclaringType.IsGenericTemplate && !_asHeader)
                // Need to create the method ENTIRELY in the header, instead of split between the C++ and the header
                return;

            if (!_resolvedTypeNames.ContainsKey(method))
                // In the event we have decided to not parse this method (in PreSerialize) don't even bother.
                return;
            if (_resolvedTypeNames[method] == null)
                throw new UnresolvedTypeException(method.DeclaringType, method.ReturnType);
            if (_declaringTypeNames[method] == null)
                throw new UnresolvedTypeException(method.DeclaringType, method.DeclaringType);
            var val = _parameterMaps[method].FindIndex(s => s == null);
            if (val != -1)
                throw new UnresolvedTypeException(method.DeclaringType, method.Parameters[val].Type);
            if (IgnoredMethods.Contains(method.Name) || _config.BlacklistMethods.Contains(method.Name))
                return;

            bool writeContent = !_asHeader || method.DeclaringType.IsGeneric;

            if (_asHeader)
            {
                var methodString = "";
                bool staticFunc = false;
                foreach (var spec in method.Specifiers)
                {
                    methodString += $"{spec} ";
                    if (spec.Static)
                    {
                        staticFunc = true;
                    }
                }
                methodString += $"{method.ReturnType} {method.Name}({method.Parameters.FormatParameters()})";
                methodString += $" // Offset: 0x{method.Offset:X}";
                writer.WriteLine($"// {methodString}");
                if (method.ImplementedFrom != null)
                    writer.WriteLine($"// Implemented from: {method.ImplementedFrom}");
                if (!writeContent)
                    writer.WriteLine(WriteMethod(staticFunc, method, false) + ";");
            }
            else
            {
                writer.WriteLine($"// Autogenerated method: {method.DeclaringType}.{method.Name}");
            }
            if (writeContent)
            {
                bool isStatic = method.Specifiers.IsStatic();
                // Write the qualified name if not in the header
                writer.WriteLine(WriteMethod(isStatic, method, !_asHeader) + " {");
                writer.Indent++;
                var s = "";
                var innard = "";
                var macro = "RET_V_UNLESS(";
                if (_config.OutputStyle == OutputStyle.CrashUnless)
                    macro = "CRASH_UNLESS(";
                if (!method.ReturnType.IsVoid())
                {
                    s = "return ";
                    innard = $"<{_resolvedTypeNames[method]}>";
                    if (_config.OutputStyle != OutputStyle.CrashUnless) macro = "";
                }

                var macroEnd = string.IsNullOrEmpty(macro) ? "" : ")";
                if (!string.IsNullOrEmpty(macro) && innard.Contains(","))
                {
                    macro += "(";
                    macroEnd += ")";
                }

                // TODO: Replace with RET_NULLOPT_UNLESS or another equivalent (perhaps literally just the ret)
                s += $"{macro}il2cpp_utils::RunMethod{innard}(";
                if (!isStatic)
                {
                    s += "this, ";
                }
                else
                {
                    // TODO: Check to ensure this works with non-generic methods in a generic type
                    s += $"\"{method.DeclaringType.Namespace}\", \"{method.DeclaringType.Name}\", ";
                }
                var paramString = method.Parameters.FormatParameters(_parameterMaps[method], FormatParameterMode.Names);
                if (!string.IsNullOrEmpty(paramString))
                    paramString = ", " + paramString;
                s += $"\"{method.Name}\"{paramString}){macroEnd};";
                // Write method with return
                writer.WriteLine(s);
                // Close method
                writer.Indent--;
                writer.WriteLine("}");
            }
            writer.Flush();
        }
    }
}