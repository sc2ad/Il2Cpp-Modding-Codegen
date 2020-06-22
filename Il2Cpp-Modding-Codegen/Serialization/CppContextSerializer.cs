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
    /// Serializes <see cref="CppSerializerContext"/> objects
    /// This does so by including all definitions necessary, forward declaring all declarations necessary, and combining contexts.
    /// Configurable to avoid combining contexts (except for nested cases).
    /// </summary>
    public class CppContextSerializer
    {
        private ITypeCollection _collection;
        private Dictionary<CppSerializerContext, (HashSet<CppSerializerContext>, Dictionary<string, HashSet<TypeRef>>)> _contextMap = new Dictionary<CppSerializerContext, (HashSet<CppSerializerContext>, Dictionary<string, HashSet<TypeRef>>)>();
        private SerializationConfig _config;
        // Hold a type serializer to use for type serialization
        // We want to split up the type serialization into steps, managing nested types ourselves, instead of letting it do it.
        // Map contexts to CppTypeDataSerializers, one to one.
        private Dictionary<CppSerializerContext, CppTypeDataSerializer> _typeSerializers = new Dictionary<CppSerializerContext, CppTypeDataSerializer>();

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
        public void Resolve(CppSerializerContext context, Dictionary<ITypeData, CppSerializerContext> map)
        {
            if (_typeSerializers.ContainsKey(context))
                // Alternatively, we can simply return early.
                //throw new InvalidOperationException($"Should not have multiple type serializers with the exact same context! Matching value: {_typeSerializers[context]}");
                return;
            if (_contextMap.ContainsKey(context))
                //throw new InvalidOperationException($"Should not have multiple includes, forward declares with the exact same context! Matching value: {_contextMap[context]}");
                return;
            var includes = new HashSet<CppSerializerContext>();
            if (!context.Header)
            {
                includes.Add(context.HeaderContext);
                AddIncludeDefinitions(context, context.HeaderContext.Definitions);
            }
            foreach (var td in context.DefinitionsToGet)
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
                    AddIncludeDefinitions(context, value.Definitions);
                }
                else
                    throw new UnresolvedTypeException(context.LocalType.This, td);
            }
            var forwardDeclares = new Dictionary<string, HashSet<TypeRef>>();
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
            // TODO: Also add our type resolution here. We need to resolve all of our fields and method types for serialization of the type later
            var typeSerializer = new CppTypeDataSerializer(_config);
            typeSerializer.Resolve(context, context.LocalType);
            // After we have resolved all of our field and method types, we need to ensure our InPlace nested types get a type created for them as well.
            // Recursively Resolve our nested types. However, we may go out of order. We may need to double check to ensure correct resolution, among other things.
            foreach (var nested in context.NestedContexts)
                Resolve(nested, map);
            _typeSerializers.Add(context, typeSerializer);
            _contextMap.Add(context, (includes, forwardDeclares));
        }

        private void AddIncludeDefinitions(CppSerializerContext context, HashSet<TypeRef> defs)
        {
            foreach (var def in defs)
            {
                if (context.Header)
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

        private void WriteIncludes(CppStreamWriter writer, CppSerializerContext context, HashSet<CppSerializerContext> defs)
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
                var incl = include.FileName + ".hpp";
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
            if (context.Header)
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

        private void WriteDeclarations(CppStreamWriter writer, CppSerializerContext context, Dictionary<string, HashSet<TypeRef>> declares)
        {
            // Write forward declarations
            writer.WriteComment("Begin forward declares");
            var completedFds = new HashSet<TypeRef>();
            foreach (var fd in declares)
            {
                writer.WriteComment("Forward declaring namespace: " + fd.Key);
                writer.WriteDefinition("namespace " + fd.Key);
                foreach (var t in fd.Value)
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
        /// Write a declaration for the given <see cref="CppSerializerContext"/> nested type
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="nested"></param>
        private void AddNestedDeclare(CppStreamWriter writer, CppSerializerContext nested)
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

        public void Serialize(CppStreamWriter writer, CppSerializerContext context)
        {
            if (!_contextMap.TryGetValue(context, out var defsAndDeclares))
                throw new InvalidOperationException("Must resolve context before attempting to serialize it! context: " + context);
            // Only write includes, declares if the type is InPlace = false, or has no declaring type
            if (!context.InPlace)
            {
                WriteIncludes(writer, context, defsAndDeclares.Item1);
                WriteDeclarations(writer, context, defsAndDeclares.Item2);
            }
            // We need to start by actually WRITING our type here. This include the first portion of our writing, including the header.
            // Then, we write our nested types (declared/defined as needed)
            // Then, we write our fields (static fields first)
            // And finally our methods
            if (!_typeSerializers.TryGetValue(context, out var typeSerializer))
                throw new InvalidOperationException($"Must have a valid {nameof(CppTypeDataSerializer)} for context type: {context.LocalType.This}!");

            // Only write the initial type and nested declares/definitions if we are a header
            if (context.Header)
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
                    Serialize(writer, inPlace);
                }

            }
            // Fields may be converted to methods, so we handle writing these in non-header contexts just in case we need definitions of the methods
            typeSerializer.WriteFields(writer, context.LocalType, context.Header);
            // Method declarations are written in the header, definitions written when the body is needed.
            typeSerializer.WriteMethods(writer, context.LocalType, context.Header);
            writer.Flush();
        }
    }
}