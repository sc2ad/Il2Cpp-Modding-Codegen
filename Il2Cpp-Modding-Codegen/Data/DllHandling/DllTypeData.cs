using Il2CppModdingCodegen.Config;
using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    internal class DllTypeData : ITypeData
    {
        public TypeEnum Type { get; }
        public TypeInfo Info { get; }
        public TypeRef This { get; }
        public TypeRef Parent { get; }
        public HashSet<ITypeData> NestedTypes { get; } = new HashSet<ITypeData>();
        public List<TypeRef> ImplementingInterfaces { get; } = new List<TypeRef>();
        public int TypeDefIndex { get; }
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public List<IField> Fields { get; } = new List<IField>();
        public List<IProperty> Properties { get; } = new List<IProperty>();
        public List<IMethod> Methods { get; } = new List<IMethod>();

        private readonly DllConfig _config;

        internal DllTypeData(TypeDefinition def, DllConfig config)
        {
            _config = config;
            foreach (var i in def.Interfaces)
                ImplementingInterfaces.Add(DllTypeRef.From(i.InterfaceType));

            This = DllTypeRef.From(def);
            Type = def.IsEnum ? TypeEnum.Enum : (def.IsInterface ? TypeEnum.Interface : (def.IsValueType ? TypeEnum.Struct : TypeEnum.Class));
            Info = new TypeInfo
            {
                Refness = def.IsValueType ? Refness.ValueType : Refness.ReferenceType
            };

            if (def.BaseType != null)
                Parent = DllTypeRef.From(def.BaseType);

            // TODO: Parse this eventually
            TypeDefIndex = -1;

            if (_config.ParseTypeAttributes && def.HasCustomAttributes)
                Attributes.AddRange(def.CustomAttributes.Select(ca => new DllAttribute(ca)));
            if (_config.ParseTypeFields)
                Fields.AddRange(def.Fields.Select(f => new DllField(f)));
            if (_config.ParseTypeProperties)
                Properties.AddRange(def.Properties.Select(p => new DllProperty(p)));
            if (_config.ParseTypeMethods)
            {
                var mappedBaseMethods = new HashSet<MethodDefinition>();
                var methods = def.Methods.Select(m => DllMethod.From(m, ref mappedBaseMethods)).ToList();
                // It's important that Foo.IBar.func() goes after func() (if present)
                Methods.AddRange(methods.Where(m => m.ImplementedFrom is null));
                Methods.AddRange(methods.Where(m => m.ImplementedFrom != null));
            }
        }

        public override string ToString()
        {
            var s = $"// Namespace: {This.Namespace}\n";
            foreach (var attr in Attributes)
                s += $"{attr}\n";
            foreach (var spec in Specifiers)
                s += $"{spec} ";
            s += $"{Type.ToString().ToLower()} {This.Name}";
            if (Parent != null)
                s += $" : {Parent}";
            s += "\n{";
            if (Fields.Count > 0)
            {
                s += "\n\t// Fields\n\t";
                foreach (var f in Fields)
                    s += $"{f}\n\t";
            }
            if (Properties.Count > 0)
            {
                s += "\n\t// Properties\n\t";
                foreach (var p in Properties)
                    s += $"{p}\n\t";
            }
            if (Methods.Count > 0)
            {
                s += "\n\t// Methods\n\t";
                foreach (var m in Methods)
                    s += $"{m}\n\t";
            }
            s = s.TrimEnd('\t');
            s += "}";
            return s;
        }
    }
}
