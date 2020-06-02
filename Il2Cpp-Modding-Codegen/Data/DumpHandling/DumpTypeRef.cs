using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DumpHandling
{
    public class DumpTypeRef : TypeRef
    {
        public static readonly DumpTypeRef ObjectType = new DumpTypeRef("object");
        public override string Namespace { get; protected set; } = string.Empty;
        public override string Name { get; protected set; }
        public override bool Generic { get; protected set; }

        public override List<TypeRef> GenericParameters { get; } = new List<TypeRef>();

        public override TypeRef DeclaringType { get; protected set; }

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

        public void Set(string typeName)
        {
            if (typeName.EndsWith(">") && !typeName.StartsWith("<"))
            {
                Generic = true;
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
                    GenericParameters.Add(new DumpTypeRef(s, false));
                }
                var declInd = typeName.LastIndexOf('.');
                if (declInd != -1)
                {
                    // Create a new TypeRef for the declaring type, it should recursively create more declaring types
                    DeclaringType = new DumpTypeRef(typeName.Substring(0, declInd));
                }
                Name = typeName.Substring(declInd + 1);
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
            }
        }

        public DumpTypeRef(string @namespace, string name)
        {
            Namespace = @namespace;
            Set(name);
        }

        public DumpTypeRef(string qualifiedName, bool qualified = false)
        {
            if (qualified)
            {
                int dotLocation = qualifiedName.LastIndexOf('.');
                if (dotLocation == -1)
                {
                    Set(qualifiedName);
                    Namespace = "";
                }
                else
                {
                    Namespace = qualifiedName.Substring(0, dotLocation);
                    Set(qualifiedName.Substring(dotLocation + 1));
                }
            }
            else
            {
                Set(qualifiedName);
                Namespace = "";
            }
        }
    }
}