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
        // TODO: remove op_Implicit and op_Explicit from IgnoredMethods once we figure out a safe way to serialize them
        private static readonly HashSet<string> IgnoredMethods = new HashSet<string>() { "op_Implicit", "op_Explicit" };

        [Flags]
        private enum OpFlags
        {
            Constructor = 1,
            RefReturn = 2,
            ConstSelf = 4,
            NonConstOthers = 8,
            InClassOnly = 16,
        }
        private static readonly Dictionary<string, (string, OpFlags)> Operators = new Dictionary<string, (string, OpFlags)>()
        {
            // https://en.cppreference.com/w/cpp/language/converting_constructor OR https://en.cppreference.com/w/cpp/language/cast_operator
            { "op_Implicit", ("{type}", OpFlags.Constructor | OpFlags.InClassOnly) },
            { "op_Explicit", ("explicit {type}", OpFlags.Constructor | OpFlags.InClassOnly) },
            // https://en.cppreference.com/w/cpp/language/operator_assignment
            { "op_Assign", ("operator =", OpFlags.RefReturn | OpFlags.InClassOnly) },
            { "op_AdditionAssignment", ("operator +=", OpFlags.RefReturn) },
            { "op_SubtractionAssignment", ("operator -=", OpFlags.RefReturn) },
            { "op_MultiplicationAssignment", ("operator *=", OpFlags.RefReturn) },
            { "op_DivisionAssignment", ("operator /=", OpFlags.RefReturn) },
            { "op_ModulusAssignment", ("operator %=", OpFlags.RefReturn) },
            { "op_BitwiseAndAssignment", ("operator &=", OpFlags.RefReturn) },
            { "op_BitwiseOrAssignment", ("operator |=", OpFlags.RefReturn) },
            { "op_ExclusiveOrAssignment", ("operator ^=", OpFlags.RefReturn) },
            { "op_LeftShiftAssignment", ("operator <<=", OpFlags.RefReturn) },
            { "op_RightShiftAssignment", ("operator >>=", OpFlags.RefReturn) },
            // https://en.cppreference.com/w/cpp/language/operator_incdec
            { "op_Increment", ("operator++", OpFlags.RefReturn) },
            { "op_Decrement", ("operator--", OpFlags.RefReturn) },
            // https://en.cppreference.com/w/cpp/language/operator_arithmetic
            { "op_UnaryPlus", ("operator+", OpFlags.ConstSelf) },
            { "op_UnaryNegation", ("operator-", OpFlags.ConstSelf) },
            { "op_Addition", ("operator+", OpFlags.ConstSelf) },
            { "op_Subtraction", ("operator-", OpFlags.ConstSelf) },
            { "op_Multiply", ("operator*", OpFlags.ConstSelf) },
            { "op_Division", ("operator/", OpFlags.ConstSelf) },
            { "op_Modulus", ("operator%", OpFlags.ConstSelf) },
            { "op_OnesComplement", ("operator~", OpFlags.ConstSelf) },
            { "op_BitwiseAnd", ("operator&", OpFlags.ConstSelf) },
            { "op_BitwiseOr", ("operator|", OpFlags.ConstSelf) },
            { "op_ExclusiveOr", ("operator^", OpFlags.ConstSelf) },
            { "op_LeftShift", ("operator<<", OpFlags.ConstSelf) },
            { "op_RightShift", ("operator>>", OpFlags.ConstSelf) },
            // https://en.cppreference.com/w/cpp/language/operator_logical
            { "op_LogicalNot", ("operator!", OpFlags.ConstSelf) },
            { "op_LogicalAnd", ("operator&&", OpFlags.ConstSelf) },
            { "op_LogicalOr", ("operator||", OpFlags.ConstSelf) },
            // https://en.cppreference.com/w/cpp/language/operator_comparison
            { "op_Equality", ("operator ==", OpFlags.ConstSelf) },
            { "op_Inequality", ("operator !=", OpFlags.ConstSelf) },
            { "op_LessThan", ("operator <", OpFlags.ConstSelf) },
            { "op_GreaterThan", ("operator >", OpFlags.ConstSelf) },
            { "op_LessThanOrEqual", ("operator <=", OpFlags.ConstSelf) },
            { "op_GreaterThanOrEqual", ("operator >=", OpFlags.ConstSelf) },
            // { "", ("operator <=>", OpFlags.ConstSelf) },
            // https://en.cppreference.com/w/cpp/language/operator_member_access
            // https://en.cppreference.com/w/cpp/language/operator_other
            { "op_Comma", ("operator,", OpFlags.RefReturn | OpFlags.ConstSelf | OpFlags.NonConstOthers) },  // returns T2& (aka B&)
        };
        internal enum MethodScope
        {
            Class,
            Static,
            Namespace
        }
        internal Dictionary<IMethod, MethodScope> Scope = new Dictionary<IMethod, MethodScope>();

        private bool _asHeader;
        private SerializationConfig _config;

        private Dictionary<IMethod, MethodTypeContainer> _resolvedReturns = new Dictionary<IMethod, MethodTypeContainer>();
        private Dictionary<IMethod, List<(MethodTypeContainer container, ParameterFlags flags)>> _parameterMaps = new Dictionary<IMethod, List<(MethodTypeContainer container, ParameterFlags flags)>>();

        // This dictionary maps from method to a list of real generic parameters.
        private Dictionary<IMethod, List<string>> _genericArgs = new Dictionary<IMethod, List<string>>();
        /// <summary>
        /// This dictionary maps from method to a list of placeholder generic arguments.
        /// These generic arguments are only ever used as replacements for types that should not be included/defined within our context.
        /// Thus, we only read these generic arguments and use them for our template<class ...> string when we populate it.
        /// If there is no value for a given <see cref="IMethod"/>, we simply avoid writing a template string at all.
        /// If we are not a header, we write template<> instead.
        /// This is populated only when <see cref="FixBadDefinition(CppTypeContext, TypeRef, IMethod)"/> is called.
        /// </summary>
        private Dictionary<IMethod, SortedSet<string>> _tempGenerics = new Dictionary<IMethod, SortedSet<string>>();

        private string _declaringNamespace;
        private string _declaringFullyQualified;
        private string _thisTypeName;

        private HashSet<(TypeRef, bool, string)> _signatures = new HashSet<(TypeRef, bool, string)>();
        private bool _ignoreSignatureMap;
        private HashSet<IMethod> _aborted = new HashSet<IMethod>();

        // Holds a mapping of IMethod to the name, as well as if the name has been specifically converted already.
        private static Dictionary<IMethod, (string, bool)> _nameMap = new Dictionary<IMethod, (string, bool)>();

        private bool performedGenericRenames = false;
        private bool _declaringIsValueType;

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
            if (NeedDefinitionInHeader(method)) return CppTypeContext.NeedAs.BestMatch;
            return CppTypeContext.NeedAs.Declaration;
        }

        private void ResolveName(IMethod method)
        {
            void FixNames(IMethod m, string n, bool isFullName, HashSet<IMethod> skip)
            {
                if (!skip.Add(m)) return;
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

            if (Operators.TryGetValue(name, out var info))
            {
                var numParams = _parameterMaps[method].Count;
                name = info.Item1;
                var flags = info.Item2;

                void PrefixConstUnlessPointer(MethodTypeContainer container)
                {
                    if (!container.IsPointer) container.Prefix("const ");
                }
                if (flags.HasFlag(OpFlags.ConstSelf))
                    PrefixConstUnlessPointer(_parameterMaps[method][0].container);
                if (!flags.HasFlag(OpFlags.NonConstOthers))
                    for (int i = 1; i < numParams; i++)
                        PrefixConstUnlessPointer(_parameterMaps[method][i].container);

                if (!flags.HasFlag(OpFlags.Constructor))
                    // fix for "overloaded '[operator]' must have at least one parameter of class or enumeration type" (pointers don't count)
                    for (int i = numParams - 1; i >= 0; i--)
                    {
                        var container = _parameterMaps[method][i].container;
                        if (container.IsClassType && container.UnPointer())
                            break;
                    }

                void SuffixRefUnlessPointer(MethodTypeContainer container)
                {
                    if (!container.IsPointer) container.Suffix("&");
                }
                _parameterMaps[method].ForEach(param => SuffixRefUnlessPointer(param.container));
                if (flags.HasFlag(OpFlags.RefReturn))
                    SuffixRefUnlessPointer(_resolvedReturns[method]);

                if (!flags.HasFlag(OpFlags.InClassOnly))
                    Scope[method] = MethodScope.Namespace;  // namespace define operators as much as possible
                else if (!flags.HasFlag(OpFlags.Constructor))
                {
                    _parameterMaps[method][0].container.Skip = true;
                    Scope[method] = MethodScope.Class;
                }
            }

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
            if (!_tempGenerics.TryGetValue(method, out var genericParameters))
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
                _tempGenerics.Add(method, genericParameters);
            return true;
        }

        public override void PreSerialize(CppTypeContext context, IMethod method)
        {
            // Get the fully qualified name of the context
            bool success = true;
            // TODO: wrap all .cpp methods in a `namespace [X] {` ?
            _declaringNamespace = context.TypeNamespace.TrimStart(':');
            _declaringFullyQualified = context.QualifiedTypeName.TrimStart(':');
            _thisTypeName = context.GetCppName(method.DeclaringType, false, needAs: CppTypeContext.NeedAs.Definition);
            var resolved = context.ResolveAndStore(method.DeclaringType, CppTypeContext.ForceAsType.Literal, CppTypeContext.NeedAs.Definition);
            _declaringIsValueType = resolved.Info.TypeFlags.HasFlag(TypeFlags.ValueType);

            if (method.Generic)
            {
                var generics = new List<string>();
                foreach (var g in method.GenericParameters)
                {
                    var s = context.GetCppName(g, true, needAs: CppTypeContext.NeedAs.Declaration);
                    if (s is null)
                        // If we fail to resolve a parameter, we will simply add a (null, p.Flags) item to our mapping.
                        // However, we should not call Resolved(method)
                        success = false;
                    generics.Add(s);
                }
                _genericArgs.Add(method, generics);
            }

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

            Scope.Add(method, method.Specifiers.IsStatic() ? MethodScope.Static : MethodScope.Class);
            //RenameGenericMethods(context, method);
            ResolveName(method);
            if (success)
                Resolved(method);
        }

        private string WriteMethod(MethodScope scope, IMethod method, bool isHeader)
        {
            var ns = "";
            var preRetStr = "";
            var overrideStr = "";
            var impl = "";

            bool namespaceQualified = !isHeader;
            if (namespaceQualified)
                ns = (scope == MethodScope.Namespace ? _declaringNamespace : _declaringFullyQualified) + "::";
            else if (scope == MethodScope.Static)
                preRetStr += "static ";

            // stringify the return type
            var retStr = _resolvedReturns[method].TypeName(isHeader);
            if (!method.ReturnType.IsVoid() && _config.OutputStyle == OutputStyle.Normal)
                retStr = "std::optional<" + retStr + ">";

            if (!_nameMap.TryGetValue(method, out var namePair))
                throw new InvalidOperationException($"Could not find method: {method} in _nameMap! Ensure it is PreSerialized first!");
            var nameStr = namePair.Item1;

            string paramString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method],
                FormatParameterMode.Names | FormatParameterMode.Types, header: isHeader);

            // Handles i.e. ".ctor"
            if (IsCtor(method))
            {
                if (method.DeclaringType.Namespace == "System" && method.DeclaringType.Name == "Object")
                    // Special case for System.Object, needs to always return ::Il2CppObject
                    retStr = "::Il2CppObject*";
                else if (method.DeclaringType.Namespace == "System" && method.DeclaringType.Name == "String")
                    // Special case for System.String, needs to always return ::Il2CppString
                    retStr = "::Il2CppString*";
                else
                {
                    retStr = !isHeader ? _declaringFullyQualified : _thisTypeName;
                    // Force return type to be a pointer
                    retStr = retStr.EndsWith("*") ? retStr : retStr + "*";
                }
                preRetStr = !namespaceQualified ? "static " : "";
                nameStr = "New" + nameStr;
            }

            var signature = $"{nameStr}({paramString})";

            if (!_ignoreSignatureMap && !_signatures.Add((method.DeclaringType, isHeader, signature)))
            {
                preRetStr = "// ABORTED: conflicts with another method. " + preRetStr;
                _aborted.Add(method);
            }

            var ret = $"{preRetStr}{retStr} {ns}{signature}{overrideStr}{impl}";
            //if (isHeader && scope == MethodScope.Namespace) Console.WriteLine(ret);
            return ret;
        }

        private bool IsCtor(IMethod method) => method.Name == "_ctor" || method.Name == ".ctor";

        private bool TemplateString(IMethod method, bool withTemps, out string templateString)
        {
            templateString = null;
            var str = "";
            bool hadGenerics = false;
            if (_genericArgs.TryGetValue(method, out var generics))
            {
                hadGenerics = true;
                str += string.Join(", ", generics.Select(s => "class " + s));
            }
            if (_tempGenerics.TryGetValue(method, out var temps))
            {
                hadGenerics = true;
                if (withTemps)
                    str += string.Join(", ", temps.Select(s => "class " + s));
            }
            if (hadGenerics)
                templateString = $"template<{str}>";
            return hadGenerics;
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
            for (int i = 0; i < _parameterMaps[method].Count; i++)
            {
                var s = _parameterMaps[method][i];
                if (s.container.TypeName(asHeader) is null && !method.Parameters[i].Type.IsGenericParameter)
                    throw new UnresolvedTypeException(method.DeclaringType, method.Parameters[i].Type);
            }
            if (IgnoredMethods.Contains(method.Il2CppName) || _config.BlacklistMethods.Contains(method.Il2CppName) || _aborted.Contains(method))
                return;
            if (method.DeclaringType.IsGeneric && !_asHeader)
                // Need to create the method ENTIRELY in the header, instead of split between the C++ and the header
                return;

            bool writeContent = !_asHeader || NeedDefinitionInHeader(method);
            var scope = Scope[method];
            if (_asHeader)
            {
                var methodComment = "";
                foreach (var spec in method.Specifiers)
                    methodComment += $"{spec} ";
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
                    var methodStr = WriteMethod(scope, method, true);
                    if (TemplateString(method, true, out var templateStr))
                        writer.WriteLine((methodStr.StartsWith("/") ? "// " : "") + templateStr);
                    writer.WriteDeclaration(methodStr);
                }
            }
            else
                // Comment for autogenerated method should use Il2CppName whenever possible
                writer.WriteComment("Autogenerated method: " + method.DeclaringType + "." + method.Il2CppName);

            if (writeContent)
            {
                // Write the qualified name if not in the header
                var methodStr = WriteMethod(scope, method, _asHeader);
                if (methodStr.StartsWith("/"))
                {
                    writer.WriteLine(methodStr);
                    writer.Flush();
                    return;
                }

                if (TemplateString(method, false, out var templateStr))
                    writer.WriteLine(templateStr);
                writer.WriteDefinition(methodStr);

                var (@namespace, @class) = method.DeclaringType.GetIl2CppName();
                var classArgs = $"\"{@namespace}\", \"{@class}\"";
                if (method.DeclaringType.IsGeneric)
                    classArgs = $"il2cpp_utils::il2cpp_type_check::il2cpp_no_arg_class<{_thisTypeName}>::get()";
                if (IsCtor(method))
                {
                    // Always use thisTypeName for the cast type, since we are already within the context of the type.
                    var typeName = _thisTypeName.EndsWith("*") ? _thisTypeName : _thisTypeName + "*";

                    var paramNames = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], FormatParameterMode.Names, asHeader);
                    if (!string.IsNullOrEmpty(paramNames))
                        // Prefix , for parameters to New
                        paramNames = ", " + paramNames;
                    var newObject = $"il2cpp_utils::New({classArgs}{paramNames})";
                    // TODO: Make this configurable
                    writer.WriteDeclaration($"return ({typeName})CRASH_UNLESS({newObject})");
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
                    if (scope == MethodScope.Class)
                        s += (_declaringIsValueType ? "*" : "") + "this, ";
                    else
                        // TODO: Check to ensure this works with non-generic methods in a generic type
                        s += $"{classArgs}, ";

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