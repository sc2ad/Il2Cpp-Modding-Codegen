using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Config
{
    public class DumpConfig
    {
        public bool ParseImages { get; } = true;
        public bool ParseTypes { get; } = true;
        public bool ParseTypeAttributes { get; } = true;
        public bool ParseTypeSpecifiers { get; } = true;
        public bool ParseTypeFields { get; } = true;
        public bool ParseTypeProperties { get; } = true;
        public bool ParseTypeMethods { get; } = true;
    }
}