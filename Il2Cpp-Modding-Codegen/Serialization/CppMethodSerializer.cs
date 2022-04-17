using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppMethodSerializer : Serializer<IMethod>
    {
        // TODO: remove op_Implicit and op_Explicit from IgnoredMethods once we figure out a safe way to serialize them
        private static readonly HashSet<string> IgnoredMethods = new HashSet<string>() { "op_Implicit" };

        [Flags]
        private enum OpFlags
        {
            NeedMoreInfo = 0,
            Constructor = 1,
            RefReturn = 2,
            ConstSelf = 4,
            NonConstOthers = 8,
            InClassOnly = 16,
        }

        internal string ConstructorName(IMethod method) => _stateMap[method.DeclaringType].type.Split(':')[^1];

        private (string, OpFlags) GetConversionOperatorInfo(string prefix, IMethod op)
        {
            if (op.ReturnType.ContainsOrEquals(op.DeclaringType))
                return (prefix + ConstructorName(op), OpFlags.Constructor | OpFlags.InClassOnly);
            else
                return ($"{prefix}operator {_resolvedReturns[op].TypeName(false)}", OpFlags.InClassOnly);
        }

        private static readonly Dictionary<string, (string, OpFlags)> Operators = new Dictionary<string, (string, OpFlags)>()
        {
            // https://en.cppreference.com/w/cpp/language/converting_constructor OR https://en.cppreference.com/w/cpp/language/cast_operator
            { "op_Implicit", ("", OpFlags.NeedMoreInfo) },
            { "op_Explicit", ("explicit ", OpFlags.NeedMoreInfo) },
            // https://en.cppreference.com/w/cpp/language/operator_assignment
            { "op_Assign", ("operator =", OpFlags.RefReturn | OpFlags.InClassOnly) },
            { "op_AdditionAssignment", ("operator +=", OpFlags.ConstSelf) },
            { "op_SubtractionAssignment", ("operator -=", OpFlags.ConstSelf) },
            { "op_MultiplicationAssignment", ("operator *=", OpFlags.ConstSelf) },
            { "op_DivisionAssignment", ("operator /=", OpFlags.ConstSelf) },
            { "op_ModulusAssignment", ("operator %=", OpFlags.ConstSelf) },
            { "op_BitwiseAndAssignment", ("operator &=", OpFlags.ConstSelf) },
            { "op_BitwiseOrAssignment", ("operator |=", OpFlags.ConstSelf) },
            { "op_ExclusiveOrAssignment", ("operator ^=", OpFlags.ConstSelf) },
            { "op_LeftShiftAssignment", ("operator <<=", OpFlags.ConstSelf) },
            { "op_RightShiftAssignment", ("operator >>=", OpFlags.ConstSelf) },
            // https://en.cppreference.com/w/cpp/language/operator_incdec
            { "op_Increment", ("operator++", OpFlags.ConstSelf) },
            { "op_Decrement", ("operator--", OpFlags.ConstSelf) },
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

        private readonly SerializationConfig _config;

        private readonly Dictionary<IMethod, MethodTypeContainer> _resolvedReturns = new Dictionary<IMethod, MethodTypeContainer>();
        private readonly Dictionary<IMethod, List<(MethodTypeContainer container, ParameterModifier modifier)>> _parameterMaps = new Dictionary<IMethod, List<(MethodTypeContainer container, ParameterModifier modifier)>>();
        private readonly Dictionary<IMethod, string> implementingTypes = new();

        // This dictionary maps from method to a list of real generic parameters.
        private readonly Dictionary<IMethod, List<string>> _genericArgs = new Dictionary<IMethod, List<string>>();

        /// <summary>
        /// This dictionary maps from method to a list of placeholder generic arguments.
        /// These generic arguments are only ever used as replacements for types that should not be included/defined within our context.
        /// Thus, we only read these generic arguments and use them for our template<class ...> string when we populate it.
        /// If there is no value for a given <see cref="IMethod"/>, we simply avoid writing a template string at all.
        /// If we are not a header, we write template<> instead.
        /// This is populated only when <see cref="FixBadDefinition(CppTypeContext, TypeRef, IMethod)"/> is called.
        /// </summary>
        private readonly Dictionary<IMethod, SortedSet<string>> _tempGenerics = new Dictionary<IMethod, SortedSet<string>>();

        // This dictionary maps from method to a list of real generic parameters.
        private readonly Dictionary<IMethod, CppTypeDataSerializer.GenParamConstraintStrings> _genParamConstraints = new Dictionary<IMethod, CppTypeDataSerializer.GenParamConstraintStrings>();

        private string? _declaringNamespace;
        private string? _declaringFullyQualified;
        private string? _thisTypeName;

        // The int is the number of generic parameters that the method has
        private readonly Dictionary<int, HashSet<(TypeRef, bool, string)>> _signatures = new Dictionary<int, HashSet<(TypeRef, bool, string)>>();

        private readonly HashSet<IMethod> _aborted = new HashSet<IMethod>();

        // Holds a mapping of IMethod to the name, as well as if the name has been specifically converted already.
        private static readonly Dictionary<IMethod, (string, bool)> _nameMap = new Dictionary<IMethod, (string, bool)>();

        private readonly Dictionary<string, List<IMethod>> _methodNameMap = new();

        private bool _declaringIsValueType;

        private readonly Dictionary<TypeRef, CppTypeDataSerializer.State> _stateMap;

        private readonly List<(IMethod, string, string)> postSerializeCollection = new();

        // Context used for double checking
        private CppTypeContext? context;

        internal CppMethodSerializer(SerializationConfig config, Dictionary<TypeRef, CppTypeDataSerializer.State> map)
        {
            _config = config;
            _stateMap = map;
        }

        private bool NeedDefinitionInHeader(IMethod method) => method.DeclaringType.IsGenericTemplate || method.Generic || (IsCtor(method) && !_declaringIsValueType);

        /// <summary>
        /// Returns whether the given method should be written as a definition or a declaration
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private CppTypeContext.NeedAs NeedTypesAs(IMethod method)
        {
            // The TArgs -> initializer_list params proxy will also need complete types
            if (NeedDefinitionInHeader(method) || method.Parameters.Any(p => p.Modifier == ParameterModifier.Params)) return CppTypeContext.NeedAs.BestMatch;
            return CppTypeContext.NeedAs.Declaration;
        }

        private void ResolveName(IMethod method)
        {
            void FixNames(IMethod m, string n, bool isFullName, HashSet<IMethod> skip)
            {
                if (!skip.Add(m))
                    return;
                // Fix all names for a given method by recursively checking our base method and our implementing methods.
                foreach (var im in m.ImplementingMethods)
                    // For each implementing method, recurse on it
                    FixNames(im, n, isFullName, skip);

                if (_nameMap.TryGetValue(m, out var pair))
                {
                    if (pair.Item2)
                        if (m.BaseMethods.Count == 1)
                            // This pair already has a converted name!
                            // We only want to log this for cases where we would actually use the name
                            // In cases where we have multiple base methods, this does not matter.
                            Console.WriteLine($"Method: {m.Name} already has rectified name: {pair.Item1}! Was trying new name: {n}");
                    if (isFullName)
                    {
                        _nameMap[m] = (n, isFullName);
                        if (_methodNameMap.TryGetValue(n, out var existing))
                            existing.Add(m);
                        else
                            _methodNameMap.Add(n, new List<IMethod> { m });
                    }
                }
                else
                {
                    _nameMap[m] = (n, isFullName);
                    if (_methodNameMap.TryGetValue(n, out var existing))
                        existing.Add(m);
                    else
                        _methodNameMap.Add(n, new List<IMethod> { m });
                }
            }

            // If this method is already in the _nameMap, with a true value in the pair, we are done.
            if (_nameMap.TryGetValue(method, out var p))
                if (p.Item2)
                {
                    //Console.WriteLine($"Already have a name for method: {method.Name}, {p.Item1}");
                    return;
                }

            // If the method has a special name, we need to use it.
            // Here we need to add our method to a mapping, we need to ensure that if we need to make changes to the name we can do so without issue
            // Basically, for any special name methods that have base methods, we need to change the name of the base method as well.
            // We do this by holding a static mapping of IMethod --> Name
            // And for each method, if it is a special name, we modify the name of the base method (as well as all implementing methods) in this map
            // Then, when we go to write the method, we look it up in this map, if we find it, we use the name.

            // Create name
            string name;
            bool fullName = false;
            if (method.IsSpecialName)
            {
                var idxDot = method.Name.LastIndexOf('.');
                var tName = method.Name.Substring(idxDot + 1);
                var implementedFrom = method.ImplementedFrom ?? throw new InvalidOperationException("Tried to construct name from null ImplementedFrom!");
                name = implementedFrom.GetQualifiedCppName().Replace("::", "_") + "_" + tName;
                fullName = true;
            }
            else
                // If the name is not a special name, set it to be the method name
                name = method.Name;
            name = _config.SafeMethodName(name.Replace('<', '$').Replace('>', '$').Replace('.', '_').Replace('|', '_').Replace(',', '_').Replace('[', '$').Replace(']', '$'));

            if (Operators.TryGetValue(name, out var info))
            {
                var map = _parameterMaps[method];
                var numParams = map.Count;
                name = info.Item1;
                var flags = info.Item2;
                if (flags == OpFlags.NeedMoreInfo)
                {
                    (name, flags) = GetConversionOperatorInfo(name, method);
                    _resolvedReturns[method].Skip = true;
                }

                static void PrefixConstUnlessPointer(MethodTypeContainer container)
                {
                    if (!container.IsPointer) container.Prefix("const ");
                }

                if (flags.HasFlag(OpFlags.ConstSelf))
                    PrefixConstUnlessPointer(map[0].container);
                if (!flags.HasFlag(OpFlags.NonConstOthers))
                    for (int i = 1; i < numParams; i++)
                        PrefixConstUnlessPointer(map[i].container);

                if (!flags.HasFlag(OpFlags.Constructor))
                    // fix for "overloaded '[operator]' must have at least one parameter of class or enumeration type" (pointers don't count)
                    for (int i = numParams - 1; i >= 0; i--)
                    {
                        var container = map[i].container;
                        if (container.IsClassType && container.UnPointer())
                            break;
                    }

                static void SuffixRefUnlessPointer(MethodTypeContainer container)
                {
                    if (!container.IsPointer) container.Suffix("&");
                }
                map.ForEach(param => SuffixRefUnlessPointer(param.container));
                if (flags.HasFlag(OpFlags.RefReturn))
                    SuffixRefUnlessPointer(_resolvedReturns[method]);

                if (!flags.HasFlag(OpFlags.InClassOnly))
                    Scope[method] = MethodScope.Namespace;  // namespace define operators as much as possible
                else if (!flags.HasFlag(OpFlags.Constructor))
                {
                    map[0].container.Skip = true;
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
                if (method.BaseMethods.Count > 1)
                {
                    // If we have more than 1 base method, we need to avoid renaming (saves time)
                    // However, if we have a fullName, AND we SOMEHOW have 2 or more base methods, we need to throw a big exception
                    if (fullName)
                        throw new InvalidOperationException($"Should not have more than one base method for special name method: {method}!");
                    break;
                }
                // Failsafe add _ to avoid overload naming
                // Ensure we have a method that is named as such that isn't the exact same
                // Fun fact: This doesn't even happen on BS 1.13.5, it might in some other scenarios though.
                while (_methodNameMap.TryGetValue(name, out var methods))
                {
                    if (methods.Any(m =>
                    {
                        if (!m.DeclaringType.Equals(method.DeclaringType))
                            return false;
                        if (m.Parameters.Count != method.Parameters.Count)
                            return false;
                        // Parameter count and types must match, otherwise the overload would work
                        for (int i = 0; i < m.Parameters.Count; i++)
                        {
                            if (!m.Parameters[i].Type.Equals(method.Parameters[i].Type))
                                return false;
                        }
                        if (m.GenericParameters.Count != method.GenericParameters.Count)
                            return false;
                        for (int i = 0; i < m.GenericParameters.Count; i++)
                        {
                            if (!m.GenericParameters[i].Equals(method.GenericParameters[i]))
                                return false;
                        }
                        return true;
                    }))
                    {
                        name += "_";
                    }
                    else
                    {
                        break;
                    }
                }
                FixNames(method, name, fullName, skips);
                // Should only have one or fewer BaseMethods at this point
                method = method.BaseMethods.FirstOrDefault();
            }
        }

        internal bool FixBadDefinition(TypeRef offendingType, IMethod method, out int found)
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
            if (_parameterMaps[method].Count != method.Parameters.Count)
                throw new ArgumentException("_parameterMaps[method] is incorrect!", nameof(method));

            found = 0;
            if (NeedDefinitionInHeader(method))
                // Can't template a definition in a header.
                return false;

            // Use existing genericParameters if it already exists (a single method could have multiple offending types!)
            var genericParameters = _tempGenerics.GetOrAdd(method);
            // Ideally, all I should have to do here is iterate over my method, see if any params or return types match offending type, and template it if so
            if (method.ReturnType.ContainsOrEquals(offendingType))
            {
                var newName = "R";
                _resolvedReturns[method].Template(newName);
                genericParameters.AddOrThrow(newName);
            }
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                if (method.Parameters[i].Type.ContainsOrEquals(offendingType))
                {
                    var newName = "T" + i;
                    _parameterMaps[method][i].container.Template(newName);
                    genericParameters.AddOrThrow(newName);
                }
            }

            found = genericParameters.Count;
            // Only add to dictionary if we actually HAVE the offending type somewhere.
            if (genericParameters.Count > 0)
                _tempGenerics.AddOrUnionWith(method, genericParameters);
            return true;
        }

        public override void PreSerialize(CppTypeContext context, IMethod method)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));
            if (method is null) throw new ArgumentNullException(nameof(method));
            this.context = context;
            // Get the fully qualified name of the context
            bool success = true;
            // TODO: wrap all .cpp methods in a `namespace [X] {` ?
            _declaringNamespace = context.TypeNamespace.TrimStart(':');
            _declaringFullyQualified = context.QualifiedTypeName.TrimStart(':');
            _thisTypeName = context.GetCppName(method.DeclaringType, false, needAs: CppTypeContext.NeedAs.Definition);
            var resolved = context.ResolveAndStore(method.DeclaringType, CppTypeContext.ForceAsType.Literal, CppTypeContext.NeedAs.Definition);
            _declaringIsValueType = resolved?.Info.Refness == Refness.ValueType;

            if (method.Parameters.Any(p => p.Modifier == ParameterModifier.Params))
                context.EnableNeedInitializerList();

            if (NeedDefinitionInHeader(method))
                context.EnableNeedIl2CppUtilsFunctionsInHeader();

            if (method.Generic)
            {
                var generics = new List<string>();
                var genParamConstraints = new CppTypeDataSerializer.GenParamConstraintStrings();
                foreach (var g in method.GenericParameters)
                {
                    var s = context.GetCppName(g, true, needAs: CppTypeContext.NeedAs.Declaration);
                    if (s is null)
                    {
                        Console.Error.WriteLine($"context.GetCppName failed for generic parameter {g}, using g.CppName() instead.");
                        s = g.CppName();
                    }
                    generics.Add(s);

                    var constraintStrs = g.GenericParameterConstraints.Select(c => context.GetCppName(c, true) ?? c.CppName()).ToList();
                    if (constraintStrs.Count > 0)
                        genParamConstraints.Add(context.GetCppName(g, false) ?? g.CppName(), constraintStrs);
                }
                _genericArgs.Add(method, generics);
                _genParamConstraints.Add(method, genParamConstraints);
            }

            if (method.IsVirtual)
            {
                // TODO: Oh no, this is going to cause so many cycles, isn't it? classof(...) is horrible for this...
                var resolvedName = context.GetCppName(method.ImplementedFrom ?? method.DeclaringType, true, asReferenceButNeedInclude: true);
                if (resolvedName is null)
                    throw new UnresolvedTypeException(method.DeclaringType, method.ImplementedFrom!);
                implementingTypes.Add(method, resolvedName);
            }

            var needAs = NeedTypesAs(method);
            // We need to forward declare everything used in methods (return types and parameters)
            // If we are writing the definition, we MUST define it
            var resolvedReturn = context.GetCppName(method.ReturnType, true, needAs: NeedTypesAs(method));
            if (_resolvedReturns.ContainsKey(method))
                // If this is ignored, we will still (at least) fail on _parameterMaps.Add
                throw new InvalidOperationException("Method has already been preserialized! Don't preserialize it again! Method: " + method);
            if (resolvedReturn is null)
                // If we fail to resolve the return type, we will simply add a null item to our dictionary.
                // However, we should not call Resolved(method)
                success = false;
            _resolvedReturns.Add(method, new MethodTypeContainer(resolvedReturn, method.ReturnType));
            var parameterMap = new List<(MethodTypeContainer, ParameterModifier)>();
            foreach (var p in method.Parameters)
            {
                var s = context.GetCppName(p.Type, true, needAs: needAs);
                if (s is null)
                    // If we fail to resolve a parameter, we will simply add a (null, p.Flags) item to our mapping.
                    // However, we should not call Resolved(method)
                    success = false;
                parameterMap.Add((new MethodTypeContainer(s, p.Type), p.Modifier));
            }
            _parameterMaps.Add(method, parameterMap);

            Scope.Add(method, method.Specifiers.IsStatic() ? MethodScope.Static : MethodScope.Class);
            //RenameGenericMethods(context, method);
            ResolveName(method);
            if (success)
                Resolved(method);
        }

        private static readonly HashSet<string> MethodModifiers = new HashSet<string> { "explicit " };

        private static (string nameStr, string modifierStr) ExtractMethodModifiers(string nameStr)
        {
            // TODO: store the modifiers somewhere instead of including them in the string? (in ResolveName)
            string modifiers = "";
            while (true)
            {
                bool didSomething = false;
                foreach (var m in MethodModifiers)
                {
                    if (nameStr.StartsWith(m))
                    {
                        nameStr = nameStr.Substring(m.Length);
                        modifiers += m;
                        didSomething = true;
                    }
                }
                if (!didSomething) break;
            }
            return (nameStr, modifiers);
        }

        private bool CanWriteMethod(int genParamCount, TypeRef declaringType, bool asHeader, string sig)
        {
            if (!_signatures.ContainsKey(genParamCount))
                _signatures[genParamCount] = new HashSet<(TypeRef, bool, string)>();
            // explicit constructor/operator cannot exist if implicit version does, but we should not add implicit version from explicit
            var (trimmedSig, _) = ExtractMethodModifiers(sig);
            if (_signatures[genParamCount].Contains((declaringType, asHeader, trimmedSig)))
                return false;
            return _signatures[genParamCount].Add((declaringType, asHeader, sig));
        }

        internal enum ReturnMode
        {
            None,
            Return,
            CppOnlyConstructor,
        }

        private static bool NeedsReturn(ReturnMode mode) => mode == ReturnMode.Return;

        private static ReturnMode GetReturnMode(string retStr, string nameStr)
        {
            if (string.IsNullOrEmpty(retStr) && !nameStr.Contains("operator "))
                return ReturnMode.CppOnlyConstructor;
            else if (retStr == "void")
                return ReturnMode.None;
            else
                return ReturnMode.Return;
        }

        public static int CopyConstructorCount { get; private set; } = 0;

        private (string declaration, string cppName, ReturnMode returnMode) WriteMethod(
            MethodScope scope, IMethod method, bool asHeader, string? overrideName, bool banMethodIfFails = true)
        {
            var ns = "";
            var preRetStr = "";
            var postParensStr = "";

            string retStr = "";
            bool namespaceQualified = !asHeader;

            if (namespaceQualified)
                ns = (scope == MethodScope.Namespace ? _declaringNamespace : _declaringFullyQualified) + "::";

            if (!_resolvedReturns[method].Skip)
            {
                if (!namespaceQualified)
                {
                    if (scope == MethodScope.Static)
                        preRetStr += "static ";
                    else if (scope == MethodScope.Namespace && NeedDefinitionInHeader(method))
                        preRetStr += "inline ";
                }

                // stringify the return type
                retStr = _resolvedReturns[method].TypeName(asHeader);
            }

            string nameStr;
            if (string.IsNullOrEmpty(overrideName))
            {
                if (!_nameMap.TryGetValue(method, out var namePair))
                    throw new InvalidOperationException($"Could not find method: {method} in _nameMap! Ensure it is PreSerialized first!");
                nameStr = namePair.Item1;
            }
            else
                nameStr = overrideName;

            // Don't write wrappers for operator calls
            string paramString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method],
                ParameterFormatFlags.Names | ParameterFormatFlags.Types, header: asHeader, wantWrappers: !nameStr.Contains("operator"));

            var returnMode = GetReturnMode(retStr, nameStr);

            // Handles i.e. ".ctor"
            if (IsCtor(method))
            {
                if (_declaringIsValueType)
                {
                    retStr = "";
                    preRetStr = "";
                    nameStr = ConstructorName(method);
                }
                else
                {
                    if (method.DeclaringType.Namespace == "System" && method.DeclaringType.Name == "Object")
                        // Special case for System.Object, needs to always return ::ObjectCppName
                        retStr = $"::{Constants.ObjectCppName}*";
                    else if (method.DeclaringType.Namespace == "System" && method.DeclaringType.Name == "String")
                        // Special case for System.String, needs to always return ::StringCppName
                        retStr = $"::{Constants.StringCppName}";
                    else
                    {
                        retStr = (!asHeader ? _declaringFullyQualified : _thisTypeName)!;
                        if (retStr is null) throw new UnresolvedTypeException(method.DeclaringType, method.DeclaringType);
                        // Force return type to be a pointer
                        retStr = retStr.EndsWith("*") ? retStr : retStr + "*";
                    }

                    preRetStr = !namespaceQualified ? "static " : "";
                    nameStr = "New" + nameStr;
                    returnMode = ReturnMode.Return;
                }
            }

            retStr = retStr.Trim();
            if (!string.IsNullOrEmpty(retStr) && retStr != "void" && _config.OutputStyle == OutputStyle.Normal)
                retStr = "std::optional<" + retStr + ">";

            // TODO: store the modifiers somewhere instead of including them in the string? (in ResolveName)
            string modifiers;
            (nameStr, modifiers) = ExtractMethodModifiers(nameStr);

            if (returnMode == ReturnMode.CppOnlyConstructor)
                nameStr = ConstructorName(method);

            // Don't write wrappers if name contains operator
            var typeOnlyParamString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method],
                ParameterFormatFlags.Types, header: asHeader, wantWrappers: !nameStr.Contains("operator"));
            var signature = $"{modifiers}{nameStr}({typeOnlyParamString})";

            if (_aborted.Contains(method) || !CanWriteMethod(method.GenericParameters.Count, method.DeclaringType, asHeader, signature))
            {
                if (_config.DuplicateMethodExceptionHandling == DuplicateMethodExceptionHandling.DisplayInFile)
                {
                    if (_aborted.Contains(method))
                        preRetStr = "// ABORTED elsewhere. " + preRetStr;
                    else
                        preRetStr = "// ABORTED: conflicts with another method. " + preRetStr;
                }
                else if (_config.DuplicateMethodExceptionHandling == DuplicateMethodExceptionHandling.Elevate)
                    throw new DuplicateMethodException(method, preRetStr);
                // Otherwise, do nothing (Skip/Ignore are identical)
                if (banMethodIfFails && _aborted.Add(method))
                    Utils.Noop();
            }
            else if (_declaringIsValueType && IsCtor(method) && method.Parameters.Count > 0 && method.DeclaringType.Equals(method.Parameters[0].Type))
            {
                preRetStr = "// ABORTED: is copy constructor. " + preRetStr;
                if (banMethodIfFails && _aborted.Add(method))
                    CopyConstructorCount++;
            }

            if (!asHeader) modifiers = "";

            var ret = $"{preRetStr}{retStr} {modifiers}{ns}{nameStr}({paramString}){postParensStr}".TrimStart();
            return (ret, $"{ns}{nameStr}", returnMode);
        }

        internal void WriteCtor(CppStreamWriter writer, CppFieldSerializer fieldSer, ITypeData type, string name, bool asHeader)
        {
            // TODO: somehow use ConstructorName instead of a "name" parameter?
            // If the type we are writing is a value type, we would like to make a constructor that takes in each non-static, non-const field.
            // This is to allow us to construct structs without having to provide initialization lists that are horribly long.
            // Always write at least one constexpr constructor.
            if (asHeader && type.Info.Refness == Refness.ValueType)
            {
                var sig = $"{name}({string.Join(", ", fieldSer.ResolvedTypeNames.Select(pair => pair.Value))})";
                if (!CanWriteMethod(0, type.This, asHeader, sig)) return;

                var signature = $"constexpr {name}(";
                if (type.Info.Refness != Refness.ValueType)
                    // Reference types will have non-constexpr constructors.
                    signature = name + "(";
                signature += string.Join(", ", fieldSer.ResolvedTypeNames.Where(p => fieldSer.FirstOrNotInUnion(p.Key)).Select(pair =>
                {
                    var typeName = pair.Value;
                    var fieldName = fieldSer.SafeFieldNames[pair.Key];
                    var defaultVal = typeName!.StartsWith("::ArrayW<") ? $"{typeName}(static_cast<void*>(nullptr))" : "{}";
                    return typeName + " " + fieldName + $"_ = {defaultVal}";
                }));
                signature += ") noexcept";
                string subConstructors = string.Join(", ", fieldSer.SafeFieldNames.Where(p => fieldSer.FirstOrNotInUnion(p.Key)).Select(pair =>
                {
                    return pair.Value + "{" + pair.Value + "_}";
                }));
                if (!string.IsNullOrEmpty(subConstructors))
                    signature += " : " + subConstructors;
                signature += " {}";
                writer.WriteComment("Creating value type constructor for type: " + name);
                writer.WriteLine(signature);
            }
        }

        internal void WriteInterfaceConversionOperator(CppStreamWriter writer, ITypeData type, string? interfaceType)
        {
            if (interfaceType is null) throw new ArgumentNullException(nameof(interfaceType));
            var name = "operator " + interfaceType;
            var sig = name + "()";
            if (!CanWriteMethod(0, type.This, true, sig)) return;

            var signature = $"{name}() noexcept";
            writer.WriteComment("Creating interface conversion operator: " + name);
            writer.WriteDefinition(signature);
            writer.WriteDeclaration($"return *reinterpret_cast<{interfaceType}*>(this)");
            writer.CloseDefinition();
        }

        internal void WriteConversionOperator(CppStreamWriter writer, CppFieldSerializer fieldSer, ITypeData type,
            FieldConversionOperator op, bool asHeader)
        {
            if (op.Field is null) return;
            // If the type we are writing is a value type with exactly one instance field, we would like to make an implicit conversion operator that
            // converts the type to the field. If a subclass then adds any instance fields, that operator must be deleted in the subclass.
            if (asHeader && op.Kind != ConversionOperatorKind.Inherited)
            {
                var name = "operator " + fieldSer.ResolvedTypeNames[op.Field];
                var sig = name + "()";
                if (!CanWriteMethod(0, type.This, asHeader, sig)) return;

                var signature = $"constexpr {name}() const noexcept";

                if (op.Kind == ConversionOperatorKind.Delete)
                {
                    writer.WriteComment("Deleting conversion operator: " + name);
                    if (op.Field.Type.Generics.Any(g => op.Field.DeclaringType.Generics.Contains(g)))
                    {
                        // Cannot delete this conversion operator, since it has a generic parameter that seems to be an inherited argument-- too annoying, don't bother.
                        writer.WriteComment("Cannot delete conversion operator because it seems to have a generic type in the definition! This may not be defined!");
                    }
                    else
                    {
                        writer.WriteDeclaration(signature + " = delete");
                    }
                }
                else if (op.Kind == ConversionOperatorKind.Yes)
                {
                    writer.WriteComment("Creating conversion operator: " + name);
                    writer.WriteDefinition(signature);
                    writer.WriteDeclaration($"return {fieldSer.SafeFieldNames[op.Field]}");
                    writer.CloseDefinition();
                }
                else
                    throw new ArgumentException($"Can't write conversion operator from kind '{op.Kind}'");
            }
        }

        private static bool IsCtor(IMethod method) => method.Il2CppName == ".ctor";

        private bool TemplateString(IMethod method, bool withTemps, [NotNullWhen(true)] out string? templateString)
        {
            templateString = null;
            var str = "";
            bool hadGenerics = false;
            if (_genericArgs.TryGetValue(method, out var generics) && generics.Any())
            {
                hadGenerics = true;
                str += string.Join(", ", generics.Select(s => "class " + s));
            }
            if (IsCtor(method) && !_declaringIsValueType)
            {
                if (hadGenerics)
                    str += ", ";
                hadGenerics = true;
                str += "::il2cpp_utils::CreationType creationType = ::il2cpp_utils::CreationType::Temporary";
            }
            if (_tempGenerics.TryGetValue(method, out var temps) && temps.Any())
            {
                if (hadGenerics)
                    str += ", ";
                hadGenerics = true;
                if (withTemps)
                    str += string.Join(", ", temps.Select(s => "class " + s));
            }
            if (hadGenerics)
            {
                templateString = $"template<{str}>";
                return true;
            }
            return false;
        }

        private static string Il2CppNoArgClass(string t)
        {
            return $"::il2cpp_utils::il2cpp_type_check::il2cpp_no_arg_class<{t}>::get()";
        }

        private string GenericTypesList(IMethod method)
        {
            if (_genericArgs.TryGetValue(method, out var generics))
            {
                var str = string.Join(", ", generics.Select(Il2CppNoArgClass));
                if (!string.IsNullOrEmpty(str))
                    return $"{{{str}}}";
            }
            return "{}";
        }

        /// <summary>
        /// Returns true if the provded method name is an operator that needs a += or -= version of itself.
        /// </summary>
        /// <param name="methodName"></param>
        /// <returns></returns>
        //private static bool NeedAssignmentVersion(string methodName)
        //{
        //}

        // Write the method here
        public override void Serialize(CppStreamWriter writer, IMethod method, bool asHeader)
        {
            if (writer is null) throw new ArgumentNullException(nameof(writer));
            if (method is null) throw new ArgumentNullException(nameof(method));
            if (!_resolvedReturns.ContainsKey(method))
                // In the event we have decided to not parse this method (in PreSerialize) don't even bother.
                return;
            if (IgnoredMethods.Contains(method.Il2CppName) || _config.BlacklistMethods.Contains(method.Il2CppName))
                return;
            if (_resolvedReturns[method] == null)
                throw new UnresolvedTypeException(method.DeclaringType, method.ReturnType);
            if (_thisTypeName == null)
                throw new UnresolvedTypeException(method.DeclaringType, method.DeclaringType);
            for (int i = 0; i < _parameterMaps[method].Count; i++)
            {
                var (container, _) = _parameterMaps[method][i];
                if (container.TypeName(asHeader) is null && !method.Parameters[i].Type.IsGenericParameter)
                    throw new UnresolvedTypeException(method.DeclaringType, method.Parameters[i].Type);
            }
            if (!asHeader && NeedDefinitionInHeader(method))
                // Need to create the method ENTIRELY in the header, instead of split between the C++ and the header
                return;

            string? overrideName = null;
            // If the method is specially named, then we need to print it normally, don't worry about any of this rename garbage
            bool performProxy = method.BaseMethods.Count >= 1 && method.Il2CppName.IndexOf('.') < 1;
            if (performProxy)
                overrideName = _config.SafeMethodName(method.Name.Replace('<', '$').Replace('>', '$').Replace('.', '_').Replace(',', '_'));

            bool writeContent = !asHeader || NeedDefinitionInHeader(method);
            var scope = Scope[method];
            var (declaration, cppName, returnMode) = WriteMethod(scope, method, asHeader, overrideName);
            bool commentMethod = declaration.StartsWith("/");
            bool needsReturn = NeedsReturn(returnMode);
            if (asHeader)
            {
                var methodComment = "";
                foreach (var spec in method.Specifiers)
                    methodComment += $"{spec} ";
                // Method comment should also use the Il2CppName whenever possible
                methodComment += $"{method.ReturnType} {method.Il2CppName}({method.Parameters.FormatParameters()})";
                writer.WriteComment(methodComment);

                writer.WriteComment($"Offset: 0x{method.Offset:X}");
                if (method.ImplementedFrom != null)
                    writer.WriteComment("Implemented from: " + method.ImplementedFrom);
                foreach (var bm in method.BaseMethods)
                    writer.WriteComment($"Base method: {bm.ReturnType} {bm.DeclaringType.CppName()}::{bm.Name}({method.Parameters.FormatParameters()})");
                if (!writeContent)
                {
                    if (TemplateString(method, asHeader, out var templateStr))
                        writer.WriteLine((commentMethod ? "// " : "") + templateStr);
                    writer.WriteDeclaration(declaration);
                }
                // Add all "would be written" methods to the post serialization collection
                postSerializeCollection.Add((method, cppName, declaration));
            }
            else
                // Comment for autogenerated method should use Il2CppName whenever possible
                writer.WriteComment($"Autogenerated method: {method.DeclaringType}.{method.Il2CppName}");

            if (writeContent)
            {
                // TODO: If ctor, write _ctor method, c++ constructor
                if (TemplateString(method, asHeader, out var templateStr))
                    writer.WriteLine((commentMethod ? "// " : "") + templateStr);

                // Write the qualified name if not in the header
                if (declaration.StartsWith("/"))
                {
                    writer.WriteLine(declaration);
                    writer.Flush();
                    return;
                }
                writer.WriteDefinition(declaration);

                if (_genParamConstraints.TryGetValue(method, out var genParamConstraints))
                    CppTypeDataSerializer.WriteGenericTypeConstraints(writer, genParamConstraints);

                var (@namespace, @class) = method.DeclaringType.GetIl2CppName();
                var classArgs = $"\"{@namespace}\", \"{@class}\"";
                if (method.DeclaringType.IsGeneric)
                    classArgs = Il2CppNoArgClass(_thisTypeName);

                var genTypesList = GenericTypesList(method);

                string returnType = _resolvedReturns[method].TypeName(asHeader);

                bool isNewCtor = IsCtor(method) && !_declaringIsValueType;
                if (IsCtor(method))
                {
                    if (method.Generic)
                        Utils.Noop();
                    // Always use thisTypeName for the cast type, since we are already within the context of the type.
                    returnType = _declaringIsValueType ? "void" : _thisTypeName.EndsWith("*") ? _thisTypeName : _thisTypeName + "*";
                    // var paramNames = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader);
                }

                // TODO: Potentially conflicting naming
                var loggerId = "___internal__logger";
                var mName = "___internal__method";
                var genMName = "___generic__method";

                writer.WriteDeclaration($"static auto {loggerId} = ::Logger::get()" +
                    $".WithContext(\"{method.DeclaringType.GetQualifiedCppName()}::{method.Name}\")");

                string s = "";
                string innard = "";
                if (needsReturn)
                    s = "return ";
                else if (returnMode == ReturnMode.CppOnlyConstructor)
                    s = "*this = " + (_declaringIsValueType ? "" : "*");

                // `*this =` doesn't work without a cast either
                if (!isNewCtor)
                {
                    // Innard should be set to not perform type checking
                    //innard = returnMode != ReturnMode.None ? $"<{returnType}, false>" : "<Il2CppObject*, false>";
                    innard = $"<{returnType}, false>";
                }
                else
                {
                    // We specify creationType for our New calls.
                    innard = "<" + returnType + ", creationType>";
                }

                // We should avoid calling RunMethod, as we can be very explicit that we are confident we are calling it correctly.
                // TODO: Eventually optimize New as well as RunGenericMethod and RunMethod
                var utilFunc = isNewCtor ? "New" : "RunMethodRethrow";

                // If we are calling RunGenericMethodThrow or RunMethodThrow, we should cache the found method first.
                // ONLY IF we are not an abstract/virtual method!
                // If we are, we need to perform a standard RunMethodThrow call, with FindMethod from the instance.
                var paramString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader, (pair, st) =>
                {
                    if (pair.Item2 != ParameterModifier.None && pair.Item2 != ParameterModifier.Params)
                    {
                        return "byref(" + st + ")";
                    }
                    return st;
                });
                string thisArg = (_declaringIsValueType ? "*" : "") + "this";
                var call = $"::il2cpp_utils::{utilFunc}{innard}(";

                string extractionString = null!;
                if (!isNewCtor)
                {
                    extractionString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader, (pm, s) =>
                    {
                        // The string used for extracting types matters here. Parameters that are non-out parameters are simply: ExtractType(name)
                        // Parameters that are types are: ExtractIndependentType<TParam>()
                        if (pm.Item2 == ParameterModifier.Out)
                        {
                            // We need to keep the & here, which will then ensure we find the method with the matching reftype.
                            return $"::il2cpp_utils::ExtractIndependentType<{pm.PrintParameter(asHeader)}>()";
                        }
                        else
                        {
                            return $"::il2cpp_utils::ExtractType({s})";
                        }
                    });
                    var invokeMethodName = "___internal__method";
                    // Static methods are cacheable, virtual methods should never be cached, methods on generic types that used generic args should not be cached.
                    bool cache = !method.IsVirtual || method.Specifiers.IsStatic() && !method.Parameters.Any(p => method.DeclaringType.Generics.Any(p2 => p2.Equals(p)));
                    if (!method.IsVirtual)
                    {
                        writer.WriteDeclaration($"{(cache ? "static " : "")}auto* {invokeMethodName} = " +
                            _config.MacroWrap(loggerId, $"::il2cpp_utils::FindMethod({(method.Specifiers.IsStatic() ? classArgs : thisArg)}, \"{method.Il2CppName}\", std::vector<Il2CppClass*>{genTypesList}, ::std::vector<const Il2CppType*>{{{extractionString}}})", true));
                    }
                    else
                    {
                        // Method IS virtual, lets do a virtual lookup for this particular method. We need to know WHERE this particular method comes from
                        // (if it is virtual and overriding) as well as use ourselves if it isn't defined in any base types.

                        // The problem here is that if the method we are trying to write out is a virtual INTERFACE method, we will have to include the interfaces for classof
                        var targetClass = $"classof({implementingTypes[method]})";
                        writer.WriteDeclaration($"{(cache ? "static " : "")}auto* {invokeMethodName} = {_config.MacroWrap(loggerId, $"::il2cpp_utils::ResolveVtableSlot({thisArg}, {targetClass}, {method.Slot})", false)}");
                    }
                    if (method.Generic)
                    {
                        writer.WriteDeclaration($"{(cache ? "static " : "")}auto* ___generic__method = " +
                            _config.MacroWrap(loggerId, $"::il2cpp_utils::MakeGenericMethod({mName}, std::vector<Il2CppClass*>{genTypesList})", true));
                        mName = genMName;
                        invokeMethodName = "___generic__method";
                    }
                    string firstParam = method.Specifiers.IsStatic() ? "static_cast<Il2CppObject*>(nullptr)" : "this";
                    call += $"{firstParam}, {invokeMethodName}" + (paramString.Length > 0 ? (", " + paramString) : "") + ")";
                    
                }
                else
                {
                    // If it is not {}
                    call += $"{(genTypesList.Length > 2 ? ", " + genTypesList : "")}{paramString})";
                }

                // Write call
                if (isNewCtor || !utilFunc.EndsWith("throw", StringComparison.Ordinal))
                    writer.WriteDeclaration(s + _config.MacroWrap(loggerId, call, needsReturn));
                else
                    writer.WriteDeclaration(s + call);
                // Close method
                writer.CloseDefinition();
            }

            // REMOVE TARG PROXYING!
            //var param = method.Parameters.Where(p => p.Modifier == ParameterModifier.Params).SingleOrDefault();
            //if (param != null && !commentMethod)
            //{
            //    var (container, _) = _parameterMaps[method][^1];
            //    var origMethod = $"{method.ReturnType} {method.Il2CppName}({method.Parameters.FormatParameters()})".TrimStart();

            //    if (container.HasTemplate || param.Type.IsOrContainsMatch(t => method.GenericParameters.Contains(t)))
            //        writer.WriteComment($"ABORTED: Cannot write std::intializer_list proxy for {origMethod} as the 'params' type ({param.Type}) already " +
            //            "is/contains a method-level generic parameter!");
            //    else
            //    {
            //        container.ExpandParams = true;

            //        var initializerListProxyInfo = WriteMethod(scope, method, asHeader, overrideName, false);
            //        declaration = initializerListProxyInfo.declaration;
            //        bool commentProxy1 = declaration.StartsWith("/");

            //        writer.WriteComment($"Creating initializer_list -> params proxy for: {origMethod}");
            //        if (TemplateString(method, !writeContent, out var templateStr))
            //            writer.WriteLine((commentProxy1 ? "// " : "") + templateStr);

            //        if (commentProxy1)
            //        {
            //            writer.WriteComment("proxy would be redundant?!");
            //            writer.WriteLine(declaration);
            //        }
            //        else if (!writeContent)
            //            writer.WriteDeclaration(declaration);
            //        else
            //        {
            //            writer.WriteDefinition(declaration);
            //            // Call original method (return as necessary)
            //            string s = needsReturn ? "return " : "";
            //            if (cppName.EndsWith("New_ctor"))
            //                cppName += "<creationType>";
            //            s += $"{cppName}({method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader)})";
            //            writer.WriteDeclaration(s);
            //            writer.CloseDefinition();
            //        }

            //        if (!commentProxy1 && asHeader)
            //        {
            //            var tempGens = _tempGenerics.GetOrAdd(method);
            //            // Temporarily add a generic TParams
            //            tempGens.AddOrThrow("...TParams");
            //            container.Template("TParams&&...");

            //            declaration = WriteMethod(scope, method, asHeader, overrideName, false).declaration;

            //            // TArgs proxies for different initializer_list T's look exactly the same.
            //            if (!declaration.StartsWith("/"))
            //            {
            //                writer.WriteComment($"Creating TArgs -> initializer_list proxy for: {origMethod}");
            //                if (TemplateString(method, true, out templateStr))
            //                    writer.WriteLine(templateStr);
            //                writer.WriteDefinition(declaration);
            //                // Call original method (return as necessary)
            //                string s = NeedsReturn(initializerListProxyInfo.returnMode) ? "return " : "";
            //                // If we have generics in our original method, we need to forward them
            //                var typeArg = "";
            //                if (_genericArgs.TryGetValue(method, out var generics) && generics.Any())
            //                {
            //                    typeArg = "<" + string.Join(", ", generics);
            //                }
            //                if (initializerListProxyInfo.cppName.EndsWith("New_ctor"))
            //                    typeArg = typeArg.Length == 0 ? "<creationType>" : typeArg + "creationType>";
            //                else if (typeArg.Length != 0)
            //                    typeArg += ">";
            //                s += $"{initializerListProxyInfo.cppName}{typeArg}({method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader)})";
            //                writer.WriteDeclaration(s);
            //                writer.CloseDefinition();
            //            }

            //            // Remove the generic TParams
            //            container.Template(null);
            //            tempGens.RemoveOrThrow("...TParams");
            //        }
            //        container.ExpandParams = false;
            //    }
            //}

            // If we have any parameters that return strings, we should make a specially named method that returns a std::stringu16, std::string

            // If we have 2 or more base methods, we need to see if either of our base methods have been renamed.
            // If any of them have been renamed, we need to create a new method for that and map it to the method we are currently serializing.
            // Basically, if we have void Clear() with two base methods, one of which is renamed, we create void Clear(), and we create void QUALIFIED_Clear()
            // Where QUALIFIED_Clear() simply calls Clear()

            // REMOVE QUALIFIED PROXYING, USES SLOTS INSTEAD!
            //if (performProxy)
            //{
            //    // Original method would have already been created by now.
            //    foreach (var bm in method.BaseMethods)
            //    {
            //        if (!_nameMap.TryGetValue(bm, out var pair))
            //            throw new InvalidOperationException($"{bm} does not have a name!");
            //        if (pair.Item2)
            //        {
            //            // If we have renamed the base method, we write the method.
            //            // If we are a header, write the comments
            //            if (asHeader)
            //            {
            //                writer.WriteComment("Creating proxy method: " + pair.Item1);
            //                // We want to map it to a method that is NOT renamed!
            //                writer.WriteComment("Maps to method: " + method.Name);
            //            }

            //            declaration = WriteMethod(scope, method, asHeader, pair.Item1, false).declaration;
            //            // Write method content
            //            if (TemplateString(method, !writeContent, out var templateStr))
            //                writer.WriteLine((declaration.StartsWith("/") ? "// " : "") + templateStr);
            //            if (!writeContent)
            //            {
            //                if (declaration.StartsWith("/"))
            //                    writer.WriteComment($"Skipping redundant proxy method: {pair.Item1}");
            //                else
            //                    writer.WriteDeclaration(declaration);
            //            }
            //            else
            //            {
            //                if (declaration.StartsWith("/"))
            //                {
            //                    // Comment failures
            //                    // If we encounter a redundant proxy method, we will continue to print "ABORTED"
            //                    // We will additionally provide information stating that this method was a redundant proxy
            //                    writer.WriteComment("Redundant proxy method!");
            //                    writer.WriteLine(declaration);
            //                    continue;
            //                }
            //                writer.WriteDefinition(declaration);
            //                // Call original method (return as necessary)
            //                string s = needsReturn ? "return " : "";
            //                s += $"{cppName}({method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader)})";
            //                writer.WriteDeclaration(s);
            //                writer.CloseDefinition();
            //            }
            //        }
            //    }
            //}
            writer.Flush();
            Serialized(method);
        }

        private static List<string> ClassFromType(TypeRef type)
        {
            // Options:
            // il2cpp_functions::Class_GetPtrClass - for adding * after pure value types
            // il2cpp_utils::GetClassFromName - for the obvious
            // il2cpp_utils::MakeGeneric - for making a generic instance from other classes
            // Nested types???
            // il2cpp_functions::MetadataCache_GetTypeInfoFromTypeDefinitionIndex(declaring->generic_class->typeDefinitionIndex) - declaring type is generic
            // il2cpp_functions::class_get_nested_types - search for nested match
            // const Il2CppGenericInst* genInst = declaring->generic_class->context.class_inst - If declaring is generic, get instantiation
            // Use it to MakeGeneric, use result.
            // If we have an element type and we are an array, call array_class_get
            if (type.IsArray() && type.ElementType is not null)
            {
                return new List<string> { $"il2cpp_functions::array_class_get({ClassFromType(type.ElementType).Single()}, 1)" };
            }
            // If we have an element type and we are not an array, call il2cpp_functions::Class_GetPtrClass(result of element type)
            else if (type.ElementType is not null)
            {
                return new List<string> { $"il2cpp_functions::Class_GetPtrClass({ClassFromType(type.ElementType).Single()})" };
            }

            var (namespaze, name) = type.GetIl2CppName();

            var decl = type.DeclaringType;
            bool genericDeclaring = false;
            while (decl is not null)
            {
                if (decl.IsGeneric)
                {
                    genericDeclaring = true;
                    break;
                }
                decl = decl.DeclaringType;
            }

            if (type.DeclaringType is null || !genericDeclaring)
            {
                // No generic declaring type works the same way, except GetIl2CppName will return the properly formatted name.
                // No declaring type is simple, works for both value types and reference types.
                var classGetter = $"::il2cpp_utils::GetClassFromName(\"{namespaze}\", \"{name}\")";
                // First check to see if the type is generic, if it is, make the generic instantiation.
                if (type.IsGeneric)
                {
                    classGetter = $"::il2cpp_utils::MakeGeneric({classGetter}, ::std::vector<const Il2CppClass*>{{{string.Join(", ", type.Generics.Select(g => ClassFromType(g).Single()))}}})";
                }
                return new List<string> { classGetter };
            }
            else
            {
                // Now, this is the problematic one.
                // This means we have at least one declaring type that is generic.
                // Logically, what we need to do is find our declaring type using the generic instantiation of it

                //var classOfDeclaring = ClassFromType(type.DeclaringType, type.DeclaringType.GetQualifiedCppName());

                // Then search it for ourselves
                // When we find a match, instantiate ourselves with the generic parameters from our declaring generics
                // return the instantiated result
                // For now we shall not even attempt to do this properly
                // YEEEEEEEEET
                return new List<string>();
                //var ret = new List<string>();
                //ret.Add($"static auto* declaring = {classOfDeclaring};");
                //ret.Add("void* myIter = nullptr;");
                //ret.Add("Il2CppClass* found = nullptr;");
                //ret.Add("while (auto* nested = il2cpp_functions::class_get_nested_types(declaring, &myIter)) {");
                //ret.Add("if (typeName == nested->name) {found = nested; break;}");
                //ret.Add("}");
                //ret.Add("CRASH_UNLESS(found);");
                //ret.Add("if (declaring->generic_class) {auto* genInst = declaring->generic_class->context.class_inst")
            }
        }

        private void WriteMetadataGetter(CppStreamWriter writer, string type, IMethod method, string castMethodPtr)
        {
            // In order to properly handle overloads, we need to emit a static_cast with the correct signature type
            writer.WriteLine("template<>");
            writer.WriteDefinition($"struct ::il2cpp_utils::il2cpp_type_check::MetadataGetter<{castMethodPtr}>");
            writer.WriteDefinition("static const MethodInfo* get()");
            // Instead of writing ExtractIndependentType, which requires the definitions of the parameter types to be present, lets use the literal calls
            for (int i = 0; i < _parameterMaps[method].Count; ++i)
            {
                var (container, modifier) = _parameterMaps[method][i];
                // klass->this_arg - for byref types
                // klass->byval_arg - for standard types

                var classGetters = ClassFromType(container.Type);
                string typeAccessor = modifier != ParameterModifier.None && modifier != ParameterModifier.Params ? "this_arg" : "byval_arg";

                // Then we simply use paramName for each of the items in the const Il2CppType* vector and it should find the match.
                var paramName = method.Parameters[i].Name;
                if (string.IsNullOrWhiteSpace(paramName))
                    paramName = $"param_{i}";
                while (_config.IllegalNames?.Contains(paramName) ?? false)
                    paramName = "_" + paramName;
                paramName = paramName.Replace('<', '$').Replace('>', '$');
                writer.WriteDeclaration($"static auto* {paramName} = &{classGetters.Single()}->{typeAccessor}");
            }
            var extractionString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names);

            writer.WriteDeclaration($"return ::il2cpp_utils::FindMethod(classof({type}), \"{method.Il2CppName}\", std::vector<Il2CppClass*>(), ::std::vector<const Il2CppType*>{{{extractionString}}})");
            writer.CloseDefinition();
            writer.CloseDefinition(";");
        }

        private static readonly List<string> ctorOptions = new()
        {
            "::il2cpp_utils::CreationType::Temporary",
            "::il2cpp_utils::CreationType::Manual"
        };

        public void PostSerialize(CppStreamWriter writer)
        {
            // Type cannot be generic
            foreach (var (method, cppName, _) in postSerializeCollection)
            {
                // We write a metadata specialization for each
                // If the type in question is generic, we need to combine generic arguments
                // If the method in question is generic, we need to combine generic arguments
                // If the method in question is only generic because we made it generic, don't call MakeGenericMethod
                var typeName = _thisTypeName == "Il2CppObject*" ? "System::Object" : _declaringFullyQualified;

                writer.WriteComment($"Writing MetadataGetter for method: {typeName}::{cppName}");
                writer.WriteComment($"Il2CppName: {method.Il2CppName}");
                if (method.Generic)
                {
                    writer.WriteComment("Cannot write MetadataGetter for generic methods!");
                    continue;
                }
                if (_parameterMaps[method].Any(p => ClassFromType(p.container.Type).Count == 0))
                {
                    // If we have a parameter we can't write, skip this
                    writer.WriteComment("Cannot write MetadataGetter for a method that has a nested type with a declaring generic type anywhere within it!");
                    writer.WriteComment("Talk to sc2ad if this is something you want");
                    continue;
                }
                var memberPtr = $"&{typeName}::{cppName}";
                var instancePtr = method.Specifiers.IsStatic() ? "*" : typeName + "::*";
                var cast = $"static_cast<{_resolvedReturns[method].TypeName(true)} ({instancePtr})({method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Types, true, wantWrappers: true)})>";
                if (IsCtor(method))
                {
                    // Constructors we need to write TWO specializations for:
                    // One where it expects ::il2cpp_utils::CreationType::Temporary
                    // And the other as manual ::il2cpp_utils::CreationType::Manual

                    writer.WriteComment("Cannot get method pointer of value based method overload from template for constructor!");
                    writer.WriteComment("Try using FindMethod instead!");
                    //foreach (var tempParam in ctorOptions)
                    //{
                    //    WriteMetadataGetter(writer, typeName + (!_declaringIsValueType ? "*" : ""), method, cast + $"({memberPtr}<{tempParam}>)");
                    //}
                }
                else if (Operators.ContainsKey(method.Il2CppName))
                {
                    writer.WriteComment("Cannot perform method pointer template specialization from operators!");
                }
                else
                {
                    WriteMetadataGetter(writer, typeName + (!_declaringIsValueType ? "*" : ""), method, cast + $"({memberPtr})");
                }
            }
        }
    }
}