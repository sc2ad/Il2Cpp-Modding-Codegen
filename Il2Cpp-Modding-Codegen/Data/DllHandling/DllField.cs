using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    public class DllField : IEquatable<DllField>
    {
        public FieldDefinition Field { get; }
        public int Offset { get; }

        public DllField(FieldDefinition f)
        {
            if (f is null)
                throw new ArgumentNullException(nameof(f));
            Field = f;
            var caOft = f.CustomAttributes.FirstOrDefault(ca => (ca.AttributeType.Name == "FieldOffsetAttribute" || ca.AttributeType.Name == "StaticFieldOffsetAttribute") && ca.HasFields);
            Offset = caOft is null ? -1 : Convert.ToInt32(caOft?.Fields.First().Argument.Value as string, 16);
        }

        public override bool Equals(object obj)
        {
            if (obj is DllField f)
                return Equals(f);
            return false;
        }

        public bool Equals(DllField other)
        {
            return Field.Equals(other.Field);
        }

        public override int GetHashCode() => Field.GetHashCode();
    }
}