using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DumpHandling
{
    internal class DumpProperty : IProperty
    {
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public TypeDefinition Type { get; }
        public TypeDefinition DeclaringType { get; }
        public string Name { get; }
        public bool GetMethod { get; }
        public bool SetMethod { get; }

        public DumpProperty(TypeDefinition declaring, PeekableStreamReader fs)
        {
            DeclaringType = declaring;
            string line = fs.PeekLine().Trim();
            while (line.StartsWith("["))
            {
                Attributes.Add(new DumpAttribute(fs));
                line = fs.PeekLine().Trim();
            }
            line = fs.ReadLine().Trim();
            var split = line.Split(' ');
            if (split.Length < 5)
            {
                throw new InvalidOperationException($"Property cannot be created from: {line}");
            }
            // Start at the end (but before the }), count back until we hit a { (or we have gone 3 steps)
            // Keep track of how far back we count
            int i;
            for (i = 0; i < 3; i++)
            {
                var val = split[split.Length - 2 - i];
                if (val == "{")
                {
                    break;
                }
                else if (val == "get;")
                {
                    GetMethod = true;
                }
                else if (val == "set;")
                {
                    SetMethod = true;
                }
            }
            Name = split[split.Length - 3 - i];
            Type = new TypeDefinition(TypeDefinition.FromMultiple(split, split.Length - 4 - i, out int adjust, -1, " "), false);
            for (int j = 0; j < adjust; j++)
            {
                Specifiers.Add(new DumpSpecifier(split[j]));
            }
        }

        public override string ToString()
        {
            var s = "";
            foreach (var atr in Attributes)
            {
                s += $"{atr}\n\t";
            }
            foreach (var spec in Specifiers)
            {
                s += $"{spec} ";
            }
            s += $"{Type} {Name}";
            s += " { ";
            if (GetMethod)
            {
                s += "get; ";
            }
            if (SetMethod)
            {
                s += "set; ";
            }
            s += "}";
            return s;
        }
    }
}