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
        public HashSet<ResolvedType> NestedForwardDeclares { get; } = new HashSet<ResolvedType>();

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
        private ITypeData _rootType;
        private ITypeData _localType;
        private ResolvedType _localDeclaringType;
        private bool _cpp;

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
            _rootType = _localType = data;
            _cpp = cpp;
            var resolvedTd = _context.ResolvedTypeRef(data.This);
            QualifiedTypeName = ConvertTypeToQualifiedName(resolvedTd, false);
            TypeNamespace = resolvedTd.ConvertTypeToNamespace();
            TypeName = ConvertTypeToName(resolvedTd, false);
            FileName = ConvertTypeToInclude(resolvedTd);
            // Check all nested classes (and ourselves) if we have generic arguments/parameters. If we do, add them to _genericTypes.
            GetGenericTypes(data);
            // Nested types need to include their declaring type
            if (!cpp && data.This.DeclaringType != null)
            {
                _localDeclaringType = ResolveType(data.This.DeclaringType);
                Includes.Add(ConvertTypeToInclude(context.ResolvedTypeRef(data.This.DeclaringType)) + ".hpp");
            }
            // Declaring types need to forward declare ALL of their nested types
            // TODO: also add them to _references?
            if (!cpp)
            {
                foreach (var nested in data.NestedTypes)
                    NestedForwardDeclares.Add(context.ResolvedTypeRef(nested.This));
            }
        }

        // TODO: Make this do stuff with same namespace and whatnot
        public void AddForwardDeclare(ResolvedType type)
        {
            if (!ForwardDeclares.Contains(type))
                ForwardDeclares.Add(type);
            // If type is generic, for each generic type, we need to add that to either the FDs or the includes (depending on the type)
            // Should almost always be includes
            foreach (var g in type.Generics)
                AddInclude(g);
        }

        public void AddPrimitive(ResolvedType type)
        {
            if (!type.Primitive)
                throw new InvalidOperationException($"{nameof(type)} must be a primitive type!");
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

        private bool DeclaringTypeHasGenerics(TypeName type)
        {
            return (type.DeclaringType != null) && (type.DeclaringType.IsGeneric);
        }

        private string GenericsToStr(TypeName type)
        {
            var generics = type.Generics.ToList();
            int origCount = generics.Count;

            if (DeclaringTypeHasGenerics(type))
            {
                if (type.IsGenericTemplate)
                    generics = generics.Except(type.DeclaringType.Generics, TypeRef.fastComparer).ToList();
                else
                {
                    // remove declaring's generics from the start of our list, ensuring that they are equal
                    int matchLength = type.DeclaringType.Generics.Count;
                    if (matchLength <= origCount)
                    {
                        var possibleMatch = generics.GetRange(0, matchLength);
                        if (TypeRef.SequenceEqualOrPrint(possibleMatch, type.DeclaringType.Generics))
                        {
                            generics.RemoveRange(0, matchLength);
                            if (generics.Count != origCount - possibleMatch.Count) throw new Exception("Failed to change generics list!");
                        }
                        else
                        {
                            Console.Error.WriteLine("Hence, did not remove any generics.");
                            Console.Error.WriteLine($"(type was {type}, declaring was {type.DeclaringType})\n");
                        }
                    }
                    else
                        Console.Error.WriteLine("Cannot remove generics: declaring type has more, but we are Instance!");
                }
                if (generics.Count > 0 && generics.Count < origCount)
                    Console.WriteLine($"{type}: removed {{{String.Join(", ", type.DeclaringType.Generics)}}}, left with " +
                        $"{{{String.Join(", ", generics)}}} " +
                        $"(IsGenInst? {type.IsGenericInstance} declaring.IsGenInst? {type.DeclaringType.IsGenericInstance})");
            }
            if (generics.Count == 0)
                return "";

            var typeStr = "<";
            bool first = true;
            foreach (var genParam in generics)
            {
                if (!first)
                    typeStr += ", ";
                typeStr += GetNameFromReference(genParam) ?? (genParam.SafeName());
                first = false;
            }
            typeStr += ">";
            if (typeStr.Length == 2)
                Console.WriteLine($"GenericArgsToStr failed for type {type}: no generics found? {String.Join(", ", generics)}");
            return typeStr;
        }

        private bool IsLocalTypeOrNestedUnderIt(TypeRef type)
        {
            //if (_localType.This.Equals(type)) return true;
            //if (type is null) return false;
            //return IsLocalTypeOrNestedUnderIt(type.DeclaringType);
            return _localType.This.Equals(type) || _localType.This.Equals(type.DeclaringType);
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

        /// <summary>
        /// Gets a string name from a type definition.
        /// Checks it against a private map.
        /// Returns null if the type is a value type and could not be created.
        /// Otherwise, will always return at least Il2CppObject
        /// </summary>
        /// <param name="def"></param>
        /// <param name="force"></param>
        /// <returns></returns>
        public string GetNameFromReference(TypeRef def, ForceAsType force = ForceAsType.None, bool qualified = true, bool genericArgs = true)
        {
            // For resolving generic type paramters
            // ex: TypeName<T1, T2>, GetNameFromReference(T1)
            if (_genericTypes.Contains(def))
                // TODO: Check to ensure ValueType is correct here. Perhaps assuming reference type is better?
                return ForceName(new TypeInfo() { TypeFlags = TypeFlags.ValueType }, def.Name, force);

            bool typeIsInThisFile = IsLocalTypeOrNestedUnderIt(def);
            if (!typeIsInThisFile)  // prevents System::Object from becoming Il2CppObject in its own definition, etc
            {
                // TODO: Need to determine a better way of resolving special names
                var primitiveName = ResolvePrimitive(def, force);
                // Primitives are automatically resolved via this call
                if (primitiveName != null)
                    return primitiveName;
            }

            // If we have already resolved it, simply return the name as one of the forced types
            if (_references.TryGetValue(def, out (TypeInfo, string) resolvedName))
                return ForceName(resolvedName.Item1, resolvedName.Item2, force);

            // Resolve the TypeRef. If the TypeRef is a generic instance, it will resolve to the generic definition.
            var type = def.Resolve(_context);
            if (type == null)
            {
                // for Dll parsing, should only happen for true generics (i.e. T, TValue, etc)
                // Console.WriteLine($"GetNameFromReference: failed to resolve {def}");
                // We have no way of resolving this type definition
                return null;
            }

            // TODO: instead, just use type
            // If we have not, map the type definition to a safe, unique name (TypeName)
            var resolvedTd = _context.ResolvedTypeRef(def);

            // If the type is us or !cpp and the type is nested directly under us, no need to include/forward declare it (see constructor)
            if (_localType.Equals(type) || (!_cpp && _localType.This.Equals(def.DeclaringType)))
            {
                return ForceName(type.Info,
                    qualified ? ConvertTypeToQualifiedName(resolvedTd, genericArgs) : ConvertTypeToName(resolvedTd, genericArgs), force);
            }

            // If we are the context for a header:
            // AND the type is not a nested type
            // AND the type is our child
            // OR, if the type is being asked to be used as a POINTER
            // OR, it is a reference type AND it is being asked to be used NOT(as a literal or as a reference):
            // Forward declare
            if (!_cpp && (def.DeclaringType is null) && (
                _localType.This.Equals(type.Parent)
                || force == ForceAsType.Pointer
                || (type.Info.TypeFlags == TypeFlags.ReferenceType && force != ForceAsType.Literal && force != ForceAsType.Reference)
            ))
            {
                // Since forward declaring a generic instance gives a template specialization, which is at best a small AOT optimization
                // and at worst a reference to an undefined template, we want to forward declare their generic definitions instead
                var fd = resolvedTd;
                if (resolvedTd.IsGenericInstance)
                    fd = _context.ResolvedTypeRef(type.This);

                if (_localType.This.Equals(def.DeclaringType))
                    NestedForwardDeclares.Add(fd);
                else if (fd.Namespace == TypeNamespace)
                    NamespaceForwardDeclares.Add(fd);
                else
                    ForwardDeclares.Add(fd);
            }
            else
            {
                // Get path to this type (namespace/name)
                // TODO: If we have namespace headers, we need to namespace declare our return value:
                // namespace::typeName
                Includes.Add(ConvertTypeToInclude(resolvedTd) + ".hpp");
            }

            // Add newly created name to _references
            _references.Add(def, (type.Info, ConvertTypeToQualifiedName(resolvedTd, genericArgs)));

            // Return safe created name
            return ForceName(type.Info, qualified ? ConvertTypeToQualifiedName(resolvedTd, genericArgs) : ConvertTypeToName(resolvedTd, genericArgs), force);
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