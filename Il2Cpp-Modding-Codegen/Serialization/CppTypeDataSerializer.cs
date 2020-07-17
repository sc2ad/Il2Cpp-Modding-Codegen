using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppTypeDataSerializer
    {
        private struct State
        {
            internal string type;
            internal string declaring;
            internal List<string> parentNames;
        }

        // Uses TypeRef instead of ITypeData because nested types have different pointers
        private Dictionary<TypeRef, State> map = new Dictionary<TypeRef, State>();

        private CppFieldSerializer fieldSerializer;
        private CppStaticFieldSerializer staticFieldSerializer;
        private CppMethodSerializer methodSerializer;
        private SerializationConfig _config;

        public CppTypeContext Context { get; private set; }

        public CppTypeDataSerializer(SerializationConfig config)
        {
            _config = config;
        }

        public void Resolve(CppTypeContext context, ITypeData type)
        {
            // Asking for ourselves as a definition will simply make things easier when resolving ourselves.
            var resolved = _config.SafeName(context.GetCppName(type.This, false, false, CppTypeContext.NeedAs.Definition, CppTypeContext.ForceAsType.Literal));
            if (resolved is null)
                throw new InvalidOperationException($"Could not resolve provided type: {type.This}!");

            State s = new State
            {
                type = resolved,
                declaring = null,
                parentNames = new List<string>()
            };
            if (string.IsNullOrEmpty(s.type))
            {
                Console.WriteLine($"{type.This.Name} -> {s.type}");
                throw new Exception("GetNameFromReference gave empty typeName");
            }

            if (type.Parent != null)
                // System::ValueType should be the 1 type where we want to extend System::Object without the Il2CppObject fields
                if (type.This.Namespace == "System" && type.This.Name == "ValueType")
                    s.parentNames.Add("System::Object");
                else
                    // TODO: just use type.Parent's QualifiedTypeName instead?
                    s.parentNames.Add(_config.SafeName(context.GetCppName(type.Parent, true, true, CppTypeContext.NeedAs.Definition, CppTypeContext.ForceAsType.Literal)));

            if (type.This.DeclaringType != null && type.This.DeclaringType.IsGeneric)
            {
                s.declaring = _config.SafeName(context.GetCppName(type.This.DeclaringType, false, true, CppTypeContext.NeedAs.Definition));
                s.parentNames.Add("::il2cpp_utils::il2cpp_type_check::NestedType");
            }

            foreach (var @interface in type.ImplementingInterfaces)
                s.parentNames.Add(_config.SafeName(context.GetCppName(@interface, true, true, CppTypeContext.NeedAs.Definition, CppTypeContext.ForceAsType.Literal)));
            map.Add(type.This, s);

            // TODO: if we upgrade to C# 8.0, change these to e.x. `fieldSerializer ??= new CppFieldSerializer(_config)`
            if (fieldSerializer is null)
                fieldSerializer = new CppFieldSerializer(_config);

            if (type.Type != TypeEnum.Interface)
                // do the non-static fields first
                foreach (var f in type.Fields)
                    if (!f.Specifiers.IsStatic())
                        fieldSerializer.PreSerialize(context, f);

            // then, the static fields
            foreach (var f in type.Fields)
                // If the field is a static field, we want to create two methods, (get and set for the static field)
                // and make a call to GetFieldValue and SetFieldValue for those methods
                if (f.Specifiers.IsStatic())
                {
                    if (staticFieldSerializer is null)
                        staticFieldSerializer = new CppStaticFieldSerializer(_config);
                    staticFieldSerializer.PreSerialize(context, f);
                }

            // then the methods
            if (methodSerializer is null)
                methodSerializer = new CppMethodSerializer(_config);
            foreach (var m in type.Methods)
                methodSerializer.PreSerialize(context, m);
        }

        public void DuplicateDefinition(CppTypeContext self, TypeRef offendingType)
        {
            int total = 0;
            // If we ever have a duplicate definition, this should be called.
            // Here, we need to check to see if we failed because of a field, method, or both
            foreach (var f in self.LocalType.Fields)
                if (f.Type.ContainsOrEquals(offendingType))
                    // If it was a field at all, we throw (this is unsolvable)
                    throw new InvalidOperationException($"Cannot fix duplicate definition for offending type: {offendingType}, it is used as field: {f} in: {self.LocalType.This}");
            foreach (var m in self.LocalType.Methods)
                // If it was simply a method, we iterate over all methods and attempt to template them
                // However, if we cannot fix it, this function will return false, informing us that we have failed.
                if (!methodSerializer.FixBadDefinition(offendingType, m, out var found))
                    throw new InvalidOperationException($"Cannot fix duplicate definition for offending type: {offendingType}, it is used in an unfixable method: {m}");
                else
                    total += found;

            if (total <= 0)
                throw new InvalidOperationException($"Failed to find any occurrences of offendingType {offendingType} in {self.LocalType.This}!");
            Console.WriteLine($"CppTypeDataSerializer has successfully replaced {total} occurrences of {offendingType} in {self.LocalType.This}!");
        }

        /// <summary>
        /// Writes the declaration for the <see cref="ITypeData"/> type.
        /// Should only be called in contexts where the writer is operating on a header.
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="type"></param>
        public void WriteInitialTypeDefinition(CppStreamWriter writer, ITypeData type, bool isNested)
        {
            if (!map.TryGetValue(type.This, out var state))
                throw new UnresolvedTypeException(type.This.DeclaringType, type.This);
            // Write the actual type definition start
            var specifiers = "";
            foreach (var spec in type.Specifiers)
                specifiers += spec + " ";
            writer.WriteComment("Autogenerated type: " + specifiers + type.This);

            string s = "";
            if (state.parentNames.Count > 0)
            {
                s = " : ";
                bool first = true;
                foreach (var parent in state.parentNames)
                {
                    if (!first)
                        s += ", ";
                    s += $"public {parent}";
                    first = false;
                }
            }

            if (type.This.IsGenericTemplate)
            {
                var genericStr = CppTypeContext.GetTemplateLine(type, isNested);
                if (!string.IsNullOrEmpty(genericStr))
                    writer.WriteLine(genericStr);
            }

            // TODO: print enums as actual C++ smart enums? backing type is type of _value and A = #, should work for the lines inside the enum
            // TODO: We need to specify generic declaring types with their generic parameters
            var typeName = state.type;
            if (isNested)
            {
                int idx = typeName.LastIndexOf("::");
                if (idx >= 0)
                    typeName = typeName.Substring(idx + 2);
            }
            writer.WriteDefinition(type.Type.TypeName() + " " + typeName + s);
            if (type.Fields.Count > 0 || type.Methods.Count > 0 || type.NestedTypes.Count > 0)
                writer.WriteLine("public:");
            if (state.declaring != null)
                writer.WriteLine($"using declaring_type = {state.declaring};");
            writer.Flush();
        }

        public void WriteFields(CppStreamWriter writer, ITypeData type, bool asHeader)
        {
            foreach (var f in type.Fields)
                try
                {
                    if (f.Specifiers.IsStatic())
                        staticFieldSerializer.Serialize(writer, f, asHeader);
                    else if (asHeader)
                        // Only write standard fields if this is a header
                        fieldSerializer.Serialize(writer, f, asHeader);
                }
                catch (UnresolvedTypeException e)
                {
                    if (_config.UnresolvedTypeExceptionHandling.FieldHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                    {
                        writer.WriteLine("/*");
                        writer.WriteLine(e);
                        writer.WriteLine("*/");
                        writer.Flush();
                    }
                    else if (_config.UnresolvedTypeExceptionHandling.FieldHandling == UnresolvedTypeExceptionHandling.Elevate)
                        throw;
                }
        }

        public void WriteSpecialCtors(CppStreamWriter writer, ITypeData type, bool isNested)
        {
            // Write the special constructor
            var state = map[type.This];
            var typeName = state.type;
            if (isNested)
            {
                int idx = typeName.LastIndexOf("::");
                if (idx >= 0)
                    typeName = typeName.Substring(idx + 2);
            }
            fieldSerializer?.WriteCtor(writer, type, typeName, true);
        }

        // Iff namespaced, writes only the namespace-scoped methods. Otherwise, writes only the non-namespace-scoped methods.
        public void WriteMethods(CppStreamWriter writer, ITypeData type, bool asHeader, bool namespaced = false)
        {
            foreach (var m in type.Methods)
                try
                {
                    if (namespaced == (methodSerializer.Scope[m] == CppMethodSerializer.MethodScope.Namespace))
                        methodSerializer?.Serialize(writer, m, asHeader);
                }
                catch (UnresolvedTypeException e)
                {
                    if (_config.UnresolvedTypeExceptionHandling.MethodHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                    {
                        writer.WriteLine("/*");
                        writer.WriteLine(e);
                        writer.WriteLine("*/");
                        writer.Flush();
                    }
                    else if (_config.UnresolvedTypeExceptionHandling.MethodHandling == UnresolvedTypeExceptionHandling.Elevate)
                        throw;
                }
        }

        public void CloseDefinition(CppStreamWriter writer, ITypeData type) => writer.CloseDefinition($"; // {type.This}");
    }
}
