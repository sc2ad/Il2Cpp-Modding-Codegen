using System;

namespace Il2CppModdingCodegen.Data
{
    [Flags]
    public enum ParameterFlags
    {
        None = 0,
        Ref = 1,
        Out = 2,
        In = 4
    }
}
