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

        public CppContextSerializer(SerializationConfig config, ITypeCollection collection)
        {
            _config = config;
            _collection = collection;
        }

        private void AddIncludeDefinitions(CppSerializerContext context, HashSet<TypeRef> defs)
        {
            foreach (var def in defs)
            {
                if (context.Header) {
                    if (def.Equals(context.LocalType.This))
                        // Cannot include something that includes us!
                        throw new InvalidOperationException($"Cannot add definition: {def} to context: {context.LocalType.This} because it is the same type!\nDefinitions to get: ({string.Join(", ", context.DefinitionsToGet.Select(d => d.GetQualifiedName()))})");
                    else if (context.Declarations.Contains(def) && context.CouldNestHere(def))
                        // Panic time!
                        // Should not be adding a definition to a class that declares the same type!
                        throw new InvalidOperationException($"Cannot add definition: {def} to context: {context.LocalType.This} because context has a declaration of the same (nested) type!\nDefinitions to get: ({string.Join(", ", context.DefinitionsToGet.Select(d => d.GetQualifiedName()))})");
                }

                // Always add the definition (if we don't throw)
                context.Definitions.Add(def);
            }
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
            _contextMap.Add(context, (includes, forwardDeclares));
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

        public void Serialize(CppStreamWriter writer, CppSerializerContext context)
        {
            if (!_contextMap.TryGetValue(context, out var value))
                throw new InvalidOperationException("Must resolve context before attempting to serialize it! context: " + context);
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
            foreach (var item in value.Item1)
            {
                writer.WriteComment("Including type: " + item.LocalType.This);
                var incl = item.FileName + ".hpp";
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
            // Write forward declarations
            writer.WriteComment("Begin forward declares");
            var completedFds = new HashSet<TypeRef>();
            foreach (var pair in value.Item2)
            {
                writer.WriteComment("Forward declaring namespace: " + pair.Key);
                writer.WriteDefinition("namespace " + pair.Key);
                foreach (var t in pair.Value)
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
            writer.Flush();
        }
    }
}