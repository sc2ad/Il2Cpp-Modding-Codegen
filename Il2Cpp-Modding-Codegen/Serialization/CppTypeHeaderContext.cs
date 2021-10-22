using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppTypeHeaderContext
    {
        private readonly TypeDefinition type;
        private CppTypeHeaderContext rootContext;

        private CppTypeHeaderContext RootContext
        {
            get
            {
                while (rootContext.InPlace && rootContext.DeclaringContext != null)
                    rootContext = rootContext.DeclaringContext;
                return rootContext;
            }
        }

        public CppTypeHeaderContext(TypeDefinition t)
        {
            type = t;
        }

        internal CppTypeHeaderContext? DeclaringContext { get; private set; }
        internal string HeaderFileName => RootContext?.HeaderFileName ?? (GetIncludeLocation() + ".hpp");
        internal bool UnNested { get; private set; }
        internal bool InPlace { get; private set; }

        internal string TypeNamespace { get; }
        internal string TypeName { get; }
        internal string QualifiedTypeName { get; }

        private const string NoNamespace = "GlobalNamespace";

        private string CppNamespace() => string.IsNullOrEmpty(type.Namespace) ? NoNamespace : type.Namespace.Replace(".", "::");

        private string CppName()
        {
            if (type.Name.StartsWith("!"))
                throw new InvalidOperationException("Tried to get the name of a copied generic parameter!");
            var name = type.Name.Replace('`', '_').Replace('<', '$').Replace('>', '$');
            name = Utils.SafeName(name);
            if (UnNested)
            {
                if (DeclaringContext == null)
                    throw new NullReferenceException("DeclaringType was null despite UnNested being true!");
                name = DeclaringContext.CppName() + "_" + name;
            }
            return name;
        }

        private string GetIncludeLocation()
        {
            var fileName = string.Join("-", CppName().Split(Path.GetInvalidFileNameChars())).Replace('$', '-');
            if (DeclaringContext != null)
                return DeclaringContext.GetIncludeLocation() + "_" + fileName;
            // Splits multiple namespaces into nested directories
            var directory = string.Join("-", string.Join("/", CppNamespace().Split(new string[] { "::" }, StringSplitOptions.RemoveEmptyEntries)).Split(Path.GetInvalidPathChars()));
            return directory + "/" + fileName;
        }
    }
}