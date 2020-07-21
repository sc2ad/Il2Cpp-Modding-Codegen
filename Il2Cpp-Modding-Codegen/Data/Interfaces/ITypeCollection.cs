using System;
using System.Collections.Generic;

namespace Il2CppModdingCodegen.Data
{
    public interface ITypeCollection
    {
        IEnumerable<ITypeData> Types { get; }
        [ObsoleteAttribute("Please call TypeRef.Resolve(ITypeCollection) instead.")]
        ITypeData Resolve(TypeRef TypeRef);
    }
}
