using Il2CppModdingCodegen.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Il2CppModdingCodegen.Data.ConversionOperatorKind;

namespace Il2CppModdingCodegen.Data
{
    class FieldConversionOperator
    {
        internal ConversionOperatorKind Kind { get; }
        internal IField? Field { get; } = null;

        public FieldConversionOperator(ITypeData type, FieldConversionOperator? parentsOperator)
        {
            var parKind = parentsOperator?.Kind ?? None;
            if (parKind == Delete)
                Kind = Invalid;
            else if (parKind == Yes || parKind == Inherited)
            {
                if (type.InstanceFields.Any())
                    Kind = Delete;
                else
                    Kind = Inherited;
                Field = parentsOperator?.Field ?? throw new ArgumentException("Must have Field!", nameof(parentsOperator));
            }
            else if (parKind == None && type.InstanceFields.Count == 1)
            {
                Kind = Yes;
                Field = type.InstanceFields.First();
            }
            else
                Kind = parKind;
        }
    }
}
