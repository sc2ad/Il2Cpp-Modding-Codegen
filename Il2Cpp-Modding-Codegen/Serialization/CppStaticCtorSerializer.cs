using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using static Il2Cpp_Modding_Codegen.Serialization.CppTypeContext;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppStaticCtorSerializer : Serializer<IMethod>
    {
        private Dictionary<IMethod, List<(MethodTypeContainer container, ParameterFlags flags)>> _parameterMaps = new Dictionary<IMethod, List<(MethodTypeContainer, ParameterFlags)>>();
        private Dictionary<IMethod, SortedSet<string>> _genericArgs = new Dictionary<IMethod, SortedSet<string>>();

        private string _declaringFullyQualified;
        private string _thisTypeName;

        private SerializationConfig _config;

        public CppStaticCtorSerializer(SerializationConfig config)
        {
            _config = config;
        }

        private NeedAs NeedMethodAs(IMethod method)
        {
            if (DefinitionInHeader(method))
                return NeedAs.BestMatch;
            return NeedAs.Declaration;
        }

        private bool DefinitionInHeader(IMethod method) => method.DeclaringType.IsGenericTemplate;

        public bool FixBadDefinition(TypeRef offendingType, IMethod method, out int found)
        {
            // This method should be relatively straightforward:
            // First, check if we have a cycle with a nested type AND we don't want to write our content of our method (throw if we are a template type, for example)
            // Then, given this particular method, compute how many of our parameters are actually in a cycle (fun part!)
            // For each of those, create a template type, T1 --> TN that maps to it, ONLY IF WE ARE A HEADER!
            // Then, set some flags such that we write the following:
            // template<class T1, class T2, ...> ADJUSTED_RETURN original_name(ADJUSTED_PARAMETERS original_names);
            // And in .cpp:
            // template<>
            // original_return original_name(original_parameters original_names) { ... }
            Contract.Requires(_parameterMaps.ContainsKey(method));
            Contract.Requires(_parameterMaps[method].Count == method.Parameters.Count);

            found = 0;
            if (DefinitionInHeader(method))
                // Can't template a definition in a header.
                return false;

            // Use existing genericParameters if it already exists (a single method could have multiple offending types!)
            if (!_genericArgs.TryGetValue(method, out var genericParameters))
                genericParameters = new SortedSet<string>();
            // Ideally, all I should have to do here is iterate over my method, see if any params or return types match offending type, and template it if so
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                if (method.Parameters[i].Type.ContainsOrEquals(offendingType))
                {
                    var newName = "T" + i;
                    _parameterMaps[method][i].container.Template(newName);
                    genericParameters.Add(newName);
                }
            }

            found = genericParameters.Count;
            if (genericParameters.Count > 0)
                // Only add to dictionary if we actually HAVE the offending type somewhere.
                _genericArgs.Add(method, genericParameters);
            return true;
        }

        public override void PreSerialize(CppTypeContext context, IMethod method)
        {
            // Get the parameters for this method.
            // Because it is a constructor, the return type is void.
            if (!method.ReturnType.IsVoid())
                return;
            _declaringFullyQualified = context.QualifiedTypeName;
            // One that is qualified, the other that is not
            _thisTypeName = context.GetCppName(method.DeclaringType, false, needAs: NeedAs.Definition, forceAsType: ForceAsType.Literal);
            bool success = true;
            var needAs = NeedMethodAs(method);
            var parameterMap = new List<(MethodTypeContainer, ParameterFlags)>();
            foreach (var p in method.Parameters)
            {
                var s = context.GetCppName(p.Type, true, needAs: needAs);
                if (s is null)
                    // If we fail to resolve a parameter, we will simply add a (null, p.Flags) item to our mapping.
                    // However, we should not call Resolved(method)
                    success = false;
                parameterMap.Add((new MethodTypeContainer(s), p.Flags));
            }
            _parameterMaps.Add(method, parameterMap);
            if (success)
                Resolved(method);
        }

        public override void Serialize(CppStreamWriter writer, IMethod method, bool asHeader)
        {
            // Serialization is fairly straightforward, ASSUMING the method isn't templated.
            // If the method needs to be templated, we simply prefix with our template string (either in cpp or header)
            if (!_parameterMaps.TryGetValue(method, out var parameters))
                return;
            var val = parameters.FindIndex(s => s.container.TypeName(asHeader) is null);
            if (val != -1)
                throw new UnresolvedTypeException(method.DeclaringType, method.Parameters[val].Type);
            bool content = !asHeader || DefinitionInHeader(method);
            var paramString = method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], FormatParameterMode.Names | FormatParameterMode.Types, header: asHeader);
            if (!content)
            {
                // Write declaration, with comments
                writer.WriteComment($"Creates an object of type: {_declaringFullyQualified}* and calls the corresponding constructor.");
                if (_genericArgs.TryGetValue(method, out var genArgs))
                    writer.WriteLine($"template<{string.Join(", ", genArgs.Select(s => "class " + s))}>");
                // TODO: Ensure name does not conflict with any existing methods!
                writer.WriteDeclaration($"static {_thisTypeName}* New_ctor({paramString})");
            }
            else
            {
                writer.WriteComment("New_ctor implementation");
                var name = !asHeader ? _declaringFullyQualified : _thisTypeName;
                var methodPrefix = !asHeader ? _declaringFullyQualified + "::" : "";
                writer.WriteDefinition($"{name}* {methodPrefix}New_ctor({paramString})");
                var namePair = method.DeclaringType.GetIl2CppName();
                var newObject = $"il2cpp_functions::object_new(il2cpp_utils::GetClassFromName(\"{namePair.@namespace}\", \"{namePair.name}\"))";
                writer.WriteDeclaration($"auto* tmp = reinterpret_cast<{name}*>({newObject})");
                writer.WriteDeclaration($"tmp->_ctor({method.Parameters.FormatParameters(_config.IllegalNames, _parameterMaps[method], FormatParameterMode.Names, header: asHeader)})");
                writer.WriteDeclaration("return tmp");
                writer.CloseDefinition();
            }
        }
    }
}