using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;

namespace Il2Cpp_Modding_Codegen.Data
{
    public class TypeRef
    {
        public static readonly TypeRef ObjectType = new TypeRef("object");
        public static readonly TypeRef VoidType = new TypeRef("void");
        public string Namespace { get; internal set; } = "";
        public string Name { get; internal set; }

        public bool Generic { get; private set; }
        public List<TypeRef> GenericParameters { get; } = new List<TypeRef>();
        public TypeRef DeclaringType { get; internal set; }

        private ITypeData _resolvedType;

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
                    GenericParameters.Add(new TypeRef(s, false));
                }
                var declInd = typeName.LastIndexOf('.');
                if (declInd != -1)
                {
                    // Create a new TypeRef for the declaring type, it should recursively create more declaring types
                    DeclaringType = new TypeRef(typeName.Substring(0, declInd));
                }
                Name = typeName.Substring(declInd + 1);
            }
            else
            {
                var declInd = typeName.LastIndexOf('.');
                if (declInd != -1)
                {
                    // Create a new TypeRef for the declaring type, it should recursively create more declaring types
                    DeclaringType = new TypeRef(typeName.Substring(0, declInd));
                }
                Name = typeName.Substring(declInd + 1);
            }
        }

        public TypeRef(string @namespace, string name)
        {
            Namespace = @namespace;
            Set(name);
        }

        public TypeRef(string qualifiedName, bool qualified = false)
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

        public TypeRef(TypeReference type)
        {
            Namespace = type.Namespace;
            Name = type.Name;
            if (type.IsPointer)
                Name += "*";
            Generic = type.IsGenericInstance;
            if (type.HasGenericParameters)
                GenericParameters.AddRange(type.GenericParameters.Select(gp => new TypeRef(gp)));
            if (type.DeclaringType != null)
                DeclaringType = new TypeRef(type.DeclaringType);
        }

        /// <summary>
        /// Resolves the type in the given context
        /// </summary>
        public ITypeData Resolve(ITypeContext context)
        {
            if (_resolvedType == null)
            {
                _resolvedType = context.Resolve(this);
            }
            return _resolvedType;
        }

        public bool IsPointer(ITypeContext context)
        {
            if (Name.EndsWith("*"))
            {
                return true;
            }
            // Resolve type, if type is not a value type, it is a pointer
            Resolve(context);
            return _resolvedType?.Info.TypeFlags == TypeFlags.ReferenceType;
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Namespace))
                return $"{Namespace}::{Name}";
            if (!Generic)
                return $"{Name}";
            var s = Name + "<";
            for (int i = 0; i < GenericParameters.Count; i++)
            {
                s += GenericParameters[i].ToString();
                if (i != GenericParameters.Count - 1)
                    s += ", ";
            }
            s += ">";
            return s;
        }

        public string SafeName()
        {
            return Name.Replace('<', '_').Replace('>', '_').Replace(".", "::");
        }

        public string SafeNamespace()
        {
            return Namespace.Replace('<', '_').Replace('>', '_').Replace(".", "::");
        }

        public string SafeFullName()
        {
            return SafeNamespace() + "_" + SafeName();
        }

        // Namespace is actually NOT useful for comparisons!
        public override int GetHashCode()
        {
            return (Namespace + Name).GetHashCode();
        }

        // Namespace is actually NOT useful for comparisons!
        public override bool Equals(object obj)
        {
            var o = obj as TypeRef;
            return o?.Namespace + o?.Name == Namespace + Name
                && o?.Generic == Generic
                && GenericParameters.SequenceEqual(o?.GenericParameters);
        }
    }
}