using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
{
    public class Parameter
    {
        public string TypeName { get; }

        public string Name { get; } = null;
        public ParameterFlags Flags { get; } = ParameterFlags.None;

        public Parameter(string innard)
        {
            var spl = innard.Split(' ');
            int typeIndex = 1;
            if (spl[0] == "ref")
            {
                Flags = ParameterFlags.Ref;
            }
            else if (spl[0] == "out")
            {
                Flags = ParameterFlags.Out;
            }
            else if (spl[0] == "in")
            {
                Flags = ParameterFlags.In;
            }
            else
            {
                Flags = ParameterFlags.None;
                typeIndex = 0;
            }

            TypeName = spl[typeIndex];
            if (typeIndex + 1 < spl.Length)
            {
                Name = spl[typeIndex + 1];
            }
        }

        public override string ToString()
        {
            string s = "";
            if (Flags != ParameterFlags.None)
            {
                s = $"{Flags.ToString().ToLower()} ";
            }
            s += $"{TypeName}";
            if (Name != null)
            {
                s += $" {Name}";
            }
            return s;
        }
    }
}