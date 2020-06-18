using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Data.DllHandling;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppMethodSerializer : Serializer<IMethod>
    {
        private static readonly HashSet<string> IgnoredMethods = new HashSet<string>() { "op_Implicit", "op_Explicit" };
        private bool _isHeader;
        private SerializationConfig _config;

        private Dictionary<IMethod, string> _resolvedReturns = new Dictionary<IMethod, string>();
        private Dictionary<IMethod, List<(string, ParameterFlags)>> _parameterMaps = new Dictionary<IMethod, List<(string, ParameterFlags)>>();
        private string _declaringFullyQualified;

        public CppMethodSerializer(SerializationConfig config, bool isHeader = true)
        {
            _config = config;
            _isHeader = isHeader;
        }

        /// <summary>
        /// Returns whether the given method should be written as a definition or a declaration
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private bool NeedDefinition(IMethod method)
        {
            if (!_isHeader)
                // Always write the method definition in a .cpp file
                return true;
            // Only write definitions if the declaring type is generic
            return method.DeclaringType?.IsGeneric ?? false;
        }

        public override void PreSerialize(CppSerializerContext context, IMethod method)
        {
            if (method.Generic)
                // Skip generic methods
                return;
            // Get the fully qualified name of the context
            bool success = true;
            _declaringFullyQualified = context.QualifiedTypeName;
            // We need to forward declare everything used in methods (return types and parameters)
            // If we are writing the definition, we MUST define it
            var resolvedReturn = context.GetCppName(method.ReturnType, true, needAs: NeedDefinition(method) ? CppSerializerContext.NeedAs.Definition : CppSerializerContext.NeedAs.BestMatch);
            if (resolvedReturn is null)
                // If we fail to resolve the return type, we will simply add a null item to our dictionary.
                // However, we should not call Resolved(method)
                success = false;
            _resolvedReturns.Add(method, resolvedReturn);
            var parameterMap = new List<(string, ParameterFlags)>();
            foreach (var p in method.Parameters)
            {
                // If this is not a header, we MUST define it
                var s = context.GetCppName(p.Type, true, needAs: NeedDefinition(method) ? CppSerializerContext.NeedAs.Definition : CppSerializerContext.NeedAs.BestMatch);
                if (s is null)
                    // If we fail to resolve a parameter, we will simply add a (null, p.Flags) item to our mapping.
                    // However, we should not call Resolved(method)
                    success = false;
                parameterMap.Add((s, p.Flags));
            }
            _parameterMaps.Add(method, parameterMap);
            if (success)
                Resolved(method);
        }

        private string WriteMethod(bool staticFunc, IMethod method, bool namespaceQualified)
        {
            // If the method is an instance method, first parameter should be a pointer to the declaringType.
            string paramString = "";
            var ns = "";
            var staticString = "";
            if (namespaceQualified)
                ns = _declaringFullyQualified + "::";
            if (!namespaceQualified && staticFunc)
                staticString = "static ";
            // Returns an optional
            // TODO: Should be configurable
            var retStr = _resolvedReturns[method];
            if (!method.ReturnType.IsVoid())
            {
                if (_config.OutputStyle == OutputStyle.Normal)
                    retStr = "std::optional<" + retStr + ">";
            }
            // Handles i.e. ".ctor"
            var nameStr = method.Name.Replace('.', '_').Replace('<', '$').Replace('>', '$');
            return $"{staticString}{retStr} {ns}{nameStr}({paramString + method.Parameters.FormatParameters(_parameterMaps[method], FormatParameterMode.Names | FormatParameterMode.Types)})";
        }

        // Write the method here
        public override void Serialize(CppStreamWriter writer, IMethod method)
        {
            if (!_resolvedReturns.ContainsKey(method))
                // In the event we have decided to not parse this method (in PreSerialize) don't even bother.
                return;
            if (_resolvedReturns[method] == null)
                throw new UnresolvedTypeException(method.DeclaringType, method.ReturnType);
            var val = _parameterMaps[method].FindIndex(s => s.Item1 is null);
            if (val != -1)
                throw new UnresolvedTypeException(method.DeclaringType, method.Parameters[val].Type);
            if (IgnoredMethods.Contains(method.Name) || _config.BlacklistMethods.Contains(method.Name))
                return;

            if (method.DeclaringType.IsGeneric && !_isHeader)
                // Need to create the method ENTIRELY in the header, instead of split between the C++ and the header
                return;
            bool writeContent = NeedDefinition(method);

            if (_isHeader)
            {
                var methodComment = "";
                bool staticFunc = false;
                foreach (var spec in method.Specifiers)
                {
                    methodComment += $"{spec} ";
                    if (spec.Static)
                        staticFunc = true;
                }
                methodComment += $"{method.ReturnType} {method.Name}({method.Parameters.FormatParameters(csharp: true)})";
                methodComment += $" // Offset: 0x{method.Offset:X}";
                writer.WriteComment(methodComment);
                if (method.ImplementedFrom != null)
                    writer.WriteComment("Implemented from: " + method.ImplementedFrom);
                if (!writeContent)
                    writer.WriteDeclaration(WriteMethod(staticFunc, method, false));
            }
            else
            {
                writer.WriteComment("Autogenerated method: " + method.DeclaringType + "." + method.Name);
            }
            if (writeContent)
            {
                bool isStatic = method.Specifiers.IsStatic();
                // Write the qualified name if not in the header
                writer.WriteDefinition(WriteMethod(isStatic, method, !_isHeader));
                var s = "";
                var innard = "";
                var macro = "RET_V_UNLESS(";
                if (_config.OutputStyle == OutputStyle.CrashUnless)
                    macro = "CRASH_UNLESS(";
                if (!method.ReturnType.IsVoid())
                {
                    s = "return ";
                    innard = $"<{_resolvedReturns[method]}>";
                    if (_config.OutputStyle != OutputStyle.CrashUnless) macro = "";
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
                s += $"\"{method.Name}\"{paramString}){(!string.IsNullOrEmpty(macro) ? ")" : "")};";
                // Write method with return
                writer.WriteLine(s);
                // Close method
                writer.CloseDefinition();
            }
            writer.Flush();
            Serialized(method);
        }
    }
}