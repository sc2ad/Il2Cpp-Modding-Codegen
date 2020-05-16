using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
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
        public TypeDefinition This { get; }
        public TypeDefinition Parent { get; private set; }
        public int TypeDefIndex { get; private set; }
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public List<IField> Fields { get; } = new List<IField>();
        public List<IProperty> Properties { get; } = new List<IProperty>();
        public List<IMethod> Methods { get; } = new List<IMethod>();

        /// <summary>
        /// List of dependency TypeDefinitions to resolve in the type
        /// </summary>
        internal HashSet<TypeDefinition> References { get; } = new HashSet<TypeDefinition>();

        private DumpConfig _config;

        private void ParseAttributes(PeekableStreamReader fs)
        {
            string line = fs.PeekLine();
            while (line.StartsWith("["))
            {
                if (_config.ParseTypeAttributes)
                    Attributes.Add(new DumpAttribute(fs));
                line = fs.PeekLine();
            }
        }

        private void ParseTypeName(PeekableStreamReader fs)
        {
            string line = fs.ReadLine();
            var split = line.Split(' ');
            TypeDefIndex = int.Parse(split[split.Length - 1]);
            int start = 4;
            if (split[split.Length - 5] == ":")
            {
                Parent = new TypeDefinition(split[split.Length - 4]);
                start = 6;
            }
            // -4 is name
            // -5 is type enum
            // all others are specifiers
            This.Name = split[split.Length - start];
            Type = (TypeEnum)Enum.Parse(typeof(TypeEnum), split[split.Length - start - 1], true);
            for (int i = 0; i < split.Length - start - 1; i++)
            {
                if (_config.ParseTypeSpecifiers)
                    Specifiers.Add(new DumpSpecifier(split[i]));
            }
            Info = new TypeInfo
            {
                TypeFlags = Type == TypeEnum.Class ? TypeFlags.ReferenceType : TypeFlags.ValueType
            };
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
            while (line != "" && line != "}" && !line.StartsWith("// Properties") && !line.StartsWith("// Methods"))
            {
                if (_config.ParseTypeFields)
                    Fields.Add(new DumpField(fs));
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
                    Properties.Add(new DumpProperty(fs));
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
                    Methods.Add(new DumpMethod(fs));
                line = fs.PeekLine().Trim();
            }
        }

        public DumpTypeData(PeekableStreamReader fs, DumpConfig config)
        {
            _config = config;
            // Extract namespace from line
            This = new TypeDefinition();
            This.Namespace = fs.ReadLine().Substring(NamespaceStartOffset).Trim();
            ParseAttributes(fs);
            ParseTypeName(fs);
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