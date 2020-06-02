using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DumpHandling
{
    public class DumpTypeRef : TypeRef
    {
        public static readonly DumpTypeRef ObjectType = new DumpTypeRef("object");
        public override string Namespace { get; } = string.Empty;
        public override string Name { get; }
        public override bool Generic { get; }

        public override IEnumerable<TypeRef> GenericParameters { get; }

        public override TypeRef DeclaringType { get; }
        public override TypeRef ElementType { get; }

        public override bool IsPointer(ITypeContext context)
        {
            if (Name.EndsWith("*"))
            {
                return true;
            }
            return base.IsPointer(context);
        }

        public override bool IsArray()
        {
            return Name.EndsWith("[]");
        }

        /// <summary>
        /// For use with text dumps. Takes a given split array that contains a type at index ind and
        /// returns the full typename and index where the end of the typename is while traversing the split array with direction and sep.
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

            if (typeName.EndsWith(">") && !typeName.StartsWith("<"))
            {
                Generic = true;
                var GenericParams = new List<TypeRef>();

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
                    GenericParams.Add(new DumpTypeRef(s));
                }
                var declInd = typeName.LastIndexOf('.');
                if (declInd != -1)
                {
                    // Create a new TypeRef for the declaring type, it should recursively create more declaring types
                    DeclaringType = new DumpTypeRef(typeName.Substring(0, declInd));
                }
                Name = typeName.Substring(declInd + 1);
                GenericParameters = GenericParams;
            }
            else
            {
                var declInd = typeName.LastIndexOf('.');
                if (declInd != -1)
                {
                    // Create a new TypeRef for the declaring type, it should recursively create more declaring types
                    DeclaringType = new DumpTypeRef(typeName.Substring(0, declInd));
                }
                Name = typeName.Substring(declInd + 1);
                if (IsArray())
                {
                    ElementType = new DumpTypeRef(typeName.Substring(0, typeName.Length - 2));
                    // TODO: else set ElementType to `this` as Mono.Cecil does?
                }
            }
        }

        public DumpTypeRef(string qualifiedName) : this("", qualifiedName) { }
    }
}