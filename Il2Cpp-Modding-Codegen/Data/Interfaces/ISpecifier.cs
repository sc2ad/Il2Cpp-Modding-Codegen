using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Data
{
    public interface ISpecifier
    {
        string Value { get; }
        bool Static { get; }
        bool Private { get; }
        bool Internal { get; }
        bool Public { get; }
        bool Sealed { get; }
        bool Override { get; }
        bool Readonly { get; }
        bool Const { get; }
    }

    public static class SpecifierExtensions
    {
        internal static bool IsStatic(this List<ISpecifier> specifiers) => specifiers.Any(s => s.Static);

        internal static bool IsConst(this List<ISpecifier> specifiers) => specifiers.Any(s => s.Const);
    }
}