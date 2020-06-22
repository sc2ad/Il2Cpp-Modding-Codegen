using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    /// <summary>
    /// Serializes <see cref="CppTypeContext"/> objects
    /// This does so by including all definitions necessary, forward declaring all declarations necessary, and combining contexts.
    /// Configurable to avoid combining contexts (except for nested cases).
    /// </summary>
    public class CppContextSerializer
    {
        private ITypeCollection _collection;
        private Dictionary<CppTypeContext, (HashSet<CppTypeContext>, Dictionary<string, HashSet<TypeRef>>)> _headerContextMap = new Dictionary<CppTypeContext, (HashSet<CppTypeContext>, Dictionary<string, HashSet<TypeRef>>)>();
        private Dictionary<CppTypeContext, (HashSet<CppTypeContext>, Dictionary<string, HashSet<TypeRef>>)> _sourceContextMap = new Dictionary<CppTypeContext, (HashSet<CppTypeContext>, Dictionary<string, HashSet<TypeRef>>)>();
        private SerializationConfig _config;
        // Hold a type serializer to use for type serialization
        // We want to split up the type serialization into steps, managing nested types ourselves, instead of letting it do it.
        // Map contexts to CppTypeDataSerializers, one to one.
        private Dictionary<CppTypeContext, CppTypeDataSerializer> _typeSerializers = new Dictionary<CppTypeContext, CppTypeDataSerializer>();

        public CppContextSerializer(SerializationConfig config, ITypeCollection collection)
        {
            _config = config;
            _collection = collection;
        }

        /// <summary>
        /// Resolves the context using the provided map.
        /// Populates a mapping of this particular context to forward declares and includes.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        /// <param name="map"></param>
        public void Resolve(CppTypeContext context, Dictionary<ITypeData, CppTypeContext> map, bool asHeader)
        {
            CppTypeDataSerializer typeSerializer;
            if (!_typeSerializers.TryGetValue(context, out typeSerializer))
            {
                // TODO: Also add our type resolution here. We need to resolve all of our fields and method types for serialization of the type later
                typeSerializer = new CppTypeDataSerializer(_config);
                typeSerializer.Resolve(context, context.LocalType);
                _typeSerializers.Add(context, typeSerializer);
            }

            // After we have resolved all of our field and method types, we need to ensure our InPlace nested types get a type created for them as well.
            // Recursively Resolve our nested types. However, we may go out of order. We may need to double check to ensure correct resolution, among other things.
            foreach (var nested in context.NestedContexts)
                Resolve(nested, map, asHeader);

            if (context.LocalType.This.Name == "Object" && context.LocalType.This.Namespace == "UnityEngine")
                Console.WriteLine("Target resolve spotted.");

            var defsToGet = context.DefinitionsToGet;
            if (!asHeader)
            {
                context.Definitions.Remove(context.LocalType.This);
                defsToGet = new HashSet<TypeRef> { context.LocalType.This };
                defsToGet.UnionWith(context.Declarations);
            }
            var includes = new HashSet<CppTypeContext>();
            foreach (var td in defsToGet)
            {
                if (context.Definitions.Contains(td))
                    // If we have the definition already in our context, continue.
                    // This could be because it is literally ourselves, a nested type, or we included something
                    continue;
                var type = td.Resolve(_collection);
                // Add the resolved context's FileName to includes
                if (map.TryGetValue(type, out var value))
                {
                    includes.Add(value);
                    AddIncludeDefinitions(context, value.Definitions, asHeader);
                }
                else
                    throw new UnresolvedTypeException(context.LocalType.This, td);
            }
            var forwardDeclares = new Dictionary<string, HashSet<TypeRef>>();
            if (asHeader)
            {
                // Remove ourselves from our required declarations (faster than checked for each addition)
                context.Declarations.Remove(context.LocalType.This);
                foreach (var td in context.Declarations)
                {
                    // Stratify by namespace
                    var ns = td.GetNamespace();
                    if (forwardDeclares.TryGetValue(ns, out var set))
                        set.Add(td);
                    else
                        forwardDeclares.Add(ns, new HashSet<TypeRef> { td });
                }
                _headerContextMap.Add(context, (includes, forwardDeclares));
            }
            else _sourceContextMap.Add(context, (includes, forwardDeclares));
        }

        private void AddIncludeDefinitions(CppTypeContext context, HashSet<TypeRef> defs, bool asHeader)
        {
            foreach (var def in defs)
            {
                if (asHeader)
                {
                    if (def.Equals(context.LocalType.This))
                        // Cannot include something that includes us!
                        throw new InvalidOperationException($"Cannot add definition: {def} to context: {context.LocalType.This} because it is the same type!\nDefinitions to get: ({string.Join(", ", context.DefinitionsToGet.Select(d => d.GetQualifiedName()))})");
                    // TODO: Add an exception for attempting to include something that claims to define us
                    // TODO: Add an exception for attempting to include anything that isn't us that claims to define us
                }

                // Always add the definition (if we don't throw)
                context.Definitions.Add(def);
            }
        }

        private void WriteForwardDeclaration(CppStreamWriter writer, TypeRef resolved, ITypeData typeData)
        {
            var comment = "Forward declaring type: " + resolved.Name;
            if (resolved.IsGenericTemplate)
            {
                // If the type being resolved is generic, we must template it.
                var generics = "template<";
                bool first = true;
                foreach (var g in resolved.Generics)
                {
                    if (!first)
                        generics += ", ";
                    else
                        first = false;
                    generics += "typename " + g.GetName();
                }
                writer.WriteComment(comment + "<" + string.Join(", ", resolved.Generics.Select(tr => tr.GetName())) + ">");
                writer.WriteLine(generics + ">");
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
            {
                // Primitives include
                writer.WriteInclude("utils/typedefs.h");
                includesWritten.Add("utils/typedefs.h");
            }
            if (_config.OutputStyle == OutputStyle.Normal)
            {
                // Optional include
                writer.WriteLine("#include <optional>");
                includesWritten.Add("optional");
            }
            foreach (var include in defs)
            {
                writer.WriteComment("Including type: " + include.LocalType.This);
                // Using the FileName property of the include here will automatically use the lowest non-InPlace type
                var incl = include.HeaderFileName;
                if (!includesWritten.Contains(incl))
                {
                    writer.WriteInclude(incl);
                    includesWritten.Add(incl);
                }
                else
                    writer.WriteComment("Already included the same include: " + incl);
            }
            if (context.LocalType.This.Namespace == "System" && context.LocalType.This.Name == "ValueType")
            {
                // Special case for System.ValueType
                if (!includesWritten.Contains("System/Object.hpp"))
                {
                    writer.WriteInclude("System/Object.hpp");
                    includesWritten.Add("System/Object.hpp");
                }
            }
            // Overall il2cpp-utils include
            if (asHeader)
            {
                writer.WriteInclude("utils/il2cpp-utils.hpp");
                includesWritten.Add("utils/il2cpp-utils.hpp");
            }
            else
            {
                writer.WriteInclude("utils/utils.h");
                includesWritten.Add("utils/utils.h");
            }
            writer.WriteComment("Completed includes");
        }

        private void WriteDeclarations(CppStreamWriter writer, CppTypeContext context, Dictionary<string, HashSet<TypeRef>> declares)
        {
            // Write forward declarations
            writer.WriteComment("Begin forward declares");
            var completedFds = new HashSet<TypeRef>();
            foreach (var byNamespace in declares)
            {
                writer.WriteComment("Forward declaring namespace: " + byNamespace.Key);
                writer.WriteDefinition("namespace " + byNamespace.Key);
                foreach (var t in byNamespace.Value)
                {
                    var typeData = t.Resolve(_collection);
                    if (typeData is null)
                        throw new UnresolvedTypeException(context.LocalType.This, t);
                    var resolved = typeData.This;
                    if (context.Definitions.Contains(resolved))
                    {
                        // Write a comment saying "we have already included this"
                        writer.WriteComment("Skipping declaration: " + resolved.Name + " because it is already included!");
                        continue;
                    }
                    if (completedFds.Contains(resolved))
                        // If we have completed this reference already, continue.
                        continue;
                    if (resolved.DeclaringType != null && !resolved.DeclaringType.Equals(context.LocalType.This))
                        // If there are any nested types in declarations, the declaring type must be defined.
                        // If the declaration is a nested type that exists in the local type, then we will serialize it within the type itself.
                        // Thus, if this ever happens, it should not be a declaration.
                        throw new InvalidOperationException($"Type: {resolved} (declaring type: {resolved.DeclaringType} cannot be declared because it is a nested type! It should be defined instead!");
                    WriteForwardDeclaration(writer, resolved, typeData);
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
            var genericsDefined = nested.LocalType.This.GetDeclaredGenerics(false);
            if (nested.LocalType.This.IsGenericTemplate)
            {
                // If the type being resolved is generic, we must template it, iff we have generic parameters that aren't in genericsDefined
                var generics = string.Empty;
                bool first = true;
                foreach (var g in nested.LocalType.This.Generics)
                {
                    if (genericsDefined.Contains(g))
                        continue;
                    if (!first)
                        generics += ", ";
                    else
                        first = false;
                    generics += "typename " + g.GetName();
                }
                // Write the comment regardless
                writer.WriteComment(comment + "<" + string.Join(", ", nested.LocalType.This.Generics.Select(tr => tr.Name)) + ">");
                if (!string.IsNullOrEmpty(generics))
                    writer.WriteLine("template<" + generics + ">");
            }
            else
                writer.WriteComment(comment);
            writer.WriteDeclaration(typeStr + " " + nested.LocalType.This.GetName());
        }

        private void WriteIncludesAndDeclares(CppStreamWriter writer, CppTypeContext context, bool asHeader,
            Dictionary<CppTypeContext, (HashSet<CppTypeContext>, Dictionary<string, HashSet<TypeRef>>)> contextMap)
        {
            if (!contextMap.TryGetValue(context, out var defsAndDeclares))
                throw new InvalidOperationException("Must resolve context before attempting to serialize it! context: " + context);

            // Only write includes, declares if the type is InPlace = false, or has no declaring type
            if (!asHeader || !context.InPlace || context.DeclaringContext is null)
            {
                WriteIncludes(writer, context, defsAndDeclares.Item1, asHeader);
                WriteDeclarations(writer, context, defsAndDeclares.Item2);
            }
            else if (context.InPlace)
                writer.WriteComment("Is in-place?");
        }

        public void Serialize(CppStreamWriter writer, CppTypeContext context, bool asHeader)
        {

            if (asHeader)
                WriteIncludesAndDeclares(writer, context, asHeader, _headerContextMap);
            else
                WriteIncludesAndDeclares(writer, context, asHeader, _sourceContextMap);

            // We need to start by actually WRITING our type here. This include the first portion of our writing, including the header.
            // Then, we write our nested types (declared/defined as needed)
            // Then, we write our fields (static fields first)
            // And finally our methods
            if (!_typeSerializers.TryGetValue(context, out var typeSerializer))
                throw new InvalidOperationException($"Must have a valid {nameof(CppTypeDataSerializer)} for context type: {context.LocalType.This}!");

            // Only write the initial type and nested declares/definitions if we are a header
            if (asHeader)
            {
                // Write namespace
                writer.WriteComment("Type namespace: " + context.LocalType.This.Namespace);
                writer.WriteDefinition("namespace " + context.TypeNamespace);
                typeSerializer.WriteInitialTypeDefinition(writer, context.LocalType);

                // Now, we must also write all of the nested contexts of this particular context object that have InPlace = true
                // We want to recurse on this, writing the declarations for our types first, followed by our nested types
                // TODO: The nested types should be written in a dependency-resolved way (ex: nested type A uses B, B should be written before A)
                // Alternatively, we don't even NEED to NOT nest in place, we could always just nest in place anyways.
                foreach (var nested in context.NestedContexts)
                {
                    // Regardless of if the nested context is InPlace or not, we need to declare it within ourselves
                    AddNestedDeclare(writer, nested);
                }
                // After all nested contexts are completely declared, we write our nested contexts that have InPlace = true, in the correct ordering.
                foreach (var inPlace in context.NestedContexts.Where(nc => nc.InPlace))
                {
                    // Indent, create nested type definition
                    Serialize(writer, inPlace, true);
                }
            }
            // Fields may be converted to methods, so we handle writing these in non-header contexts just in case we need definitions of the methods
            typeSerializer.WriteFields(writer, context.LocalType, asHeader);
            // Method declarations are written in the header, definitions written when the body is needed.
            typeSerializer.WriteMethods(writer, context.LocalType, asHeader);
            writer.Flush();

            if (asHeader)
                typeSerializer.CloseDefinition(writer, context.LocalType);
        }
    }
}