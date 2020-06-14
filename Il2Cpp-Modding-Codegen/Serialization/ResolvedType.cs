using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    /// <summary>
    /// A resolved TypeRef.
    /// It is the responsibility of the caller to modify the context as necessary in order to ensure this resolved type is valid.
    /// This includes forward declaring and including as necessary.
    /// </summary>
    public class ResolvedType : IEquatable<ResolvedType>
    {
        internal const string NoNamespace = "GlobalNamespace";

        public TypeRef Reference { get; }
        public TypeInfo Info { get; }
        public ITypeData Type { get; }
        public TypeRef Definition { get; }
        public ResolvedType DeclaringType { get; }
        public bool GenericParameter { get; }
        public bool Primitive { get; }
        public string PrimitiveName { get; }
        public bool GenericInstance { get; }
        public IReadOnlyList<ResolvedType> Generics { get; } = new List<ResolvedType>();
        public ResolvedType ElementType { get; }

        public ResolvedType(TypeRef reference, ITypeData type, ResolvedType declaringType)
        {
            Reference = reference;
            Info = type.Info;
            Type = type;
            GenericParameter = false;
            Definition = type.This;
            DeclaringType = declaringType;
        }

        public ResolvedType(TypeRef reference, ITypeData type, ResolvedType declaringType, IReadOnlyList<ResolvedType> generics) : this(reference, type, declaringType)
        {
            Generics = generics;
        }

        public ResolvedType(TypeRef reference)
        {
            Reference = reference;
            GenericParameter = true;
        }

        public ResolvedType(TypeRef reference, ITypeData resolved, string alias, ResolvedType elementType)
        {
            Reference = reference;
            Primitive = true;
            Type = resolved;
            PrimitiveName = alias;
            ElementType = elementType;
        }

        private static string SafeName(TypeRef def)
        {
            return def.Name.Replace('<', '_').Replace('>', '_').Replace('`', '_').Replace("/", "::");
        }

        private static string SafeNamespace(TypeRef def)
        {
            if (!string.IsNullOrEmpty(def.Namespace))
                return def.Namespace?.Replace('<', '_').Replace('>', '_').Replace('`', '_').Replace('/', '_').Replace(".", "::");
            return NoNamespace;
        }

        // TODO: Figure out a clean (ish?) way of managing this without having to resolve everything and its mother
        // Probably could do something like:
        // - Make DeclaringType a ResolvedType
        // - Iterate over DeclaringType's Generics
        // YAY! (but I'm too tired to implement that atm, especially given the 20000 other compile errors I have created)
        private string GenericsToStr()
        {
            // If the TypeRef is a generic template, and our generic parameters aren't inherited from our declaring type
            if (Definition.IsGeneric)
                // If the TypeRef is not generic, return an empty string
                return string.Empty;
            var generics = Generics;
            if (Definition.IsGenericTemplate)
                // If we are a generic template, write out only any generic parameters that are not inherited from our parent.
                generics = generics.Except(DeclaringType.Generics).ToList();
            else if (Definition.IsGenericInstance)
                // If we are a generic instance, write out only our actual arguments. Any inherited from our parent should be skipped.
                generics = generics.Except(DeclaringType.Generics).ToList();
            if (generics.Count == 0)
                // If generics is ever empty, return an empty string
                return string.Empty;
            // Iterate over all the generic parameters, for each one, get its qualified TypeName, and append it to the string
            var str = "<";
            bool first = true;
            foreach (var g in generics)
            {
                if (!first)
                    str += ", ";
                else
                    first = false;
                str += g.GetTypeName();
            }
            return str + ">";
        }

        // TODO: ditto to above comment. Ideally we don't even need to recurse through our declaring types (but we can do so if that makes everyone happy)
        private string GetTypeName(ResolvedType type, bool generics = true)
        {
            if (type.GenericParameter)
                // If we are a generic parameter, return our name
                return type.Reference.Name;
            if (type.Primitive)
                // If we are a primitive type, we need to convert our name to a primitive name.
                // Also keep in mind, we don't actually include or forward declare anything. That's all left up to our callers.
                // If they want to use us as a primtive, they should be ready to include the necessary headers/make the correct FDs.
                return type.PrimitiveName;
            var name = SafeName(type.Definition);
            if (!generics || !type.Definition.IsGeneric)
                return name;

            var types = type.GenericsToStr();
            // Nothing left to do unless declaring type has additional generic args/params
            if (!type.Definition.DeclaringType?.IsGeneric == true)
                return name + types;

            int nestInd = name.LastIndexOf("::");
            if (nestInd >= 0)
                name = name.Substring(nestInd);
            return DeclaringType?.GetQualifiedTypeName(generics) + name + types;
        }

        public string GetNamespace() => !Primitive && !GenericParameter ? Definition.Namespace : null;

        public string GetTypeName(bool generics = true) => GetTypeName(this, generics);

        public string GetQualifiedTypeName(bool generics = true) => !Primitive && !GenericParameter ? SafeNamespace(Definition) + "::" + GetTypeName(generics) : GetTypeName(generics);

        public string GetIncludeLocation()
        {
            if (GenericParameter)
                throw new InvalidOperationException("Cannot get the include location of a generic parameter!");
            if (Primitive)
            {
                if (PrimitiveName.StartsWith("Array<") || PrimitiveName.StartsWith("Il2Cpp"))
                    // For now, just include all il2cpp types, no need to FD them (really)
                    return "utils/typedefs.h";
                else
                    return "";
            }
            var fileName = string.Join("-", string.Join("_", Definition.Name.Split('<', '>', '`', '/')).Split(Path.GetInvalidFileNameChars()));
            var directory = string.Join("-",
                (string.IsNullOrEmpty(Definition.Namespace) ? NoNamespace : string.Join("_", Definition.Namespace.Split('<', '>', '`', '/', '.')))
                .Split(Path.GetInvalidPathChars()));
            return $"{directory}/{fileName}.hpp";
        }

        public IReadOnlyList<string> GetIncludeLocations()
        {
            // Get current location, add our generic parameters to our locations
            var includes = new List<string>();
            var name = GetIncludeLocation();
            if (!string.IsNullOrEmpty(name))
                includes.Add(name);
            foreach (var g in Generics)
            {
                // Need to add an include to all the generics this type could have as well.
                if (!g.GenericParameter)
                    includes.AddRange(g.GetIncludeLocations());
            }
            return includes;
        }

        public bool Equals(ResolvedType other)
        {
            if (GenericParameter)
                return other.GenericParameter && Reference.Equals(other.Reference);
            if (Primitive)
                return other.Primitive && PrimitiveName == other.PrimitiveName;
            return Definition.Equals(other.Definition);
        }
    }
}