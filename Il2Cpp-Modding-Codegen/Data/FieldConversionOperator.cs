using Mono.Cecil;
using System;
using System.Linq;
using static Il2CppModdingCodegen.Data.ConversionOperatorKind;

namespace Il2CppModdingCodegen.Data
{
    internal class FieldConversionOperator
    {
        internal ConversionOperatorKind Kind { get; }
        internal FieldDefinition? Field { get; } = null;

        public FieldConversionOperator(TypeDefinition type, FieldConversionOperator? parentsOperator)
        {
            var parKind = parentsOperator?.Kind ?? None;
            var instanceFields = type.Fields.Where(f => !f.IsStatic);
            if (parKind == Delete)
                Kind = Invalid;
            else if (parKind == Yes || parKind == Inherited)
            {
                Kind = instanceFields.Any() ? Delete : Inherited;
                Field = parentsOperator?.Field ?? throw new ArgumentException("Must have Field!", nameof(parentsOperator));
            }
            else if (parKind == None && instanceFields.Count() == 1)
            {
                var field = instanceFields.First();
                if (field.FieldType.IsGenericParameter)
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