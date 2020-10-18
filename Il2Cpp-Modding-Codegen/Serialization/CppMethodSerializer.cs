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

        private readonly SerializationConfig _config;

        private readonly Dictionary<IMethod, MethodTypeContainer> _resolvedReturns = new Dictionary<IMethod, MethodTypeContainer>();
        private readonly Dictionary<IMethod, List<(MethodTypeContainer container, ParameterModifier modifier)>> _parameterMaps = new Dictionary<IMethod, List<(MethodTypeContainer container, ParameterModifier modifier)>>();

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

        private bool _declaringIsValueType;

        private readonly Dictionary<TypeRef, CppTypeDataSerializer.State> _stateMap;

        internal CppMethodSerializer(SerializationConfig config, Dictionary<TypeRef, CppTypeDataSerializer.State> map)
        {
            _config = config;
            _stateMap = map;
        }

        private static bool NeedDefinitionInHeader(IMethod method) => method.DeclaringType.IsGenericTemplate || method.Generic;

        /// <summary>
        /// Returns whether the given method should be written as a definition or a declaration
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private static CppTypeContext.NeedAs NeedTypesAs(IMethod method)
        {
            // The TArgs -> initializer_list params proxy will also need complete types
            if (NeedDefinitionInHeader(method) || method.Parameters.Any(p => p.Modifier == ParameterModifier.Params)) return CppTypeContext.NeedAs.BestMatch;
            return CppTypeContext.NeedAs.Declaration;
        }

        private void ResolveName(IMethod method)
        {
            static void FixNames(IMethod m, string n, bool isFullName, HashSet<IMethod> skip)
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
                var implementedFrom = method.ImplementedFrom ?? throw new InvalidOperationException("Tried to construct name from null ImplementedFrom!");
                name = implementedFrom.GetQualifiedCppName().Replace("::", "_") + "_" + tName;
                fullName = true;
            }
            else
                // If the name is not a special name, set it to be the method name
                name = method.Name;
            name = _config.SafeMethodName(name).Replace('<', '$').Replace('>', '$').Replace('.', '_').Replace('|', '_');

            if (Operators.TryGetValue(name, out var info))
            {
                var numParams = _parameterMaps[method].Count;
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

                static void SuffixRefUnlessPointer(MethodTypeContainer container)
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
                if (method.BaseMethods.Count > 1)
                {
                    // If we have more than 1 base method, we need to avoid renaming (saves time)
                    // However, if we have a fullName, AND we SOMEHOW have 2 or more base methods, we need to throw a big exception
                    if (fullName)
                        throw new InvalidOperationException($"Should not have more than one base method for special name method: {method}!");
                    break;
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
            _resolvedReturns.Add(method, new MethodTypeContainer(resolvedReturn));
            var parameterMap = new List<(MethodTypeContainer, ParameterModifier)>();
            foreach (var p in method.Parameters)
            {
                var s = context.GetCppName(p.Type, true, needAs: needAs);
                if (s is null)
                    // If we fail to resolve a parameter, we will simply add a (null, p.Flags) item to our mapping.
                    // However, we should not call Resolved(method)
                    success = false;
                parameterMap.Add((new MethodTypeContainer(s), p.Modifier));
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
        static bool NeedsReturn(ReturnMode mode) => mode == ReturnMode.Return;

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

            string paramString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method],
                ParameterFormatFlags.Names | ParameterFormatFlags.Types, header: asHeader);

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
                        retStr = $"::{Constants.StringCppName}*";
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

            var typeOnlyParamString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method],
                ParameterFormatFlags.Types, header: asHeader);
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
            else if (_declaringIsValueType && IsCtor(method) && method.Parameters.Count > 0 && method.DeclaringType == method.Parameters[0].Type)
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
            if (type.Info.Refness == Refness.ValueType && asHeader)
            {
                var sig = $"{name}({string.Join(", ", fieldSer.ResolvedTypeNames.Select(pair => pair.Value))})";
                if (!CanWriteMethod(0, type.This, asHeader, sig)) return;

                var signature = $"constexpr {name}(";
                signature += string.Join(", ", fieldSer.ResolvedTypeNames.Select(pair =>
                {
                    var typeName = pair.Value;
                    var fieldName = fieldSer.SafeFieldNames[pair.Key];
                    return typeName + " " + fieldName + "_ = {}";
                }));
                signature += ") noexcept";
                string subConstructors = string.Join(", ", fieldSer.SafeFieldNames.Select(pair =>
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
                    writer.WriteDeclaration(signature + " = delete");
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
            return $"il2cpp_utils::il2cpp_type_check::il2cpp_no_arg_class<{t}>::get()";
        }

        private string GenericTypesList(IMethod method)
        {
            if (_genericArgs.TryGetValue(method, out var generics))
            {
                var str = string.Join(", ", generics.Select(Il2CppNoArgClass));
                if (!string.IsNullOrEmpty(str))
                    return $", {{{str}}}";
            }
            return "";
        }

        // Write the method here
        public override void Serialize(CppStreamWriter writer, IMethod method, bool asHeader)
        {
            if (writer is null) throw new ArgumentNullException(nameof(writer));
            if (method is null) throw new ArgumentNullException(nameof(method));
            if (!_resolvedReturns.ContainsKey(method))
                // In the event we have decided to not parse this method (in PreSerialize) don't even bother.
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
            if (IgnoredMethods.Contains(method.Il2CppName) || _config.BlacklistMethods.Contains(method.Il2CppName))
                return;
            if (!asHeader && NeedDefinitionInHeader(method))
                // Need to create the method ENTIRELY in the header, instead of split between the C++ and the header
                return;

            string? overrideName = null;
            // If the method is specially named, then we need to print it normally, don't worry about any of this rename garbage
            bool performProxy = method.BaseMethods.Count >= 1 && method.Il2CppName.IndexOf('.') < 1;
            if (performProxy)
                overrideName = _config.SafeMethodName(method.Name.Replace('<', '$').Replace('>', '$').Replace('.', '_'));

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
            }
            else
                // Comment for autogenerated method should use Il2CppName whenever possible
                writer.WriteComment($"Autogenerated method: {method.DeclaringType}.{method.Il2CppName}");

            if (writeContent)
            {
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
                    returnType = (_thisTypeName.EndsWith("*") || _declaringIsValueType) ? _thisTypeName : _thisTypeName + "*";
                    // var paramNames = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader);
                }

                string s = "";
                string innard = "";
                if (needsReturn)
                    s = "return ";
                else if (returnMode == ReturnMode.CppOnlyConstructor)
                    s = "*this = " + (_declaringIsValueType ? "" : "*");

                // `*this =` doesn't work without a cast either
                if (returnMode != ReturnMode.None)
                    innard = $"<{returnType}>";

                var utilFunc = method.Generic ? "RunGenericMethod" : (isNewCtor ? "New" : "RunMethod");
                var call = $"il2cpp_utils::{utilFunc}{innard}(";

                var paramString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader);
                if (!isNewCtor)
                {
                    string thisArg = (_declaringIsValueType ? "*" : "") + "this";
                    if (!string.IsNullOrEmpty(paramString))
                        paramString = ", " + paramString;
                    if (method.Specifiers.IsStatic() && scope == MethodScope.Class)
                        paramString = ", " + thisArg + paramString;
                    if (method.Specifiers.IsStatic())
                        call += $"{classArgs}, ";
                    else if (scope == MethodScope.Class)
                        call += $"{thisArg}, ";
                    // Macro string should use Il2CppName (of course, without _, $ replacement)
                    call += $"\"{method.Il2CppName}\"";
                }
                call += $"{genTypesList}{paramString})";
                // Write call
                writer.WriteDeclaration(s + _config.MacroWrap(call, needsReturn));
                // Close method
                writer.CloseDefinition();
            }

            var param = method.Parameters.Where(p => p.Modifier == ParameterModifier.Params).SingleOrDefault();
            if (param != null && !commentMethod)
            {
                var (container, _) = _parameterMaps[method][^1];
                var origMethod = $"{method.ReturnType} {method.Il2CppName}({method.Parameters.FormatParameters()})".TrimStart();

                if (container.HasTemplate || param.Type.IsOrContainsMatch(t => method.GenericParameters.Contains(t)))
                    writer.WriteComment($"ABORTED: Cannot write std::intializer_list proxy for {origMethod} as the 'params' type ({param.Type}) already " +
                        "is/contains a method-level generic parameter!");
                else
                {
                    container.ExpandParams = true;

                    var initializerListProxyInfo = WriteMethod(scope, method, asHeader, overrideName, false);
                    declaration = initializerListProxyInfo.declaration;
                    bool commentProxy1 = declaration.StartsWith("/");

                    writer.WriteComment($"Creating initializer_list -> params proxy for: {origMethod}");
                    if (TemplateString(method, !writeContent, out var templateStr))
                        writer.WriteLine((commentProxy1 ? "// " : "") + templateStr);

                    if (commentProxy1)
                    {
                        writer.WriteComment("proxy would be redundant?!");
                        writer.WriteLine(declaration);
                    }
                    else if (!writeContent)
                        writer.WriteDeclaration(declaration);
                    else
                    {
                        writer.WriteDefinition(declaration);
                        // Call original method (return as necessary)
                        string s = needsReturn ? "return " : "";
                        s += $"{cppName}({method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader)})";
                        writer.WriteDeclaration(s);
                        writer.CloseDefinition();
                    }

                    if (!commentProxy1 && asHeader)
                    {
                        var tempGens = _tempGenerics.GetOrAdd(method);
                        // Temporarily add a generic TParams
                        tempGens.AddOrThrow("...TParams");
                        container.Template("TParams&&...");

                        declaration = WriteMethod(scope, method, asHeader, overrideName, false).declaration;

                        // TArgs proxies for different initializer_list T's look exactly the same.
                        if (!declaration.StartsWith("/"))
                        {
                            writer.WriteComment($"Creating TArgs -> initializer_list proxy for: {origMethod}");
                            if (TemplateString(method, true, out templateStr))
                                writer.WriteLine(templateStr);
                            writer.WriteDefinition(declaration);
                            // Call original method (return as necessary)
                            string s = NeedsReturn(initializerListProxyInfo.returnMode) ? "return " : "";
                            s += $"{initializerListProxyInfo.cppName}({method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader)})";
                            writer.WriteDeclaration(s);
                            writer.CloseDefinition();
                        }

                        // Remove the generic TParams
                        container.Template(null);
                        tempGens.RemoveOrThrow("...TParams");
                    }
                    container.ExpandParams = false;
                }
            }

            // If we have 2 or more base methods, we need to see if either of our base methods have been renamed.
            // If any of them have been renamed, we need to create a new method for that and map it to the method we are currently serializing.
            // Basically, if we have void Clear() with two base methods, one of which is renamed, we create void Clear(), and we create void QUALIFIED_Clear()
            // Where QUALIFIED_Clear() simply calls Clear()
            if (performProxy)
            {
                // Original method would have already been created by now.
                foreach (var bm in method.BaseMethods)
                {
                    if (!_nameMap.TryGetValue(bm, out var pair))
                        throw new InvalidOperationException($"{bm} does not have a name!");
                    if (pair.Item2)
                    {
                        // If we have renamed the base method, we write the method.
                        // If we are a header, write the comments
                        if (asHeader)
                        {
                            writer.WriteComment("Creating proxy method: " + pair.Item1);
                            // We want to map it to a method that is NOT renamed!
                            writer.WriteComment("Maps to method: " + method.Name);
                        }

                        declaration = WriteMethod(scope, method, asHeader, pair.Item1, false).declaration;
                        // Write method content
                        if (TemplateString(method, !writeContent, out var templateStr))
                            writer.WriteLine((declaration.StartsWith("/") ? "// " : "") + templateStr);
                        if (!writeContent)
                        {
                            if (declaration.StartsWith("/"))
                                writer.WriteComment($"Skipping redundant proxy method: {pair.Item1}");
                            else
                                writer.WriteDeclaration(declaration);
                        }
                        else
                        {
                            if (declaration.StartsWith("/"))
                            {
                                // Comment failures
                                // If we encounter a redundant proxy method, we will continue to print "ABORTED"
                                // We will additionally provide information stating that this method was a redundant proxy
                                writer.WriteComment("Redundant proxy method!");
                                writer.WriteLine(declaration);
                                continue;
                            }
                            writer.WriteDefinition(declaration);
                            // Call original method (return as necessary)
                            string s = needsReturn ? "return " : "";
                            s += $"{cppName}({method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], ParameterFormatFlags.Names, asHeader)})";
                            writer.WriteDeclaration(s);
                            writer.CloseDefinition();
                        }
                    }
                }
            }
            writer.Flush();
            Serialized(method);
        }
    }
}