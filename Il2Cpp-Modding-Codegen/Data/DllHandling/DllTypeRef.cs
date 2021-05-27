using Il2CppModdingCodegen.Serialization;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    public class DllTypeRef : TypeRef
    {
        internal TypeReference This;

        private readonly string _namespace;
        public override string Namespace { get => _namespace; }
        private readonly string _name;
        public override string Name { get => _name; }

        public override bool IsGenericParameter { get => This.IsGenericParameter; }
        public override bool IsCovariant { get; }
        public override IReadOnlyList<TypeRef> GenericParameterConstraints { get; } = new List<TypeRef>();
        public override bool IsGenericInstance { get => This.IsGenericInstance; }
        public override bool IsGenericTemplate { get => This.HasGenericParameters; }

        public override IReadOnlyList<TypeRef> Generics { get; }

        protected override TypeRef? OriginalDeclaringType { get => From(This.DeclaringType); }

        public override TypeRef? ElementType
        {
            get
            {
                if (!(This is TypeSpecification typeSpec)) return null;
                if (typeSpec.MetadataType == MetadataType.GenericInstance) return null;
                return From(typeSpec.ElementType);
            }
        }

        public override bool IsVoid() => This.MetadataType == MetadataType.Void;

        public override bool IsPointer() => This.IsPointer;

        public override bool IsArray() => This.IsArray;

        public override TypeRef MakePointer() => From(This.MakePointerType());

        internal override TypeRef MakeGenericInstance(GenericTypeMap genericTypes)
        {
            if (Generics.Count <= 0) return this;
            var genericArgumentsInOrder = new List<TypeReference>(Generics.Count);
            foreach (var genericParameter in Generics)
            {
                if (genericParameter.IsGeneric)
                    genericArgumentsInOrder.Add(genericParameter.MakeGenericInstance(genericTypes).AsDllTypeRef.This);
                else if (genericParameter.IsArray() && genericParameter.ElementType!.IsGenericParameter)
                {
                    if (genericTypes.TryGetValue(genericParameter.ElementType, out var genArg))
                    {
                        genericArgumentsInOrder.Add(genArg.AsDllTypeRef.This.MakeArrayType());
                    }
                    else
                    {
                        throw new UnresolvedTypeException(genericParameter, this);
                    }
                }
                else if (genericTypes.TryGetValue(genericParameter, out var genericArgument))
                    genericArgumentsInOrder.Add(genericArgument.AsDllTypeRef.This);
                else
                    throw new UnresolvedTypeException(genericParameter, this);
            }
            return From(This.Resolve().MakeGenericInstanceType(genericArgumentsInOrder.ToArray()));
        }

        private static readonly Dictionary<TypeReference, DllTypeRef> cache = new Dictionary<TypeReference, DllTypeRef>();

        internal static int Hits { get; private set; } = 0;
        internal static int Misses { get; private set; } = 0;

        // Should use DllTypeRef.From instead!
        private DllTypeRef(TypeReference reference)
        {
            cache.Add(reference, this);
            This = reference;

            if (This.IsByReference)
            {
                // TODO: Set as ByReference? For method params, the ref keyword is handled by Parameter.cs
                This = ((ByReferenceType)This).ElementType;
            }
            _name = This.Name;

            Generics = IsGenericInstance
                ? ((GenericInstanceType)This).GenericArguments.Select(g => From(g)).ToList()
                : IsGenericTemplate ? This.GenericParameters.Select(g => From(g)).ToList() : (IReadOnlyList<TypeRef>)new List<TypeRef>();

            //if ((IsArray() || IsPointer()) && This.GetElementType() != null && Generics.Count == 0)
            //{
            //    var elemT = This.GetElementType();
            //    // If we have an element type that is generic, we need to properly handle it
            //    if (elemT.IsGenericParameter)
            //    {
            //        Generics = new List<TypeRef> { From(elemT) };
            //    }
            //}

            if (IsGeneric && Generics.Count == 0)
                throw new InvalidDataException($"Wtf? In DllTypeRef constructor, a generic with no generics: {this}, IsGenInst: {this.IsGenericInstance}");

            if ((This.Name == "GameNoteType" && This.DeclaringType?.Name == "GameNoteController") ||  // referenced by IGameNoteTypeProvider
                (This.Name == "MessageType" && This.DeclaringType?.Name == "MultiplayerSessionManager") ||  // referenced by IMultiplayerSessionManager
                (This.Name == "Score" && This.DeclaringType?.Name == "StandardScoreSyncState") ||  // SSSState implements IStateTable_2<SSSState::Score, int>
                (This.Name == "NodePose" && This.DeclaringType?.Name == "NodePoseSyncState"))  // NPSState implements IStateTable_2<NPSState::NodePose, PoseSerializable>
                UnNested = true;

            else if ((This.Name == "CombineTexturesIntoAtlasesCoroutineResult" && This.DeclaringType?.Name == "MB3_TextureCombiner") ||
                (This.Name == "BrainEvent" && This.DeclaringType?.Name == "CinemachineBrain") ||
                (This.Name == "CallbackContext" && This.DeclaringType?.Name == "InputAction"))
                UnNested = true;
            else if (This.DeclaringType?.Name == "OVRManager")
                UnNested = true;

            //if (This.DeclaringType is not null)
            //{
            //    // Because nested types have extra data in them, their size doesn't match what they should be.
            //    // For this reason, we shall unnest EVERYTHING!
            //    // Much to the disappointment of ourselves...
            //    UnNested = true;
            //}

            DllTypeRef? refDeclaring = null;
            if (!This.IsGenericParameter && This.IsNested)
                refDeclaring = From(This.DeclaringType);

            // Remove *, [] from end of variable name
            _name = Regex.Replace(_name, @"\W+$", "");

            _namespace = (refDeclaring?.Namespace ?? This.Namespace) ?? "";

            if (IsGenericParameter && (This is GenericParameter genParam))
            {
                IsCovariant = genParam.IsCovariant;
                if (genParam.HasConstraints)
                {
                    GenericParameterConstraints = genParam.Constraints.Select(c => From(c.ConstraintType)).ToList();
                    if (genParam.Constraints.Any(c => c.HasCustomAttributes))
                    {
                        var declaring = genParam.DeclaringType is null ? $"method {genParam.DeclaringMethod}" : $"{genParam.DeclaringType}";
                        Console.WriteLine($"Constraints with CustomAttributes on {genParam} (in {declaring}): ");
                        Console.WriteLine(string.Join(", ",
                            genParam.Constraints.Select(c => $"({c.ConstraintType}, {{{string.Join(", ", c.CustomAttributes)}}})")));
                    }
                }
            }
        }

        [return: NotNullIfNotNull("type")]
        internal static DllTypeRef? From(TypeReference? type)
        {
            if (type is null) return null;
            if (cache.TryGetValue(type, out var value))
            {
                Hits++;
                return value;
            }
            Misses++;

            // Creates new TypeRef and add it to map
            value = new DllTypeRef(type);
            return value;
        }

        // For better comments
        public override string ToString() => This.ToString();
    }
}