using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppTypeDataSerializer
    {
        internal class GenParamConstraintStrings : Dictionary<string, List<string>> { }

        internal struct State
        {
            internal string type;
            internal string? declaring;
            internal List<string?> parentNames;
            internal List<string?> unsafeParents;
            internal GenParamConstraintStrings genParamConstraints;
        }

        // Uses TypeRef instead of ITypeData because nested types have different pointers
        private readonly Dictionary<TypeRef, State> map = new Dictionary<TypeRef, State>();

        private CppFieldSerializer? _fieldSerializer;
        internal CppFieldSerializer FieldSerializer { get => _fieldSerializer ??= new CppFieldSerializer(_config); }
        private CppStaticFieldSerializer? _staticFieldSerializer;
        private CppStaticFieldSerializer StaticFieldSerializer { get => _staticFieldSerializer ??= new CppStaticFieldSerializer(_config); }
        private CppMethodSerializer? _methodSerializer;
        private CppMethodSerializer MethodSerializer { get => _methodSerializer ??= new CppMethodSerializer(_config, map); }
        private readonly SerializationConfig _config;

        internal CppTypeDataSerializer(SerializationConfig config)
        {
            _config = config;
        }

        internal void Resolve(CppTypeContext context, ITypeData type)
        {
            // Asking for ourselves as a definition will simply make things easier when resolving ourselves.
            var resolved = context.GetCppName(type.This, false, false, CppTypeContext.NeedAs.Definition, CppTypeContext.ForceAsType.Literal);
            if (resolved is null)
                throw new InvalidOperationException($"Could not resolve provided type: {type.This}!");

            State s = new State
            {
                type = resolved,
                declaring = null,
                parentNames = new List<string?>(),
                unsafeParents = new List<string?>(),
                genParamConstraints = new GenParamConstraintStrings()
            };
            if (string.IsNullOrEmpty(s.type))
            {
                Console.WriteLine($"{type.This.Name} -> {s.type}");
                throw new Exception("GetNameFromReference gave empty typeName");
            }

            // Duplicate interfaces be damned, parents MUST go first for reinterpret_cast and field offset validity!
            if (type.Parent != null)
                // System::ValueType should be the 1 type where we want to extend System::Object without the Il2CppObject fields
                // Technically, any field that has size 0 should be ignored...
                if (type.This.Namespace == "System" && type.This.Name == "ValueType")
                    s.unsafeParents.Add("System::Object");
                else
                    // TODO: just use type.Parent's QualifiedTypeName instead?
                    s.unsafeParents.Add(context.GetCppName(type.Parent, true, true, CppTypeContext.NeedAs.Definition, CppTypeContext.ForceAsType.Literal));

            if (type.This.DeclaringType != null && type.This.DeclaringType.IsGeneric)
            {
                s.declaring = context.GetCppName(type.This.DeclaringType, false, true, CppTypeContext.NeedAs.Definition);
                s.parentNames.Add("::il2cpp_utils::il2cpp_type_check::NestedType");
                // TODO: include type-check instead?
                context.EnableNeedIl2CppUtilsFunctionsInHeader();
            }
            // Not as simple as iterating over all interface methods. We actually need to create a type tree and remove all duplicates.
            // That is, if we explicitly implement a type that is already implemented within our set of interface types, we need to ignore it.
            // We offload this logic to context's construction (context.UniqueInterfaces)
            foreach (var @interface in context.UniqueInterfaces)
                s.unsafeParents.Add(context.GetCppName(@interface, true, true, CppTypeContext.NeedAs.Definition, CppTypeContext.ForceAsType.Literal));

            // TODO: actually move any interface that will add no duplicates? place them before the first parent?

            foreach (var g in type.This.Generics)
            {
                if (g.DeclaringType != type.This)
                    continue;
                var constraintStrs = g.GenericParameterConstraints.Select(c => context.GetCppName(c, true) ?? c.CppName()).ToList();
                if (constraintStrs.Count > 0)
                    s.genParamConstraints.Add(context.GetCppName(g, false) ?? g.CppName(), constraintStrs);
            }

            map.Add(type.This, s);

            if (type.Type != TypeEnum.Interface)
            {
                FieldSerializer.Initialize(type.InstanceFields, type.Methods);
                // do the non-static fields first
                foreach (var f in type.InstanceFields)
                    FieldSerializer.PreSerialize(context, f);
            }

            // then, the static fields
            foreach (var f in type.StaticFields)
                // If the field is a static field, we want to create two methods, (get and set for the static field)
                // and make a call to GetFieldValue and SetFieldValue for those methods
                StaticFieldSerializer.PreSerialize(context, f);

            // then the methods
            foreach (var m in type.Methods)
                MethodSerializer.PreSerialize(context, m);
        }

        internal void DuplicateDefinition(CppTypeContext self, TypeRef offendingType)
        {
            int total = 0;
            // If we ever have a duplicate definition, this should be called.
            // Here, we need to check to see if we failed because of a field, method, or both
            foreach (var f in self.LocalType.InstanceFields)
                if (f.Type.ContainsOrEquals(offendingType))
                    // If it was ever used as an instance field, we throw (this is unsolvable)
                    throw new InvalidOperationException($"Cannot fix duplicate definition for offending type: {offendingType}, it is used as field: {f} in: {self.LocalType.This}");
            foreach (var m in self.LocalType.Methods)
                // If it was simply a method, we iterate over all methods and attempt to template them
                // However, if we cannot fix it, this function will return false, informing us that we have failed.
                if (!MethodSerializer.FixBadDefinition(offendingType, m, out var found))
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
        internal void WriteInitialTypeDefinition(CppStreamWriter writer, ITypeData type, bool nestedInPlace, bool inheritParent)
        {
            if (!map.TryGetValue(type.This, out var state))
                throw new UnresolvedTypeException(type.This, type.This);
            // Write the actual type definition start
            var specifiers = "";
            foreach (var spec in type.Specifiers)
                specifiers += spec + " ";
            writer.WriteComment("Autogenerated type: " + specifiers + type.This);
            foreach (var a in type.Attributes)
            {
                writer.WriteComment($"[{a.Name}] Offset: {a.Offset:X}");
            }

            if (state.unsafeParents.Any() && inheritParent)
            {
                // Inherit parent
                state.parentNames.Add(state.unsafeParents[0]);
                state.unsafeParents.RemoveAt(0);
            }
            string s = "";
            if (state.parentNames.Any())
            {
                s = $" : ";
                bool first = true;
                foreach (var parent in state.parentNames)
                {
                    // We only inherit our parentNames, which is (at most) the nested type
                    if (!first)
                        s += ", ";
                    s += $"public {parent}";
                    first = false;
                }
            }
            if (state.unsafeParents.Any())
            {
                s += "/*";
                foreach (var parent in state.unsafeParents)
                    s += $", public {parent}";
                s += "*/";
            }

            if (type.This.IsGenericTemplate)
            {
                var genericStr = CppTypeContext.GetTemplateLine(type, nestedInPlace);
                if (!string.IsNullOrEmpty(genericStr))
                    writer.WriteLine(genericStr);
            }

            // TODO: print enums as actual C++ smart enums? backing type is type of _value and A = #, should work for the lines inside the enum
            // TODO: We need to specify generic declaring types with their generic parameters
            var typeName = state.type;
            if (nestedInPlace)
            {
                int idx = typeName.LastIndexOf("::");
                if (idx >= 0)
                    typeName = typeName.Substring(idx + 2);
            }
            writer.WriteDefinition(type.Type.TypeName() + " " + typeName + s);
            // TODO: debug is_complete
            // WriteGenericTypeConstraints(writer, state.genParamConstraints, true);

            writer.WriteLine("public:");
            //if (type.Info.Refness != Refness.ValueType)
            //{
            //    // Only write a constexpr constructor if we aren't a value type. Otherwise, it is handled by WriteSpecialCtors
            //    // Which will write a better version anyways.
            //    // Alternatively, we can make WriteSpecialCtors work for ALL types (ignoring ref-ness)
            //    writer.WriteComment("constexpr Constructor to ensure offset_of is valid.");
            //    int idxC = typeName.LastIndexOf("::");
            //    if (idxC >= 0)
            //        writer.WriteLine("constexpr " + typeName.Substring(idxC + 2) + "() = default;");
            //    else
            //        writer.WriteLine("constexpr " + typeName + "() = default;");
            //}
            if (state.declaring != null)
            {
                writer.WriteDeclaration($"using declaring_type = {state.declaring}");
                writer.WriteDeclaration($"static constexpr std::string_view NESTED_NAME = \"{typeName}\"");
            }

            writer.Flush();
        }

        private void WriteFields(CppStreamWriter writer, ITypeData type, bool asHeader, bool instanceFields)
        {
            var serializer = instanceFields ? (Serializer<IField>)FieldSerializer : StaticFieldSerializer;
            foreach (var f in instanceFields ? type.InstanceFields : type.StaticFields)
                try
                {
                    serializer.Serialize(writer, f, asHeader);
                }
                catch (UnresolvedTypeException e)
                {
                    if (_config.UnresolvedTypeExceptionHandling?.FieldHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                    {
                        writer.WriteLine("/*");
                        writer.WriteLine(e);
                        writer.WriteLine("*/");
                        writer.Flush();
                    }
                    else if (_config.UnresolvedTypeExceptionHandling?.FieldHandling == UnresolvedTypeExceptionHandling.Elevate)
                        throw;
                }
        }

        internal void WriteInstanceFields(CppStreamWriter writer, ITypeData type)
        {
            WriteFields(writer, type, true, true);
        }

        internal void WriteStaticFields(CppStreamWriter writer, ITypeData type, bool asHeader)
        {
            WriteFields(writer, type, asHeader, false);
        }

        internal void WriteSpecialCtors(CppStreamWriter writer, ITypeData type, bool isNested)
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
            MethodSerializer.WriteCtor(writer, FieldSerializer, type, typeName, true);
        }

        internal void WriteInterfaceConversionOperators(CppStreamWriter writer, ITypeData type)
        {
            // Write the special constructor
            var state = map[type.This];
            foreach (var @interface in state.unsafeParents)
                MethodSerializer.WriteInterfaceConversionOperator(writer, type, @interface);
        }

        internal void WriteConversionOperator(CppStreamWriter writer, CppTypeDataSerializer scopedSer, ITypeData type,
            FieldConversionOperator op, bool asHeader)
        {
            MethodSerializer.WriteConversionOperator(writer, scopedSer.FieldSerializer, type, op, asHeader);
        }

        // Iff namespaced, writes only the namespace-scoped methods. Otherwise, writes only the non-namespace-scoped methods.
        internal void WriteMethods(CppStreamWriter writer, ITypeData type, bool asHeader, bool namespaced = false)
        {
            foreach (var m in type.Methods)
                try
                {
                    if (namespaced == (MethodSerializer.Scope[m] == CppMethodSerializer.MethodScope.Namespace))
                        MethodSerializer.Serialize(writer, m, asHeader);
                }
                catch (UnresolvedTypeException e)
                {
                    if (_config.UnresolvedTypeExceptionHandling?.MethodHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                    {
                        writer.WriteLine("/*");
                        writer.WriteLine(e);
                        writer.WriteLine("*/");
                        writer.Flush();
                    }
                    else if (_config.UnresolvedTypeExceptionHandling?.MethodHandling == UnresolvedTypeExceptionHandling.Elevate)
                        throw;
                }
        }

        internal static void CloseDefinition(CppStreamWriter writer, ITypeData type) => writer.CloseDefinition($"; // {type.This}");

        internal static void WriteGenericTypeConstraints(CppStreamWriter writer, GenParamConstraintStrings generics, bool forTypeDef = false)
        {
            foreach (var p in generics)
            {
                IEnumerable<string>? constraintStrs;
                string ToConstraintString(string constraintType)
                {
                    string ret;
                    if (constraintType.TrimEnd('*') == "System::ValueType")
                        ret = $"is_value_type_v<{p.Key}>";
                    else if (Regex.IsMatch(constraintType, "::I[A-Z]")) // TODO: use actual interface checking
                        // note: because of the "inaccessible base" issue caused by lack of virtual inheritance, it may not be convertible to Iface*
                        ret = $"std::is_base_of_v<{constraintType.TrimEnd('*')}, std::remove_pointer_t<{p.Key}>>";
                    else
                        ret = $"std::is_convertible_v<{p.Key}, {constraintType}>";
                    if (forTypeDef)
                        ret = $"(!std::is_complete_v<std::remove_pointer_t<{p.Key}>> || {ret})";
                    return ret;
                }
                constraintStrs = p.Value.Select(ToConstraintString);
                writer.WriteDeclaration($"static_assert({string.Join(" && ", constraintStrs)})");
            }
        }
    }
}