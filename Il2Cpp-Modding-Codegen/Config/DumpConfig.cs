using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Config
{
    public class DumpConfig
    {
        public bool ParseImages { get; set; } = true;
        public bool ParseTypes { get; set; } = true;
        public bool ParseTypeAttributes { get; set; } = true;
        public bool ParseTypeSpecifiers { get; set; } = true;
        public bool ParseTypeFields { get; set; } = true;
        public bool ParseTypeProperties { get; set; } = true;
        public bool ParseTypeMethods { get; set; } = true;
    }
}