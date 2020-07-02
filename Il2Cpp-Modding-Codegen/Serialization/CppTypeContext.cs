using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppTypeContext
    {
        public enum NeedAs
        {
            Definition,
            Declaration,
            BestMatch
        }

        public enum ForceAsType
        {
            None,
            Literal
        }

        public HashSet<TypeRef> Declarations { get; } = new HashSet<TypeRef>();
        public HashSet<TypeRef> DeclarationsToMake { get; } = new HashSet<TypeRef>();
        public HashSet<TypeRef> Definitions { get; } = new HashSet<TypeRef>();
        public HashSet<TypeRef> DefinitionsToGet { get; } = new HashSet<TypeRef>();
        public CppTypeContext DeclaringContext { get; private set; }
        public bool InPlace { get; private set; } = false;
        public IReadOnlyList<CppTypeContext> NestedContexts { get => _nestedContexts; }

        private CppTypeContext _rootContext;
        private CppTypeContext RootContext
        {
            get
            {
                while (_rootContext.InPlace && _rootContext.DeclaringContext != null)
                {
                    _rootContext = _rootContext.DeclaringContext;
                }
                return _rootContext;
            }
        }

        public string HeaderFileName { get => RootContext.LocalType.This.GetIncludeLocation() + ".hpp"; }

        public string CppFileName { get => LocalType.This.GetIncludeLocation() + ".cpp"; }

        public string TypeNamespace { get; }
        public string TypeName { get; }
        public string QualifiedTypeName { get; }
        public ITypeData LocalType { get; }

        /// <summary>
        /// Returns true if this context uses primitive il2cpp types.
        /// </summary>
        public bool NeedPrimitives { get; private set; }

        // Holds generic types (ex: T1, T2, ...) defined by the type
        private HashSet<TypeRef> _genericTypes = new HashSet<TypeRef>();

        private List<CppTypeContext> _nestedContexts = new List<CppTypeContext>();

        private ITypeCollection _types;

        private void AddGenericTypes(TypeRef type)
        {
            if (type is null)
                return;
            if (type.IsGenericTemplate)
                foreach (var g in type.Generics)
                    _genericTypes.Add(g);
            AddGenericTypes(type.DeclaringType);
        }

        public CppTypeContext(ITypeCollection types, ITypeData data)
        {
            _rootContext = this;
            _types = types;
            LocalType = data;

            // Check all declaring types (and ourselves) if we have generic arguments/parameters. If we do, add them to _genericTypes.
            AddGenericTypes(data.This);

            // Requiring it as a definition here simply makes it easier to remove (because we are asking for a definition of ourself, which we have)
            QualifiedTypeName = GetCppName(data.This, true, true, NeedAs.Definition, ForceAsType.Literal);
            TypeNamespace = data.This.GetNamespace();
            TypeName = data.This.GetName();

            // Types need a definition of their parent type
            if (data.Parent != null)
            {
                // If the parent is a primitive, like System::Object or something, this should still work out.
                AddDefinition(data.Parent);
            }
            // Nested types need to define their declaring type
            if (data.This.DeclaringType != null)
            {
                AddDefinition(data.This.DeclaringType);
            }
            // Declaring types need to declare (or define) ALL of their nested types
            foreach (var nested in data.NestedTypes)
                AddNestedDeclaration(nested.This, nested.This.Resolve(_types));
            // Add ourselves to our Definitions
            Definitions.Add(data.This);
        }

        public string GetTemplateLine(bool localOnly = true)
        {
            var s = "";
            if (LocalType.This.IsGeneric)
            {
                var generics = LocalType.This.GetDeclaredGenerics(true);
                if (localOnly)
                    generics = generics.Except(LocalType.This.GetDeclaredGenerics(false), TypeRef.fastComparer);

                bool first = true;
                foreach (var g in generics)
                {
                    if (!first)
                        s += ", ";
                    s += "typename " + g.GetName();
                    first = false;
                }
            }
            if (!string.IsNullOrEmpty(s))
                s = $"template<{s}>";
            return s;
        }

        public static string GetTemplateLine(ITypeData type, bool localOnly = true) => CppDataSerializer.TypeToContext[type].GetTemplateLine(localOnly);

        public void AbsorbInPlaceNeeds()
        {
            // inherit DefinitionsToGet, Declarations from in-place NestedContexts
            var prevInPlace = new HashSet<CppTypeContext>();
            var newInPlace = new HashSet<CppTypeContext>(NestedContexts.Where(n => n.InPlace));
            do
            {
                foreach (var nested in newInPlace)
                    TakeDefsAndDeclares(nested);
                prevInPlace.UnionWith(newInPlace);
                newInPlace = new HashSet<CppTypeContext>(NestedContexts.Where(n => n.InPlace).Except(prevInPlace));
            } while (newInPlace.Count > 0);
        }

        // TODO: instead of having AbsorbInPlaceNeeds, call this when nested first becomes in-place and direct all ResolveAndStore calls to RootContext?
        private void TakeDefsAndDeclares(CppTypeContext nested)
        {
            Contract.Requires(nested.InPlace);
            Contract.Requires(this == RootContext);

            foreach (var dec in nested.DeclarationsToMake.Except(nested.LocalType.NestedTypes.Select(t => t.This)).Except(Definitions))
                AddDeclaration(dec, null);
            foreach (var def in nested.DefinitionsToGet.Except(Definitions))
                AddDefinition(def);
        }

        public void AddNestedContext(ITypeData type, CppTypeContext context)
        {
            Contract.Requires(type != null);
            Contract.Requires(type.This.DeclaringType.Equals(LocalType.This));
            Contract.Requires(context != null);
            // Add the type, context pair to our immediately nested contexts
            // TODO: Add a mapping from type --> context so we can search our immediate nesteds faster
            // atm, just add it because we can be lazy
            _nestedContexts.Add(context);
        }

        public void SetDeclaringContext(CppTypeContext context)
        {
            Contract.Requires(DeclaringContext is null);
            Contract.Requires(context != null);
            // Set our declaring context to be the one provided. Our original declaring context should always be null before
            // There shouldn't be too much sorcery here, instead, when we add definitions, we ensure we check our inheritance tree.
            // If we find that the type we are looking for is nested under a declaring type that we share, we need to ensure that type (and all of its declaring types)
            // are set to InPlace
            DeclaringContext = context;
            Contract.Ensures(DeclaringContext != null);
        }

        internal bool HasInNestedHierarchy(TypeRef type) {
            var resolved = type.Resolve(_types);
            if (resolved == null) throw new UnresolvedTypeException(LocalType.This, type);
            return HasInNestedHierarchy(resolved);
        }
        internal bool HasInNestedHierarchy(ITypeData resolved) => HasInNestedHierarchy(CppDataSerializer.TypeToContext[resolved]);
        internal bool HasInNestedHierarchy(CppTypeContext context)
        {
            if (context.DeclaringContext is null) return false;
            else if (context.DeclaringContext == this) return true;
            return HasInNestedHierarchy(context.DeclaringContext);
        }

        private void AddDefinition(TypeRef def, ITypeData resolved = null)
        {
            // Adding a definition is simple, ensure the type is resolved and add it
            if (resolved is null)
                resolved = def.Resolve(_types);
            if (resolved is null)
                throw new UnresolvedTypeException(LocalType.This, def);
            // Remove anything that is already declared, we only need to define it
            if (!DeclarationsToMake.Remove(def))
                Declarations.Remove(def);

            // When we add a definition, we add it to our DefinitionsToGet
            // However, if the type we are looking for is a nested type with a declaring type that we share:
            // We need to set the InPlace property for all declaring types of that desired type to true
            // (up until the DeclaringType is shared between them)
            if (!CppDataSerializer.TypeToContext.TryGetValue(resolved, out var defContext) || !RootContext.HasInNestedHierarchy(defContext))
                DefinitionsToGet.Add(def);
            else
                while (defContext != RootContext)
                {
                    defContext.InPlace = true;
                    Definitions.Add(defContext.LocalType.This);
                    defContext = defContext.DeclaringContext;
                }
        }

        private void AddNestedDeclaration(TypeRef def, ITypeData resolved)
        {
            Contract.Requires(!def.IsVoid());
            Contract.Requires(def.DeclaringType != null);
            Contract.Requires(resolved != null);
            Contract.Requires(resolved.This.DeclaringType.Equals(LocalType));
            DeclarationsToMake.Add(def);
        }

        private void AddDeclaration(TypeRef def, ITypeData resolved)
        {
            Contract.Requires(!def.IsVoid());

            if (DefinitionsToGet.Contains(def))
                // If we have it in our DefinitionsToGet, no need to declare as well
                return;
            if (resolved is null)
                resolved = def.Resolve(_types);
            if (resolved is null)
                throw new UnresolvedTypeException(LocalType.This, def);

            if (resolved?.This.DeclaringType != null && !Definitions.Contains(resolved?.This.DeclaringType))
            {
                // If def's declaring type is not defined, we cannot declare def. Define def's declaring type instead.
                AddDefinition(resolved?.This.DeclaringType);
                Declarations.Add(def);
            }
            else if (resolved != null)
                // Otherwise, we can safely add it to declarations
                DeclarationsToMake.Add(def);
        }

        /// <summary>
        /// Gets the C++ fully qualified name for the TypeRef.
        /// </summary>
        /// <returns>Null if the type has not been resolved (and is not a generic parameter or primitive)</returns>
        public string GetCppName(TypeRef data, bool qualified, bool generics = true, NeedAs needAs = NeedAs.BestMatch, ForceAsType forceAsType = ForceAsType.None)
        {
            // If the TypeRef is a generic parameter, return its name
            if (_genericTypes.Contains(data))
                return data.Name;

            if (forceAsType != ForceAsType.Literal || !data.Equals(LocalType.This))
            {
                // If the TypeRef is a primitive, we need to convert it to a C++ name upfront.
                var primitiveName = ConvertPrimitive(data, forceAsType, needAs);
                if (!string.IsNullOrEmpty(primitiveName))
                    return primitiveName;
            }

            var resolved = ResolveAndStore(data, forceAsType, needAs);
            if (resolved is null)
                return null;
            var name = string.Empty;
            if (resolved.This.DeclaringType != null)
            {
                // Each declaring type must be defined, and must also have its generic parameters specified (confirm this is the case)
                // If data.IsGenericInstance, then we need to use the provided generic arguments instead of the template types
                // Create a map of declared generics (including our own) that map to each of data's generic arguments
                // First, we get a list of all the template parameters and a list of all our generic parameters/arguments from our reference
                var genericParams = resolved.This.GetDeclaredGenerics(true).ToList();
                var count = genericParams.Count;
                var genericArgs = genericParams;
                if (data.IsGenericInstance)
                {
                    genericArgs = data.GetDeclaredGenerics(true).ToList();
                    if (count != genericArgs.Count)
                        // In cases where we have non-matching counts, this usually means we need to remove generic parameters from our generic arguments
                        foreach (var gp in genericParams)
                            // Remove each remaining generic template parameter
                            genericArgs.Remove(gp);
                }
                // If the two lists are of differing lengths, throw
                if (count != genericArgs.Count)
                    throw new InvalidOperationException($"{nameof(genericParams)}.Count != {nameof(genericArgs)}.Count, {count} != {genericArgs.Count}");
                // Create a mapping from generic parameter to generic argument
                // If data is not a generic instance, no need to bother
                Dictionary<TypeRef, TypeRef> argMapping = null;
                if (data.IsGenericInstance)
                {
                    argMapping = new Dictionary<TypeRef, TypeRef>(TypeRef.fastComparer);
                    for (int i = 0; i < count; i++)
                        argMapping.Add(genericParams[i], genericArgs[i]);
                }
                // Get full generic map of declaring type --> list of generic parameters declared in that type
                var genericMap = resolved.This.GetGenericMap(true);
                var declString = string.Empty;
                var declType = resolved.This;
                bool isThisType = true;
                while (declType != null)
                {
                    // Recurse upwards starting at ourselves
                    string declaringGenericParams = "";
                    if (genericMap.TryGetValue(declType, out var declaringGenerics))
                    {
                        // If we are thisType AND we DO NOT want generics, we should not write any generics.
                        // Otherwise, we write out the generics defined in this type.
                        if (!isThisType || generics)
                        {
                            declaringGenericParams += "<";
                            bool first = true;
                            foreach (var g in declaringGenerics)
                            {
                                if (!first)
                                    declaringGenericParams += ", ";
                                if (data.IsGenericInstance)
                                    declaringGenericParams += GetCppName(argMapping[g], true, true);
                                else
                                    declaringGenericParams += GetCppName(g, true, true);
                                first = false;
                            }
                            declaringGenericParams += ">";
                        }
                    }

                    var temp = declType.GetName() + declaringGenericParams;
                    if (!isThisType)
                        temp += "::" + declString;
                    isThisType = false;

                    declString = temp;
                    // AddDefinition(declType);
                    if (declType.DeclaringType is null)
                        // Grab namespace for name here
                        if (qualified)
                            name = declType.GetNamespace() + "::";
                    declType = declType.DeclaringType;
                }
                name += declString;
            }
            else
            {
                if (qualified)
                    name = resolved.This.GetNamespace() + "::";
                name += data.Name;
                name = name.Replace('`', '_').Replace('<', '$').Replace('>', '$');
                if (generics && data.Generics.Count > 0)
                {
                    name += "<";
                    bool first = true;
                    for (int i = 0; i < data.Generics.Count; i++)
                    {
                        var g = data.Generics[i];
                        if (!first)
                            name += ", ";
                        else
                            first = false;
                        if (data.IsGenericTemplate)
                            // If this is a generic template, use literal names for our generic parameters
                            name += g.Name;
                        else if (data.IsGenericInstance)
                            // If this is a generic instance, call each of the generic's GetCppName
                            name += GetCppName(g, qualified, true);
                    }
                    name += ">";
                }
            }
            // Ensure the name has no bad characters
            // Append pointer as necessary
            if (resolved is null)
                return null;
            if (forceAsType == ForceAsType.Literal)
                return name;
            if (resolved.This.DeclaringType?.IsGeneric ?? false)  // note: it's important that ForceAsType.Literal is ruled out first
                name = "typename " + name;
            if (resolved.Info.TypeFlags == TypeFlags.ReferenceType)
                return name + "*";
            return name;
        }

        /// <summary>
        /// Simply adds the resolved <see cref="ITypeData"/> to either the declarations or definitions and returns it.
        /// If typeRef matches a generic parameter of this generic template, or the resolved type is null, returns false.
        /// </summary>
        /// <param name="typeRef"></param>
        /// <param name="needAs"></param>
        /// <returns>A bool representing if the type was resolved successfully</returns>
        public ITypeData ResolveAndStore(TypeRef typeRef, ForceAsType forceAs, NeedAs needAs = NeedAs.BestMatch)
        {
            if (_genericTypes.Contains(typeRef))
                // Generic parameters are resolved to nothing and shouldn't even attempted to be resolved.
                return null;
            var resolved = typeRef.Resolve(_types);
            if (resolved is null)
                return null;
            switch (needAs)
            {
                case NeedAs.Declaration:
                    AddDeclaration(typeRef, resolved);
                    break;

                case NeedAs.BestMatch:
                    if (forceAs != ForceAsType.Literal && (typeRef.IsPointer() || resolved.Info.TypeFlags == TypeFlags.ReferenceType))
                        AddDeclaration(typeRef, resolved);
                    else
                        AddDefinition(typeRef, resolved);
                    break;

                case NeedAs.Definition:
                default:
                    // If I need it as a definition, add it as one
                    AddDefinition(typeRef, resolved);
                    break;
            }
            if (typeRef.IsGenericTemplate && typeRef.Generics != null)
            {
                // Resolve and store each generic argument
                foreach (var g in typeRef.Generics)
                    // Only need them as declarations, since we don't need the literal pointers.
                    ResolveAndStore(g, forceAs, NeedAs.Declaration);
            }
            return resolved;
        }

        // We only need a declaration for the element type (if we aren't needed as a definition)
        private NeedAs NeedAsForPrimitiveEtype(NeedAs needAs) => needAs == NeedAs.Definition ? needAs : NeedAs.Declaration;

        private string ConvertPrimitive(TypeRef def, ForceAsType forceAs, NeedAs needAs)
        {
            string s = null;
            if (def.IsArray())
            {
                // We should ensure we aren't attemping to force it to something it shouldn't be, so it should still be ForceAsType.None
                var eName = GetCppName(def.ElementType, true, true, NeedAsForPrimitiveEtype(needAs));
                s = $"Array<{eName}>";
            }
            else if (def.IsPointer())
            {
                s = GetCppName(def.ElementType, true, true, NeedAsForPrimitiveEtype(needAs)) + "*";
            }
            else if (string.IsNullOrEmpty(def.Namespace) || def.Namespace == "System")
            {
                var name = def.Name.ToLower();
                if (name == "void")
                    s = "void";
                else if (name == "object")
                    s = "Il2CppObject";
                else if (name == "string")
                    s = "Il2CppString";
                else if (name == "char")
                    s = "Il2CppChar";
                else if (def.Name == "bool" || def.Name == "Boolean")
                    s = "bool";
                else if (name == "byte")
                    s = "int8_t";
                else if (name == "sbyte")
                    s = "uint8_t";
                else if (def.Name == "short" || def.Name == "Int16")
                    s = "int16_t";
                else if (def.Name == "ushort" || def.Name == "UInt16")
                    s = "uint16_t";
                else if (def.Name == "int" || def.Name == "Int32")
                    s = "int";
                else if (def.Name == "uint" || def.Name == "UInt32")
                    s = "uint";
                else if (def.Name == "long" || def.Name == "Int64")
                    s = "int64_t";
                else if (def.Name == "ulong" || def.Name == "UInt64")
                    s = "uint64_t";
                else if (def.Name == "float" || def.Name == "Single")
                    s = "float";
                else if (name == "double")
                    s = "double";
            }
            if (s is null)
                return null;
            if (s.StartsWith("Il2Cpp") || s.StartsWith("Array<"))
            {
                bool defaultPtr = false;
                if (s != "Il2CppChar")
                    defaultPtr = true;
                // For Il2CppTypes, should refer to type as :: to avoid ambiguity
                if (forceAs == ForceAsType.Literal)
                    s = "::" + s;
                else
                    s = "::" + s + (defaultPtr ? "*" : "");
                NeedPrimitives = true;
            }
            return s;
        }
    }
}