// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Will go away in C# 8.0 https://stackoverflow.com/questions/291340/mark-parameters-as-not-nullable-in-c-net")]
[assembly: SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "language support is too much work")]
[assembly: SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "language support is too much work")]
[assembly: SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "language support is too much work")]
[assembly: SuppressMessage("Globalization", "CA1307:Specify StringComparison", Justification = "language support is too much work")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "I ain't a part of your system, man")]