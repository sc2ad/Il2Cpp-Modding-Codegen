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
    public class CppSerializerContext
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
        public HashSet<TypeRef> Definitions { get; } = new HashSet<TypeRef>();
        public HashSet<TypeRef> DefinitionsToGet { get; } = new HashSet<TypeRef>();
        public CppSerializerContext DeclaringContext { get; }
        public IReadOnlyList<CppSerializerContext> NestedContexts { get; }
        public string FileName { get; }
        public string TypeNamespace { get; }
        public string TypeName { get; }
        public string QualifiedTypeName { get; }
        public ITypeData LocalType { get; }

        /// <summary>
        /// Returns true if this context uses primitive il2cpp types.
        /// </summary>
        public bool NeedPrimitives { get; private set; }

        public bool Header { get; }

        // Holds generic types (ex: T1, T2, ...) defined by the type
        private HashSet<TypeRef> _genericTypes = new HashSet<TypeRef>();

        private ITypeCollection _context;

        private void AddGenericTypes(TypeRef type)
        {
            if (type is null)
                return;
            if (type.IsGenericTemplate)
                foreach (var g in type.Generics)
                    _genericTypes.Add(g);
            AddGenericTypes(type.DeclaringType);
        }

        public CppSerializerContext(ITypeCollection context, ITypeData data, bool asHeader = true)
        {
            _context = context;
            Header = asHeader;
            LocalType = data;
            // Requiring it as a definition here simply makes it easier to remove (because we are asking for a definition of ourself, which we have)
            QualifiedTypeName = GetCppName(data.This, true, false, NeedAs.Definition, ForceAsType.Literal);
            TypeNamespace = data.This.GetNamespace();
            TypeName = data.This.GetName();
            var root = data;
            while (root.IsNestedInPlace)
                root = root.This.DeclaringType.Resolve(context);
            FileName = root.This.GetIncludeLocation();
            // Check all declaring types (and ourselves) if we have generic arguments/parameters. If we do, add them to _genericTypes.

            AddGenericTypes(data.This);
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
            if (asHeader)
            {
                // This should only happen in the declaring type's header, however.
                foreach (var nested in data.NestedTypes)
                    AddDeclaration(nested.This, nested.This.Resolve(_context));
            }
            // Add ourselves (and any truly nested types) to our Definitions
            if (asHeader)
                Definitions.Add(data.This);
            else
                DefinitionsToGet.Add(data.This);
        }

        // Recursively makes type nested-in-place iff it has a declaring type, then ditto for the declaring type.
        private void MakeNestHere(ITypeData type)
        {
            if (type.This.DeclaringType is null) return;
            type.IsNestedInPlace = true;
            // TODO: something with NestedContexts?
            var declaring = type.This.DeclaringType.Resolve(_context);
            if (declaring is null)
                throw new UnresolvedTypeException(type.This, type.This.DeclaringType);
            MakeNestHere(declaring);
        }

        private bool CouldNestHere(TypeRef def)
        {
            if (def is null) return false;
            if (LocalType.This.Equals(def)) return true;
            return CouldNestHere(def.DeclaringType);
        }

        private void AddDefinition(TypeRef def, ITypeData resolved = null)
        {
            // Adding a definition is simple, ensure the type is resolved and add it
            if (resolved is null)
                resolved = def.Resolve(_context);
            if (resolved is null)
                return;
            // Remove anything that is already declared, we only need to define it
            Declarations.Remove(def);

            if (!CouldNestHere(def))
                DefinitionsToGet.Add(def);
            else if (Definitions.Add(def))
                MakeNestHere(resolved);
        }

        private void AddDeclaration(TypeRef def, ITypeData resolved)
        {
            Contract.Requires(!def.IsVoid());

            if (DefinitionsToGet.Contains(def))
                // If we have it in our DefinitionsToGet, no need to declare as well
                return;
            if (resolved is null)
                resolved = def.Resolve(_context);

            // TODO: Header flag may not be needed anymore, with the introduction of NeedAs.BestMatch
            if (Header)
            {
                if (resolved?.This.DeclaringType != null && !Definitions.Contains(resolved?.This.DeclaringType))
                    // If def's declaring type is not defined, we cannot declare def.
                    AddDefinition(def, resolved);
                else if (resolved != null && !DefinitionsToGet.Contains(def))
                    // Failing that, we define it
                    Declarations.Add(def);
            }
            else
                // If we are a C++ file, we must have a definition since we need this type resolved
                AddDefinition(def, resolved);
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
            if (data.IsVoid())
                // If the TypeRef is void, easily return void
                return "void";
            if (data.IsPrimitive())
            {
                // If the TypeRef is a primitive, we need to convert it to a C++ name upfront.
                var primitiveName = ConvertPrimitive(data);
                if (!string.IsNullOrEmpty(primitiveName))
                    return primitiveName;
                // Failsafe return non-primitive converted name for special types like System.IntPtr
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
                    var declaringGenericParams = string.Empty;
                    if (genericMap.TryGetValue(declType, out var declaringGenerics))
                    {
                        // Write out the generics defined in this type
                        declaringGenericParams += "<";
                        bool first = true;
                        foreach (var g in declaringGenerics)
                        {
                            if (!first)
                                declaringGenericParams += ", ";
                            else
                                first = false;
                            if (data.IsGenericInstance)
                                declaringGenericParams += GetCppName(argMapping[g], true, true);
                            else
                                declaringGenericParams += GetCppName(g, true, true);
                        }
                        declaringGenericParams += ">";
                    }

                    var temp = declType.GetName() + declaringGenericParams;
                    if (!isThisType)
                        temp += "::" + declString;
                    else
                        isThisType = false;
                    declString = temp;
                    AddDefinition(declType);
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
                    foreach (var g in data.Generics)
                    {
                        if (!first)
                            name += ", ";
                        else
                            first = false;
                        if (data.IsGenericTemplate)
                        {
                            // If this is a generic template, use literal names for our generic parameters
                            name += g.Name;
                        }
                        else if (data.IsGenericInstance)
                        {
                            // If this is a generic instance, call each of the generic's GetCppName
                            name += GetCppName(g, qualified, true, needAs, ForceAsType.None);
                        }
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
            var resolved = typeRef.Resolve(_context);
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

        private string ConvertPrimitive(TypeRef def)
        {
            var name = def.Name.ToLower();
            if (name == "void*" || (def.Name == "Void" && def.IsPointer()))
                return "void*";
            else if (def.IsVoid())
                return "void";
            string s = null;
            if (def.IsArray())
            {
                // Technically, we only need to add a declaration for the array (if it isn't needed as a definition)
                // however, we will add it as a definition, because it's annoying to create a new generic ITypeData for it.
                var eName = GetCppName(def.ElementType, true, true);
                s = $"Array<{eName}>";
            }
            else if (def.IsPointer())
            {
                s = GetCppName(def.ElementType, true, true) + "*";
            }
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
            if (s is null)
                return null;
            if (s.StartsWith("Il2Cpp") || s.StartsWith("Array<"))
            {
                bool defaultPtr = false;
                if (s != "Il2CppChar")
                    defaultPtr = true;
                // For Il2CppTypes, should refer to type as :: to avoid ambiguity
                s = "::" + s + (defaultPtr ? "*" : "");
                NeedPrimitives = true;
            }
            return s;
        }
    }
}