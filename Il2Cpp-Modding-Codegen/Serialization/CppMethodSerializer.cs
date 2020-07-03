using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppMethodSerializer : Serializer<IMethod>
    {
        private static readonly HashSet<string> IgnoredMethods = new HashSet<string>() { "op_Implicit", "op_Explicit" };
        private bool _asHeader;
        private SerializationConfig _config;

        private Dictionary<IMethod, MethodTypeContainer> _resolvedReturns = new Dictionary<IMethod, MethodTypeContainer>();
        private Dictionary<IMethod, List<(MethodTypeContainer container, ParameterFlags flags)>> _parameterMaps = new Dictionary<IMethod, List<(MethodTypeContainer container, ParameterFlags flags)>>();

        /// <summary>
        /// This dictionary maps from method to a list of generic arguments.
        /// These generic arguments are only ever used as replacements for types that should not be included/defined within our context.
        /// Thus, we only read these generic arguments and use them for our template<class ...> string when we populate it.
        /// If there is no value for a given <see cref="IMethod"/>, we simply avoid writing a template string at all.
        /// If we are not a header, we write template<> instead.
        /// This is populated only when <see cref="FixBadDefinition(CppTypeContext, TypeRef, IMethod)"/> is called.
        /// </summary>
        private Dictionary<IMethod, SortedSet<string>> _genericArgs = new Dictionary<IMethod, SortedSet<string>>();

        private string _declaringFullyQualified;
        private string _thisTypeName;
        private bool _isInterface;
        private bool _pureVirtual;

        private HashSet<(TypeRef, bool, string)> _signatures = new HashSet<(TypeRef, bool, string)>();
        private bool _ignoreSignatureMap;
        private HashSet<IMethod> _aborted = new HashSet<IMethod>();

        // Holds a mapping of IMethod to the name, as well as if the name has been specifically converted already.
        private static Dictionary<IMethod, (string, bool)> _nameMap = new Dictionary<IMethod, (string, bool)>();

        private bool performedGenericRenames = false;

        public CppMethodSerializer(SerializationConfig config)
        {
            _config = config;
        }

        private bool NeedDefinitionInHeader(IMethod method)
        {
            return method.DeclaringType.IsGenericTemplate;
        }

        /// <summary>
        /// Returns whether the given method should be written as a definition or a declaration
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private CppTypeContext.NeedAs NeedTypesAs(IMethod method, bool returnType = false)
        {
            if (returnType && _pureVirtual && method.HidesBase)
                // Prevents `error: return type of virtual function [name] is not covariant with the return type of the function
                //   it overrides ([return type] is incomplete)`
                return CppTypeContext.NeedAs.Definition;
            if (NeedDefinitionInHeader(method)) return CppTypeContext.NeedAs.BestMatch;
            return CppTypeContext.NeedAs.Declaration;
        }

        private void ResolveName(IMethod method)
        {
            void FixNames(IMethod m, string n, bool isFullName, HashSet<IMethod> skip)
            {
                if (skip.Contains(m))
                    return;
                skip.Add(m);
                // Fix all names for a given method by recursively checking our base method and our implementing methods.
                foreach (var im in m.ImplementingMethods)
                {
                    // For each implementing method, recurse on it
                    FixNames(im, n, isFullName, skip);
                }
                if (_nameMap.TryGetValue(m, out var pair))
                {
                    if (pair.Item2)
                        // This pair already has a converted name!
                        Console.WriteLine($"Method: {m.Name} already has rectified name: {pair.Item1}! Was trying new name: {n}");
                    if (isFullName)
                        _nameMap[m] = (n, isFullName);
                }
                else
                    _nameMap[m] = (n, isFullName);
            }

            // If this method is already in the _nameMap, with a true value in the pair, we are done.
            if (_nameMap.TryGetValue(method, out var p))
                if (p.Item2)
                {
                    //Console.WriteLine($"Already have a name for method: {method.Name}, {p.Item1}");
                    return;
                }

            // If the method has a special name, we need to use it.
            int idxDot = method.Name.LastIndexOf('.');
            // Here we need to add our method to a mapping, we need to ensure that if we need to make changes to the name we can do so without issue
            // Basically, for any special name methods that have base methods, we need to change the name of the base method as well.
            // We do this by holding a static mapping of IMethod --> Name
            // And for each method, if it is a special name, we modify the name of the base method (as well as all implementing methods) in this map
            // Then, when we go to write the method, we look it up in this map, if we find it, we use the name.

            // Create name
            string name;
            bool fullName = false;
            if (idxDot >= 2)
            {
                var tName = method.Name.Substring(idxDot + 1);
                name = method.ImplementedFrom.GetQualifiedName().Replace("::", "_") + "_" + tName;
                fullName = true;
            }
            else
                // If the name is not a special name, set it to be the method name
                name = method.Name;
            name = _config.SafeMethodName(name).Replace('<', '$').Replace('>', '$').Replace('.', '_');
            // Iterate over all implementing and base methods and change their names accordingly
            var skips = new HashSet<IMethod>();
            // Chances are, for the first time a method is hit, it has a false for fullName.
            // We essentially only truly modify all the methods when fullName is true, otherwise they are pretty much exactly as they would be normally.
            while (method != null)
            {
                // For each base method, iterate over it and all its implementing methods
                // Change the name for each of these methods to have the name
                FixNames(method, name, fullName, skips);
                foreach (var m in method.ImplementingMethods)
                {
                    FixNames(m, name, fullName, skips);
                }
                method = method.BaseMethod;
            }
        }

        private void RenameGenericMethods(CppTypeContext context, IMethod method)
        {
            // We want to ONLY do this once, for all methods.
            // That is because we don't want to end up renaming a bunch of methods multiple times, which is slow.
            if (performedGenericRenames)
                return;
            // During preserialization, if we find that we have a generic method that matches the name of a non-generic method, we need to rename it forcibly.
            // This is to avoid a possibility of generic methods being called with equivalent arguments as a non-generic method (not allowed in C++)
            // Sadly, the best way I can think of to do this is to iterate over all methods that match names and check their returns/parameters.
            // If there are 2 or more, rename all methods that have at least one generic template parameter using some static index, which is reset to 0 after.
            // Renaming occurs by suffixing _i, and placing the name into the _nameMap with a "false".

            var allMethods = context.LocalType.Methods.Where(m => !m.Generic);
            // For each method in allMethods
            // TODO: This is slow: O(N^2) where N is methods
            var completedMethods = new HashSet<IMethod>();
            foreach (var m in allMethods)
            {
                if (completedMethods.Contains(m))
                    continue;
                // Get the overloads
                var overloads = allMethods.Where(am => am.Name == m.Name).ToList();
                // If we have two or more, iterate over all of them
                if (overloads.Count > 2)
                {
                    int genericRenameIdx = 0;
                    foreach (var om in overloads)
                    {
                        // If the overload method in question has generic parameters for its return type or parameters
                        // we suffix its rectified name with _i
                        // TODO: This probably renames far more than it should
                        if (context.IsGenericParameter(om.ReturnType) || om.Parameters.FirstOrDefault(p => context.IsGenericParameter(p.Type)) != null)
                        {
                            if (_nameMap.TryGetValue(om, out var pair))
                            {
                                if (pair.Item2)
                                    continue;
                            }
                            // Only rename a method that has NOT been renamed!
                            _nameMap[om] = (string.IsNullOrEmpty(pair.Item1) ? om.Name : pair.Item1 + "_" + genericRenameIdx, false);
                            genericRenameIdx++;
                        }
                        completedMethods.Add(om);
                        // Non generic methods don't need to be renamed at all.
                    }
                }
            }
            performedGenericRenames = true;
        }

        public bool FixBadDefinition(TypeRef offendingType, IMethod method, out int found)
        {
            // This method should be relatively straightforward:
            // First, check if we have a cycle with a nested type AND we don't want to write our content of our method (throw if we are a template type, for example)
            // Then, given this particular method, compute how many of our parameters are actually in a cycle (fun part!)
            // For each of those, create a template type, T1 --> TN that maps to it, ONLY IF WE ARE A HEADER!
            // Then, set some flags such that we write the following:
            // template<class T1, class T2, ...> ADJUSTED_RETURN original_name(ADJUSTED_PARAMETERS original_names);
            // And in .cpp:
            // template<>
            // original_return original_name(original_parameters original_names) { ... }
            Contract.Requires(_resolvedReturns.ContainsKey(method));
            Contract.Requires(_parameterMaps.ContainsKey(method));
            Contract.Requires(_parameterMaps[method].Count == method.Parameters.Count);

            found = 0;
            if (NeedDefinitionInHeader(method))
                // Can't template a definition in a header.
                return false;

            // Use existing genericParameters if it already exists (a single method could have multiple offending types!)
            if (!_genericArgs.TryGetValue(method, out var genericParameters))
                genericParameters = new SortedSet<string>();
            // Ideally, all I should have to do here is iterate over my method, see if any params or return types match offending type, and template it if so
            if (method.ReturnType.ContainsOrEquals(offendingType))
            {
                var newName = "R";
                _resolvedReturns[method].Template(newName);
                genericParameters.Add(newName);
            }
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                if (method.Parameters[i].Type.ContainsOrEquals(offendingType))
                {
                    var newName = "T" + i;
                    _parameterMaps[method][i].container.Template(newName);
                    genericParameters.Add(newName);
                }
            }

            found = genericParameters.Count;
            if (genericParameters.Count > 0)
                // Only add to dictionary if we actually HAVE the offending type somewhere.
                _genericArgs.Add(method, genericParameters);
            return true;
        }

        public override void PreSerialize(CppTypeContext context, IMethod method)
        {
            if (method.Generic)
                // Skip generic methods
                return;

            // Get the fully qualified name of the context
            bool success = true;
            _declaringFullyQualified = context.QualifiedTypeName;
            _thisTypeName = context.GetCppName(method.DeclaringType, false, needAs: CppTypeContext.NeedAs.Definition, forceAsType: CppTypeContext.ForceAsType.Literal);
            var resolved = context.ResolveAndStore(method.DeclaringType, CppTypeContext.ForceAsType.Literal, CppTypeContext.NeedAs.Definition);
            _isInterface = resolved?.Type == TypeEnum.Interface;
            _pureVirtual = _isInterface && !method.DeclaringType.IsGeneric;
            var needAs = NeedTypesAs(method);
            // We need to forward declare everything used in methods (return types and parameters)
            // If we are writing the definition, we MUST define it
            var resolvedReturn = context.GetCppName(method.ReturnType, true, needAs: NeedTypesAs(method, true));
            if (resolvedReturn is null)
                // If we fail to resolve the return type, we will simply add a null item to our dictionary.
                // However, we should not call Resolved(method)
                success = false;
            if (_resolvedReturns.ContainsKey(method))
                // If this is ignored, we will still (at least) fail on _parameterMaps.Add
                throw new InvalidOperationException("Method has already been preserialized! Don't preserialize it again! Method: " + method);
            _resolvedReturns.Add(method, new MethodTypeContainer(resolvedReturn));
            var parameterMap = new List<(MethodTypeContainer, ParameterFlags)>();
            foreach (var p in method.Parameters)
            {
                var s = context.GetCppName(p.Type, true, needAs: needAs);
                if (s is null)
                    // If we fail to resolve a parameter, we will simply add a (null, p.Flags) item to our mapping.
                    // However, we should not call Resolved(method)
                    success = false;
                parameterMap.Add((new MethodTypeContainer(s), p.Flags));
            }
            _parameterMaps.Add(method, parameterMap);
            //RenameGenericMethods(context, method);
            ResolveName(method);
            if (success)
                Resolved(method);
        }

        private string WriteMethod(bool staticFunc, IMethod method, bool isHeader)
        {
            var ns = "";
            var preRetStr = "";
            var overrideStr = "";
            var impl = "";
            var namespaceQualified = !isHeader;
            if (namespaceQualified)
                ns = _declaringFullyQualified + "::";
            else
            {
                if (staticFunc)
                    preRetStr += "static ";

                // TODO: apply override correctly? It basically requires making all methods virtual
                // and if you miss any override the compiler gives you warnings
                //if (IsOverride(method))
                //    overrideStr += " override";
                if (_pureVirtual)
                {
                    preRetStr += "virtual ";
                    impl += " = 0";
                }
            }
            // Returns an optional
            // TODO: Should be configurable
            var retStr = _resolvedReturns[method].TypeName(isHeader);
            if (!method.ReturnType.IsVoid())
            {
                if (_config.OutputStyle == OutputStyle.Normal)
                    retStr = "std::optional<" + retStr + ">";
            }
            if (!_nameMap.TryGetValue(method, out var namePair))
                throw new InvalidOperationException($"Could not find method: {method} in _nameMap! Ensure it is PreSerialized first!");
            var nameStr = namePair.Item1;

            string paramString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], FormatParameterMode.Names | FormatParameterMode.Types, header: isHeader);

            // Handles i.e. ".ctor"
            if (IsCtor(method))
            {
                retStr = (!isHeader ? _declaringFullyQualified : _thisTypeName) + "*";
                preRetStr = !namespaceQualified ? "static " : "";
                nameStr = "New" + nameStr;
            }

            var signature = $"{nameStr}({paramString})";

            if (!_ignoreSignatureMap && !_signatures.Add((method.DeclaringType, isHeader, signature)))
            {
                preRetStr = "// ABORTED: conflicts with another method. " + preRetStr;
                _aborted.Add(method);
            }
            return $"{preRetStr}{retStr} {ns}{signature}{overrideStr}{impl}";
        }

        private void WriteCtor(CppStreamWriter writer, IMethod method, bool asHeader)
        {
            bool content = !asHeader || NeedDefinitionInHeader(method);
            var paramString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], FormatParameterMode.Names | FormatParameterMode.Types, header: asHeader);

            if (!_nameMap.TryGetValue(method, out var methodNamePair))
                throw new InvalidOperationException($"Could not find method: {method} in _nameMap! Ensure it is PreSerialized first!");
            if (content)
            {
                var name = !asHeader ? _declaringFullyQualified : _thisTypeName;
                var methodPrefix = !asHeader ? _declaringFullyQualified + "::" : "";
                if (_genericArgs.ContainsKey(method))
                    if (!asHeader)
                        writer.WriteLine("template<>");
                    else
                        throw new InvalidOperationException("Cannot create specialization in a header!");
                writer.WriteDefinition($"{name}* {methodPrefix}New{methodNamePair.Item1}({paramString})");
                var namePair = method.DeclaringType.GetIl2CppName();
                var paramNames = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], FormatParameterMode.Names, asHeader);
                if (!string.IsNullOrEmpty(paramNames))
                    // Prefix , for parameters to New
                    paramNames = ", " + paramNames;
                var newObject = $"il2cpp_utils::New(\"{namePair.@namespace}\", \"{namePair.name}\"{paramNames})";
                // TODO: Make this configurable
                writer.WriteDeclaration($"return reinterpret_cast<{name}*>(CRASH_UNLESS({newObject}))");
                writer.CloseDefinition();
            }
            else
            {
                // Write declaration, with comments
                writer.WriteComment($"Creates an object of type: {_declaringFullyQualified}* and calls the corresponding constructor.");
                if (_genericArgs.TryGetValue(method, out var genArgs))
                    writer.WriteLine($"template<{string.Join(", ", genArgs.Select(s => "class " + s))}>");
                // TODO: Ensure name does not conflict with any existing methods!
                writer.WriteDeclaration($"static {_thisTypeName}* New{methodNamePair.Item1}({paramString})");
            }
        }

        private bool IsCtor(IMethod method)
        {
            return method.Name == "_ctor" || method.Name == ".ctor";
        }

        // Write the method here
        public override void Serialize(CppStreamWriter writer, IMethod method, bool asHeader)
        {
            if (_asHeader && !asHeader)
                _ignoreSignatureMap = true;
            _asHeader = asHeader;
            if (!_resolvedReturns.ContainsKey(method))
                // In the event we have decided to not parse this method (in PreSerialize) don't even bother.
                return;
            if (_resolvedReturns[method] == null)
                throw new UnresolvedTypeException(method.DeclaringType, method.ReturnType);
            var val = _parameterMaps[method].FindIndex(s => s.container.TypeName(asHeader) is null);
            if (val != -1)
                throw new UnresolvedTypeException(method.DeclaringType, method.Parameters[val].Type);
            // Blacklist and IgnoredMethods should still use method.Name, not method.Il2CppName
            if (IgnoredMethods.Contains(method.Name) || _config.BlacklistMethods.Contains(method.Name) || _aborted.Contains(method))
                return;
            if (method.DeclaringType.IsGeneric && !_asHeader)
                // Need to create the method ENTIRELY in the header, instead of split between the C++ and the header
                return;

            bool writeContent = !_asHeader || NeedDefinitionInHeader(method);
            if (_asHeader)
            {
                var methodComment = "";
                bool staticFunc = false;
                foreach (var spec in method.Specifiers)
                {
                    methodComment += $"{spec} ";
                    if (spec.Static)
                        staticFunc = true;
                }
                // Method comment should also use the Il2CppName whenever possible
                methodComment += $"{method.ReturnType} {method.Il2CppName}({method.Parameters.FormatParameters(csharp: true)})";
                writer.WriteComment(methodComment);
                writer.WriteComment($"Offset: 0x{method.Offset:X}");
                if (method.ImplementedFrom != null)
                    writer.WriteComment("Implemented from: " + method.ImplementedFrom);
                if (method.BaseMethod != null)
                    writer.WriteComment($"Base method: {method.BaseMethod.ReturnType} {method.BaseMethod.DeclaringType.Name}::{method.BaseMethod.Name}({method.Parameters.FormatParameters(csharp: true)})");
                if (!writeContent)
                {
                    if (_genericArgs.TryGetValue(method, out var types))
                        writer.WriteLine($"template<{string.Join(", ", types.Select(s => "class " + s))}>");
                    writer.WriteDeclaration(WriteMethod(staticFunc, method, true));
                }
            }
            else
            {
                // Comment for autogenerated method should use Il2CppName whenever possible
                writer.WriteComment("Autogenerated method: " + method.DeclaringType + "." + method.Il2CppName);
            }
            if (writeContent)
            {
                bool isStatic = method.Specifiers.IsStatic();
                // Write the qualified name if not in the header
                var methodStr = WriteMethod(isStatic, method, _asHeader);
                if (methodStr.StartsWith("/"))
                {
                    writer.WriteLine(methodStr);
                    writer.Flush();
                    return;
                }

                if (_genericArgs.ContainsKey(method))
                    writer.WriteLine("template<>");
                writer.WriteDefinition(methodStr);

                if (IsCtor(method))
                {
                    var typeName = !asHeader ? _declaringFullyQualified : _thisTypeName;
                    var (@namespace, name) = method.DeclaringType.GetIl2CppName();
                    var paramNames = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], FormatParameterMode.Names, asHeader);
                    if (!string.IsNullOrEmpty(paramNames))
                        // Prefix , for parameters to New
                        paramNames = ", " + paramNames;
                    var newObject = $"il2cpp_utils::New(\"{@namespace}\", \"{name}\"{paramNames})";
                    // TODO: Make this configurable
                    writer.WriteDeclaration($"return static_cast<{typeName}*>(CRASH_UNLESS({newObject}))");
                    writer.CloseDefinition();
                }
                else
                {
                    var s = "";
                    var innard = "";
                    var macro = "RET_V_UNLESS(";
                    if (_config.OutputStyle == OutputStyle.CrashUnless)
                        macro = "CRASH_UNLESS(";
                    if (!method.ReturnType.IsVoid())
                    {
                        s = "return ";
                        innard = $"<{_resolvedReturns[method].TypeName(asHeader)}>";
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
                        var namePair = method.DeclaringType.GetIl2CppName();
                        s += $"\"{namePair.@namespace}\", \"{namePair.name}\", ";
                    }
                    var paramString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], FormatParameterMode.Names);
                    if (!string.IsNullOrEmpty(paramString))
                        paramString = ", " + paramString;
                    // Macro string should use Il2CppName (of course, without _, $ replacement)
                    s += $"\"{method.Il2CppName}\"{paramString}){macroEnd};";
                    // Write method with return
                    writer.WriteLine(s);
                    // Close method
                    writer.CloseDefinition();
                }
            }
            writer.Flush();
            Serialized(method);
        }
    }
}