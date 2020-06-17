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
        public HashSet<TypeName> NestedForwardDeclares { get; } = new HashSet<TypeName>();

        public HashSet<string> Includes { get; } = new HashSet<string>();
        public string FileName { get; private set; }
        public string TypeNamespace { get; }
        public string TypeName { get; }
        public ITypeCollection Types { get => _types; }
        public readonly TypeName type;
        public string QualifiedTypeName { get; }

        // Maps TypeRefs to resolved names
        private Dictionary<TypeRef, (TypeInfo, string)> _references = new Dictionary<TypeRef, (TypeInfo, string)>();

        // Holds generic types (ex: T1, T2, ...) defined by the type
        private HashSet<TypeRef> _genericTypes = new HashSet<TypeRef>();

        private ITypeCollection _types;
        private ITypeData _rootType;
        private ITypeData _localType;
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

        public CppSerializerContext(ITypeCollection types, ITypeData data, bool cpp = false)
        {
            _types = types;
            _rootType = _localType = data;
            _cpp = cpp;
            var resolvedTd = _types.ResolvedTypeRef(data.This);
            type = resolvedTd;
            QualifiedTypeName = ConvertTypeToQualifiedName(resolvedTd, false);
            TypeNamespace = resolvedTd.ConvertTypeToNamespace();
            TypeName = ConvertTypeToName(resolvedTd, false);
            FileName = ConvertTypeToInclude(resolvedTd, original: true);
            // Check all nested classes (and ourselves) if we have generic arguments/parameters. If we do, add them to _genericTypes.
            GetGenericTypes(data);
            // Nested types need to include their declaring type
            if (!cpp && data.This.DeclaringType != null)
                Includes.Add(ConvertTypeToInclude(_types.ResolvedTypeRef(data.This.DeclaringType)) + ".hpp");
            // Declaring types need to declare ALL of their nested types (even the ones they don't use)
            // TODO: also add them to _references?
            if (!cpp)
            {
                foreach (var nested in data.NestedTypes)
                    NestedForwardDeclares.Add(_types.ResolvedTypeRef(nested.This));
            }
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

        private string GenericsToStr(TypeName type, bool mayNeedComplete)
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
                        // if (TypeRef.SequenceEqualOrPrint(possibleMatch, type.DeclaringType.Generics))
                        if (possibleMatch.SequenceEqual(type.DeclaringType.Generics))
                        {
                            generics.RemoveRange(0, matchLength);
                            if (generics.Count != origCount - possibleMatch.Count) throw new Exception("Failed to change generics list!");
                        }
                        //else
                        //{
                        //    Console.Error.WriteLine("Hence, did not remove any generics.");
                        //    Console.Error.WriteLine($"(type was {type}, declaring was {type.DeclaringType})\n");
                        //}
                    }
                    else
                        Console.Error.WriteLine("Cannot remove generics: declaring type has more, but we are Instance!");
                }
                //if (generics.Count > 0 && generics.Count < origCount)
                //    Console.WriteLine($"{type}: removed {{{String.Join(", ", type.DeclaringType.Generics)}}}, left with " +
                //        $"{{{String.Join(", ", generics)}}} " +
                //        $"(IsGenInst? {type.IsGenericInstance} declaring.IsGenInst? {type.DeclaringType.IsGenericInstance})");
            }
            if (generics.Count == 0)
                return "";

            var typeStr = "<";
            bool first = true;
            foreach (var genParam in generics)
            {
                if (!first)
                    typeStr += ", ";
                typeStr += GetNameFromReference(genParam, mayNeedComplete: mayNeedComplete) ?? (genParam.SafeName());
                first = false;
            }
            typeStr += ">";
            if (typeStr.Length == 2)
                Console.WriteLine($"GenericArgsToStr failed for type {type}: no generics found? {String.Join(", ", generics)}");
            return typeStr;
        }

        public string ConvertTypeToName(TypeName def, bool generics = true, bool mayNeedComplete = false)
        {
            var name = def.Name;
            if (!generics || !def.IsGeneric)
                return name;

            var types = GenericsToStr(def, mayNeedComplete);
            // Nothing left to do unless declaring type has additional generic args/params
            if (!DeclaringTypeHasGenerics(def))
                return name + types;

            var declaring = _types.ResolvedTypeRef(def.DeclaringType);
            int nestInd = name.LastIndexOf("::");
            if (nestInd >= 0)
                name = name.Substring(nestInd);
            return ConvertTypeToName(declaring, generics, mayNeedComplete) + name + types;
        }

        public string ConvertTypeToQualifiedName(TypeName def, bool generics = true, bool mayNeedComplete = false)
        {
            return def.ConvertTypeToNamespace() + "::" + ConvertTypeToName(def, generics, mayNeedComplete);
        }

        public string ConvertTypeToInclude(TypeName def, bool original = false)
        {
            if (!original)
            {
                if (!def.GetsOwnHeader)
                    return ConvertTypeToInclude(_types.ResolvedTypeRef(def.DeclaringType));
                def.IncludeCount++;
            }
            // TODO: instead split on :: and Path.Combine?
            var fileName = string.Join("-", ConvertTypeToName(def, false).Replace("::", "_").Split(Path.GetInvalidFileNameChars()));
            var directory = string.Join("-", def.ConvertTypeToNamespace().Replace("::", "_").Split(Path.GetInvalidPathChars()));
            return $"{directory}/{fileName}";
        }

        private bool IsLocalTypeOrNestedUnderIt(TypeRef type)
        {
            // TODO: re-write to only include in-place nested types?
            if (_localType.This.Equals(type)) return true;
            if (type is null) return false;
            return IsLocalTypeOrNestedUnderIt(type.DeclaringType);
            // return _localType.This.Equals(type) || _localType.This.Equals(type.DeclaringType);
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
        public string GetNameFromReference(TypeRef def, ForceAsType force = ForceAsType.None, bool qualified = true, bool genericArgs = true,
            bool mayNeedComplete = false)
        {
            if (def.Name == "IActivator" && force == ForceAsType.Literal && mayNeedComplete)
            {
                Console.WriteLine("Hello world");
            }
            // For resolving generic type paramters
            // ex: TypeName<T1, T2>, GetNameFromReference(T1)
            if (_genericTypes.Contains(def))
                // TODO: Check to ensure ValueType is correct here. Perhaps assuming reference type is better?
                return ForceName(new TypeInfo() { TypeFlags = TypeFlags.ValueType }, def.Name, force);

            bool typeCouldGoInThisFile = IsLocalTypeOrNestedUnderIt(def);
            if (!typeCouldGoInThisFile)  // prevents System::Object from becoming Il2CppObject in its own definition, etc
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
            var type = def.Resolve(_types);
            if (type == null)
            {
                // for Dll parsing, should only happen for true generics (i.e. T, TValue, etc)
                // Console.WriteLine($"GetNameFromReference: failed to resolve {def}");
                // We have no way of resolving this type definition
                return null;
            }

            // TODO: instead, just use type
            // If we have not, map the type definition to a safe, unique name (TypeName)
            var resolvedTd = _types.ResolvedTypeRef(def);

            // If the type is us, no need to include/forward declare it
            if (_localType.Equals(type))
            {
                return ForceName(type.Info,
                    qualified ? ConvertTypeToQualifiedName(resolvedTd, genericArgs, mayNeedComplete)
                    : ConvertTypeToName(resolvedTd, genericArgs, mayNeedComplete), force);
            }

            // If we are the context for a header:
            // AND the type is not a nested type
            // AND the type is our child
            // OR, the type's use definitely doesn't require a complete definition
            // OR, the type is being asked to be used as a POINTER
            // OR, it is a reference type AND it is being asked to be used NOT(as a literal or as a reference):
            // Forward declare
            if (!_cpp && (def.DeclaringType is null || def.DeclaringType.Equals(_localType.This)) && (
                _localType.This.Equals(type.Parent)
                || !mayNeedComplete
                || force == ForceAsType.Pointer
                || (type.Info.TypeFlags == TypeFlags.ReferenceType && force != ForceAsType.Literal && force != ForceAsType.Reference)
            ))
            {
                // Since forward declaring a generic instance gives a template specialization, which is at best a small AOT optimization
                // and at worst a reference to an undefined template, we want to forward declare their generic definitions instead
                var fd = resolvedTd;
                if (resolvedTd.IsGenericInstance)
                    fd = _types.ResolvedTypeRef(type.This);

                if (_localType.This.Equals(def.DeclaringType))
                    NestedForwardDeclares.Add(fd);
                else if (fd.Namespace == TypeNamespace)
                    NamespaceForwardDeclares.Add(fd);
                else
                    ForwardDeclares.Add(fd);
            }
            else
            {
                if (!_cpp && typeCouldGoInThisFile)
                {
                    var tmpType = type;
                    while (!tmpType.Equals(_localType))
                    {
                        var declaringType = tmpType.This.DeclaringType.Resolve(_types);
                        if (declaringType is null)
                            throw new UnresolvedTypeException(tmpType.This, tmpType.This.DeclaringType);
                        declaringType.NestedInPlace.Add(tmpType);
                        _types.ResolvedTypeRef(tmpType.This).GetsOwnHeader = false;
                        tmpType.GetsOwnHeader = false;
                        tmpType = declaringType;
                    }
                }
                else
                {
                    // Get path to this type (namespace/name)
                    // TODO: If we have namespace headers, we need to namespace declare our return value:
                    // namespace::typeName
                    Includes.Add(ConvertTypeToInclude(resolvedTd) + ".hpp");
                }
            }

            // Add newly created name to _references
            _references.Add(def, (type.Info, ConvertTypeToQualifiedName(resolvedTd, genericArgs, mayNeedComplete)));

            // Return safe created name
            return ForceName(type.Info, qualified ? ConvertTypeToQualifiedName(resolvedTd, genericArgs, mayNeedComplete)
                : ConvertTypeToName(resolvedTd, genericArgs, mayNeedComplete), force);
        }

        private string ResolvePrimitive(TypeRef def, ForceAsType force)
        {
            var name = def.Name.ToLower();
            if (def.Name == "void*" || (def.Name == "Void" && def.IsPointer(_types)))
                return "void*";
            else if (name == "void")
                return "void";

            // Note: names on the right side of an || are for Dll only
            string s = null;
            if (def.IsArray())
                s = $"Array<{GetNameFromReference(def.ElementType)}>";
            else if (def.IsPointer(_types))
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