using Il2CppModdingCodegen.Serialization.Interfaces;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppTypeHeaderContext : CppContext
    {
        internal string HeaderFileName => (RootContext as CppTypeHeaderContext)?.HeaderFileName ?? (GetIncludeLocation() + ".hpp");
        internal bool UnNested { get; private set; }

        internal string TypeNamespace { get; }
        internal string TypeName { get; }
        internal string QualifiedTypeName { get; }

        internal int BaseSize { get; }

        private readonly IEnumerable<ISerializer<TypeDefinition>> serializers;

        public CppTypeHeaderContext(TypeDefinition t, SizeTracker sz, IEnumerable<ISerializer<TypeDefinition>> serializers, CppTypeHeaderContext? declaring = null) : base(t, declaring)
        {
            if (sz is null)
                throw new ArgumentNullException(nameof(sz));
            TypeNamespace = CppNamespace(t);
            TypeName = CppName(t);

            // Determine whether this type has a base type that has size or not.
            BaseSize = sz.GetSize(t?.BaseType.Resolve()!);

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
            foreach (var s in serializers)
            {
                s.Resolve(Type);
            }
        }
    }
}