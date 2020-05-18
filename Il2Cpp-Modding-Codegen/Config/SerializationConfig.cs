using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Config
{
    public class SerializationConfig
    {
        public string OutputDirectory { get; set; }
        public string OutputHeaderDirectory { get; set; }
        public string OutputSourceDirectory { get; set; }

        /// <summary>
        /// How to output the created methods
        /// </summary>
        public OutputStyle OutputStyle { get; set; }

        /// <summary>
        /// How to handle unresolved type exceptions
        /// </summary>
        public UnresolvedTypeExceptionHandling UnresolvedTypeExceptionHandling { get; set; }

        /// <summary>
        /// Types blacklisted are explicitly not converted, even if it causes unresolved type exceptions in other types
        /// </summary>
        public List<string> BlacklistTypes { get; set; }

        /// <summary>
        /// Types whitelisted are explicitly converted, even if some have unresolved type exceptions
        /// </summary>
        public List<string> WhitelistTypes { get; set; }

        /// <summary>
        /// Will attempt to resolve all type exceptions (ignores blacklists and whitelists to look through full context)
        /// </summary>
        public bool EnsureReferences { get; set; }

        /// <summary>
        /// How to handle generics
        /// </summary>
        public GenericHandling GenericHandling { get; set; }
    }

    public enum OutputStyle
    {
        CrashUnless,
        Normal
    }

    public enum UnresolvedTypeExceptionHandling
    {
        DisplayInFile,
        Continue,
        Skip
    }

    public enum GenericHandling
    {
        Do,
        Skip
    }
}