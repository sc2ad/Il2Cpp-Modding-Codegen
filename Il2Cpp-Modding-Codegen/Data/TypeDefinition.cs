namespace Il2Cpp_Modding_Codegen.Data
{
    public class TypeDefinition
    {
        public string Namespace { get; internal set; }
        public string Name { get; internal set; }

        private ITypeData _resolvedType;

        internal TypeDefinition()
        {
        }

        public TypeDefinition(string qualifiedName)
        {
            int dotLocation = qualifiedName.IndexOf('.');
            if (dotLocation == -1)
            {
                Name = qualifiedName;
                Namespace = "";
            }
            else
            {
                Namespace = qualifiedName.Substring(0, dotLocation);
                Name = qualifiedName.Substring(dotLocation + 1);
            }
        }

        /// <summary>
        /// Resolves the type in the given context
        /// </summary>
        public void Resolve(ITypeContext context)
        {
            if (_resolvedType == null)
            {
                _resolvedType = context.Resolve(this);
            }
        }

        public bool IsPointer(ITypeContext context)
        {
            if (Name.EndsWith("*"))
            {
                return true;
            }
            // Resolve type, if type is not a value type, it is a pointer
            Resolve(context);
            return _resolvedType?.Info.TypeFlags == TypeFlags.ReferenceType;
        }

        public string SafeName()
        {
            return Name.Replace('<', '_').Replace('>', '_').Replace('.', '_');
        }

        public string SafeNamespace()
        {
            return Namespace.Replace('<', '_').Replace('>', '_').Replace('.', '_');
        }

        public string SafeFullName()
        {
            return SafeNamespace() + "_" + SafeName();
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Namespace))
                return $"{Namespace}.{Name}";
            return $"{Name}";
        }
    }
}