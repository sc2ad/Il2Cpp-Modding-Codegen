﻿using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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

        public void Serialize(ISerializer<ITypeData> serializer, ITypeData data)
        {
            var headerLocation = Path.Combine(_config.OutputDirectory, _config.OutputHeaderDirectory, _context.FileName) + ".hpp";
            Directory.CreateDirectory(Path.GetDirectoryName(headerLocation));
            using (var ms = new MemoryStream())
            {
                var writer = new StreamWriter(ms);
                // Write header
                writer.WriteLine($"// Autogenerated from {nameof(CppHeaderCreator)} on {DateTime.Now}");
                writer.WriteLine($"// Created by Sc2ad");
                writer.WriteLine("// =========================================================================");
                writer.WriteLine("#pragma once");
                writer.WriteLine("#pragma pack(8)");
                // Write includes
                writer.WriteLine("// Includes");
                writer.WriteLine("#include \"utils/il2cpp-utils.hpp\"");
                foreach (var include in _context.Includes)
                {
                    writer.WriteLine($"#include \"{include}\"");
                }
                writer.WriteLine("// End Includes");
                // Write forward declarations
                writer.WriteLine("// Forward declarations");
                foreach (var fd in _context.ForwardDeclares)
                {
                    writer.WriteLine($"typedef struct {fd} {fd};");
                }
                writer.WriteLine("// End Forward declarations");
                writer.Flush();
                // Write actual type
                try
                {
                    serializer.Serialize(writer.BaseStream, data);
                }
                catch (UnresolvedTypeException e)
                {
                    writer.WriteLine("// Unresolved type exception!");
                    writer.WriteLine("/*");
                    writer.WriteLine(e);
                    writer.WriteLine("*/");
                }
                writer.WriteLine($"DEFINE_IL2CPP_ARG_TYPE({_context.TypeName}, \"{data.This.Namespace}\", \"{data.This.Name}\");");
                writer.Flush();
                using (var fs = File.OpenWrite(headerLocation))
                {
                    writer.BaseStream.Position = 0;
                    writer.BaseStream.CopyTo(fs);
                }
            }
        }
    }
}