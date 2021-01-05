using Il2CppModdingCodegen.Data;
using Il2CppModdingCodegen.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2CppModdingCodegen
{
    /// <summary>
    /// Tracks the size of all used value types.
    /// </summary>
    internal class SizeTracker
    {
        private static readonly Dictionary<ITypeData, int> sizeMap = new();

        public static int GetSize(ITypeCollection types, TypeRef type)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));
            if (type.IsArray() || type.IsPointer())
                return Constants.PointerSize;
            if (type.IsGenericParameter)
                // Can't compute size of a generic parameter
                // TODO: Probably could, if it has constraints, or we just assume it's a reference type/boxed value type
                return -1;
            var td = type.Resolve(types);
            if (td is null)
                throw new InvalidOperationException("Cannot get size of something that cannot be resolved!");
            if (td.Info.Refness == Refness.ReferenceType || type.IsPointer())
                return Constants.PointerSize;
            return GetSize(types, td);
        }

        public static int GetSize(ITypeCollection types, ITypeData type)
        {
            if (sizeMap.TryGetValue(type, out var res))
                return res;
            // Otherwise, we need to compute the size of this type.
            if (type.This.IsGeneric)
                return -1;
            // Handle primitive types explicitly
            if (type.This.Namespace == "System")
            {
                switch (type.This.Name)
                {
                    case "Boolean":
                    case "Byte":
                    case "SByte":
                        return 1;

                    case "Char":
                    case "UInt16":
                    case "Int16":
                        return 2;

                    case "Int32":
                    case "UInt32":
                    case "Single":
                        return 4;

                    case "Int64":
                    case "UInt64":
                    case "Double":
                        return 8;
                }
            }
            var last = type.InstanceFields.LastOrDefault();
            if (last is null)
            {
                if (type.Parent is null)
                {
                    sizeMap.Add(type, 0);
                    // We have 0 size if we have no parent and no fields.
                    return 0;
                }
                // Our size is just our parent's size
                var pt = type.Parent.Resolve(types);
                if (pt is null)
                {
                    sizeMap.Add(type, -1);
                    // We probably inherit a generic type, or something similar
                    return -1;
                }
                int parentSize = GetSize(types, type.Parent.Resolve(types)!);
                // Add our size to the map
                sizeMap.Add(type, parentSize);
                return parentSize;
            }
            // If the offset is greater than 0, we trust it.
            if (last.Offset > 0)
            {
                int fSize = GetSize(types, last.Type);
                if (fSize <= 0)
                {
                    sizeMap.Add(type, -1);
                    return -1;
                }
                sizeMap.Add(type, fSize + last.Offset);
                return fSize + last.Offset;
            }
            bool acceptZeroOffset = type.Parent is null;
            if (type.Parent is not null)
            {
                // If we have a parent
                var resolved = type.Parent.Resolve(types);
                if (resolved is null)
                {
                    sizeMap.Add(type, -1);
                    return -1;
                }
                acceptZeroOffset = GetSize(types, resolved) == 0;
            }
            // If we have no parent, or we have a parent of size 0, and we only have one field, then we trust offset 0
            if (acceptZeroOffset && type.InstanceFields.Count == 1 && last.Offset == 0)
            {
                int fSize = GetSize(types, last.Type);
                sizeMap.Add(type, fSize);
                return fSize;
            }
            // We don't know how to handle negative offsets, so we return -1 for those.
            return -1;
        }
    }
}