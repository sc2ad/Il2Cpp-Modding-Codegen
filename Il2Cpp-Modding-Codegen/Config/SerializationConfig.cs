using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Config
{
    public class SerializationConfig
    {
        public string OutputDirectory { get; set; }
        public string OutputHeaderDirectory { get; set; }
        public string OutputSourceDirectory { get; set; }

        /// <summary>
        /// Id for the mod to use, also the resultant library name
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The (ideally SemVer) version of the library being created
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Libil2cpp path, necessary for proper building of Android.mk
        /// </summary>
        public string Libil2cpp { get; set; }

        /// <summary>
        /// To create multiple shared/static libraries or a single file
        /// </summary>
        public bool MultipleLibraries { get; set; } = false;

        /// <summary>
        /// The maximum amount of characters (not including additional characters added by make) for shared libraries
        /// </summary>
        public int SharedLibraryCharacterLimit { get; set; } = 4000;

        /// <summary>
        /// The maximum amount of characters (not including additional characters added by make) for source files
        /// </summary>
        public int SourceFileCharacterLimit { get; set; } = 4500;

        /// <summary>
        /// The maximum amount of characters (not including additional characters added by make) for static libraries
        /// </summary>
        public int StaticLibraryCharacterLimit { get; set; } = 5000;

        /// <summary>
        /// A set of illegal method, field, or type names that must be renamed.
        /// The renaming approach is to simply prefix with a _ until it is no longer within this set.
        /// </summary>
        public HashSet<string> IllegalNames { get; set; }

        /// <summary>
        /// A set of illegal method names that must be renamed.
        /// The renaming approach is to simply suffix with a _ until it is no longer within this set.
        /// </summary>
        public HashSet<string> IllegalMethodNames { get; set; }

        /// <summary>
        /// How to output the created methods
        /// </summary>
        public OutputStyle OutputStyle { get; set; }

        /// <summary>
        /// How to handle unresolved type exceptions
        /// </summary>
        public ExceptionHandling UnresolvedTypeExceptionHandling { get; set; }

        /// <summary>
        /// Types blacklisted are explicitly not converted, even if it causes unresolved type exceptions in other types
        /// </summary>
        public List<string> BlacklistTypes { get; set; }

        /// <summary>
        /// Methods blacklisted are explicitly not converted
        /// </summary>
        public HashSet<string> BlacklistMethods { get; set; } = new HashSet<string>();

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

        /// <summary>
        /// To display progress using <see cref="PrintSerializationProgressFrequency"/> while serializing
        /// </summary>
        public bool PrintSerializationProgress { get; set; }

        /// <summary>
        /// Frequency to display progress. Only used if <see cref="PrintSerializationProgress"/> is true
        /// </summary>
        public int PrintSerializationProgressFrequency { get; set; }

        /// <summary>
        /// Returns a name that is definitely not within IllegalNames
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string SafeName(string name)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            while (IllegalNames?.Contains(name) is true)
                name = "_" + name;
            return name;
        }

        public static Dictionary<string, int> specialMethodNames = new Dictionary<string, int>();

        public string SafeMethodName(string name)
        {
            if (name.StartsWith("op_"))
            {
                if (specialMethodNames.ContainsKey(name))
                    specialMethodNames[name]++;
                else
                    specialMethodNames.Add(name, 1);
            }
            if (!string.IsNullOrEmpty(name))
                while (IllegalNames?.Contains(name) is true || IllegalMethodNames?.Contains(name) is true)
                    name += "_";
            return name;
        }
    }

    public struct ExceptionHandling
    {
        public UnresolvedTypeExceptionHandling TypeHandling { get; set; }

        // TODO: May not work as intended
        public UnresolvedTypeExceptionHandling FieldHandling { get; set; }

        // TODO: May not work as intended
        public UnresolvedTypeExceptionHandling MethodHandling { get; set; }
    }

    public enum OutputStyle
    {
        Normal,
        CrashUnless
    }

    public enum UnresolvedTypeExceptionHandling
    {
        Ignore,
        DisplayInFile,
        SkipIssue,

        /// <summary>
        /// Skips the current, forcing the parent to resolve it.
        /// In most cases, this involves forwarding field --> type, or method --> type
        /// So this will force TypeHandling to resolve it as if it were a higher level exception
        /// </summary>
        Elevate
    }

    public enum GenericHandling
    {
        Do,
        Skip
    }
}