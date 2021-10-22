using Mono.Cecil;
using System;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppContext
    {
        public TypeDefinition Type { get; }

        public CppContext(TypeDefinition t)
        {
            Type = t;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1717:Only FlagsAttribute enums should have plural names", Justification = "As")]
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

        private void AddDefinition(TypeReference def)
        {
            // Adding a definition is simple, ensure the type is resolved and add it
            var resolved = def.Resolve();
            if (resolved is null)
                throw new UnresolvedTypeException(Type, def);

            def = resolved;
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
            if (!RootContext.HasInNestedHierarchy(resolved, out var defContext))
                DefinitionsToGet.AddOrThrow(def);
            else
                InPlaceNestedType(defContext);
        }

        private void AddNestedDeclaration(TypeRef def, ITypeData? resolved)
        {
            if (def.IsVoid()) throw new ArgumentException("cannot be void!", nameof(def));
            if (def.DeclaringType is null) throw new ArgumentException("DeclaringType cannot be void!", nameof(def));
            if (resolved is null) throw new ArgumentNullException(nameof(resolved));
            Contract.Requires(LocalType.Equals(resolved.This.DeclaringType));
            DeclarationsToMake.AddOrThrow(def);
        }

        private void AddDeclaration(TypeRef def, ITypeData? resolved)
        {
            Contract.Requires(!def.IsVoid());
            if (resolved is null)
                resolved = def.Resolve(Types);
            if (resolved is null)
                throw new UnresolvedTypeException(LocalType.This, def);

            def = resolved.This;
            // If we have it in our DefinitionsToGet, no need to declare as well
            if (DefinitionsToGet.Contains(def)) return;
            if (DeclarationsToMake.Contains(def)) return;  // this def is already queued for declaration
            if (Declarations.Contains(def)) return;

            if (def.DeclaringType != null && !Definitions.Contains(def.DeclaringType))
            {
                // If def's declaring type is not defined, we cannot declare def. Define def's declaring type instead.
                AddDefinition(def.DeclaringType);
                Declarations.AddOrThrow(def);
            }
            else
                // Otherwise, we can safely add it to declarations
                DeclarationsToMake.AddOrThrow(def);
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
            var resolved = typeRef.Resolve();
            if (resolved is null)
                return null;

            switch (needAs)
            {
                case NeedAs.Declaration:
                    AddDeclaration(typeRef, resolved);
                    break;

                case NeedAs.BestMatch:
                    if (forceAs != ForceAsType.Literal && (typeRef.IsPointer || !(resolved.IsValueType || resolved.IsEnum)))
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

            if (typeRef.IsGenericInstance)
                // Resolve and store each generic argument
                foreach (var g in typeRef.GenericParameters)
                    // Only need them as declarations, since we don't need the literal pointers.
                    ResolveAndStore(g, forceAs, NeedAs.Declaration);

            return resolved;
        }

        private string? ConvertPrimitive(TypeReference r, ForceAsType forceAs, NeedAs needAs)
        {
            string? s = null;
            if (r.IsArray)
            {
                NeedArrayInclude = true;
                s = $"::ArrayW<{GetCppName(r.GetElementType(), true, true, NeedAs.BestMatch)}>";
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
                    EnableNeedPrimitivesBeforeLateHeader();
                else
                {
                    PrimitiveDeclarations.Add("struct " + s);
                    s += "*";
                }

                // For Il2CppTypes, should refer to type as :: to avoid ambiguity
                s = "::" + s;
            }
            else if (s.EndsWith("_t"))
                NeedStdint = true;
            return s;
        }

        /// <summary>
        /// Gets the C++ fully qualified name for the TypeRef.
        /// </summary>
        /// <returns>Null if the type has not been resolved (and is not a generic parameter or primitive)</returns>
        public string? GetCppName(TypeReference? data, bool qualified, bool generics = true, NeedAs needAs = NeedAs.BestMatch, ForceAsType forceAsType = ForceAsType.None)
        {
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

            if (forceAsType == ForceAsType.Literal)
                return name;
            if (resolved.)
                return name;
        }

        private static NeedAs NeedAsForPrimitiveEtype(NeedAs needAs) => needAs == NeedAs.Definition ? needAs : NeedAs.Declaration;
    }
}