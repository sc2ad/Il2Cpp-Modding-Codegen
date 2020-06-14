using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public enum ForceAsType
    {
        None,
        Literal,
        Pointer,
        Reference
    }

    public class CppSerializerContext
    {
        public HashSet<ResolvedType> ForwardDeclares { get; } = new HashSet<ResolvedType>();

        // For same namespace forward declares
        public HashSet<ResolvedType> NamespaceForwardDeclares { get; } = new HashSet<ResolvedType>();

        // For forward declares that will go inside the class definition
        public HashSet<TypeRef> NestedForwardDeclares { get; } = new HashSet<TypeRef>();

        public HashSet<string> Includes { get; } = new HashSet<string>();
        public string FileName { get; private set; }
        public string TypeNamespace { get; }
        public string TypeName { get; }
        public string QualifiedTypeName { get; }

        // Maps TypeRefs to resolved types. This includes forward declares and #includes
        private Dictionary<TypeRef, ResolvedType> _references = new Dictionary<TypeRef, ResolvedType>();

        // Holds generic types (ex: T1, T2, ...) defined by the type
        private HashSet<TypeRef> _genericTypes = new HashSet<TypeRef>();

        private ITypeContext _context;
        private ITypeData _localType;
        private ResolvedType _localResolvedType;
        private ResolvedType _localDeclaringType;

        private void GetGenericTypes(ITypeData data)
        {
            if (data.This.IsGeneric)
            {
                foreach (var g in data.This.Generics)
                {
                    // Add all of our generic arguments or parameters
                    _genericTypes.Add(g);
                }
            }
            //foreach (var nested in data.NestedTypes)
            //{
            //    // Add all of our nested types
            //    GetGenericTypes(nested);
            //}
        }

        public CppSerializerContext(ITypeContext context, ITypeData data, bool cpp = false)
        {
            _context = context;
            _localType = data;
            _localResolvedType = ResolveType(data.This);
            QualifiedTypeName = _localResolvedType.GetQualifiedTypeName();
            TypeNamespace = _localResolvedType.GetNamespace();
            TypeName = _localResolvedType.GetTypeName(false);
            FileName = _localResolvedType.GetIncludeLocation();
            // Check all nested classes (and ourselves) if we have generic arguments/parameters. If we do, add them to _genericTypes.
            GetGenericTypes(data);
            // Nested types need to include their declaring type
            if (!cpp && data.This.DeclaringType != null)
            {
                _localDeclaringType = ResolveType(data.This.DeclaringType);
                Includes.Add(_localDeclaringType.GetIncludeLocation());
            }
            // Declaring types need to forward declare ALL of their nested types
            // TODO: also add them to _references?
            if (!cpp)
            {
                foreach (var nested in data.NestedTypes)
                    NestedForwardDeclares.Add(nested.This);
            }
        }

        // TODO: Make this do stuff with same namespace and whatnot
        public void AddForwardDeclare(ResolvedType type)
        {
            if (type.DeclaringType?.Type.This.Equals(_localType.This) == true)
            {
                if (!NestedForwardDeclares.Contains(type.Definition))
                    NestedForwardDeclares.Add(type.Definition);
            }
            else if (type.Definition?.Namespace == _localType.This.Namespace)
            {
                if (!NamespaceForwardDeclares.Contains(type))
                    NamespaceForwardDeclares.Add(type);
            }
            else if (!ForwardDeclares.Contains(type))
                ForwardDeclares.Add(type);
            // If type is generic, for each generic type, we need to add that to either the FDs or the includes (depending on the type)
            // Should almost always be includes
            foreach (var g in type.Generics)
                AddInclude(g);
        }

        public void AddInclude(ResolvedType type)
        {
            foreach (var s in type.GetIncludeLocations())
                if (!string.IsNullOrEmpty(s))
                    if (!Includes.Contains(s))
                        Includes.Add(s);
        }

        private string ForceName(TypeInfo info, string name, ForceAsType force)
        {
            if (info.TypeFlags == TypeFlags.ReferenceType && force != ForceAsType.Literal)
                name += "*";
            switch (force)
            {
                case ForceAsType.Pointer:
                    return name + "*";

                case ForceAsType.Reference:
                    return name + "&";

                default:
                    return name;
            }
        }

        /// <summary>
        /// Returns the <see cref="ResolvedType"/> that matches the provided <see cref="TypeRef"/>.
        /// Returns null if it the <see cref="ResolvedType"/> cannot be created (most likely because the <see cref="ITypeData"/> does not exist)
        /// </summary>
        /// <param name="def"></param>
        /// <returns></returns>
        public ResolvedType ResolveType(TypeRef typeRef)
        {
            // If typeRef is a generic type, we return a ResolvedType that maps to a null ITypeData.
            if (_genericTypes.Contains(typeRef))
                return new ResolvedType(typeRef);
            // If typeRef is ourselves, return ourselves.
            if (_localType.This.Equals(typeRef))
                return new ResolvedType(typeRef, _localType, _localDeclaringType);
            // If typeRef is a primitive type, return the primitive resolved type
            var primitive = ResolvePrimitive(typeRef);
            if (!(primitive is null))
                return primitive;
            // If typeRef is already in our references, return it.
            if (_references.TryGetValue(typeRef, out var val))
                return val;
            // Attempt to resolve typeRef in the ITypeContext
            var type = _context.Resolve(typeRef);
            if (type is null)
                // We cannot resolve the type, therefore it does not exist.
                return null;
            // If the type is generic, we need to resolve all of its generic parameters too
            var generics = new List<ResolvedType>();
            if (typeRef.IsGeneric)
            {
                foreach (var g in typeRef.Generics)
                {
                    // Here, we need to attempt to resolve any generic types. However, we need to create a new context for nested classes.
                    var r = ResolveType(g);
                    if (r is null)
                        // If any of the generic types fail to resolve, we can't resolve the type at all.
                        return null;
                    generics.Add(r);
                }
            }
            // If the type has a declaring type, resolve it.
            // TODO: Ensure this doesn't stack overflow.
            ResolvedType declaring = null;
            if (!(typeRef.DeclaringType is null))
            {
                declaring = ResolveType(typeRef.DeclaringType);
                if (declaring is null)
                    // If we cannot resolve the declaring type, we cannot resolve at all.
                    return null;
            }
            var ret = new ResolvedType(typeRef, type, declaring, generics);
            _references.Add(typeRef, ret);
            return ret;
        }

        private ResolvedType ResolvePrimitive(TypeRef def)
        {
            var name = def.Name.ToLower();
            if (name == "void*" || (def.Name == "Void" && def.IsPointer(_context)))
                return new ResolvedType(def, null, "void*", null);
            else if (name == "void")
                return new ResolvedType(def, null, "void", null);

            // Note: names on the right side of an || are for Dll only
            string s = null;
            ResolvedType elementType = null;
            ITypeData resolved = _context.Resolve(def);
            if (def.IsArray())
            {
                // We need to resolve the element type
                elementType = ResolveType(def.ElementType);
                s = $"Array<{elementType.GetTypeName()}>";
            }
            else if (def.IsPointer(_context))
            {
                elementType = ResolveType(def.ElementType);
                s = $"{elementType.GetTypeName()}*";
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
            if (s is null) return null;

            if (s.StartsWith("Il2Cpp") || s.StartsWith("Array<"))
            {
                bool defaultPtr = false;
                if (s != "Il2CppChar")
                    defaultPtr = true;
                // For Il2CppTypes, should refer to type as :: to avoid ambiguity
                s = "::" + s + (defaultPtr ? "*" : "");
            }
            return new ResolvedType(def, resolved, s, elementType);
        }
    }
}