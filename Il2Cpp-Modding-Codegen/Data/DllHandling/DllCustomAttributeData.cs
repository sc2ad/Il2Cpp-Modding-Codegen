using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    public struct DllCustomAttributeData : IEquatable<DllCustomAttributeData>
    {
        public string Name { get; }
        public int Offset { get; }
        public int RVA { get; }

        public DllCustomAttributeData(CustomAttribute ca)
        {
            if (ca is null)
                throw new ArgumentNullException(nameof(ca));
            Name = ca.AttributeType.Name;
            Offset = -1;
            RVA = -1;
            if (ca.AttributeType.Name == "AttributeAttribute")
            {
                foreach (var f in ca.Fields)
                {
                    switch (f.Name)
                    {
                        case "Name":
                            Name = (f.Argument.Value as string)!;
                            break;

                        case "Offset":
                            Offset = Convert.ToInt32(f.Argument.Value as string, 16);
                            break;

                        case "RVA":
                            RVA = Convert.ToInt32(f.Argument.Value as string, 16);
                            break;

                        default:
                            throw new InvalidOperationException($"Unknown handling for field: {f.Name}");
                    }
                }
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is DllCustomAttributeData res)
            {
                return Equals(res);
            }
            return false;
        }

        public bool Equals(DllCustomAttributeData dat) => Name == dat.Name;

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        public static bool operator ==(DllCustomAttributeData left, DllCustomAttributeData right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(DllCustomAttributeData left, DllCustomAttributeData right)
        {
            return !(left == right);
        }
    }
}