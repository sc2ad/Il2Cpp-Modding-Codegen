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
        public TypeDefinition DeclaringType { get; }
        public TypeDefinition ImplementedFrom { get; }
        public string Name { get; }
        public List<Parameter> Parameters { get; } = new List<Parameter>();

        public DumpMethod(TypeDefinition declaring, PeekableStreamReader fs)
        {
            DeclaringType = declaring;
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
            if (split[start - 1] == "VA:")
            {
                if (split[start] == "-1")
                    VA = -1;
                else
                    VA = Convert.ToInt32(split[start], 16);
                start -= 2;
            }
            if (split[start - 1] == "Offset")
            {
                if (split[start] == "-1")
                    Offset = -1;
                else
                    Offset = Convert.ToInt32(split[start], 16);
                start -= 2;
            }
            if (split[start - 1] == "RVA")
            {
                if (split[start] == "-1")
                    RVA = -1;
                else
                    RVA = Convert.ToInt32(split[start], 16);
            }
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
            int startIndex = methodSplit[methodSplit.Length - 1].LastIndexOf(".");
            if (startIndex != -1)
                ImplementedFrom = new TypeDefinition(methodSplit[methodSplit.Length - 1].Substring(0, startIndex));
            Name = methodSplit[methodSplit.Length - 1].Substring(startIndex + 1);
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
            s += $"{ReturnType} {Name}({Parameters.FormatParameters()}) ";
            s += "{}";
            return s;
        }
    }
}