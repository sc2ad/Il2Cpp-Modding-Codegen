using Il2Cpp_Modding_Codegen.Parsers;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    internal class DllAttribute : IAttribute
    {
        public string Name { get; }
        public int RVA { get; }
        public int Offset { get; }
        public int VA { get; }

        public DllAttribute(string name)
        {
            Name = name;
        }

        public override string ToString()
        {
            return $"[{Name}] // Offset: 0x{Offset:X}";
        }

        internal static IEnumerable<IAttribute> From(TypeDefinition def)
        {
            List<DllAttribute> list = new List<DllAttribute>();
            foreach (var attr in def.CustomAttributes)
            {
                list.Add(new DllAttribute(attr.AttributeType.FullName));
            }
            // TODO: how many of these are actual attributes?
            foreach (TypeAttributes flag in Enum.GetValues(typeof(TypeAttributes)))
            {
                if (def.Attributes.HasFlag(flag))
                {
                    list.Add(new DllAttribute(flag.ToString()));
                }
            }
            return list;
        }

        internal static IEnumerable<IAttribute> From(FieldDefinition f)
        {
            throw new NotImplementedException();
        }
    }
}