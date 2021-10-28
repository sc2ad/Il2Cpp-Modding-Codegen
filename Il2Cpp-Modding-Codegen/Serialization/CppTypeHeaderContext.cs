using Il2CppModdingCodegen.CppSerialization;
using Il2CppModdingCodegen.Data.DllHandling;
using Il2CppModdingCodegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppTypeHeaderContext : CppContext
    {
        internal string HeaderFileName => (RootContext as CppTypeHeaderContext)?.HeaderFileName ?? (GetIncludeLocation() + ".hpp");

        internal string TypeNamespace { get; }
        internal string TypeName { get; }
        internal string QualifiedTypeName { get; }

        internal int BaseSize { get; private set; }

        // Error reporting
        /// <summary>
        /// This event is invoked whenever a definition is defined at least twice in a single <see cref="CppTypeHeaderContext"/>
        /// This is usually due to including something that (indirectly or directly) ends up including the original type.
        /// Called with: this, offending <see cref="CppTypeHeaderContext"/>, offending <see cref="TypeDefinition"/>
        /// </summary>
        public static event Action<CppTypeHeaderContext, CppTypeHeaderContext, TypeDefinition>? CyclicDefinition;

        /// <summary>
        /// This event is invoked whenever a definition is attempted to be included but is already nested.
        /// Called with: this, offending <see cref="CppTypeHeaderContext"/>, offending <see cref="TypeDefinition"/>
        /// </summary>
        public static event Action<CppTypeHeaderContext, CppTypeHeaderContext, TypeDefinition>? NestedDefinitionTwice;

        private readonly IEnumerable<ISerializer<TypeDefinition, CppStreamWriter>> serializers;

        public CppTypeHeaderContext(TypeDefinition t, SizeTracker sz, IEnumerable<ISerializer<TypeDefinition, CppStreamWriter>> serializers, CppTypeHeaderContext? declaring = null) : base(t, declaring)
        {
            if (sz is null)
                throw new ArgumentNullException(nameof(sz));
            TypeNamespace = CppNamespace(t);
            TypeName = CppName(t);

            // Create a hashset of all the unique interfaces implemented explicitly by this type.
            // Necessary for avoiding base ambiguity.
            //SetUniqueInterfaces(data);
            // Interfaces are currently not really handled reasonably anyways.
            // TODO: Properly handle interface operator conversions

            this.serializers = serializers;

            QualifiedTypeName = GetCppName(t!, true, true, NeedAs.Definition, ForceAsType.Literal)
                ?? throw new ArgumentException($"Input type cannot be unresolvable to a valid C++ name!");
        }

        private string GetIncludeLocation()
        {
            var fileName = string.Join("-", CppName(Type).Split(Path.GetInvalidFileNameChars())).Replace('$', '-');
            if (DeclaringContext != null)
                return (DeclaringContext as CppTypeHeaderContext)!.GetIncludeLocation() + "_" + fileName;
            // Splits multiple namespaces into nested directories
            var directory = string.Join("-", string.Join("/", CppNamespace(Type).Split(new string[] { "::" }, StringSplitOptions.RemoveEmptyEntries)).Split(Path.GetInvalidPathChars()));
            return directory + "/" + fileName;
        }

        public void Resolve()
        {
            Resolve(new HashSet<CppTypeHeaderContext>());
        }

        private void Resolve(HashSet<CppTypeHeaderContext> resolved)
        {
            if (!resolved.Add(this))
                return;
            foreach (var s in serializers)
            {
                s.Resolve(this, Type);
            }

            foreach (var n in NestedContexts)
            {
                (n as CppTypeHeaderContext)!.Resolve(resolved);
                if (n.InPlace)
                {
                    foreach (var dec in n.DeclarationsToMake.Except(n.Type.NestedTypes).Except(Definitions))
                    {
                        AddDeclaration(dec);
                    }
                    foreach (var def in n.DefinitionsToGet.Except(Definitions))
                    {
                        AddDefinition(def);
                    }
                    // Also combine our "special" includes
                    ExplicitIncludes.Union(n.ExplicitIncludes);
                }
            }

            // Combine usages here across nested contexts here
            var defsToGet = DefinitionsToGet.Where(t => !Definitions.Contains(t));
            foreach (var td in defsToGet)
            {
                Resolve(resolved);
            }
            foreach (var td in DefinitionsToGet)
            {
                if (Definitions.Contains(td))
                    continue;
                CppTypeHeaderContext ctx;
                lock (TypesToContexts)
                {
                    ctx = (TypesToContexts[td] as CppTypeHeaderContext)!;
                }
                var defs = new HashSet<TypeDefinition>(Definitions);
                var incls = new HashSet<CppTypeHeaderContext>(Includes);
                AddIncludeDefinitions(ctx, defs, incls);
                Definitions.UnionWith(defs);
                Includes.UnionWith(incls);
            }
            DeclarationsToMake.Remove(Type);
            foreach (var td in DeclarationsToMake)
            {
                ForwardDeclares.GetOrAdd(CppNamespace(td)).Add(td);
            }
        }

        public override void NeedIl2CppUtils()
        {
            ExplicitIncludes.Add("beatsaber-hook/shared/utils/il2cpp-utils-methods.hpp");
            ExplicitIncludes.Add("beatsaber-hook/shared/utils/il2cpp-utils-properties.hpp");
            ExplicitIncludes.Add("beatsaber-hook/shared/utils/il2cpp-utils-fields.hpp");
        }

        // For serialization
        private HashSet<CppTypeHeaderContext> Includes { get; } = new();

        private Dictionary<string, HashSet<TypeDefinition>> ForwardDeclares { get; } = new();

        private bool AddIncludeDefinitions(CppTypeHeaderContext context, HashSet<TypeDefinition> defs, HashSet<CppTypeHeaderContext> includes)
        {
            bool allGood = true;
            foreach (var d in new HashSet<TypeDefinition>(context.Definitions))
            {
                if (d == Type)
                {
                    // Cannot include something that includes us!
                    // Invoke our DuplicateDefinition callback
                    // Optimally, we don't actually remove the problematic type from our includes
                    // Instead, we actually want to completely recalculate them AFTER the methods have been templated and hope that it doesn't exist.
                    // Not to mention that the cycle issue ocurring HERE is actually problematic-- we want to call DuplicateDefinition on the FIRST type
                    // that leads us down a recursive include chain. In fact, it should probably be one of our OWN includes.
                    // Ideally, this means that for a given type, if we find a cycle, we need to remove an include that our type was performing in order to fix it.
                    // TODO: Basically read this
                    CyclicDefinition?.Invoke(this, context, d);
                    allGood = false;
                }
                else if (HasInNestedHierarchy(d, out var _))
                {
                    // It is invalid to include something that claims to define one of our nested types!
                    NestedDefinitionTwice?.Invoke(this, context, d);
                    allGood = false;
                }
            }

            if (allGood)
            {
                defs.UnionWith(context.Definitions);
                includes.Add(context);
            }
            return allGood;
        }

        private void AddContextualInclude(CppStreamWriter writer, CppTypeHeaderContext include, HashSet<string> includesWritten)
        {
            includesWritten.Add(include.HeaderFileName);
            // After each include, see what we may have unintentionally gotten
            foreach (var item in include.Includes)
            {
                // For each nested include, recurse and add them to the includes we have written.
                AddContextualInclude(writer, item, includesWritten);
            }
        }

        private static void WriteHeader(CppStreamWriter writer)
        {
            writer.WriteComment("Autogenerated code");
            writer.WriteComment("TODO: Add commit ID here :)");
            writer.WriteLine("#pragma once");
        }

        private void WriteIncludes(CppStreamWriter writer, HashSet<string> includesWritten)
        {
            writer.WriteComment("Begin includes");
            // So, we want to combine our includes such that we only write out what we need once.
            if (Type.Namespace == "System" && Type.Name == "ValueType")
            {
                if (includesWritten.Add("System/Object.hpp"))
                    writer.WriteInclude("System/Object.hpp");
            }
            writer.WriteComment("Including types");
            foreach (var include in Includes)
            {
                var incl = include.HeaderFileName;
                if (includesWritten.Add(incl))
                {
                    writer.WriteComment($"Including type: {include.Type}");
                    writer.WriteInclude(incl);
                }
                else
                    writer.WriteComment($"Already included include: {incl}");
                AddContextualInclude(writer, include, includesWritten);
            }
            writer.WriteComment("Explicit includes");
            foreach (var item in ExplicitIncludes)
            {
                if (includesWritten.Add(item))
                    writer.WriteLine(item);
                else
                    writer.WriteComment($"Already included explicit include: {item}");
            }
            writer.WriteComment("Completed includes");
        }

        private static void WriteForwardDeclaration(CppNamespaceWriter writer, TypeDefinition t)
        {
            var genStr = t.GetTemplateLine();
            var comment = $"Forward declaring type: {t.Name}";
            if (!string.IsNullOrEmpty(genStr))
            {
                writer.WriteComment(comment + $"<{string.Join(", ", t.GenericParameters.Select(g => g.Name))}>");
                writer.WriteLine(genStr);
            }
            else
                writer.WriteComment(comment);
            // Determine FD type here?
            // For now, we shall just assume "struct"
            writer.WriteDeclaration($"struct {CppName(t)}");
        }

        private void WriteDeclarations(CppStreamWriter writer)
        {
            if (ForwardDeclares.Count == 0)
                return;
            // TODO: Includes should apply to our list of definitions such that we don't FD something we have included
            // OR nested included, which would save quite a few lines.
            var res = ForwardDeclares.Where(kvp => kvp.Value.Except(Definitions).Any());
            if (res.Any())
                writer.WriteComment("Begin forward declares");
            foreach (var ns in res)
            {
                writer.WriteComment($"Forward declaring namespace: {ns.Key}");
                using var namespaceWriter = writer.OpenNamespace(ns.Key);
                foreach (var t in ns.Value)
                {
                    if (t.DeclaringType is not null)
                    {
                        if (!HasInNestedHierarchy(t, out var _))
                        {
                            // If there are any nested types in declarations, the declaring type must be defined.
                            // If the declaration is a nested type that exists in the local type, then we will serialize it within the type itself.
                            // Thus, if this ever happens, it should not be a declaration.
                            throw new InvalidOperationException($"Type: {t} (declaring: {t.DeclaringType} cannot be declared (because it is nested), it should be defined instead!");
                        }
                        continue;
                    }
                    WriteForwardDeclaration(namespaceWriter, t);
                }
            }
            writer.WriteComment("Completed forward declares");
        }

        private void WritePrimitiveDeclarations(CppStreamWriter writer)
        {
            if (PrimitiveDeclarations.Count == 0 || ExplicitIncludes.Contains(typedefsInclude))
                return;
            writer.WriteComment("Begin il2cpp-utils forward declares");
            foreach (var p in PrimitiveDeclarations)
                writer.WriteDeclaration(p);
            writer.WriteComment("End il2cpp-utils forward declares");
        }

        public void Write(string fullHeaderOutputPath)
        {
            if (!Directory.Exists(fullHeaderOutputPath))
            {
                Directory.CreateDirectory(fullHeaderOutputPath);
            }
            var path = Path.Combine(fullHeaderOutputPath, HeaderFileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            using var fs = File.OpenWrite(path);
            using var sw = new StreamWriter(fs);
            using var cpp = new CppStreamWriter(sw);
            // Create and open a C++ writer at the include location
            WriteHeader(cpp);
            // Write our includes
            WriteIncludes(cpp, new());
            // Then our FDs
            WriteDeclarations(cpp);
            WritePrimitiveDeclarations(cpp);
            // Then pass our writer AS IS to our serializers
            // They will be responsible for opening/closing the type writers
            foreach (var s in serializers)
            {
                s.Write(cpp, Type);
            }
        }
    }
}