using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppTypeDataSerializer : ISerializer<ITypeData>
    {
        private bool _asHeader;

        private struct State
        {
            public string typeName;
            public List<string> parentNames;
        };

        private Dictionary<ITypeData, State> stateDict = new Dictionary<ITypeData, State>();
        private CppFieldSerializer fieldSerializer;
        private CppStaticFieldSerializer staticFieldSerializer;
        private CppMethodSerializer methodSerializer;
        private SerializationConfig _config;
        CppSerializerContext _context;

        public CppTypeDataSerializer(SerializationConfig config, bool asHeader = true)
        {
            _config = config;
            _asHeader = asHeader;
        }

        public void PreSerialize(ISerializerContext context, ITypeData type)
        {
            _context = context as CppSerializerContext;
            if (_asHeader)
            {
                var name = context.GetNameFromReference(type.This, ForceAsType.Literal, false, false);
                if (!type.GetsOwnHeader)
                {
                    int nestInd = name.LastIndexOf("::");
                    if (nestInd >= 0)
                        name = name.Substring(nestInd + 2);
                }

                State s = new State
                {
                    typeName = name,
                    parentNames = new List<string>()
                };
                if (string.IsNullOrEmpty(s.typeName))
                {
                    Console.WriteLine($"{type.This.Name} -> {s.typeName}");
                    throw new Exception("GetNameFromReference gave empty typeName");
                }
                if (type.Parent != null)
                {
                    // System::ValueType should be the 1 type where we want to extend System::Object without the Il2CppObject fields
                    if (_asHeader && type.This.Namespace == "System" && type.This.Name == "ValueType")
                        s.parentNames.Add("Object");
                    else
                        s.parentNames.Add(context.GetNameFromReference(type.Parent, ForceAsType.Literal, mayNeedComplete: true));
                }
                foreach (var @interface in type.ImplementingInterfaces)
                {
                    s.parentNames.Add("virtual " + context.GetNameFromReference(@interface, ForceAsType.Literal, mayNeedComplete: true));
                }
                stateDict[type] = s;

                if (fieldSerializer is null)
                    fieldSerializer = new CppFieldSerializer();
            }

            if (type.Type != TypeEnum.Interface)
            {
                // do the non-static fields first
                if (_asHeader)
                    foreach (var f in type.Fields)
                        if (!f.Specifiers.IsStatic())
                            fieldSerializer.PreSerialize(context, f);
                // then, the static fields
                foreach (var f in type.Fields)
                {
                    // If the field is a static field, we want to create two methods, (get and set for the static field)
                    // and make a call to GetFieldValue and SetFieldValue for those methods
                    if (f.Specifiers.IsStatic())
                    {
                        if (staticFieldSerializer is null)
                            staticFieldSerializer = new CppStaticFieldSerializer(_asHeader, _config);
                        staticFieldSerializer.PreSerialize(context, f);
                    }
                }
            }

            if (methodSerializer is null)
                methodSerializer = new CppMethodSerializer(_config, _asHeader);
            foreach (var m in type.Methods)
                methodSerializer?.PreSerialize(context, m);
            // TODO: Add a specific interface method serializer here, or provide more state to the original method serializer to support it

            // PreSerialize any in-place nested types
            // Until NestedInPlace no longer gains new children, copy all its elements and PreSerialize the new ones
            var prevInPlace = new HashSet<ITypeData>();
            var newInPlace = new HashSet<ITypeData>(type.NestedInPlace);
            do
            {
                foreach (var nested in newInPlace)
                    PreSerialize(context, nested);
                prevInPlace.UnionWith(newInPlace);
                newInPlace = new HashSet<ITypeData>(type.NestedInPlace.Except(prevInPlace));
            } while (newInPlace.Count > 0);
        }

        CppHeaderCreator _header;
        public void Serialize(IndentedTextWriter writer, ITypeData type, CppHeaderCreator header)
        {
            _header = header;
            Serialize(writer, type);
        }

        // Should be provided a file, with all references resolved:
        // That means that everything is already either forward declared or included (with included files "to be built")
        // That is the responsibility of our parent serializer, who is responsible for converting the context into that
        public void Serialize(IndentedTextWriter writer, ITypeData type)
        {
            // Populated only for headers; contains the e.g. `struct X` or `class Y` for type
            string typeHeader = "";
            if (_asHeader)
            {
                var state = stateDict[type];
                // Write the actual type definition start
                var specifiers = "";
                foreach (var spec in type.Specifiers)
                    specifiers += spec + " ";
                writer.WriteLine($"// Autogenerated type: {specifiers}{type.This}");

                string s = "";
                if (state.parentNames.Count > 0)
                {
                    s = " : ";
                    bool first = true;
                    foreach (var parent in state.parentNames)
                    {
                        if (!first)
                            s += ", ";
                        s += $"public {parent}";
                        first = false;
                    }
                }

                if (type.This.IsGenericTemplate)
                {
                    var generics = type.This.Generics;
                    if (!type.GetsOwnHeader)
                        generics = generics.Except(type.This.DeclaringType.Generics, TypeRef.fastComparer).ToList();
                    if (generics.Count > 0)
                    {
                        var templateStr = "template<";
                        bool first = true;
                        foreach (var genParam in generics)
                        {
                            if (!first) templateStr += ", ";
                            templateStr += "typename " + genParam.Name;
                            first = false;
                        }
                        writer.WriteLine(templateStr + ">");
                    }
                }

                // TODO: print enums as actual C++ smart enums? backing type is type of _value and A = #, should work for the lines inside the enum
                typeHeader = (type.Type == TypeEnum.Struct ? "struct " : "class ") + state.typeName;
                writer.WriteLine(typeHeader + s + " {");
                writer.WriteLine(" public:");
                writer.Indent++;
            }
            writer.Flush();

            // now write any in-place nested types (or for cpp, their methods)
            foreach (var nested in type.NestedInPlace)
                Serialize(writer, nested);

            if (_asHeader)
            {
                // write any nested forward declares
                if (_context.NestedForwardDeclares.Count > 0)
                {
                    writer.WriteLine("// Nested forward declarations");
                    foreach (var fd in _context.NestedForwardDeclares)
                        if (fd.GetsOwnHeader)
                            _header.WriteForwardDeclare(writer, fd);
                    writer.WriteLine("// End nested forward declarations");
                }
                writer.Flush();
            }

            if (type.Type != TypeEnum.Interface)
            {
                // Write fields if not an interface
                foreach (var f in type.Fields)
                {
                    try
                    {
                        if (f.Specifiers.IsStatic())
                            staticFieldSerializer.Serialize(writer, f);
                        else if (_asHeader)
                            // Only write standard fields if this is a header
                            fieldSerializer.Serialize(writer, f);
                    }
                    catch (UnresolvedTypeException e)
                    {
                        if (_config.UnresolvedTypeExceptionHandling.FieldHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                        {
                            writer.WriteLine("/*");
                            writer.WriteLine(e);
                            writer.WriteLine("*/");
                            writer.Flush();
                        }
                        else if (_config.UnresolvedTypeExceptionHandling.FieldHandling == UnresolvedTypeExceptionHandling.Elevate)
                            throw;
                    }
                }
            }

            // Finally, we write the methods
            foreach (var m in type.Methods)
            {
                try
                {
                    methodSerializer?.Serialize(writer, m);
                }
                catch (UnresolvedTypeException e)
                {
                    if (_config.UnresolvedTypeExceptionHandling.MethodHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                    {
                        writer.WriteLine("/*");
                        writer.WriteLine(e);
                        writer.WriteLine("*/");
                        writer.Flush();
                    }
                    else if (_config.UnresolvedTypeExceptionHandling.MethodHandling == UnresolvedTypeExceptionHandling.Elevate)
                        throw;
                }
            }

            // Write type closing "};"
            if (_asHeader)
            {
                writer.Indent--;
                writer.WriteLine($"}};  // {typeHeader}");
                _header.AddArgType(type);
            }
            writer.Flush();
        }
    }
}