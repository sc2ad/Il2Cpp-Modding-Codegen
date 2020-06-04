using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppSerializerContext : ISerializerContext
    {
        public HashSet<TypeName> ForwardDeclares { get; } = new HashSet<TypeName>();

        // For same namespace forward declares
        public HashSet<TypeName> NamespaceForwardDeclares { get; } = new HashSet<TypeName>();

        public HashSet<string> Includes { get; } = new HashSet<string>();
        public string FileName { get; private set; }
        public string TypeNamespace { get; }
        public string TypeName { get; }
        public string QualifiedTypeName { get; }

        // Maps TypeRefs to resolved names
        private Dictionary<TypeRef, (TypeInfo, string)> _references = new Dictionary<TypeRef, (TypeInfo, string)>();

        // Holds generic types (ex: T1, T2, ...) defined by the type
        private HashSet<TypeRef> _genericTypes = new HashSet<TypeRef>();

        private ITypeContext _context;
        private ITypeData _localType;
        private bool _cpp;

        public CppSerializerContext(ITypeContext context, ITypeData data, bool cpp = false)
        {
            _context = context;
            _localType = data;
            _cpp = cpp;
            var resolvedTd = _context.ResolvedTypeRef(data.This);
            QualifiedTypeName = resolvedTd.ConvertTypeToQualifiedName();
            TypeNamespace = resolvedTd.ConvertTypeToNamespace();
            TypeName = resolvedTd.ConvertTypeToName();
            FileName = resolvedTd.ConvertTypeToInclude();
            if (data.This.Generic)
            {
                foreach (var g in data.This.GenericParameters)
                {
                    _genericTypes.Add(g);
                }
            }
        }

        private string ForceName(TypeInfo info, string name, ForceAsType force)
        {
            switch (force)
            {
                case ForceAsType.Pointer:
                    return name + "*";

                case ForceAsType.Reference:
                    return name + "&";

                case ForceAsType.Literal:
                    return name;

                case ForceAsType.None:
                    if (info.TypeFlags == TypeFlags.ReferenceType)
                        return name + "*";
                    return name;

                default:
                    return name;
            }
        }

        private string GenericParamsToStr(TypeRef type, bool genericParams)
        {
            var typeStr = "";
            if (genericParams)
            {
                typeStr = "<";
                bool first = true;
                foreach (var genParam in type.GenericParameters)
                {
                    if (!first)
                        typeStr += ", ";
                    if (genParam is null)
                        typeStr += "T";
                    else
                        typeStr += GetNameFromReference(genParam);
                    first = false;
                }
                typeStr += ">";
                if (typeStr.Length == 2)
                    Console.WriteLine($"GenericParamsToStr failed for type {type}: no generic parameters found? {String.Join(", ", type.GenericParameters)}");
            }
            return typeStr;
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
        public string GetNameFromReference(TypeRef def, ForceAsType force = ForceAsType.None, bool qualified = true, bool genericParams = true)
        {
            // For resolving generic type paramters
            // ex: TypeName<T1, T2>, GetNameFromReference(T1)
            if (_genericTypes.Contains(def))
                // TODO: Check to ensure ValueType is correct here. Perhaps assuming reference type is better?
                return ForceName(new TypeInfo() { TypeFlags = TypeFlags.ValueType }, def.Name, force);

            // TODO: Need to determine a better way of resolving special names
            if (!_localType.This.Equals(def))
            {
                var primitiveName = ResolvePrimitive(def, force);
                // Primitives are automatically resolved via this call
                if (primitiveName != null)
                    return primitiveName;
            }

            // If we have already resolved it, simply return the name as one of the forced types
            if (_references.TryGetValue(def, out (TypeInfo, string) resolvedName))
            {
                return ForceName(resolvedName.Item1, resolvedName.Item2, force);
            }
            // We may have already resolved this type, but without a namespace. Check that
            var found = _references.Keys.FirstOrDefault(td => td.Name == def.Name);
            if (found != null)
            {
                if (!found.Generic)
                    return ForceName(_references[found].Item1, _references[found].Item2, force);
                var typeStr = GenericParamsToStr(found, genericParams);
                return ForceName(_references[found].Item1, _references[found].Item2 + typeStr, force);
            }

            // Resolve the type definition
            var type = def.Resolve(_context);
            if (type == null)
                // We have no way of resolving this type definition
                return null;

            // TODO: instead, just use type
            // If we have not, map the type definition to a safe, unique name (TypeName)
            var resolvedTd = _context.ResolvedTypeRef(type.This);

            // If this is a generic type, we need to ensure we are providing correct type parameters
            var types = "";
            if (def.Generic && genericParams)
            {
                types = GenericParamsToStr(def, genericParams);
                // Modify resolved type definition's name to include generic arguments
            }

            // If the type is ourselves, no need to include/forward declare it
            if (type.Equals(_localType))
            {
                return ForceName(type.Info, (qualified ? resolvedTd.ConvertTypeToQualifiedName() : resolvedTd.ConvertTypeToName()) + types, force);
            }

            // If we are the context for a header:
            // AND the type is our child
            // OR, if the type is being asked to be used as a POINTER
            // OR, it is a reference type AND it is being asked to be used NOT(as a literal or as a reference):
            // Forward declare
            if (!_cpp && (
                _localType.This.Equals(type.Parent)
                || force == ForceAsType.Pointer
                || (type.Info.TypeFlags == TypeFlags.ReferenceType && force != ForceAsType.Literal && force != ForceAsType.Reference)
            ))
            {
                if (resolvedTd.Namespace == TypeNamespace)
                    NamespaceForwardDeclares.Add(resolvedTd);
                else
                    ForwardDeclares.Add(resolvedTd);
            }
            else
            {
                // Get path to this type (namespace/name)
                // TODO: If we have namespace headers, we need to namespace declare our return value:
                // namespace::typeName
                Includes.Add(resolvedTd.ConvertTypeToInclude() + ".hpp");
            }

            // Add newly created name to _references
            _references.Add(def, (type.Info, resolvedTd.ConvertTypeToQualifiedName() + types));

            // Return safe created name
            return ForceName(type.Info, (qualified ? resolvedTd.ConvertTypeToQualifiedName() : resolvedTd.ConvertTypeToName()) + types, force);
        }

        private string ResolvePrimitive(TypeRef def, ForceAsType force)
        {
            var name = def.Name.ToLower();
            if (name == "void")
                return "void";
            else if (name == "void*")
                return "void*";

            // Note: names on the right side of an || are for Dll only
            string s = null;
            if (name == "object")
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
            else if (def.IsArray())
            {
                s = $"Array<{GetNameFromReference(def.ElementType)}>";
            }

            if (s is null) return null;
            switch (force)
            {
                case ForceAsType.Pointer:
                    return s + "*";

                case ForceAsType.Reference:
                    return s + "&";

                case ForceAsType.Literal:
                    // Special cases for Il2Cpp types, need to forward declare/include typedefs.h iff force valuetype
                    if (s.StartsWith("Il2Cpp") || s.StartsWith("Array<"))
                    {
                        Includes.Add("utils/typedefs.h");
                    }
                    return s;

                default:
                    // Pointer type for Il2Cpp types on default
                    if ((s.StartsWith("Il2Cpp") && (s != "Il2CppChar")) || s.StartsWith("Array<"))
                    {
                        // TODO: resolve Array's types as generic?
                        ForwardDeclares.Add(new TypeName("", s));
                        return "::" + s + "*";
                    }
                    return s;
            }
        }
    }
}