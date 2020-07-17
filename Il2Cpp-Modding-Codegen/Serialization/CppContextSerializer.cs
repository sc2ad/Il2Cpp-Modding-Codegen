using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    /// <summary>
    /// Serializes <see cref="CppTypeContext"/> objects
    /// This does so by including all definitions necessary, forward declaring all declarations necessary, and combining contexts.
    /// Configurable to avoid combining contexts (except for nested cases).
    /// </summary>
    public class CppContextSerializer
    {
        private readonly ITypeCollection _collection;
        private readonly Dictionary<CppTypeContext, (HashSet<CppTypeContext>, Dictionary<string, HashSet<TypeRef>>)> _headerContextMap = new Dictionary<CppTypeContext, (HashSet<CppTypeContext>, Dictionary<string, HashSet<TypeRef>>)>();
        private readonly Dictionary<CppTypeContext, (HashSet<CppTypeContext>, Dictionary<string, HashSet<TypeRef>>)> _sourceContextMap = new Dictionary<CppTypeContext, (HashSet<CppTypeContext>, Dictionary<string, HashSet<TypeRef>>)>();
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
        public event Action<CppContextSerializer, CppTypeContext, TypeRef> DuplicateDefinition;

        public CppContextSerializer(SerializationConfig config, ITypeCollection collection)
        {
            _config = config;
            _collection = collection;
        }

        private void ForwardToTypeDataSerializer(CppContextSerializer self, CppTypeContext context, TypeRef offendingType)
        {
            Console.Error.WriteLine("Forwarding to CppTypeDataSerializer!");
            _typeSerializers[context].DuplicateDefinition(context, offendingType);
        }

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
            if (stack.Contains(context)) return;
            stack.Add(context);
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
                includes.Add(context);
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
                if (map.TryGetValue(resolved, out var value))
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
                var value = map[resolved];  // any error should have fired in previous loop
                if (!AddIncludeDefinitions(context, defs, value, asHeader, includes))
                {
                    ForwardToTypeDataSerializer(this, context, td);
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
                {
                    // Stratify by namespace
                    var ns = td.GetNamespace();
                    if (forwardDeclares.TryGetValue(ns, out var set))
                        set.Add(td);
                    else
                        forwardDeclares.Add(ns, new HashSet<TypeRef> { td });
                }
            }
            _contextMap.Add(context, (includes, forwardDeclares));
        }

        public void Resolve(CppTypeContext context, Dictionary<ITypeData, CppTypeContext> map, bool asHeader)
        {
            HashSet<CppTypeContext> stack = new HashSet<CppTypeContext>();
            Resolve(context, map, asHeader, stack);
        }

        // Returns whether the include is valid/has been made.
        private bool AddIncludeDefinitions(CppTypeContext context, HashSet<TypeRef> defs, CppTypeContext newContext, bool asHeader, HashSet<CppTypeContext> includesOfType)
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
                        Console.Error.WriteLine($"Cannot add definition: {newDef} from context {newContext.LocalType.This} to context: {context.LocalType.This} because it is the same type!\nDefinitions to get: ({string.Join(", ", context.DefinitionsToGet.Select(d => d.GetQualifiedName()))})");
                        // DuplicateDefinition?.Invoke(this, context, newDef);
                    }
                    else if (context.HasInNestedHierarchy(newDef))
                    {
                        allGood = false;
                        // Cannot include something that claims to define our nested type!
                        Console.Error.WriteLine($"Cannot add definition: {newDef} from context {newContext.LocalType.This} to context: {context.LocalType.This} because it is a nested type of the context!\nDefinitions to get: ({string.Join(", ", context.DefinitionsToGet.Select(d => d.GetQualifiedName()))})");
                    }

            if (allGood)
            {
                defs.UnionWith(newContext.Definitions);
                includesOfType.Add(newContext);
            }
            return allGood;
        }

        private void WriteForwardDeclaration(CppStreamWriter writer, ITypeData typeData)
        {
            var resolved = typeData.This;
            var comment = "Forward declaring type: " + resolved.Name;
            if (resolved.IsGenericTemplate)
            {
                // If the type being resolved is generic, we must template it.
                var genericStr = CppTypeContext.GetTemplateLine(typeData);
                writer.WriteComment(comment + "<" + string.Join(", ", resolved.Generics.Select(tr => tr.GetName())) + ">");
                if (!string.IsNullOrEmpty(genericStr))
                    writer.WriteLine(genericStr);
            }
            else
                writer.WriteComment(comment);
            // Write forward declarations
            writer.WriteDeclaration(typeData.Type.TypeName() + " " + resolved.GetName());
        }

        private void WriteIncludes(CppStreamWriter writer, CppTypeContext context, HashSet<CppTypeContext> defs, bool asHeader)
        {
            // Write includes
            var includesWritten = new HashSet<string>();
            writer.WriteComment("Begin includes");
            if (context.NeedPrimitives)
                // Primitives include
                if (includesWritten.Add("utils/typedefs.h"))
                    writer.WriteInclude("utils/typedefs.h");

            if (_config.OutputStyle == OutputStyle.Normal)
                // Optional include
                if (includesWritten.Add("optional"))
                    writer.WriteLine("#include <optional>");

            foreach (var include in defs)
            {
                writer.WriteComment("Including type: " + include.LocalType.This);
                // Using the HeaderFileName property of the include here will automatically use the lowest non-InPlace type
                var incl = include.HeaderFileName;
                if (includesWritten.Add(incl))
                    writer.WriteInclude(incl);
                else
                    writer.WriteComment("Already included the same include: " + incl);
            }
            if (context.LocalType.This.Namespace == "System" && context.LocalType.This.Name == "ValueType")
                // Special case for System.ValueType
                if (includesWritten.Add("System/Object.hpp"))
                    writer.WriteInclude("System/Object.hpp");

            // Overall il2cpp-utils include
            if (asHeader)
            {
                if (includesWritten.Add("utils/il2cpp-utils.hpp"))
                    writer.WriteInclude("utils/il2cpp-utils.hpp");
            }
            else if (includesWritten.Add("utils/utils.h"))
                writer.WriteInclude("utils/utils.h");
            writer.WriteComment("Completed includes");
        }

        private void WriteDeclarations(CppStreamWriter writer, CppTypeContext context, Dictionary<string, HashSet<TypeRef>> declares)
        {
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

        /// <summary>
        /// Write a declaration for the given <see cref="CppTypeContext"/> nested type
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="nested"></param>
        private void AddNestedDeclare(CppStreamWriter writer, CppTypeContext nested)
        {
            var comment = "Nested type: " + nested.LocalType.This.GetQualifiedName();
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
            writer.WriteDeclaration(typeStr + " " + nested.LocalType.This.GetName());
        }

        private void WriteNamespacedMethods(CppStreamWriter writer, CppTypeContext context, bool asHeader)
        {
            var typeSerializer = _typeSerializers[context];
            typeSerializer.WriteMethods(writer, context.LocalType, asHeader, true);
            foreach (var inPlace in context.NestedContexts.Where(nc => nc.InPlace))
                WriteNamespacedMethods(writer, inPlace, asHeader);
        }

        public void Serialize(CppStreamWriter writer, CppTypeContext context, bool asHeader)
        {
            var contextMap = asHeader ? _headerContextMap : _sourceContextMap;
            if (!contextMap.TryGetValue(context, out var defsAndDeclares))
                throw new InvalidOperationException("Must resolve context before attempting to serialize it! context: " + context);

            // Only write includes, declares for non-headers or if the type is InPlace = false, or has no declaring type
            if (!asHeader || !context.InPlace || context.DeclaringContext is null)
            {
                WriteIncludes(writer, context, defsAndDeclares.Item1, asHeader);
                if (asHeader) WriteDeclarations(writer, context, defsAndDeclares.Item2);
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

                typeSerializer.WriteInitialTypeDefinition(writer, context.LocalType, context.InPlace);

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
            // Fields may be converted to methods, so we handle writing these in non-header contexts just in case we need definitions of the methods
            typeSerializer.WriteFields(writer, context.LocalType, asHeader);
            // Write special ctors, if this is a header
            if (asHeader)
                typeSerializer.WriteSpecialCtors(writer, context.LocalType, context.LocalType.This.DeclaringType != null);
            // Method declarations are written in the header, definitions written when the body is needed.
            typeSerializer.WriteMethods(writer, context.LocalType, asHeader);
            writer.Flush();

            if (asHeader)
                typeSerializer.CloseDefinition(writer, context.LocalType);
            if (!context.InPlace)
                WriteNamespacedMethods(writer, context, asHeader);
        }
    }
}
