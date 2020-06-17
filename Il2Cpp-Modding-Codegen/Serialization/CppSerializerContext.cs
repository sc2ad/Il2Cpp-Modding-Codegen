using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
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
            Declaration,
            Definition
        }

        public enum ForceAsType
        {
            None,
            Literal
        }

        public HashSet<TypeRef> RequiredDeclarations { get; } = new HashSet<TypeRef>();
        public HashSet<TypeRef> RequiredDefinitions { get; } = new HashSet<TypeRef>();
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

        // Holds generic types (ex: T1, T2, ...) defined by the type
        private HashSet<TypeRef> _genericTypes = new HashSet<TypeRef>();

        private ITypeCollection _context;

        public CppSerializerContext(ITypeCollection context, ITypeData data, bool cpp = false)
        {
            _context = context;
            LocalType = data;
            QualifiedTypeName = GetCppName(data.This);
            TypeNamespace = data.This.GetNamespace();
            TypeName = data.This.GetName();
            FileName = data.This.GetIncludeLocation();
            // Check all nested classes (and ourselves) if we have generic arguments/parameters. If we do, add them to _genericTypes.
            if (data.This.IsGenericTemplate)
                foreach (var g in data.This.Generics)
                    _genericTypes.Add(g);
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
            // Declaring types need to declare ALL of their nested types
            // TODO: also add them to _references?
            if (!cpp)
            {
                // This should only happen in the declaring type's header, however.
                foreach (var nested in data.NestedTypes)
                    AddDeclaration(nested.This);
            }
        }

        private void AddDefinition(TypeRef def)
        {
            // Adding a definition is simple, ensure the type is resolved and add it
            var resolved = def.Resolve(_context);
            if (resolved != null)
                RequiredDefinitions.Add(def);
        }

        private void AddDeclaration(TypeRef def)
        {
            var resolved = def.Resolve(_context);
            if (resolved != null)
                RequiredDeclarations.Add(def);
        }

        /// <summary>
        /// Gets the C++ fully qualified name for the TypeRef.
        /// </summary>
        /// <returns>Null if the type has not been resolved (and is not a generic parameter or primitive)</returns>
        public string GetCppName(TypeRef data, bool generics = true, ForceAsType forceAsType = ForceAsType.None)
        {
            // If the TypeRef is a generic parameter, return its name
            if (_genericTypes.Contains(data))
                return data.Name;
            // If the TypeRef is a primitive, we need to convert it to a C++ name upfront.
            if (data.IsPrimitive())
                return ConvertPrimitive(data);
            var resolved = ResolveAndStore(data, NeedAs.Declaration);
            if (resolved is null)
                return null;
            var name = data.GetNamespace() + "::";
            if (data.DeclaringType != null)
            {
                // Each declaring type must be defined (confirm this is the case)
                var declString = string.Empty;
                var declType = data.DeclaringType;
                while (declType != null)
                {
                    declString = declType.Name + "::" + declString;
                    AddDefinition(declType);
                    declType = declType.DeclaringType;
                }
                name += declString;
            }
            name += data.Name;
            if (generics && data.Generics != null)
            {
                name += "<";
                bool first = true;
                foreach (var g in data.Generics)
                {
                    if (!first)
                    {
                        name += ", ";
                        first = false;
                    }
                    if (data.IsGenericTemplate)
                    {
                        // If this is a generic template, use literal names for our generic parameters
                        name += g.Name;
                    }
                    else if (data.IsGenericInstance)
                    {
                        // If this is a generic instance, call each of the generic's GetCppName
                        name += GetCppName(g);
                    }
                }
                name += ">";
            }
            // Ensure the name has no bad characters
            name = name.Replace('`', '_').Replace('<', '$').Replace('>', '$');
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
        public ITypeData ResolveAndStore(TypeRef typeRef, NeedAs needAs = NeedAs.Declaration)
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
                    // If we need it as a declaration, but it isn't a reference type, then we add it to definitions
                    if (resolved.Info.TypeFlags == TypeFlags.ReferenceType)
                        AddDeclaration(typeRef);
                    else
                        AddDefinition(typeRef);
                    break;

                case NeedAs.Definition:
                default:
                    AddDefinition(typeRef);
                    break;
            }
            if (typeRef.IsGenericTemplate && typeRef.Generics != null)
            {
                // Resolve and store each generic argument
                foreach (var g in typeRef.Generics)
                    // Only need them as declarations, since we don't need the literal pointers.
                    ResolveAndStore(g, NeedAs.Declaration);
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
                var eName = GetCppName(def.ElementType);
                s = $"Array<{eName}>";
            }
            else if (def.IsPointer())
            {
                s = GetCppName(def.ElementType) + "*";
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