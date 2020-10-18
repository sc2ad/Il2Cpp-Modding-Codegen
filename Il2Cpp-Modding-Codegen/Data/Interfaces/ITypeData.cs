using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Data
{
    public interface ITypeData
    {
        TypeRef This { get; }
        TypeEnum Type { get; }
        TypeInfo Info { get; }
        TypeRef? Parent { get; }
        HashSet<ITypeData> NestedTypes { get; }
        List<TypeRef> ImplementingInterfaces { get; }
        int TypeDefIndex { get; }
        List<IAttribute> Attributes { get; }
        List<ISpecifier> Specifiers { get; }
        List<IField> InstanceFields { get; }
        List<IField> StaticFields { get; }
        IEnumerable<IField> Fields => InstanceFields.Concat(StaticFields);
        List<IProperty> Properties { get; }
        List<IMethod> Methods { get; }

        string ToString()
        {
            var s = $"// Namespace: {This.Namespace}\n";
            foreach (var attr in Attributes)
                s += $"{attr}\n";
            foreach (var spec in Specifiers)
                s += $"{spec} ";
            s += $"{Type.ToString().ToLower()} {This.CppName()}";
            if (Parent != null)
                s += $" : {Parent}";
            s += "\n{";
            if (Fields.Any())
            {
                s += "\n\t// Fields\n\t";
                foreach (var f in Fields)
                    s += $"{f}\n\t";
            }
            if (Properties.Any())
            {
                s += "\n\t// Properties\n\t";
                foreach (var p in Properties)
                    s += $"{p}\n\t";
            }
            if (Methods.Any())
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
