using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2Cpp_Modding_Codegen.Data.DumpHandling
{
    public class DumpTypeRef : TypeRef
    {
        public static readonly DumpTypeRef ObjectType = new DumpTypeRef("object");
        public override string Namespace { get; } = string.Empty;
        public override string Name { get; }
        public override bool IsGenericParameter => throw new NotImplementedException();
        public override bool IsGenericInstance { get; }
        public override bool IsGenericTemplate { get; }

        public override IReadOnlyList<TypeRef> Generics { get; }

        public override TypeRef DeclaringType { get; }
        public override TypeRef ElementType { get; }

        public override bool IsCovariant { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override bool IsPointer()
        {
            if (Name.EndsWith("*"))
                return true;
            return base.IsPointer();
        }

        public override bool IsArray()
        {
            return Name.EndsWith("[]");
        }

        public override bool IsPrimitive()
        {
            // TODO
            return Name.Equals("void", StringComparison.OrdinalIgnoreCase);
        }

        private DumpTypeRef(DumpTypeRef other, string nameOverride = null)
        {
            Namespace = other.Namespace;
            Name = nameOverride ?? other.Name;
            IsGenericInstance = other.IsGenericInstance;
            IsGenericTemplate = other.IsGenericTemplate;
            Generics = new List<TypeRef>(other.Generics);
            DeclaringType = other.DeclaringType;
            ElementType = other.ElementType;
        }

        public override TypeRef MakePointer() => new DumpTypeRef(this, this.Name + "*");

        /// <summary>
        /// For use with text dumps. Takes a given split array that contains a type at index ind and
        /// returns the full type name and index where the end of the type name is while traversing the split array with direction and sep.
        /// </summary>
        /// <param name="spl"></param>
        /// <param name="ind"></param>
        /// <param name="adjustedIndex"></param>
        /// <param name="direction"></param>
        /// <param name="sep"></param>
        /// <returns></returns>
        internal static string FromMultiple(string[] spl, int ind, out int adjustedIndex, int direction = -1, string sep = " ")
        {
            adjustedIndex = ind;
            if (direction < 0 ? !spl[ind].Contains(">") : !spl[ind].Contains("<"))
            {
                adjustedIndex = ind;
                return spl[ind];
            }
            int countToClose = 0;
            string s = "";
            for (; direction > 0 ? ind < spl.Length : ind >= 0; ind += direction, adjustedIndex += direction)
            {
                // Depending which way we are travelling, either decrease or increase countToClose
                countToClose -= direction * spl[ind].Count(c => c == '>');
                countToClose += direction * spl[ind].Count(c => c == '<');
                s = direction > 0 ? s + sep + spl[ind] : spl[ind] + sep + s;
                if (countToClose == 0)
                    break;
            }
            return direction > 0 ? s.Substring(sep.Length) : s.Substring(0, s.Length - sep.Length);
        }

        public DumpTypeRef(string @namespace, string typeName)
        {
            Namespace = @namespace;

            var GenericTypes = new List<TypeRef>();
            if (typeName.EndsWith(">") && !typeName.StartsWith("<"))
            {
                var ind = typeName.IndexOf("<");
                var types = typeName.Substring(ind + 1, typeName.Length - ind - 2);
                var spl = types.Split(new string[] { ", " }, StringSplitOptions.None);
                for (int i = 0; i < spl.Length;)
                {
                    string s = spl[i];
                    int unclosed = s.Count(c => c == '<');
                    unclosed -= s.Count(c => c == '>');
                    i++;
                    while (unclosed > 0)
                    {
                        unclosed += spl[i].Count(c => c == '<');
                        unclosed -= spl[i].Count(c => c == '>');
                        s += ", " + spl[i];
                        i++;
                    }
                    // TODO: if this DumpTypeRef is the This for a DumpTypeData, mark these IsGenericParameter, and if they are "out ", mark IsCovariant
                    GenericTypes.Add(new DumpTypeRef(s));
                }
            }
            Generics = GenericTypes;
            // TODO: check that this gives correct results
            if (string.IsNullOrEmpty(@namespace))
                IsGenericInstance = true;
            else
                IsGenericTemplate = true;

            var declInd = typeName.LastIndexOf('.');
            if (declInd != -1)
            {
                // Create a new TypeRef for the declaring type, it should recursively create more declaring types
                DeclaringType = new DumpTypeRef(typeName.Substring(0, declInd));
                // TODO: need to resolve DeclaringType before this will make sense?
                Namespace = DeclaringType.Namespace;
            }
            Name = typeName.Replace('.', '/');
            if (IsArray())
            {
                ElementType = new DumpTypeRef(Name.Substring(0, Name.Length - 2));
                // TODO: else set ElementType to `this` as Mono.Cecil does?
            }
        }

        public DumpTypeRef(string qualifiedName) : this("", qualifiedName)
        {
        }
    }
}