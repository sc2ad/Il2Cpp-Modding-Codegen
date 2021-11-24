using Il2CppModdingCodegen.Data.DllHandling;
using Mono.Cecil;
using Mono.Collections.Generic;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Il2CppModdingCodegen.Serialization
{
    public abstract class CppContext
    {
        [SuppressMessage("Naming", "CA1717:Only FlagsAttribute enums should have plural names", Justification = "As")]
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

        public abstract void NeedIl2CppUtils();

        internal abstract void Resolve(HashSet<CppContext> ctxs);

        protected const string typedefsInclude = "#include \"beatsaber-hook/shared/utils/typedefs.h\"";

        internal static Dictionary<TypeDefinition, CppContext> TypesToContexts { get; } = new(new TypeDefinitionComparer());

        public TypeDefinition Type { get; }

        private readonly SizeTracker? sizeTracker;

        public CppContext(TypeDefinition t, SizeTracker? sz, CppContext? declaring = null, bool add = true)
        {
            if (t is null)
                throw new ArgumentNullException(nameof(t));
            sizeTracker = sz;
            Type = t;
            if (add)
            {
                lock (TypesToContexts)
                    TypesToContexts.TryAdd(t, this);
            }
            rootContext = declaring;

            // Add ourselves to our Definitions
            Definitions.AddOrThrow(t);
            // Declaring types need to declare (or define) ALL of their nested types
            foreach (var nested in t.NestedTypes)
                AddNestedDeclaration(nested);

            // Nested types need to define their declaring type
            if (t.DeclaringType != null)
                AddDefinition(t.DeclaringType);

            DeclaringContext = declaring;
            if (declaring != null)
                declaring.AddNestedContext(t, this);
        }

        public int GetSize(TypeReference t) => sizeTracker?.GetSize(t) ?? -1;

        private void AddNestedContext(TypeDefinition t, CppContext context)
        {
            NestedContexts.Add(context);
            if (t.HasGenericParameters)
                InPlaceNestedType(context);
        }

        internal HashSet<TypeDefinition> Declarations { get; } = new HashSet<TypeDefinition>();
        internal HashSet<TypeDefinition> DeclarationsToMake { get; } = new HashSet<TypeDefinition>();
        internal HashSet<TypeDefinition> Definitions { get; } = new HashSet<TypeDefinition>(new TypeDefinitionComparer());
        internal HashSet<TypeDefinition> DefinitionsToGet { get; } = new HashSet<TypeDefinition>();
        internal List<CppContext> NestedContexts { get; } = new();

        // Declarations that should be made by our includes (DefinitionsToGet)
        internal HashSet<string> PrimitiveDeclarations { get; } = new HashSet<string>();

        private CppContext? rootContext;
        internal CppContext? DeclaringContext { get; private set; }

        public HashSet<string> ExplicitIncludes { get; } = new();

        internal bool InPlace { get; private set; }

        protected virtual CppContext? RootContext
        {
            get
            {
                while (rootContext?.DeclaringContext != null)
                    rootContext = rootContext.DeclaringContext;
                return rootContext;
            }
        }

        protected bool HasInNestedHierarchy(TypeDefinition resolved, out CppContext context)
        {
            bool res;
            lock (TypesToContexts)
            {
                res = TypesToContexts.TryGetValue(resolved, out context);
            }
            if (res)
                return HasInNestedHierarchy(context);
            return false;
        }

        protected bool HasInNestedHierarchy(CppContext? context)
        {
            while (context is not null)
            {
                if (context == this)
                    return true;
                context = context?.DeclaringContext;
            }
            return false;
        }

        /// <summary>
        /// Given a nested context, somewhere within our same RootContext, InPlace it and all of its DeclaringContexts up until RootContext.
        /// </summary>
        /// <param name="defContext"></param>
        private void InPlaceNestedType(CppContext defContext)
        {
            // If the type we want is a type that is nested within ourselves, our declaring context... till RootContext
            // Then we set InPlace to true.
            var rc = RootContext ?? this;
            while (defContext != rc)
            {
                if (!defContext.InPlace)
                {
                    defContext.InPlace = true;
                    if (defContext.DeclaringContext != null)
                    {
                        // Add each definition that exists in the declaring context to the InPlace nested context since they share definitions
                        defContext.Definitions.UnionWith(defContext.DeclaringContext.Definitions);
                        // Remove each definition that exists in the declaring context from the InPlace nested context since they share definitions
                        defContext.DefinitionsToGet.ExceptWith(defContext.DeclaringContext.Definitions);
                    }
                }
                // Add the now InPlace type to our own Definitions
                rc!.Definitions.Add(defContext.Type);
                // Go to the DeclaringContext of the type we just InPlace'd into ourselves, and continue inplacing DeclaringContexts until we hit ourselves.
                defContext = defContext.DeclaringContext!;
            }
        }

        protected void AddDefinition(TypeDefinition def)
        {
            // Adding a definition is simple, ensure the type is resolved and add it
            if (def is null)
                throw new ArgumentNullException(nameof(def));

            if (Definitions.Contains(def)) return;
            if (DefinitionsToGet.Contains(def)) return;
            // Remove anything that is already declared, we only need to define it
            if (!DeclarationsToMake.Remove(def))
                Declarations.Remove(def);

            // When we add a definition, we add it to our DefinitionsToGet
            // However, if the type we are looking for is a nested type with a declaring type that we share:
            // We need to set the InPlace property for all declaring types of that desired type to true
            // (up until the DeclaringType is shared between them)

            // If the definition I am adding shares the same RootContext as me, I need to InPlace nest it.
            if (RootContext is not null && RootContext.HasInNestedHierarchy(def, out var defContext))
                InPlaceNestedType(defContext);
            else
                DefinitionsToGet.AddOrThrow(def);
        }

        private void AddNestedDeclaration(TypeDefinition def)
        {
            if (def is null) throw new ArgumentNullException(nameof(def));
            if (def.MetadataType == MetadataType.Void) throw new ArgumentException("Cannot be void!", nameof(def));
            if (def.DeclaringType is null) throw new ArgumentException("DeclaringType cannot be null!", nameof(def));
            DeclarationsToMake.AddOrThrow(def);
        }

        protected void AddDeclaration(TypeReference def)
        {
            if (def is null)
                throw new ArgumentNullException(nameof(def));
            var resolved = def.ResolveLocked();
            if (resolved is null)
                throw new UnresolvedTypeException(Type, def);

            // If we have it in our DefinitionsToGet, no need to declare as well
            if (DefinitionsToGet.Contains(resolved)) return;
            if (DeclarationsToMake.Contains(resolved)) return;  // this def is already queued for declaration
            if (Declarations.Contains(resolved)) return;

            if (def.DeclaringType != null && !Definitions.Contains(resolved.DeclaringType))
            {
                // If def's declaring type is not defined, we cannot declare def. Define def's declaring type instead.
                AddDefinition(resolved.DeclaringType);
                Declarations.AddOrThrow(resolved);
            }
            else
                // Otherwise, we can safely add it to declarations
                DeclarationsToMake.AddOrThrow(resolved);
        }

        /// <summary>
        /// Simply adds the resolved <see cref="ITypeData"/> to either the declarations or definitions and returns it.
        /// If typeRef matches a generic parameter of this generic template, or the resolved type is null, returns false.
        /// </summary>
        /// <param name="typeRef"></param>
        /// <param name="needAs"></param>
        /// <returns>A bool representing if the type was resolved successfully</returns>
        internal TypeDefinition? ResolveAndStore(TypeReference typeRef, ForceAsType forceAs, NeedAs needAs = NeedAs.BestMatch)
        {
            if (typeRef.IsGenericParameter)
                // Generic parameters are resolved to nothing and shouldn't even attempted to be resolved.
                return null;

            switch (needAs)
            {
                case NeedAs.Declaration:
                    AddDeclaration(typeRef);
                    break;

                case NeedAs.BestMatch:
                    var resolved = typeRef.ResolveLocked();
                    if (forceAs != ForceAsType.Literal && (typeRef.IsPointer || !(resolved.IsValueType || resolved.IsEnum)))
                        AddDeclaration(typeRef);
                    else
                        AddDefinition(resolved);
                    break;

                case NeedAs.Definition:
                default:
                    // If I need it as a definition, add it as one
                    AddDefinition(typeRef.ResolveLocked());
                    break;
            }

            if (typeRef.IsGenericInstance)
                // Resolve and store each generic argument
                foreach (var g in typeRef.GenericParameters)
                    // Only need them as declarations, since we don't need the literal pointers.
                    ResolveAndStore(g, forceAs, NeedAs.Declaration);

            return typeRef.ResolveLocked();
        }

        internal static string CppName(TypeReference type)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));
            if (type.Name.StartsWith("!"))
                throw new InvalidOperationException("Tried to get the name of a copied generic parameter!");
            var name = type.Name.Replace('`', '_').Replace('<', '_').Replace('>', '_');
            name = Utils.SafeName(name);
            // TODO: Type should actually check if the type is supposed to be nested
            var resolved = type.ResolveLocked();
            if (resolved is not null)
            {
                bool res;
                CppContext ctx;
                lock (TypesToContexts)
                {
                    res = TypesToContexts.TryGetValue(resolved, out ctx);
                }
                if (res && ctx is CppTypeHeaderContext && resolved.DeclaringType is not null)
                {
                    // Handle unnested types here
                    if (type.DeclaringType is null)
                        throw new NullReferenceException("DeclaringType was null despite UnNested being true!");
                    var dc = type.DeclaringType;
                    var dcName = "";
                    while (dc is not null)
                    {
                        dcName = string.IsNullOrEmpty(dcName) ? CppName(dc) : CppName(dc) + "_" + dcName;
                        dc = dc.DeclaringType;
                    }
                    name = dcName + "_" + name;
                }
            }
            return name;
        }

        public (string, string) GetIl2CppName()
        {
            var name = Type.Name;
            var dt = Type.DeclaringType;
            while (dt.DeclaringType != null)
            {
                name = dt.DeclaringType.Name + "/" + name;
                dt = dt.DeclaringType;
            }
            // Namespace obtained from final declaring type
            return (dt.Namespace.Replace("::", "."), name);
        }

        private const string NoNamespace = "GlobalNamespace";

        internal static string CppNamespace(TypeReference data)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            return string.IsNullOrEmpty(data.Namespace) ? NoNamespace : data.Namespace.Replace(".", "::");
        }

        private void GetCppNameWithGenerics(ref string name, TypeReference data, TypeDefinition resolved, bool generics, bool qualified)
        {
            if (resolved.DeclaringType is not null)
            {
                Dictionary<TypeReference, TypeReference>? paramMapping = null;
                if (data.IsGenericInstance)
                {
                    // TODO: Check to make sure that .GenericArguments and GenericParameters have declaring type params
                    var genArgs = (data as GenericInstanceType)!.GenericArguments;
                    var genParams = resolved.GenericParameters;
                    if (genParams.Count != genArgs.Count)
                        throw new InvalidOperationException($"{nameof(genArgs)}.Count != {nameof(genParams)}.Count!");
                    paramMapping = new(genArgs.Count);
                    for (int i = 0; i < genArgs.Count; i++)
                    {
                        paramMapping.Add(genParams[i], genArgs[i]);
                    }
                    // Param mapping complete
                }

                var declType = resolved;

                // Resolve: Dictionary<int, int>::Enumerator*
                // Dictionary<T1, T2>
                //  - Enumerator<KeyValuePair<T1, T2>>
                // T1 --> int
                // T2 --> int
                // Resolve: List<int>::Enumerator::TestNode<float>
                // List<T>
                //  - Enumerator<T>
                //    - TestNode<T, T2>
                // T --> int
                // T2 --> float
                string? GenName(GenericParameter g) => data.IsGenericParameter ?
                            GetCppName(paramMapping![g], true, true, NeedAs.BestMatch) :
                            GetCppName(g, true, true, NeedAs.BestMatch);
                bool isTypeType = true;
                string declString = "";
                while (declType is not null)
                {
                    // Generic parameters for THIS type
                    var gens = declType.GenericParameters;
                    string declaringGenericParams = "";
                    if (generics || !isTypeType)
                    {
                        declaringGenericParams = $"<{string.Join(", ", gens.Select(g => GenName(g)))}>";
                    }

                    var temp = CppName(declType) + declaringGenericParams;
                    if (!isTypeType)
                        temp += "::" + declString;
                    isTypeType = false;

                    declString = temp;
                    if (declType.DeclaringType is null && qualified)
                        // Prefix c++ namespace
                        name = CppNamespace(declType) + "::";
                    declType = declType.DeclaringType;
                }
                name += declString;
            }
            else
            {
                if (qualified)
                    name = CppNamespace(resolved) + "::";
                name += CppName(resolved);
                if (generics && (data.IsGenericInstance || data.HasGenericParameters))
                {
                    name += "<";
                    if (data.IsGenericInstance)
                    {
                        var gens = (data as GenericInstanceType)!.GenericArguments;
                        name += string.Join(", ", gens.Select(g => GetCppName(g, true, true, NeedAs.BestMatch)));
                    }
                    else
                    {
                        name += string.Join(", ", data.GenericParameters.Select(g => CppName(g)));
                    }
                    name += ">";
                }
            }
        }

        /// <summary>
        /// Gets the C++ fully qualified name for the TypeRef.
        /// </summary>
        /// <returns>Null if the type has not been resolved (and is not a generic parameter or primitive)</returns>
        public string GetCppName(TypeReference data, bool qualified, bool generics = true, NeedAs needAs = NeedAs.BestMatch, ForceAsType forceAsType = ForceAsType.None)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (forceAsType != ForceAsType.Literal || !data.Equals(Type))
            {
                var primitiveName = ConvertPrimitive(data, forceAsType, needAs);
                if (primitiveName is not null)
                    return primitiveName;
            }
            if (data.IsGenericParameter)
                return data.Name;

            var resolved = ResolveAndStore(data, forceAsType, needAs);
            if (resolved is null)
                throw new InvalidOperationException("C++ name must be resolvable");

            string name = "";
            GetCppNameWithGenerics(ref name, data, resolved, generics, qualified);

            // Ensure the name has no bad characters
            // Append pointer as necessary
            if (forceAsType == ForceAsType.Literal)
                return name;
            if ((resolved.DeclaringType?.IsGenericInstance ?? false) || (resolved.DeclaringType?.HasGenericParameters ?? false))  // note: it's important that ForceAsType.Literal is ruled out first
                name = "typename " + name;
            if (!resolved.IsValueType || !resolved.IsEnum)
                return name + "*";
            return name;
        }

        private string? ConvertPrimitive(TypeReference r, ForceAsType forceAs, NeedAs needAs)
        {
            string? s = null;
            if (r.IsArray)
            {
                ExplicitIncludes.Add("#include \"beatsaber-hook/shared/utils/typedefs-array.hpp\"");
                s = $"::ArrayW<{GetCppName((r as ArrayType)!.ElementType, true, true, NeedAs.BestMatch)}>";
            }
            else if (r.IsPointer || r.IsFunctionPointer)
                return GetCppName(r.GetElementType(), true, true, NeedAsForPrimitiveEtype(needAs)) + "*";
            else if (string.IsNullOrEmpty(r.Namespace) || r.Namespace == "System")
            {
                var name = r.Name.ToLower();
                if (name == "void")
                    s = "void";
                else if (name == "object")
                    s = Constants.ObjectCppName;
                else if (name == "string")
                    s = Constants.StringCppName;
                else if (name == "char")
                    s = "Il2CppChar";
                else if (r.Name == "bool" || r.Name == "Boolean")
                    s = "bool";
                else if (name == "sbyte")
                    s = "int8_t";
                else if (name == "byte")
                    s = "uint8_t";
                else if (r.Name == "short" || r.Name == "Int16")
                    s = "int16_t";
                else if (r.Name == "ushort" || r.Name == "UInt16")
                    s = "uint16_t";
                else if (r.Name == "int" || r.Name == "Int32")
                    s = "int";
                else if (r.Name == "uint" || r.Name == "UInt32")
                    s = "uint";
                else if (r.Name == "long" || r.Name == "Int64")
                    s = "int64_t";
                else if (r.Name == "ulong" || r.Name == "UInt64")
                    s = "uint64_t";
                else if (r.Name == "float" || r.Name == "Single")
                    s = "float";
                else if (name == "double")
                    s = "double";
            }
            if (s is null)
                return null;
            if (s.StartsWith("Il2Cpp") || s.StartsWith("Cs"))
            {
                bool defaultPtr = (s != "Il2CppChar");

                if (!defaultPtr || forceAs == ForceAsType.Literal)
                    ExplicitIncludes.Add(typedefsInclude);
                else
                {
                    PrimitiveDeclarations.Add("struct " + s);
                    s += "*";
                }

                // For Il2CppTypes, should refer to type as :: to avoid ambiguity
                s = "::" + s;
            }
            else if (s.EndsWith("_t"))
                ExplicitIncludes.Add("#include <stdint.h>");
            return s;
        }

        private static NeedAs NeedAsForPrimitiveEtype(NeedAs needAs) => needAs == NeedAs.Definition ? needAs : NeedAs.Declaration;
    }
}