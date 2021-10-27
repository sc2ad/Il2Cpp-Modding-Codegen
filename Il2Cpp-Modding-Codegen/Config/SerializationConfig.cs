using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Il2CppModdingCodegen.Config
{
    public class SerializationConfig
    {
        public string? OutputDirectory { get; set; }
        public string? OutputHeaderDirectory { get; set; }
        public string? OutputSourceDirectory { get; set; }

        /// <summary>
        /// If all of the C++ code should end up in one source file instead of spanned across multiple files.
        /// </summary>
        public bool OneSourceFile { get; set; } = false;

        /// <summary>
        /// How often to chunk .cpp files when <see cref="OneSourceFile"/> is true.
        /// </summary>
        public int ChunkFrequency { get; set; } = 500;

        /// <summary>
        /// Id for the mod to use, also the resultant library name
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// The (ideally SemVer) version of the library being created
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Libil2cpp path, necessary for proper building of Android.mk
        /// </summary>
        public string? Libil2cpp { get; set; }

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
        public HashSet<string>? IllegalNames { get; set; }

        /// <summary>
        /// A set of illegal method names that must be renamed.
        /// The renaming approach is to simply suffix with a _ until it is no longer within this set.
        /// </summary>
        public HashSet<string>? IllegalMethodNames { get; set; }

        /// <summary>
        /// How to output the created methods
        /// </summary>
        public OutputStyle OutputStyle { get; set; }

        /// <summary>
        /// Pointer size for ensuring valid size checks.
        /// </summary>
        public int PointerSize { get; set; } = 8;

        public string MacroWrap(string loggerId, string toWrap, bool isReturn)
        {
            if (toWrap is null) throw new ArgumentNullException(nameof(toWrap));
            string parenWrapped = Regex.IsMatch(toWrap, @"<.*,.*>") ? $"({toWrap})" : toWrap;
            switch (OutputStyle)
            {
                case OutputStyle.CrashUnless:
                    return $"CRASH_UNLESS({parenWrapped})";

                case OutputStyle.ThrowUnless:
                    return $"THROW_UNLESS({parenWrapped})";

                default:
                    if (isReturn) return toWrap;
                    return $"RET_V_UNLESS({loggerId}, {parenWrapped})";
            }
        }

        /// <summary>
        /// How to handle unresolved type exceptions
        /// </summary>
        public UnresolvedTypeExceptionHandlingWrapper? UnresolvedTypeExceptionHandling { get; set; }

        /// <summary>
        /// How to handle duplicate methods that are serialized
        /// </summary>
        public DuplicateMethodExceptionHandling DuplicateMethodExceptionHandling { get; set; } = DuplicateMethodExceptionHandling.DisplayInFile;

        /// <summary>
        /// Types blacklisted are explicitly not converted, even if it causes unresolved type exceptions in other types
        /// </summary>
        public List<string>? BlacklistTypes { get; set; }

        /// <summary>
        /// Methods blacklisted are explicitly not converted
        /// </summary>
        public HashSet<string> BlacklistMethods { get; set; } = new HashSet<string>();

        /// <summary>
        /// Methods here are explicitly blacklisted based off of full qualification
        /// </summary>
        public HashSet<(string @namespace, string typeName, string methodName)> QualifiedBlacklistMethods { get; set; } = new HashSet<(string @namespace, string typeName, string methodName)>();

        /// <summary>
        /// Types whitelisted are explicitly converted, even if some have unresolved type exceptions
        /// </summary>
        public List<string>? WhitelistTypes { get; set; }

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

        public static Dictionary<string, int> SpecialMethodNames { get; private set; } = new Dictionary<string, int>();

        public string SafeMethodName(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (name.StartsWith("op_"))
            {
                if (SpecialMethodNames.ContainsKey(name))
                    SpecialMethodNames[name]++;
                else
                    SpecialMethodNames.Add(name, 1);
            }
            if (!string.IsNullOrEmpty(name))
                while (IllegalNames?.Contains(name) is true || IllegalMethodNames?.Contains(name) is true)
                    name += "_";
            return name;
        }
    }

    public class UnresolvedTypeExceptionHandlingWrapper
    {
        public UnresolvedTypeExceptionHandling TypeHandling { get; set; }

        // TODO: May not work as intended
        public UnresolvedTypeExceptionHandling FieldHandling { get; set; }

        // TODO: May not work as intended
        public UnresolvedTypeExceptionHandling MethodHandling { get; set; }
    }

    public enum DuplicateMethodExceptionHandling
    {
        Ignore,
        DisplayInFile,
        Skip,
        Elevate
    }

    public enum OutputStyle
    {
        Normal,
        CrashUnless,
        ThrowUnless
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