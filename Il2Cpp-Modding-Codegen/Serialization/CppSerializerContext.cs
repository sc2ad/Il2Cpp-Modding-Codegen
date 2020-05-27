using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppSerializerContext : ISerializerContext
    {
        private const string NoNamespace = "GlobalNamespace";
        public HashSet<string> ForwardDeclares { get; } = new HashSet<string>();
        public HashSet<string> Includes { get; } = new HashSet<string>();
        public string FileName { get; private set; }
        public string TypeNamespace { get; }
        public string TypeName { get; }
        public string QualifiedTypeName { get; }

        // Maps TypeDefinitions to resolved names
        private Dictionary<TypeDefinition, (TypeInfo, string)> _references = new Dictionary<TypeDefinition, (TypeInfo, string)>();

        // Holds generic types (ex: T1, T2, ...) defined by the type
        private HashSet<TypeDefinition> _genericTypes = new HashSet<TypeDefinition>();

        private ITypeContext _context;
        private ITypeData _localType;

        private string ConvertTypeToName(TypeDefinition def)
        {
            return def.Name;
        }

        private string ConvertTypeToNamespace(TypeDefinition def)
        {
            if (string.IsNullOrWhiteSpace(def.Namespace))
                return NoNamespace;
            return def.Namespace;
        }

        private string ConvertTypeToQualifiedName(TypeDefinition def)
        {
            return ConvertTypeToNamespace(def) + "::" + ConvertTypeToName(def);
        }

        private string ConvertTypeToInclude(TypeDefinition def)
        {
            return ConvertTypeToNamespace(def) + "/" + ConvertTypeToName(def);
        }

        public CppSerializerContext(ITypeContext context, ITypeData data)
        {
            _context = context;
            _localType = data;
            var resolvedTd = _context.ResolvedTypeDefinition(data.This);
            QualifiedTypeName = ConvertTypeToQualifiedName(resolvedTd);
            TypeNamespace = ConvertTypeToNamespace(resolvedTd);
            TypeName = ConvertTypeToName(resolvedTd);
            FileName = ConvertTypeToInclude(resolvedTd);
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

        /// <summary>
        /// Gets a string name from a type definition.
        /// Checks it against a private map.
        /// Returns null if the type is a value type and could not be created.
        /// Otherwise, will always return at least Il2CppObject
        /// </summary>
        /// <param name="def"></param>
        /// <param name="force"></param>
        /// <returns></returns>
        public string GetNameFromReference(TypeDefinition def, ForceAsType force, bool qualified)
        {
            // For resolving generic type paramters
            // ex: TypeName<T1, T2>, GetNameFromReference(T1)
            if (_genericTypes.Contains(def))
                // TODO: Check to ensure ValueType is correct here. Perhaps assuming reference type is better?
                return ForceName(new TypeInfo() { TypeFlags = TypeFlags.ValueType }, def.Name, force);

            // TODO: Need to determine a better way of resolving special names
            var primitiveName = ResolvePrimitive(def, force);
            // Primitives are automatically resolved via this call
            if (primitiveName != null)
                return primitiveName;

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
                var typeStr = "";
                for (int i = 0; i < found.GenericParameters.Count; i++)
                {
                    typeStr += GetNameFromReference(found.GenericParameters[i], ForceAsType.None, true);
                    if (i != found.GenericParameters.Count - 1)
                        typeStr += ", ";
                }
                return ForceName(_references[found].Item1, _references[found].Item2 + "<" + typeStr + ">", force);
            }

            // Resolve the type definition
            var type = def.Resolve(_context);
            if (type == null)
                // We have no way of resolving this type definition
                return null;

            // If we have not, map the type definition to a safe, unique name (TypeDefinition)
            var resolvedTd = _context.ResolvedTypeDefinition(type.This);

            // If this is a generic type, we need to ensure we are providing correct type parameters
            var types = "";
            if (def.Generic)
            {
                types = "<";
                for (int i = 0; i < def.GenericParameters.Count; i++)
                {
                    types += GetNameFromReference(def.GenericParameters[i], ForceAsType.None, true);
                    if (i != def.GenericParameters.Count - 1)
                        types += ", ";
                }
                types += ">";
                // Modify resolved type definition's name to include generic arguments
            }

            // If the type is ourselves, no need to include/forward declare it
            if (type.Equals(_localType))
            {
                return ForceName(type.Info, (qualified ? ConvertTypeToQualifiedName(resolvedTd) : ConvertTypeToName(resolvedTd)) + types, force);
            }

            // If the type exists, AND it is a reference type AND it is being asked to be used like a reference type:
            // Forward declare
            if (type.Info.TypeFlags == TypeFlags.ReferenceType && force == ForceAsType.Pointer)
            {
                ForwardDeclares.Add(ConvertTypeToQualifiedName(resolvedTd));
            }
            else
            {
                // Get path to this type (namespace/name)
                // TODO: If we have namespace headers, we need to namespace declare our return value:
                // namespace::typeName
                Includes.Add(ConvertTypeToInclude(resolvedTd) + ".hpp");
            }

            // Add newly created name to _references
            _references.Add(def, (type.Info, ConvertTypeToQualifiedName(resolvedTd) + types));

            // Return safe created name
            return ForceName(type.Info, (qualified ? ConvertTypeToQualifiedName(resolvedTd) : ConvertTypeToName(resolvedTd)) + types, force);
        }

        private string ResolvePrimitive(TypeDefinition def, ForceAsType force)
        {
            if (def.Name == "void")
                return "void";
            else if (def.Name == "void*")
                return "void*";
            string s = null;
            if (def.Name == "object")
                s = "Il2CppObject";
            else if (def.Name == "string")
                s = "Il2CppString";
            else if (def.Name == "int")
                s = "int";
            else if (def.Name == "float")
                s = "float";
            else if (def.Name == "double")
                s = "double";
            else if (def.Name == "uint")
                s = "uint";
            else if (def.Name == "char")
                s = "uint16_t";
            else if (def.Name == "byte")
                s = "int8_t";
            else if (def.Name == "sbyte")
                s = "uint8_t";
            else if (def.Name == "bool")
                s = "bool";
            else if (def.Name == "short")
                s = "int16_t";
            else if (def.Name == "ushort")
                s = "uint16_t";
            else if (def.Name == "long")
                s = "int64_t";
            else if (def.Name == "ulong")
                s = "uint64_t";
            else if (def.Name.EndsWith("[]"))
            {
                // Array
                // TODO: Make this use Array<ElementType> instead of Il2CppArray
                s = "Il2CppArray";
            }
            switch (force)
            {
                case ForceAsType.Pointer:
                    return s != null ? s += "*" : null;

                case ForceAsType.Reference:
                    return s != null ? s += "&" : null;

                case ForceAsType.Literal:
                    // Special cases for Il2Cpp types, need to forward declare/include typedefs.h iff force valuetype
                    if (s != null && s.StartsWith("Il2Cpp"))
                    {
                        Includes.Add("utils/typedefs.h");
                        return s;
                    }
                    return s;

                default:
                    // Pointer type for Il2Cpp types on default
                    if (s != null && s.StartsWith("Il2Cpp"))
                    {
                        ForwardDeclares.Add(s);
                        return s + "*";
                    }
                    return s;
            }
        }
    }

    internal class AdaptiveTypeName
    {
        public string TypeName { get; private set; }
        public string AsPointer { get; private set; }
        public string AsValue { get; private set; }
        public string AsRef { get; private set; }
    }
}