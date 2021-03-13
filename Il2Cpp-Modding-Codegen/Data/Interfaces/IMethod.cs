using System.Collections.Generic;

namespace Il2CppModdingCodegen.Data
{
    public interface IMethod
    {
        bool Generic { get; }
        IReadOnlyList<TypeRef> GenericParameters { get; }
        List<IAttribute> Attributes { get; }
        List<ISpecifier> Specifiers { get; }
        int RVA { get; }
        int Offset { get; }
        int VA { get; }
        int Slot { get; }
        TypeRef ReturnType { get; }
        TypeRef DeclaringType { get; }
        TypeRef? ImplementedFrom { get; }
        List<IMethod> BaseMethods { get; }
        List<IMethod> ImplementingMethods { get; }
        bool IsVirtual { get; }

        // Does this method hide (by signature or override) an existing method in a base class or interface?
        bool HidesBase { get; }

        bool IsSpecialName { get; }

        string Name { get; }

        /// <summary>
        /// The name of the method in Il2Cpp form.
        /// If this is a method with the special name flag set, this will appear as a fully qualified type suffixed by the method name.
        /// Otherwise, this name matches <see cref="Name"/>
        /// </summary>
        string Il2CppName { get; }

        List<Parameter> Parameters { get; }
    }
}