using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
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
        static HashSet<string> filesWritten = new HashSet<string>();
        private List<ITypeData> defineArgTypes = new List<ITypeData>();

        public CppHeaderCreator(SerializationConfig config, CppSerializerContext context)
        {
            _config = config;
            _context = context;
        }

        enum ForwardDeclareLevel
        {
            Global,
            Namespace,
            Class
        }
        private void WriteForwardDeclare(IndentedTextWriter writer, TypeName fd, ForwardDeclareLevel level)
        {
            // TODO: handle this better?
            if (fd.Name == "Il2CppChar")  // cannot forward declare a primitive typedef without exactly copying typedef which is a bad idea
                return;

            string @namespace = fd.ConvertTypeToNamespace();
            bool putNamespace = level == ForwardDeclareLevel.Global;
            if (@namespace is null) putNamespace = false;
            if (putNamespace)
            {
                writer.WriteLine($"namespace {@namespace} {{");
                writer.Indent++;
            }

            var name = fd.Name;
            if (fd.IsGeneric || name.Contains("<"))
            {
                // TODO: Resolve the type for GenericParameters if IsGenericInstance?

                // If the forward declare is a generic template, we need to write an empty version of the template type instead
                if (fd.IsGenericTemplate)  // better to forward declare nothing than something invalid
                {
                    var generics = fd.Generics;
                    if (level >= ForwardDeclareLevel.Class && fd.DeclaringType != null && !fd.DeclaringType.IsGenericInstance)
                        generics = fd.Generics.Except(fd.DeclaringType.Generics, TypeRef.fastComparer).ToList();

                    if (generics.Count > 0)
                    {
                        var s = "template<";
                        for (int i = 0; i < generics.Count; i++)
                        {
                            s += "typename " + generics[i].SafeName();
                            if (i != generics.Count - 1)
                                s += ", ";
                        }
                        s += ">";
                        writer.WriteLine(s);
                    }

                    // Remove the <blah> from the name for the upcoming print
                    var genericStart = name.IndexOf("<");
                    if (genericStart >= 0)
                    {
                        name = name.Substring(0, genericStart);
                    }
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
                writer.WriteLine($"struct {name};");
            else
            {
                var errorStr = $"Aborted forward declaration of {fd}";
                writer.WriteLine($"// {errorStr}");
                if (!fd.ToString().StartsWith("Array"))
                    Console.Error.WriteLine(errorStr);
            }

            if (putNamespace)
            {
                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        internal void WriteForwardDeclare(IndentedTextWriter writer, TypeName fd)
        {
            WriteForwardDeclare(writer, fd, ForwardDeclareLevel.Class);
        }

        internal void AddArgType(ITypeData type)
        {
            defineArgTypes.Insert(0, type);
        }

        public void Serialize(CppTypeDataSerializer serializer, ITypeData data)
        {
            var headerLocation = Path.Combine(_config.OutputDirectory, _config.OutputHeaderDirectory, _context.FileName) + ".hpp";
            Directory.CreateDirectory(Path.GetDirectoryName(headerLocation));
            if (filesWritten.Contains(headerLocation))
                throw new ArgumentException($"Type {data.This} tried to write to already written header {headerLocation}!");
            filesWritten.Add(headerLocation);
            using (var ms = new MemoryStream())
            {
                bool isSystemValueType = (data.This.Namespace == "System") && (data.This.Name == "ValueType");

                var rawWriter = new StreamWriter(ms);
                var writer = new IndentedTextWriter(rawWriter, "  ");
                // Write header
                writer.WriteLine($"// Autogenerated from {nameof(CppHeaderCreator)} on {DateTime.Now}");
                writer.WriteLine($"// Created by Sc2ad");
                writer.WriteLine("// =========================================================================");
                writer.WriteLine("#pragma once");
                // TODO: determine when/if we need this
                writer.WriteLine("#pragma pack(push, 8)");
                // Write includes
                writer.WriteLine("// Includes");
                writer.WriteLine("#include \"utils/il2cpp-utils.hpp\"");
                if (_config.OutputStyle == OutputStyle.Normal)
                    writer.WriteLine("#include <optional>");
                if (data.Type != TypeEnum.Interface)
                {
                    if (isSystemValueType) _context.Includes.Add("System/Object.hpp");
                    foreach (var include in _context.Includes)
                    {
                        writer.WriteLine($"#include \"{include}\"");
                    }
                    writer.WriteLine("// End Includes");
                    // Write forward declarations
                    if (_context.ForwardDeclares.Count > 0)
                    {
                        writer.WriteLine("// Forward declarations");
                        foreach (var fd in _context.ForwardDeclares)
                        {
                            WriteForwardDeclare(writer, fd, ForwardDeclareLevel.Global);
                        }
                        writer.WriteLine("// End Forward declarations");
                    }
                }
                // Write namespace
                writer.WriteLine("namespace " + _context.TypeNamespace + " {");
                writer.Indent++;
                writer.Flush();
                if (_context.NamespaceForwardDeclares.Count > 0)
                {
                    writer.WriteLine("// Same-namespace forward declarations");
                    foreach (var fd in _context.NamespaceForwardDeclares)
                    {
                        WriteForwardDeclare(writer, fd, ForwardDeclareLevel.Namespace);
                    }
                    writer.WriteLine("// End same-namespace forward declarations");
                }
                writer.Flush();
                // Write actual type
                try
                {
                    serializer.Serialize(writer, data, this);
                }
                catch (UnresolvedTypeException e)
                {
                    if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                    {
                        writer.WriteLine("// Unresolved type exception!");
                        writer.WriteLine("/*");
                        writer.WriteLine(e);
                        writer.WriteLine("*/");
                    }
                    else if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.SkipIssue)
                        return;
                    else if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.Elevate)
                        throw new InvalidOperationException($"Cannot elevate {e} to a parent type- there is no parent type!");
                }
                writer.Flush();
                // End the namespace
                writer.Indent--;
                writer.WriteLine("}");

                if (isSystemValueType)
                {
                    writer.WriteLine("template<class T>");
                    writer.WriteLine("struct is_value_type<T, typename std::enable_if_t<std::is_base_of_v<System::ValueType, T>>> : std::true_type{};");
                }

                // DEFINE_IL2CPP_ARG_TYPE
                foreach (var argType in defineArgTypes)
                {
                    string arg0 = _context.GetNameFromReference(argType.This, ForceAsType.Literal, genericArgs: false);
                    string arg1 = "";
                    if (argType.Info.TypeFlags == TypeFlags.ReferenceType)
                        arg1 = "*";
                    // For Name and Namespace here, we DO want all the `, /, etc
                    if (!argType.This.IsGeneric)
                        writer.WriteLine($"DEFINE_IL2CPP_ARG_TYPE({arg0 + arg1}, \"{argType.This.Namespace}\", \"{argType.This.Name}\");");
                    else
                        writer.WriteLine($"DEFINE_IL2CPP_ARG_TYPE_GENERIC({arg0}, {arg1}, \"{argType.This.Namespace}\", \"{argType.This.Name}\");");
                }

                writer.WriteLine("#pragma pack(pop)");
                writer.Flush();
                rawWriter.Flush();
                using (var fs = File.OpenWrite(headerLocation))
                {
                    rawWriter.BaseStream.Position = 0;
                    rawWriter.BaseStream.CopyTo(fs);
                }
            }
        }
    }
}