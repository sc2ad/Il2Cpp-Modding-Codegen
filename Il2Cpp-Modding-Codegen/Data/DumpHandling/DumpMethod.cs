using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DumpHandling
{
    internal class DumpMethod : IMethod
    {
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public int RVA { get; }
        public int Offset { get; }
        public int VA { get; }
        public int Slot { get; }
        public TypeDefinition ReturnType { get; }
        public string Name { get; }
        public List<Parameter> Parameters { get; } = new List<Parameter>();

        public DumpMethod(PeekableStreamReader fs)
        {
            // Read Attributes
            string line = fs.PeekLine().Trim();
            while (line.StartsWith("["))
            {
                Attributes.Add(new DumpAttribute(fs));
                line = fs.PeekLine().Trim();
            }
            // Read prefix comment
            line = fs.ReadLine().Trim();
            var split = line.Split(' ');
            if (split.Length < 5)
            {
                throw new InvalidOperationException($"Method cannot be created from: {line}");
            }
            int start = split.Length - 1;
            if (split[split.Length - 2] == "Slot:")
            {
                Slot = int.Parse(split[split.Length - 1]);
                start = split.Length - 3;
            }
            VA = Convert.ToInt32(split[start], 16);
            Offset = Convert.ToInt32(split[start - 2], 16);
            RVA = Convert.ToInt32(split[start - 4], 16);
            // Read parameters
            line = fs.ReadLine().Trim();
            int end = line.LastIndexOf(')');
            int startSubstr = line.LastIndexOf('(', end - 1);
            string paramLine = line.Substring(startSubstr + 1, end - startSubstr - 1);
            if (paramLine.Length != 0)
            {
                var spl = paramLine.Split(new string[] { ", " }, StringSplitOptions.None);
                foreach (var s in spl)
                {
                    Parameters.Add(new Parameter(s));
                }
            }
            // Read method
            var methodSplit = line.Substring(0, startSubstr).Split(' ');
            Name = methodSplit[methodSplit.Length - 1];
            ReturnType = new TypeDefinition(methodSplit[methodSplit.Length - 2]);
            for (int i = 0; i < methodSplit.Length - 2; i++)
            {
                Specifiers.Add(new DumpSpecifier(methodSplit[i]));
            }
        }

        public override string ToString()
        {
            var s = "";
            foreach (var atr in Attributes)
            {
                s += $"{atr}\n\t";
            }
            s += $"// Offset: 0x{Offset:X}\n\t";
            foreach (var spec in Specifiers)
            {
                s += $"{spec} ";
            }
            s += $"{ReturnType} {Name}(";
            for (int i = 0; i < Parameters.Count; i++)
            {
                s += $"{Parameters[i]}";
                if (i != Parameters.Count - 1)
                {
                    s += ", ";
                }
            }
            s += ") { }";
            return s;
        }
    }
}