using System.Collections.Generic;

namespace Il2CppModdingCodegen.Config
{
    public class DllConfig
    {
        internal bool ParseImages { get; set; } = true;
        internal bool ParseTypes { get; set; } = true;
        internal bool ParseTypeAttributes { get; set; } = true;
        internal bool ParseTypeSpecifiers { get; set; } = true;
        internal bool ParseTypeFields { get; set; } = true;
        internal bool ParseTypeProperties { get; set; } = true;
        internal bool ParseTypeMethods { get; set; } = true;
        internal HashSet<string> BlacklistDlls { get; set; } = new HashSet<string>();
        internal HashSet<string> BlacklistTypes { get; set; } = new HashSet<string>();
    }
}
