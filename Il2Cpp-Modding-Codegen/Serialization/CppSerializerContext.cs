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

        // For forward declares that will go inside the class definition
        public HashSet<TypeName> ClassForwardDeclares { get; } = new HashSet<TypeName>();

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
        private ITypeData _rootType;
        private ITypeData _localType;
        private bool _cpp;

        private void GetGenericTypes(ITypeData data)
        {
            var generics = data.This.Generic ? data.This?.GenericArguments ?? data.This.GenericParameters : null;
            if (generics != null)
            {
                foreach (var g in generics)
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
            QualifiedTypeName = resolvedTd.ConvertTypeToQualifiedName(context);
            TypeNamespace = resolvedTd.ConvertTypeToNamespace();
            TypeName = resolvedTd.ConvertTypeToName();
            FileName = resolvedTd.ConvertTypeToInclude(context);
            // Check all nested classes (and ourselves) if we have generic arguments/parameters. If we do, add them to _genericTypes.
            GetGenericTypes(data);
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

        private string GenericArgsToStr(TypeRef type, bool genericArgs)
        {
            var typeStr = "";
            if (genericArgs)
            {
                typeStr = "<";
                bool first = true;
                var generics = type.GenericArguments ?? type.GenericParameters;
                foreach (var genParam in generics)
                {
                    if (!first)
                        typeStr += ", ";
                    typeStr += GetNameFromReference(genParam) ?? genParam.SafeName();
                    first = false;
                }
                typeStr += ">";
                if (typeStr.Length == 2)
                    Console.WriteLine($"GenericArgsToStr failed for type {type}: no generics found? {String.Join(", ", generics)}");
            }
            return typeStr;
        }

        private bool IsLocalTypeOrNestedUnderIt(TypeRef type)
        {
            if (_localType.This.Equals(type)) return true;
            if (type is null) return false;
            return IsLocalTypeOrNestedUnderIt(type.DeclaringType);
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
            var resolvedTd = _context.ResolvedTypeRef(type.This);

            // If this is a generic type, we need to ensure we are providing correct type parameters
            var types = "";
            if (def.Generic && genericArgs)
            {
                types = GenericArgsToStr(def, genericArgs);
            }

            // If the type is ourselves, no need to include/forward declare it
            if (type.Equals(_localType))
            {
                return ForceName(type.Info, (qualified ? resolvedTd.ConvertTypeToQualifiedName(_context) : resolvedTd.ConvertTypeToName()) + types, force);
            }

            // If we are the context for a header:
            // AND the type is our child OR a nested type
            // OR, if the type is being asked to be used as a POINTER
            // OR, it is a reference type AND it is being asked to be used NOT(as a literal or as a reference):
            // Forward declare
            if (!_cpp && (
                _localType.This.Equals(type.Parent)
                || _localType.This.Equals(def.DeclaringType)
                || force == ForceAsType.Pointer
                || (type.Info.TypeFlags == TypeFlags.ReferenceType && force != ForceAsType.Literal && force != ForceAsType.Reference)
            ))
            {
                if (_localType.This.Equals(def.DeclaringType))
                    ClassForwardDeclares.Add(resolvedTd);
                else if (resolvedTd.Namespace == TypeNamespace)
                    NamespaceForwardDeclares.Add(resolvedTd);
                else
                    ForwardDeclares.Add(resolvedTd);
            }
            else
            {
                // Get path to this type (namespace/name)
                // TODO: If we have namespace headers, we need to namespace declare our return value:
                // namespace::typeName
                Includes.Add(resolvedTd.ConvertTypeToInclude(_context) + ".hpp");
            }

            // Add newly created name to _references
            _references.Add(def, (type.Info, resolvedTd.ConvertTypeToQualifiedName(_context) + types));

            // Return safe created name
            return ForceName(type.Info, (qualified ? resolvedTd.ConvertTypeToQualifiedName(_context) : resolvedTd.ConvertTypeToName()) + types, force);
        }

        private string ResolvePrimitive(TypeRef def, ForceAsType force)
        {
            var name = def.Name.ToLower();
            if (def.Name == "void*" || (def.Name == "Void" && def.IsPointer(_context)))
                return "void*";
            else if (name == "void")
                return "void";

            // Note: names on the right side of an || are for Dll only
            string s = null;
            if (def.IsArray())
                s = $"Array<{GetNameFromReference(def.ElementType)}>";
            else if (def.IsPointer(_context))
                s = $"{GetNameFromReference(def.ElementType)}*";
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
                if (_cpp || force == ForceAsType.Literal)  // for .cpp or as a parent
                    Includes.Add("utils/typedefs.h");
                else
                    ForwardDeclares.Add(new TypeName(null, s));

                bool defaultPtr = false;
                if (s != "Il2CppChar")
                    defaultPtr = true;
                // For Il2CppTypes, should refer to type as :: to avoid ambiguity
                if (force != ForceAsType.Literal)
                    s = "::" + s + (defaultPtr ? "*" : "");
            }

            switch (force)
            {
                case ForceAsType.Pointer:
                    return s + "*";

                case ForceAsType.Reference:
                    return s + "&";

                default:
                    return s;
            }
        }
    }
}