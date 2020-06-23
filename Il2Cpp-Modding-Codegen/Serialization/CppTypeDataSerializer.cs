using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Serialization;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppTypeDataSerializer
    {
        private struct State
        {
            internal string type;
            internal List<string> parentNames;
        }

        // Uses TypeRef instead of ITypeData because nested types have different pointers
        private Dictionary<TypeRef, State> map = new Dictionary<TypeRef, State>();

        private CppFieldSerializer fieldSerializer;
        private CppStaticFieldSerializer staticFieldSerializer;
        private CppMethodSerializer methodSerializer;
        private SerializationConfig _config;

        public CppTypeContext Context { get; private set; }

        public CppTypeDataSerializer(SerializationConfig config)
        {
            _config = config;
        }

        public void Resolve(CppTypeContext context, ITypeData type)
        {
            // Asking for ourselves as a definition will simply make things easier when resolving ourselves.
            var resolved = context.GetCppName(type.This, false, false, CppTypeContext.NeedAs.Definition, CppTypeContext.ForceAsType.Literal);
            if (resolved is null)
                throw new InvalidOperationException($"Could not resolve provided type: {type.This}!");

            State s = new State
            {
                type = resolved,
                parentNames = new List<string>()
            };
            if (string.IsNullOrEmpty(s.type))
            {
                Console.WriteLine($"{type.This.Name} -> {s.type}");
                throw new Exception("GetNameFromReference gave empty typeName");
            }
            if (type.Parent != null)
            {
                // System::ValueType should be the 1 type where we want to extend System::Object without the Il2CppObject fields
                if (type.This.Namespace == "System" && type.This.Name == "ValueType")
                    s.parentNames.Add("Object");
                else
                    // TODO: just use type.Parent's QualifiedTypeName instead?
                    s.parentNames.Add(context.GetCppName(type.Parent, true, true, CppTypeContext.NeedAs.Definition, CppTypeContext.ForceAsType.Literal));
            }
            foreach (var @interface in type.ImplementingInterfaces)
                s.parentNames.Add("virtual " + context.GetCppName(@interface, true, true, CppTypeContext.NeedAs.Definition, CppTypeContext.ForceAsType.Literal));
            map.Add(type.This, s);

            if (fieldSerializer is null)
                fieldSerializer = new CppFieldSerializer();

            if (type.Type != TypeEnum.Interface)
            {
                // do the non-static fields first
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
                            staticFieldSerializer = new CppStaticFieldSerializer(_config);
                        staticFieldSerializer.PreSerialize(context, f);
                    }
                }
            }

            // then the methods
            if (methodSerializer is null)
                methodSerializer = new CppMethodSerializer(_config);
            foreach (var m in type.Methods)
                methodSerializer?.PreSerialize(context, m);
        }

        // Should be provided a file, with all references resolved:
        // That means that everything is already either forward declared or included (with included files "to be built")
        // That is the responsibility of our parent serializer, who is responsible for converting the context into that
        /// <summary>
        /// Writes the declaration for the <see cref="ITypeData"/> type.
        /// Should only be called in contexts where the writer is operating on a header.
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="type"></param>
        public void WriteInitialTypeDefinition(CppStreamWriter writer, ITypeData type, bool isNested)
        {
            if (!map.TryGetValue(type.This, out var state))
                throw new UnresolvedTypeException(type.This.DeclaringType, type.This);
            // Write the actual type definition start
            var specifiers = "";
            foreach (var spec in type.Specifiers)
                specifiers += spec + " ";
            writer.WriteComment("Autogenerated type: " + specifiers + type.This);

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
                // Even if we are a template, we need to write out our inherited declaring types
                var generics = type.This.GetDeclaredGenerics(true);
                if (isNested)
                    generics = generics.Except(type.This.GetDeclaredGenerics(false), TypeRef.fastComparer);
                var declaredGenerics = generics.ToList();
                if (declaredGenerics.Count > 0)
                {
                    var templateStr = "template<";
                    bool first = true;
                    foreach (var genParam in declaredGenerics)
                    {
                        if (!first)
                            templateStr += ", ";
                        templateStr += "typename " + genParam.Name;
                        first = false;
                    }
                    writer.WriteLine(templateStr + ">");
                }
            }

            // TODO: print enums as actual C++ smart enums? backing type is type of _value and A = #, should work for the lines inside the enum
            // TODO: We need to specify generic declaring types with their generic parameters
            var typeName = state.type;
            if (isNested)
            {
                int idx = typeName.LastIndexOf("::");
                if (idx >= 0)
                    typeName = typeName.Substring(idx + 2);
            }
            writer.WriteDefinition(type.Type.TypeName() + " " + typeName + s);
            if (type.Fields.Count > 0 || type.Methods.Count > 0 || type.NestedTypes.Count > 0)
                writer.WriteLine("public:");
            writer.Flush();
        }

        public void WriteFields(CppStreamWriter writer, ITypeData type, bool asHeader)
        {
            if (type.Type == TypeEnum.Interface)
                // Don't write fields for interfaces
                return;
            // Write fields if not an interface
            foreach (var f in type.Fields)
            {
                try
                {
                    if (f.Specifiers.IsStatic())
                        staticFieldSerializer.Serialize(writer, f, asHeader);
                    else if (asHeader)
                        // Only write standard fields if this is a header
                        fieldSerializer.Serialize(writer, f, asHeader);
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

        public void WriteMethods(CppStreamWriter writer, ITypeData type, bool asHeader)
        {
            // Finally, we write the methods
            foreach (var m in type.Methods)
            {
                try
                {
                    methodSerializer?.Serialize(writer, m, asHeader);
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
        }

        public void CloseDefinition(CppStreamWriter writer, ITypeData type)
        {
            writer.CloseDefinition($"; // {type.This}");
        }
    }
}