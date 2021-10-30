using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public interface IHeaderContext
    {
        string HeaderFileName { get; }
        HashSet<IHeaderContext> Includes { get; }
        TypeDefinition Type { get; }
    }
}