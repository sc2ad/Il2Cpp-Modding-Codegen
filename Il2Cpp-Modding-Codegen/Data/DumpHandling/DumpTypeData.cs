﻿using Il2CppModdingCodegen.Config;
using Il2CppModdingCodegen.Parsers;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;

namespace Il2CppModdingCodegen.Data.DumpHandling
{
    internal class DumpTypeData : ITypeData
    {
        /// <summary>
        /// Number of characters the namespace name starts on
        /// </summary>
        private const int NamespaceStartOffset = 13;

        public TypeEnum Type { get; private set; }
        public TypeInfo Info { get; private set; }
        public TypeRef This { get; private set; }
        public TypeRef? Parent { get; private set; }
        public HashSet<ITypeData> NestedTypes { get; } = new HashSet<ITypeData>();
        public List<TypeRef> ImplementingInterfaces { get; } = new List<TypeRef>();
        public int TypeDefIndex { get; private set; }
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public List<IField> InstanceFields { get; } = new List<IField>();
        public List<IField> StaticFields { get; } = new List<IField>();
        public List<IProperty> Properties { get; } = new List<IProperty>();
        public List<IMethod> Methods { get; } = new List<IMethod>();
        public ITypeData.LayoutKind Layout { get => throw new InvalidOperationException(); }

        private readonly DumpConfig _config;

        private void ParseAttributes(PeekableStreamReader fs)
        {
            var line = fs.PeekLine();
            while (line != null && line.StartsWith("["))
            {
                if (_config.ParseTypeAttributes)
                    Attributes.Add(new DumpAttribute(fs));
                else
                    fs.ReadLine();
                line = fs.PeekLine();
            }
        }

        private void ParseTypeName(string @namespace, PeekableStreamReader fs)
        {
            string line = fs.ReadLine() ?? throw new InvalidDataException();
            var split = line.Split(' ');
            TypeDefIndex = int.Parse(split[^1]);
            // : at least 4 from end
            int start = 4;
            bool found = false;
            for (int i = split.Length - 1 - start; i > 1; i--)
            {
                // Count down till we hit the :
                if (split[i] == ":")
                {
                    start = i - 1;
                    found = true;
                    break;
                }
            }
            if (found)
            {
                // 1 after is Parent
                // We will assume that if the Parent type starts with an I, it is an interface
                // TODO: Fix this assumption, perhaps by resolving the types forcibly and ensuring they are interfaces?
                var parentCandidate = DumpTypeRef.FromMultiple(split, start + 2, out int tmp, 1, " ").TrimEnd(',');
                if (parentCandidate.StartsWith("I"))
                    ImplementingInterfaces.Add(new DumpTypeRef(parentCandidate));
                else
                    Parent = new DumpTypeRef(parentCandidate);
                // Go from 2 after : to length - 3
                for (int i = tmp + 1; i < split.Length - 3; i++)
                {
                    ImplementingInterfaces.Add(new DumpTypeRef(DumpTypeRef.FromMultiple(split, i, out tmp, 1, " ").TrimEnd(',')));
                    i = tmp;
                }
            }
            else
                start = split.Length - start;

            // -4 is name
            // -5 is type enum
            // all others are specifiers
            // This will have DeclaringType set on it
            This = new DumpTypeRef(@namespace, DumpTypeRef.FromMultiple(split, start, out int adjusted, -1, " "));
            Type = (TypeEnum)Enum.Parse(typeof(TypeEnum), split[adjusted - 1], true);
            for (int i = 0; i < adjusted - 1; i++)
                if (_config.ParseTypeSpecifiers)
                    Specifiers.Add(new DumpSpecifier(split[i]));

            Info = new TypeInfo
            {
                Refness = Type == TypeEnum.Class || Type == TypeEnum.Interface ? Refness.ReferenceType : Refness.ValueType
            };

            if (Parent is null)
                // If the type is a value type, it has no parent.
                // If the type is a reference type, it has parent Il2CppObject
                if (Info.Refness == Refness.ReferenceType)
                    Parent = DumpTypeRef.ObjectType;
            Contract.Ensures(Info != null);
            Contract.Ensures(This != null);
        }

        private void ParseFields(PeekableStreamReader fs)
        {
            var line = fs.PeekLine()?.Trim();
            if (line != "{")
                // Nothing in the type
                return;
            fs.ReadLine();

            line = fs.PeekLine()?.Trim();
            // Fields should be second line, if it isn't there are no fields.
            if (line is null || !line.StartsWith("// Fields"))
                // No fields, but other things
                return;
            // Read past // Fields
            fs.ReadLine();

            while (!string.IsNullOrEmpty(line) && line != "}" && !line.StartsWith("// Properties") && !line.StartsWith("// Methods"))
            {
                if (_config.ParseTypeFields)
                {
                    var field = new DumpField(This, fs);
                    if (field.Specifiers.IsStatic())
                        StaticFields.Add(field);
                    else
                        InstanceFields.Add(field);
                }
                else
                    fs.ReadLine();
                line = fs.PeekLine()?.Trim();
            }
        }

        private void ParseProperties(PeekableStreamReader fs)
        {
            string? line = fs.PeekLine()?.Trim();
            if (!string.IsNullOrEmpty(line))
            {
                // Spaced after fields
                fs.ReadLine();
                line = fs.PeekLine()?.Trim();
            }
            if (line is null || !line.StartsWith("// Properties"))
                // No properties
                return;
            // Read past // Properties
            fs.ReadLine();

            while (!string.IsNullOrEmpty(line) && line != "}" && !line.StartsWith("// Methods"))
            {
                if (_config.ParseTypeProperties)
                    Properties.Add(new DumpProperty(This, fs));
                else
                    fs.ReadLine();
                line = fs.PeekLine()?.Trim();
            }
        }

        private void ParseMethods(PeekableStreamReader fs)
        {
            string? line = fs.PeekLine()?.Trim();
            if (string.IsNullOrEmpty(line))
            {
                // Spaced after fields or properties
                fs.ReadLine();
                line = fs.PeekLine()?.Trim();
            }
            if (line is null || !line.StartsWith("// Methods"))
                // No methods
                return;
            // Read past // Methods
            fs.ReadLine();

            while (!string.IsNullOrEmpty(line) && line != "}")
            {
                if (_config.ParseTypeMethods)
                    Methods.Add(new DumpMethod(This, fs));
                else
                    fs.ReadLine();
                line = fs.PeekLine()?.Trim();
            }

            // It's important that Foo.IBar.func() goes after func() (if present)
            var methods = new List<IMethod>(Methods);
            Methods.Clear();
            Methods.AddRange(methods.Where(m => m.ImplementedFrom is null));
            Methods.AddRange(methods.Where(m => m.ImplementedFrom != null));
        }

        internal DumpTypeData(PeekableStreamReader fs, DumpConfig config)
        {
            _config = config;
            // Extract namespace from line
            var @namespace = fs.ReadLine()?.Substring(NamespaceStartOffset).Trim() ?? "";
            ParseAttributes(fs);

            This = null!;  // this silences the "uninitialized" warnings
            Info = null!;  // but these should actually be set in ParseTypeName
            ParseTypeName(@namespace, fs);
            if (This is null || Info is null)
                throw new Exception("ParseTypeName failed to properly initialize This and/or Info!");

            // Empty type
            if (fs.PeekLine() == "{}")
            {
                fs.ReadLine();
                return;
            }
            ParseFields(fs);
            ParseProperties(fs);
            ParseMethods(fs);
            // Read closing brace, if it needs to be read
            if (fs.PeekLine() == "}")
                fs.ReadLine();
        }
    }
}