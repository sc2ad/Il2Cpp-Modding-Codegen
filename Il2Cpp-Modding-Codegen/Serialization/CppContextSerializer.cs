using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Data.DllHandling;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Serialization
{
    /// <summary>
    /// Serializes <see cref="CppTypeContext"/> objects
    /// This does so by including all definitions necessary, forward declaring all declarations necessary, and combining contexts.
    /// Configurable to avoid combining contexts (except for nested cases).
    /// </summary>
    public class CppContextSerializer
    {
        private readonly ITypeCollection _collection;

        private class ContextMap : Dictionary<CppTypeContext, (HashSet<CppTypeContext>, Dictionary<string, HashSet<TypeRef>>, HashSet<string>)>
        { }

        private readonly ContextMap _headerContextMap = new ContextMap();
        private readonly ContextMap _sourceContextMap = new ContextMap();
        private readonly SerializationConfig _config;

        // Hold a type serializer to use for type serialization
        // We want to split up the type serialization into steps, managing nested types ourselves, instead of letting it do it.
        // Map contexts to CppTypeDataSerializers, one to one.
        private readonly Dictionary<CppTypeContext, CppTypeDataSerializer> _typeSerializers = new Dictionary<CppTypeContext, CppTypeDataSerializer>();

        /// <summary>
        /// This event is invoked whenever a definition is defined at least twice in a single <see cref="CppTypeContext"/>
        /// This is usually due to including something that (indirectly or directly) ends up including the original type.
        /// Called with: this, current <see cref="CppTypeContext"/>, offending <see cref="TypeRef"/>
        /// </summary>
        internal event Action<CppContextSerializer, CppTypeContext, TypeRef>? DuplicateDefinition;

        internal CppContextSerializer(SerializationConfig config, ITypeCollection collection)
        {
            _config = config;
            _collection = collection;
        }

        //private void ForwardToTypeDataSerializer(CppTypeContext context, TypeRef offendingType)
        //{
        //    Console.Error.WriteLine("Forwarding to CppTypeDataSerializer!");
        //    // TODO: try the other way around? (doesn't work well with primitives as written)
        //    // TODO: if either is a primitive, try replacing it with the C++ primitive? how did they get included in the first place?
        //    //try {
        //    _typeSerializers[context].DuplicateDefinition(context, offendingType);
        //    //} catch {
        //    //    var resolved = offendingType.Resolve(_collection);
        //    //    if (resolved == null) throw new UnresolvedTypeException(context.LocalType.This, offendingType);
        //    //    var offendingContext = CppDataSerializer.TypeToContext[resolved];
        //    //    _typeSerializers[offendingContext].DuplicateDefinition(offendingContext, context.LocalType.This);
        //    //}
        //}

        /// <summary>
        /// Resolves the context using the provided map.
        /// Populates a mapping of this particular context to forward declares and includes.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        /// <param name="map"></param>
        private void Resolve(CppTypeContext context, Dictionary<ITypeData, CppTypeContext> map,
            bool asHeader, HashSet<CppTypeContext> stack)
        {
            if (!stack.Add(context))
                return;
            var _contextMap = asHeader ? _headerContextMap : _sourceContextMap;
            if (_contextMap.ContainsKey(context)) return;

            if (!_typeSerializers.ContainsKey(context))
            {
                // TODO: Also add our type resolution here. We need to resolve all of our fields and method types for serialization of the type later
                var typeSerializer = new CppTypeDataSerializer(_config);
                typeSerializer.Resolve(context, context.LocalType);
                _typeSerializers.Add(context, typeSerializer);
            }
            // Attempts to change context's set's before this point will fail!

            // Recursively Resolve our nested types. However, we may go out of order. We may need to double check to ensure correct resolution, among other things.
            foreach (var nested in context.NestedContexts)
                Resolve(nested, map, asHeader, stack);

            if (asHeader)
                context.AbsorbInPlaceNeeds();

            var includes = new HashSet<CppTypeContext>();

            // create (replaceable) aliases to the context collections
            var defs = context.Definitions;
            var defsToGet = context.DefinitionsToGet;
            if (!asHeader)
            {
                // Handle cpp file definitions in new sets so we don't lie to our future includers
                includes.AddOrThrow(context);
                defs = new HashSet<TypeRef>(context.Definitions);
                defsToGet = new HashSet<TypeRef>(context.DeclarationsToMake);
                defsToGet.UnionWith(context.Declarations);
            }

            // Make a copy of defsToGet, preprocessed as much as possible before the loop
            var typesToInclude = defsToGet.Where(td => !context.Definitions.Contains(td)).ToList();
            // Call Resolve recursively on each type we want to include
            foreach (var td in typesToInclude)
            {
                var resolved = td.Resolve(_collection);
                // Add the resolved context's HeaderFileName to includes
                if (resolved != null && map.TryGetValue(resolved, out var value))
                    // this may change defsToGet (if it makes us in-place, our DeclaringType will move from defsToGet to defs)
                    Resolve(value, map, asHeader, stack);
                else
                    throw new UnresolvedTypeException(context.LocalType.This, td);
            }
            // Now loop through the original and attempt to include what remains in it
            foreach (var td in defsToGet)
            {
                if (context.Definitions.Contains(td)) continue;
                var resolved = td.Resolve(_collection);
                var value = map[resolved!];  // any error should have fired in previous loop
                if (!AddIncludeDefinitions(context, defs, value, asHeader, includes))
                {
                    //ForwardToTypeDataSerializer(context, td);
                    DuplicateDefinition?.Invoke(this, context, td);
                }
                // No need to inherit declarations, since our own declarations should be all the types we need?
            }

            var forwardDeclares = new Dictionary<string, HashSet<TypeRef>>();
            if (asHeader)
            {
                // Remove ourselves from our required declarations (faster than checked for each addition)
                context.DeclarationsToMake.Remove(context.LocalType.This);
                foreach (var td in context.DeclarationsToMake)
                    // Stratify by namespace
                    forwardDeclares.GetOrAdd(td.CppNamespace()).Add(td);
            }

            var primitiveDeclares = asHeader ? context.PrimitiveDeclarations : new HashSet<string>();

            _contextMap.Add(context, (includes, forwardDeclares, primitiveDeclares));
        }

        internal void Resolve(CppTypeContext context, Dictionary<ITypeData, CppTypeContext> map, bool asHeader)
        {
            HashSet<CppTypeContext> stack = new HashSet<CppTypeContext>();
            Resolve(context, map, asHeader, stack);
        }

        // Returns whether the include is valid/has been made.
        private static bool AddIncludeDefinitions(CppTypeContext context, HashSet<TypeRef> defs, CppTypeContext newContext, bool asHeader,
            HashSet<CppTypeContext> includesOfType)
        {
            if (newContext != context && includesOfType.Contains(newContext)) return true;

            bool allGood = true;
            if (asHeader)
                foreach (var newDef in newContext.Definitions)
                    if (newDef.Equals(context.LocalType.This))
                    {
                        // Cannot include something that includes us!
                        // Invoke our DuplicateDefinition callback
                        // Optimally, we don't actually remove the problematic type from our includes
                        // Instead, we actually want to completely recalculate them AFTER the methods have been templated and hope that it doesn't exist.
                        // Not to mention that the cycle issue ocurring HERE is actually problematic-- we want to call DuplicateDefinition on the FIRST type
                        // that leads us down a recursive include chain. In fact, it should probably be one of our OWN includes.
                        // Ideally, this means that for a given type, if we find a cycle, we need to remove an include that our type was performing in order to fix it.
                        // TODO: Basically read this
                        allGood = false;
                        Console.Error.WriteLine($"Cannot add definition: {newDef} from context {newContext.LocalType.This} to context: {context.LocalType.This} because it is the same type!\nDefinitions to get: ({string.Join(", ", context.DefinitionsToGet.Select(d => d.GetQualifiedCppName()))})");
                        // DuplicateDefinition?.Invoke(this, context, newDef);
                    }
                    else if (context.HasInNestedHierarchy(newDef))
                    {
                        allGood = false;
                        // Cannot include something that claims to define our nested type!
                        Console.Error.WriteLine($"Cannot add definition: {newDef} from context {newContext.LocalType.This} to context: {context.LocalType.This} because it is a nested type of the context!\nDefinitions to get: ({string.Join(", ", context.DefinitionsToGet.Select(d => d.GetQualifiedCppName()))})");
                    }

            if (allGood)
            {
                defs.UnionWith(newContext.Definitions);
                includesOfType.Add(newContext);
                if (asHeader && newContext.NeedPrimitivesBeforeLateHeader)
                    context.EnableNeedPrimitivesBeforeLateHeader();
            }
            return allGood;
        }

        private static void WriteForwardDeclaration(CppStreamWriter writer, ITypeData typeData)
        {
            var resolved = typeData.This;
            var comment = "Forward declaring type: " + resolved.Name;
            if (resolved.IsGenericTemplate)
            {
                // If the type being resolved is generic, we must template it.
                var genericStr = CppTypeContext.GetTemplateLine(typeData);
                writer.WriteComment(comment + "<" + string.Join(", ", resolved.Generics.Select(tr => tr.CppName())) + ">");
                if (!string.IsNullOrEmpty(genericStr))
                    writer.WriteLine(genericStr);
            }
            else
                writer.WriteComment(comment);
            // Write forward declarations
            writer.WriteDeclaration(typeData.Type.TypeName() + " " + resolved.CppName());
        }

        private void WriteIncludes(CppStreamWriter writer, CppTypeContext context, HashSet<CppTypeContext> defs, bool asHeader)
        {
            // Write includes
            var includesWritten = new HashSet<string>();
            writer.WriteComment("Begin includes");
            if (asHeader ? context.NeedPrimitivesBeforeLateHeader : context.PrimitiveDeclarations.Count > 0)
            {
                // Primitives include

                if (includesWritten.Add("extern/beatsaber-hook/shared/utils/typedefs.h"))
                    writer.WriteInclude("extern/beatsaber-hook/shared/utils/typedefs.h");
            }
            else if (context.NeedStdint && asHeader && includesWritten.Add("stdint.h"))
                writer.WriteLine("#include <stdint.h>");

            if (asHeader)
            {
                if (context.NeedInitializerList)
                    // std::initializer_list include
                    if (includesWritten.Add("initializer_list"))
                        writer.WriteLine("#include <initializer_list>");

                if (_config.OutputStyle == OutputStyle.Normal)
                    // std::optional include
                    if (includesWritten.Add("optional"))
                        writer.WriteLine("#include <optional>");

                if (context.LocalType.This.Namespace == "System" && context.LocalType.This.Name == "ValueType")
                    // Special case for System.ValueType
                    if (includesWritten.Add("System/Object.hpp"))
                        writer.WriteInclude("System/Object.hpp");
                // Always include byref because it is so small
                if (includesWritten.Add("extern/beatsaber-hook/shared/utils/byref.hpp"))
                    writer.WriteInclude("extern/beatsaber-hook/shared/utils/byref.hpp");
            }

            // I don't know why, but this seems to be what we need for type completion in templates
            var isDescriptor = defs.ToLookup(c => c.LocalType.This.Name.EndsWith("Descriptor"));
            var includes = isDescriptor[true].ToList();
            includes.AddRange(isDescriptor[false]);
            foreach (var include in includes)
            {
                writer.WriteComment("Including type: " + include.LocalType.This);
                // Using the HeaderFileName property of the include here will automatically use the lowest non-InPlace type
                var incl = include.HeaderFileName;
                if (includesWritten.Add(incl))
                    writer.WriteInclude(incl);
                else
                    writer.WriteComment("Already included the same include: " + incl);
            }

            // Overall il2cpp-utils include
            if (!asHeader || context.NeedIl2CppUtilsFunctionsInHeader)
            {
                if (!asHeader)
                {
                    if (includesWritten.Add("extern/beatsaber-hook/shared/utils/il2cpp-utils.hpp"))
                        writer.WriteInclude("extern/beatsaber-hook/shared/utils/il2cpp-utils.hpp");
                }
                else
                {
                    if (includesWritten.Add("extern/beatsaber-hook/shared/utils/il2cpp-utils-methods.hpp"))
                        writer.WriteInclude("extern/beatsaber-hook/shared/utils/il2cpp-utils-methods.hpp");
                    if (includesWritten.Add("extern/beatsaber-hook/shared/utils/il2cpp-utils-properties.hpp"))
                        writer.WriteInclude("extern/beatsaber-hook/shared/utils/il2cpp-utils-properties.hpp");
                    if (includesWritten.Add("extern/beatsaber-hook/shared/utils/il2cpp-utils-fields.hpp"))
                        writer.WriteInclude("extern/beatsaber-hook/shared/utils/il2cpp-utils-fields.hpp");
                }
                if (includesWritten.Add("extern/beatsaber-hook/shared/utils/utils.h"))
                    writer.WriteInclude("extern/beatsaber-hook/shared/utils/utils.h");
            }
            writer.WriteComment("Completed includes");
        }

        private void WriteDeclarations(CppStreamWriter writer, CppTypeContext context, Dictionary<string, HashSet<TypeRef>> declares)
        {
            if (declares.Count <= 0) return;
            // Write forward declarations
            writer.WriteComment("Begin forward declares");
            foreach (var byNamespace in declares)
            {
                writer.WriteComment("Forward declaring namespace: " + byNamespace.Key);
                writer.WriteDefinition("namespace " + byNamespace.Key);
                foreach (var t in byNamespace.Value)
                {
                    var resolved = t.Resolve(_collection);
                    if (resolved is null)
                        throw new UnresolvedTypeException(context.LocalType.This, t);
                    var typeRef = resolved.This;
                    if (resolved != context.LocalType && context.Definitions.Contains(typeRef))
                    {
                        // Write a comment saying "we have already included this"
                        writer.WriteComment("Skipping declaration: " + typeRef.Name + " because it is already included!");
                        continue;
                    }
                    if (typeRef.DeclaringType != null)
                    {
                        if (!context.HasInNestedHierarchy(resolved))
                            // TODO: move this error to Resolve or earlier
                            // If there are any nested types in declarations, the declaring type must be defined.
                            // If the declaration is a nested type that exists in the local type, then we will serialize it within the type itself.
                            // Thus, if this ever happens, it should not be a declaration.
                            throw new InvalidOperationException($"Type: {typeRef} (declaring type: {typeRef.DeclaringType} cannot be declared by {context.LocalType.This} because it is a nested type! It should be defined instead!");
                        continue;  // don't namespace declare our own types
                    }
                    WriteForwardDeclaration(writer, resolved);
                }
                // Close namespace after all types in the same namespace have been FD'd
                writer.CloseDefinition();
            }
            writer.WriteComment("Completed forward declares");
        }

        private static void WritePrimitiveForwardDeclaration(CppStreamWriter writer, string primitive)
        {
            writer.WriteDeclaration("struct " + primitive);
        }

        private static void WritePrimitiveDeclarations(CppStreamWriter writer, CppTypeContext context, HashSet<string> declares)
        {
            if (declares.Count <= 0 || context.NeedPrimitivesBeforeLateHeader) return;
            // Write forward declarations
            writer.WriteComment("Begin il2cpp-utils forward declares");
            foreach (var primitive in declares)
                writer.WriteDeclaration(primitive);
            writer.WriteComment("Completed il2cpp-utils forward declares");
        }

        /// <summary>
        /// Write a declaration for the given <see cref="CppTypeContext"/> nested type
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="nested"></param>
        private static void AddNestedDeclare(CppStreamWriter writer, CppTypeContext nested)
        {
            var comment = "Nested type: " + nested.LocalType.This.GetQualifiedCppName();
            var typeStr = nested.LocalType.Type.TypeName();
            if (nested.LocalType.This.IsGenericTemplate)
            {
                var genericStr = nested.GetTemplateLine(true);
                // Write the comment regardless
                writer.WriteComment(comment + "<" + string.Join(", ", nested.LocalType.This.Generics.Select(tr => tr.Name)) + ">");
                // if (!string.IsNullOrEmpty(generics)) writer.WriteLine("template<" + generics + ">");
                if (!string.IsNullOrEmpty(genericStr))
                    writer.WriteLine(genericStr);
            }
            else
                writer.WriteComment(comment);
            writer.WriteDeclaration(typeStr + " " + nested.LocalType.This.CppName());
        }

        private void WriteNamespacedMethods(CppStreamWriter writer, CppTypeContext context, bool asHeader)
        {
            var typeSerializer = _typeSerializers[context];
            typeSerializer.WriteMethods(writer, context.LocalType, asHeader, true);
            foreach (var inPlace in context.NestedContexts.Where(nc => nc.InPlace))
                WriteNamespacedMethods(writer, inPlace, asHeader);
        }

        internal void WritePostSerializeMethods(CppStreamWriter writer, CppTypeContext context, bool asHeader)
        {
            if (!_typeSerializers.TryGetValue(context, out var typeSerializer))
                throw new InvalidOperationException($"Must have a valid {nameof(CppTypeDataSerializer)} for context type: {context.LocalType.This}!");
            if (!context.LocalType.This.IsGeneric && asHeader)
            {
                typeSerializer.WritePostSerializeMethods(writer);
            }
        }

        internal void Serialize(CppStreamWriter writer, CppTypeContext context, bool asHeader)
        {
            var contextMap = asHeader ? _headerContextMap : _sourceContextMap;
            if (!contextMap.TryGetValue(context, out var defsAndDeclares))
                throw new InvalidOperationException($"Must resolve context before attempting to serialize it! context for: {context.LocalType.This}");

            // Only write includes, declares for non-headers or if the type is InPlace = false, or has no declaring type
            if (!asHeader || !context.InPlace || context.DeclaringContext is null)
            {
                WriteIncludes(writer, context, defsAndDeclares.Item1, asHeader);
                if (asHeader)
                {
                    WriteDeclarations(writer, context, defsAndDeclares.Item2);
                    WritePrimitiveDeclarations(writer, context, defsAndDeclares.Item3);
                }
            }

            // We need to start by actually WRITING our type here. This includes the first portion of our writing, including the header.
            // Then, we write our nested types (declared/defined as needed)
            // Then, we write our fields (static fields first)
            // And finally our methods
            if (!_typeSerializers.TryGetValue(context, out var typeSerializer))
                throw new InvalidOperationException($"Must have a valid {nameof(CppTypeDataSerializer)} for context type: {context.LocalType.This}!");

            // Only write the initial type and nested declares/definitions if we are a header
            if (asHeader)
            {
                if (!context.InPlace)
                {
                    // Write namespace
                    writer.WriteComment("Type namespace: " + context.LocalType.This.Namespace);
                    writer.WriteDefinition("namespace " + context.TypeNamespace);
                }

                if (context.GetLocalSize() == -1)
                    writer.WriteComment("WARNING Size may be invalid!");
                else
                    writer.WriteComment($"Size: 0x{context.GetLocalSize():X}");

                if (context.GetLocalSize() != -1)
                    writer.WriteLine("#pragma pack(push, 1)");

                if (context.LocalType.Layout > ITypeData.LayoutKind.Auto)
                    writer.WriteComment($"WARNING Layout: {context.LocalType.Layout} may not be correctly taken into account!");

                typeSerializer.WriteInitialTypeDefinition(writer, context.LocalType, context.InPlace, context.BaseHasFields && context.LocalType.Type != TypeEnum.Interface);

                if (context.GetBaseSize() != -1)
                {
                    // Only when our parent is #pragma pack(push, 1) do we want to perform this.
                    // If we don't have a valid size, then we don't bother writing our base type
                    // We may need to add a padding field here for base type --> first instance field offset.
                    var firstField = context.LocalType.InstanceFields.FirstOrDefault();
                    // If we have an EXPLICIT layout, it doesn't matter if we have a non-zero base or not.
                    // In fact, it ALWAYS doesn't matter if we have a non-zero base, we should ensure it is fixed.
                    if (firstField is not null && firstField.Offset > 0 && firstField.Offset - context.GetBaseSize() != 0)
                    {
                        // If we have any fields that have a positive offset, we need to perform the math to create our padding.
                        writer.WriteComment($"Writing base type padding for base size: 0x{context.GetBaseSize():X} to desired offset: 0x{firstField.Offset:X}");
                        writer.WriteDeclaration($"char ___base_padding[0x{firstField.Offset - context.GetBaseSize():X}] = {{}}");
                    }
                }

                // Now, we must also write all of the nested contexts of this particular context object that have InPlace = true
                // We want to recurse on this, writing the declarations for our types first, followed by our nested types
                // TODO: The nested types should be written in a dependency-resolved way (ex: nested type A uses B, B should be written before A)
                // Alternatively, we don't even NEED to NOT nest in place, we could always just nest in place anyways.
                foreach (var nested in context.NestedContexts)
                    // Regardless of if the nested context is InPlace or not, we can declare it within ourselves
                    AddNestedDeclare(writer, nested);

                // After all nested contexts are completely declared, we write our nested contexts that have InPlace = true, in the correct ordering.
                foreach (var inPlace in context.NestedContexts.Where(nc => nc.InPlace))
                    // Indent, create nested type definition
                    Serialize(writer, inPlace, true);
            }

            // Write instance fields and special ctors, if this is a header
            if (asHeader)
            {
                typeSerializer.WriteInstanceFields(writer, context.LocalType);
                typeSerializer.WriteSpecialCtors(writer, context.LocalType, context.LocalType.This.DeclaringType != null);

                typeSerializer.WriteInterfaceConversionOperators(writer, context.LocalType);

                var op = context.SoloFieldConversionOperator;
                if (op.Field != null && op.Kind != ConversionOperatorKind.Inherited)
                {
                    var scopedSerializer = typeSerializer;
                    if (!op.Field.DeclaringType.Equals(context.LocalType.This))
                    {
                        // we may not have a name for this field type; ask the field's declaring type instead
                        var resolved = op.Field.DeclaringType.Resolve(_collection);
                        if (resolved is null) throw new UnresolvedTypeException(context.LocalType.This, op.Field.DeclaringType);
                        var resolvedContext = CppDataSerializer.TypeToContext[resolved];
                        if (!_typeSerializers.TryGetValue(resolvedContext, out scopedSerializer))
                            throw new InvalidOperationException($"Must have a valid {nameof(CppTypeDataSerializer)} for context type: {resolvedContext.LocalType.This}!");
                    }
                    typeSerializer.WriteConversionOperator(writer, scopedSerializer, context.LocalType, op, asHeader);
                }
            }

            // Static fields are converted to methods, so declarations are written in the header, definitions written when the body is needed.
            typeSerializer.WriteStaticFields(writer, context.LocalType, asHeader);
            typeSerializer.WriteInvokeFields(writer, context.LocalType, asHeader);

            // Method declarations are written in the header, definitions written when the body is needed.
            typeSerializer.WriteMethods(writer, context.LocalType, asHeader);
            writer.Flush();

            if (asHeader)
            {
                CppTypeDataSerializer.CloseDefinition(writer, context.LocalType);
                // Close packing here
                if (context.GetLocalSize() != -1)
                    writer.WriteLine("#pragma pack(pop)");
                // TODO: Check size of created type here
                if (context.LocalType.InstanceFields.Any(fi => fi.HasSize() && fi.Offset >= 0) && !context.LocalType.This.IsGeneric)
                {
                    var typeName = context.GetCppName(context.LocalType.This, false, false, CppTypeContext.NeedAs.Definition, CppTypeContext.ForceAsType.Literal);
                    //writer.WriteLine("#if defined(__clang__)");
                    //writer.WriteLine("#pragma clang diagnostic push");
                    //writer.WriteLine("#pragma clang diagnostic ignored \"-Winvalid-offsetof\"");
                    //writer.WriteLine("#else");
                    //writer.WriteLine("#pragma GCC diagnostic push");
                    //writer.WriteLine("#pragma GCC diagnostic ignored \"-Winvalid-offsetof\"");
                    //writer.WriteLine("#endif");
                    //foreach (var fi in context.LocalType.InstanceFields)
                    //{
                    //    if (fi.Offset >= 0 && fi.HasSize())
                    //        //  __{context.LocalType.This.CppNamespace().Replace("::", "_")}_{typeName?.Replace("::", "_")}_{fi.SafeFieldName(_config)}OffsetCheck"
                    //        writer.WriteDeclaration($"static_assert(offsetof({typeName}, {fi.SafeFieldName(_config)}) == {fi.Offset})");
                    //}
                    //writer.WriteLine("#if defined(__clang__)");
                    //writer.WriteLine("#pragma clang diagnostic pop");
                    //writer.WriteLine("#else");
                    //writer.WriteLine("#pragma GCC diagnostic pop");
                    //writer.WriteLine("#endif");

                    // TODO: We want the largest type of all field types that are at this offset
                    var f = context.LocalType.InstanceFields.LastOrDefault(fi => fi.HasSize());
                    if (f is not null && f.Offset >= 0)
                    {
                        // TODO: Check issues with final fields being unions
                        // Also need to account for padding
                        // Don't actually need size checks, since offset checks should cover everything feasible.
                        // Extra bytes don't really matter, assuming it doesn't impact any OTHER structure.
                        if (context.GetLocalSize() != -1)
                            // If we know the explicit size, we check it because we will be packed.
                            writer.WriteDeclaration($"static check_size<sizeof({typeName}), {f.Offset} + sizeof({context.GetCppName(f.Type, true)})> __{context.LocalType.This.CppNamespace().Replace("::", "_")}_{typeName?.Replace("::", "_")}SizeCheck");
                        else
                            writer.WriteComment("WARNING Not writing size check since size may be invalid!");
                        // For multiple fields, we need to ensure we are align 8
                        //writer.WriteDeclaration($"static check_size<sizeof({typeName}), ({f.Offset} + sizeof({context.GetCppName(f.Type, true)})) % 8 != 0 ? (8 - ({f.Offset} + sizeof({context.GetCppName(f.Type, true)})) % 8) + {f.Offset} + sizeof({context.GetCppName(f.Type, true)}) : {f.Offset} + sizeof({context.GetCppName(f.Type, true)})> __{context.LocalType.This.CppNamespace().Replace("::", "_")}_{typeName?.Replace("::", "_")}SizeCheck");
                    }
                    else
                    {
                        if (f is null)
                            writer.WriteComment($"Could not write field size check! There are no fields that have sizes! (they all have IgnoreAttribute)");
                        else
                            writer.WriteComment($"Could not write field size check! Last field: {f.Name} Offset: {f.Offset} is of type: {f.Type}");
                    }
                    var localTypeSize = context.GetLocalSize();
                    if (localTypeSize > 0)
                        writer.WriteDeclaration($"static_assert(sizeof({typeName}) == 0x{localTypeSize:X})");
                }
                else if (context.LocalType.This.IsGeneric)
                {
                    writer.WriteComment($"Could not write size check! Type: {context.LocalType.This} is generic, or has no fields that are valid for size checks!");
                }
            }
            if (!context.InPlace)
                WriteNamespacedMethods(writer, context, asHeader);
        }
    }
}
