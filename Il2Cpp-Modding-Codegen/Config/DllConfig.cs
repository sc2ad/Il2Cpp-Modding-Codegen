using System.Collections.Generic;

namespace Il2Cpp_Modding_Codegen.Config
{
    public class DllConfig
    {
        public bool ParseImages { get; set; } = true;
        public bool ParseTypes { get; set; } = true;
        public bool ParseTypeAttributes { get; set; } = true;
        public bool ParseTypeSpecifiers { get; set; } = true;
        public bool ParseTypeFields { get; set; } = true;
        public bool ParseTypeProperties { get; set; } = true;
        public bool ParseTypeMethods { get; set; } = true;
        public HashSet<string> BlacklistDlls { get; set; } = new HashSet<string>();
        public HashSet<string> BlacklistTypes { get; set; } = new HashSet<string>();
    }
}