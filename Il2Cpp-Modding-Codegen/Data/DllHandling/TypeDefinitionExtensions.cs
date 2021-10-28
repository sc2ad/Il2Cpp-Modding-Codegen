using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    public static class TypeDefinitionExtensions
    {
        public static string GetTemplateLine(this TypeDefinition t)
        {
            if (!t.HasGenericParameters)
                return "";
            return $"template<{string.Join(", ", t.GenericParameters.Select(g => "typename " + g.Name))}>";
        }

        private static readonly object resolutionLock = new();

        public static TypeDefinition ResolveLocked(this TypeReference r)
        {
            lock (resolutionLock)
            {
                return r.Resolve();
            }
        }
    }
}