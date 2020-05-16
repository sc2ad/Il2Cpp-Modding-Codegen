using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
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
}