using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppModdingCodegen.Serialization
{
    public class CppTypeSourceContextContainer
    {
        public HashSet<CppTypeSourceContext> NestedContexts { get; } = new HashSet<CppTypeSourceContext>();

        public CppTypeSourceContextContainer()
        {
        }

        // Should allow for adding multiple contexts under it
        // Only top level write out will be done, collects all includes
        public void Add(CppTypeSourceContext ctx)
        {
            lock (NestedContexts)
            {
                NestedContexts.Add(ctx);
            }
        }
    }
}