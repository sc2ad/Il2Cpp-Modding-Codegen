using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.IO;
using System.Text;
using System.Linq;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppHeaderCreator
    {
        private SerializationConfig _config;
        private CppSerializerContext _context;

        public CppHeaderCreator(SerializationConfig config, CppSerializerContext context)
        {
            _config = config;
            _context = context;
        }

        private enum ForwardDeclareLevel
        {
            Global,
            Namespace,
            Class
        }

        private void WriteForwardDeclare(CppStreamWriter writer, TypeRef fd, ForwardDeclareLevel level)
        {
            var name = fd.Name;
            // TODO: handle this better?
            if (name == "Il2CppChar")  // cannot forward declare a primitive typedef without exactly copying typedef which is a bad idea
                throw new InvalidOperationException($"Should not be forward declaring: {name} type reference: {fd}!");

            string @namespace = string.IsNullOrEmpty(fd.Namespace) ? ResolvedType.NoNamespace : fd.Namespace;
            bool putNamespace = level == ForwardDeclareLevel.Global || !string.IsNullOrEmpty(@namespace);
            if (putNamespace)
                writer.WriteDeclaration("namespace " + @namespace);

            if (fd.Generics.Count > 0 || name.Contains("<"))
            {
                // TODO: Resolve the type for GenericParameters if IsGenericInstance?

                // If the forward declare is a generic template, we need to write an empty version of the template type instead
                if (fd.IsGenericTemplate == true)  // better to forward declare nothing than something invalid
                {
                    var generics = fd.Generics;
                    if (level >= ForwardDeclareLevel.Class && fd.DeclaringType != null && !fd.DeclaringType.IsGenericInstance)
                        generics = fd.Generics.Except(fd.DeclaringType.Generics, TypeRef.fastComparer).ToList();

                    if (generics.Count > 0)
                    {
                        var s = "template<";
                        for (int i = 0; i < generics.Count; i++)
                        {
                            s += "typename " + generics[i].Name;
                            if (i != generics.Count - 1)
                                s += ", ";
                        }
                        s += ">";
                        writer.WriteLine(s);
                    }

                    // Remove the <blah> from the name for the upcoming print
                    var genericStart = name.IndexOf("<");
                    if (genericStart >= 0)
                        name = name.Substring(0, genericStart);
                }
                else name = "";
            }

            if (level == ForwardDeclareLevel.Class && !string.IsNullOrEmpty(name))
            {
                var nestedStart = name.LastIndexOf("::");
                if (nestedStart >= 0)
                    name = name.Substring(nestedStart + 2);
            }

            // TODO write class instead if we did so for the definition
            if (name.Length > 0)
                writer.WriteDeclaration("struct " + name);
            else
            {
                var errorStr = $"Aborted forward declaration of {fd}";
                writer.WriteComment(errorStr);
                if (!fd.ToString().StartsWith("Array"))
                    Console.Error.WriteLine(errorStr);
            }

            if (putNamespace)
                writer.CloseDefinition();
        }

        private void WriteForwardDeclare(CppStreamWriter writer, ResolvedType fd, ForwardDeclareLevel level)
        {
            var name = fd.GetTypeName(false);
            // TODO: handle this better?
            if (name == "Il2CppChar")  // cannot forward declare a primitive typedef without exactly copying typedef which is a bad idea
                throw new InvalidOperationException($"Should not be forward declaring: {name} type reference: {fd.Reference}!");

            string @namespace = fd.GetNamespace();
            bool putNamespace = level == ForwardDeclareLevel.Global || !string.IsNullOrEmpty(@namespace);
            if (putNamespace)
                writer.WriteDeclaration("namespace " + @namespace);

            if (fd.Generics.Count > 0 || name.Contains("<"))
            {
                // TODO: Resolve the type for GenericParameters if IsGenericInstance?

                // If the forward declare is a generic template, we need to write an empty version of the template type instead
                if (fd.Definition?.IsGenericTemplate == true)  // better to forward declare nothing than something invalid
                {
                    var generics = fd.Definition.Generics;
                    if (level >= ForwardDeclareLevel.Class && fd.DeclaringType != null && !fd.DeclaringType.Definition.IsGenericInstance)
                        generics = fd.Definition.Generics.Except(fd.DeclaringType.Definition.Generics, TypeRef.fastComparer).ToList();

                    if (generics.Count > 0)
                    {
                        var s = "template<";
                        for (int i = 0; i < generics.Count; i++)
                        {
                            s += "typename " + generics[i].Name;
                            if (i != generics.Count - 1)
                                s += ", ";
                        }
                        s += ">";
                        writer.WriteLine(s);
                    }

                    // Remove the <blah> from the name for the upcoming print
                    var genericStart = name.IndexOf("<");
                    if (genericStart >= 0)
                        name = name.Substring(0, genericStart);
                }
                else name = "";
            }

            if (level == ForwardDeclareLevel.Class && !string.IsNullOrEmpty(name))
            {
                var nestedStart = name.LastIndexOf("::");
                if (nestedStart >= 0)
                {
                    name = name.Substring(nestedStart + 2);
                }
            }

            // TODO write class instead if we did so for the definition
            if (name.Length > 0)
                writer.WriteDeclaration("struct " + name);
            else
            {
                var errorStr = $"Aborted forward declaration of {fd.Reference}";
                writer.WriteComment(errorStr);
                if (!fd.Reference.ToString().StartsWith("Array"))
                    Console.Error.WriteLine(errorStr);
            }

            if (putNamespace)
                writer.CloseDefinition();
        }

        internal void WriteForwardDeclare(CppStreamWriter writer, ResolvedType fd)
        {
            WriteForwardDeclare(writer, fd, ForwardDeclareLevel.Class);
        }

        internal void WriteForwardDeclare(CppStreamWriter writer, TypeRef fd)
        {
            WriteForwardDeclare(writer, fd, ForwardDeclareLevel.Class);
        }

        public void Serialize(CppTypeDataSerializer serializer, ITypeData data)
        {
            var headerLocation = Path.Combine(_config.OutputDirectory, _config.OutputHeaderDirectory, _context.FileName) + ".hpp";
            Directory.CreateDirectory(Path.GetDirectoryName(headerLocation));
            using (var ms = new MemoryStream())
            {
                bool isSystemValueType = (data.This.Namespace == "System") && (data.This.Name == "ValueType");

                var rawWriter = new StreamWriter(ms);
                var writer = new CppStreamWriter(rawWriter, "  ");
                // Write header
                writer.WriteComment($"Autogenerated from {nameof(CppHeaderCreator)} on {DateTime.Now}");
                writer.WriteComment("Created by Sc2ad");
                writer.WriteComment("=========================================================================");
                writer.WriteLine("#pragma once");
                // TODO: determine when/if we need this
                writer.WriteLine("#pragma pack(push, 8)");
                // Write includes
                writer.WriteComment("Includes");
                writer.WriteLine("#include \"utils/il2cpp-utils.hpp\"");
                if (_config.OutputStyle == OutputStyle.Normal)
                    writer.WriteLine("#include <optional>");
                if (data.Type != TypeEnum.Interface)
                {
                    if (isSystemValueType)
                    {
                        var objectHeader = "System/Object.hpp";
                        if (!_context.Includes.Contains(objectHeader))
                            _context.Includes.Add(objectHeader);
                    }
                    foreach (var include in _context.Includes)
                        writer.WriteLine($"#include \"{include}\"");
                    writer.WriteComment("End Includes");
                    // Write forward declarations
                    if (_context.ForwardDeclares.Count > 0)
                    {
                        writer.WriteComment("Forward declarations");
                        foreach (var fd in _context.ForwardDeclares)
                            WriteForwardDeclare(writer, fd, ForwardDeclareLevel.Global);
                        writer.WriteComment("End Forward declarations");
                    }
                }
                // Write namespace
                writer.WriteDeclaration("namespace " + _context.TypeNamespace);
                writer.Flush();
                if (_context.NamespaceForwardDeclares.Count > 0)
                {
                    writer.WriteComment("Same-namespace forward declarations");
                    foreach (var fd in _context.NamespaceForwardDeclares)
                        WriteForwardDeclare(writer, fd, ForwardDeclareLevel.Namespace);
                    writer.WriteComment("End same-namespace forward declarations");
                }
                writer.Flush();
                // Write actual type
                try
                {
                    // TODO: use the indentWriter?
                    serializer.Serialize(writer, data, this, _context);
                }
                catch (UnresolvedTypeException e)
                {
                    if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                    {
                        writer.WriteComment("Unresolved type exception!");
                        writer.WriteLine("/*");
                        writer.WriteLine(e);
                        writer.WriteLine("*/");
                    }
                    else if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.SkipIssue)
                        return;
                    else if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.Elevate)
                        throw new InvalidOperationException($"Cannot elevate {e} to a parent type- there is no parent type!");
                }
                // End the namespace
                writer.CloseDefinition();

                if (isSystemValueType)
                {
                    writer.WriteLine("template<class T>");
                    writer.WriteLine("struct is_value_type<T, typename std::enable_if_t<std::is_base_of_v<System::ValueType, T>>> : std::true_type{};");
                }

                // DEFINE_IL2CPP_ARG_TYPE
                string arg0 = _context.QualifiedTypeName;
                string arg1 = "";
                if (data.Info.TypeFlags == TypeFlags.ReferenceType)
                    arg1 = "*";
                // For Name and Namespace here, we DO want all the `, /, etc
                if (!data.This.IsGeneric)
                    writer.WriteLine($"DEFINE_IL2CPP_ARG_TYPE({arg0 + arg1}, \"{data.This.Namespace}\", \"{data.This.Name}\");");
                else
                    writer.WriteLine($"DEFINE_IL2CPP_ARG_TYPE_GENERIC({arg0}, {arg1}, \"{data.This.Namespace}\", \"{data.This.Name}\");");

                writer.WriteLine("#pragma pack(pop)");
                writer.Flush();
                using (var fs = File.OpenWrite(headerLocation))
                {
                    rawWriter.BaseStream.Position = 0;
                    rawWriter.BaseStream.CopyTo(fs);
                }
            }
        }
    }
}