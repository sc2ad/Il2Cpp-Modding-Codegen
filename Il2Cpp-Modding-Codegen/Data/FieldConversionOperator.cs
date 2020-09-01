using System;
using System.Linq;
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
                var field = type.InstanceFields.First();
                if (field.Type.IsGenericParameter || field.Type.IsGenericTemplate)
                    Kind = Invalid;  // todo: resolve conversion operators properly for generic types?
                else
                {
                    Kind = Yes;
                    Field = field;
                }
            }
            else
                Kind = parKind;
        }
    }
}
