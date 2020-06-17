using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DumpHandling
{
    internal class DumpTypeData : ITypeData
    {
        /// <summary>
        /// Number of characters the namespace name starts on
        /// </summary>
        private const int NamespaceStartOffset = 13;

        public TypeEnum Type { get; private set; }
        public TypeInfo Info { get; private set; }
        public TypeRef This { get; private set; }
        public TypeRef Parent { get; private set; }
        public HashSet<ITypeData> NestedTypes { get; } = new HashSet<ITypeData>();
        public HashSet<ITypeData> NestedInPlace { get; } = new HashSet<ITypeData>();
        public List<TypeRef> ImplementingInterfaces { get; } = new List<TypeRef>();
        public int TypeDefIndex { get; private set; }
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public List<IField> Fields { get; } = new List<IField>();
        public List<IProperty> Properties { get; } = new List<IProperty>();
        public List<IMethod> Methods { get; } = new List<IMethod>();
        public bool GetsOwnHeader { get; set; } = true;

        private DumpConfig _config;

        private void ParseAttributes(PeekableStreamReader fs)
        {
            string line = fs.PeekLine();
            while (line.StartsWith("["))
            {
                if (_config.ParseTypeAttributes)
                    Attributes.Add(new DumpAttribute(fs));
                else
                    fs.ReadLine();
                line = fs.PeekLine();
            }
        }

        private void ParseTypeName(string @namespace, PeekableStreamReader fs)
        {
            string line = fs.ReadLine();
            var split = line.Split(' ');
            TypeDefIndex = int.Parse(split[split.Length - 1]);
            // : at least 4 from end
            int start = 4;
            bool found = false;
            for (int i = split.Length - 1 - start; i > 1; i--)
            {
                // Count down till we hit the :
                if (split[i] == ":")
                {
                    start = i - 1;
                    found = true;
                    break;
                }
            }
            if (found)
            {
                // 1 after is Parent
                // We will assume that if the Parent type starts with an I, it is an interface
                // TODO: Fix this assumption, perhaps by resolving the types forcibly and ensuring they are interfaces?
                var parentCandidate = DumpTypeRef.FromMultiple(split, start + 2, out int tmp, 1, " ").TrimEnd(',');
                if (parentCandidate.StartsWith("I"))
                    ImplementingInterfaces.Add(new DumpTypeRef(parentCandidate));
                else
                    Parent = new DumpTypeRef(parentCandidate);
                // Go from 2 after : to length - 3
                for (int i = tmp + 1; i < split.Length - 3; i++)
                {
                    ImplementingInterfaces.Add(new DumpTypeRef(DumpTypeRef.FromMultiple(split, i, out tmp, 1, " ").TrimEnd(',')));
                    i = tmp;
                }
            }
            else
            {
                start = split.Length - start;
            }
            // -4 is name
            // -5 is type enum
            // all others are specifiers
            // This will have DeclaringType set on it
            This = new DumpTypeRef(@namespace, DumpTypeRef.FromMultiple(split, start, out int adjusted, -1, " "));
            Type = (TypeEnum)Enum.Parse(typeof(TypeEnum), split[adjusted - 1], true);
            for (int i = 0; i < adjusted - 1; i++)
            {
                if (_config.ParseTypeSpecifiers)
                    Specifiers.Add(new DumpSpecifier(split[i]));
            }
            Info = new TypeInfo
            {
                TypeFlags = Type == TypeEnum.Class || Type == TypeEnum.Interface ? TypeFlags.ReferenceType : TypeFlags.ValueType
            };
            if (Parent is null)
            {
                // If the type is a value type, it has no parent.
                // If the type is a reference type, it has parent Il2CppObject
                if (Info.TypeFlags == TypeFlags.ReferenceType)
                    Parent = DumpTypeRef.ObjectType;
            }
        }

        private void ParseFields(PeekableStreamReader fs)
        {
            string line = fs.PeekLine().Trim();
            if (line != "{")
            {
                // Nothing in the type
                return;
            }
            fs.ReadLine();
            line = fs.PeekLine().Trim();
            // Fields should be second line, if it isn't there are no fields.
            if (!line.StartsWith("// Fields"))
            {
                // No fields, but other things
                return;
            }
            // Read past // Fields
            fs.ReadLine();
            while (!string.IsNullOrEmpty(line) && line != "}" && !line.StartsWith("// Properties") && !line.StartsWith("// Methods"))
            {
                if (_config.ParseTypeFields)
                    Fields.Add(new DumpField(This, fs));
                else
                    fs.ReadLine();
                line = fs.PeekLine().Trim();
            }
        }

        private void ParseProperties(PeekableStreamReader fs)
        {
            string line = fs.PeekLine().Trim();
            if (line == "")
            {
                // Spaced after fields
                fs.ReadLine();
                line = fs.PeekLine().Trim();
            }
            if (!line.StartsWith("// Properties"))
            {
                // No properties
                return;
            }
            // Read past // Properties
            fs.ReadLine();
            while (line != "" && line != "}" && !line.StartsWith("// Methods"))
            {
                if (_config.ParseTypeProperties)
                    Properties.Add(new DumpProperty(This, fs));
                else
                    fs.ReadLine();
                line = fs.PeekLine().Trim();
            }
        }

        private void ParseMethods(PeekableStreamReader fs)
        {
            string line = fs.PeekLine().Trim();
            if (line == "")
            {
                // Spaced after fields or properties
                fs.ReadLine();
                line = fs.PeekLine().Trim();
            }
            if (!line.StartsWith("// Methods"))
            {
                // No methods
                return;
            }
            // Read past // Methods
            fs.ReadLine();
            while (line != "" && line != "}")
            {
                if (_config.ParseTypeMethods)
                    Methods.Add(new DumpMethod(This, fs));
                else
                    fs.ReadLine();
                line = fs.PeekLine().Trim();
            }

            // It's important that Foo.IBar.func() goes after func() (if present)
            var methods = new List<IMethod>(Methods);
            Methods.Clear();
            Methods.AddRange(methods.Where(m => m.ImplementedFrom is null));
            Methods.AddRange(methods.Where(m => m.ImplementedFrom != null));
        }

        public DumpTypeData(PeekableStreamReader fs, DumpConfig config)
        {
            _config = config;
            // Extract namespace from line
            var @namespace = fs.ReadLine().Substring(NamespaceStartOffset).Trim();
            ParseAttributes(fs);
            ParseTypeName(@namespace, fs);
            // Empty type
            if (fs.PeekLine() == "{}")
            {
                fs.ReadLine();
                return;
            }
            ParseFields(fs);
            ParseProperties(fs);
            ParseMethods(fs);
            // Read closing brace, if it needs to be read
            if (fs.PeekLine() == "}")
            {
                fs.ReadLine();
            }
        }

        public override string ToString()
        {
            var s = $"// Namespace: {This.Namespace}\n";
            foreach (var attr in Attributes)
            {
                s += $"{attr}\n";
            }
            foreach (var spec in Specifiers)
            {
                s += $"{spec} ";
            }
            s += $"{Type.ToString().ToLower()} {This.Name}";
            if (Parent != null)
            {
                s += $" : {Parent}";
            }
            s += "\n{";
            if (Fields.Count > 0)
            {
                s += "\n\t// Fields\n\t";
                foreach (var f in Fields)
                {
                    s += $"{f}\n\t";
                }
            }
            if (Properties.Count > 0)
            {
                s += "\n\t// Properties\n\t";
                foreach (var p in Properties)
                {
                    s += $"{p}\n\t";
                }
            }
            if (Methods.Count > 0)
            {
                s += "\n\t// Methods\n\t";
                foreach (var m in Methods)
                {
                    s += $"{m}\n\t";
                }
            }
            s = s.TrimEnd('\t');
            s += "}";
            return s;
        }
    }
}