using System.Collections.Generic;
using System.Linq;
using Nino.Generator.Common;

namespace Nino.Generator.Metadata;

/// <summary>
/// Extension methods for TypeInfoDto, replacing NinoTypeHelper.cs functionality.
/// These methods provide type classification and analysis without requiring ITypeSymbol.
/// </summary>
public static class TypeInfoDtoExtensions
{
    /// <summary>
    /// Determines if a type is polymorphic (can have derived types).
    /// A type is polymorphic if it's a non-sealed reference type or an interface.
    /// </summary>
    public static bool IsPolymorphicType(this TypeInfoDto type)
    {
        // Interfaces are always polymorphic
        if (type.Kind == TypeKindDto.Interface)
            return true;

        // Value types cannot be polymorphic
        if (type.IsValueType)
            return false;

        // Sealed classes cannot be polymorphic
        if (type.IsSealed)
            return false;

        // Abstract and non-sealed classes are polymorphic
        return type.IsReferenceType;
    }

    /// <summary>
    /// Determines if a type is a ref struct (stack-only type).
    /// </summary>
    public static bool IsRefStruct(this TypeInfoDto type)
    {
        return type.IsRefLikeType;
    }

    /// <summary>
    /// Determines if a type is sealed or a struct (cannot be inherited).
    /// </summary>
    public static bool IsSealedOrStruct(this TypeInfoDto type)
    {
        return type.IsSealed || type.IsValueType;
    }

    /// <summary>
    /// Determines if a type can have instances created.
    /// Returns false for static, abstract, or interface types.
    /// </summary>
    public static bool IsInstanceType(this TypeInfoDto type)
    {
        if (type.IsStatic)
            return false;

        if (type.IsAbstract)
            return false;

        if (type.Kind == TypeKindDto.Interface)
            return false;

        return true;
    }

    /// <summary>
    /// Gets the hierarchy level of a type based on parent count.
    /// This is a placeholder - actual implementation requires access to parent type information.
    /// </summary>
    public static int GetTypeHierarchyLevel(this TypeInfoDto type, EquatableArray<int> parentTypeIds)
    {
        // Base types have level 0
        if (parentTypeIds.Length == 0)
            return 0;

        // Derived types have level based on parent count
        // Note: This is a simplified version. Full implementation would need to traverse the graph
        return parentTypeIds.Length;
    }

    /// <summary>
    /// Gets the Nino-specific type classification for code generation.
    /// Determines how the type should be serialized (boxed, unmanaged, custom, built-in).
    /// </summary>
    public static NinoTypeKind GetKind(
        this TypeInfoDto type,
        NinoGraph graph,
        HashSet<int> generatedTypeIds)
    {
        // Check if it's a generated Nino type
        if (generatedTypeIds.Contains(type.TypeId))
            return NinoTypeKind.NinoType;

        // Check if it's in the Nino type graph
        if (graph.TypeMap.Values.Any(ninoType => ninoType.TypeInfo.TypeId == type.TypeId))
            return NinoTypeKind.NinoType;

        // Unmanaged types are serialized directly
        if (type.IsUnmanagedType)
            return NinoTypeKind.Unmanaged;

        // Built-in types (arrays, collections, etc.) have special handling
        if (IsBuiltInType(type))
            return NinoTypeKind.BuiltIn;

        // Reference types are boxed
        if (type.IsReferenceType)
            return NinoTypeKind.Boxed;

        // Default to invalid
        return NinoTypeKind.Invalid;
    }

    /// <summary>
    /// Determines if a type is a built-in type with special serialization handling.
    /// Includes arrays, collections, nullable types, tuples, etc.
    /// </summary>
    private static bool IsBuiltInType(TypeInfoDto type)
    {
        // Arrays
        if (type.ArrayRank > 0)
            return true;

        // Nullable value types
        if (type.IsNullableValueType)
            return true;

        // Tuples
        if (type.IsTupleType)
            return true;

        // Generic collections - check by namespace and type name
        if (type.IsGenericType && type.ContainingNamespace.StartsWith("System.Collections"))
            return true;

        // Special types
        switch (type.SpecialType)
        {
            case SpecialTypeDto.System_String:
            case SpecialTypeDto.System_Array:
            case SpecialTypeDto.System_Collections_IEnumerable:
            case SpecialTypeDto.System_Collections_Generic_IEnumerable_T:
            case SpecialTypeDto.System_Collections_Generic_IList_T:
            case SpecialTypeDto.System_Collections_Generic_ICollection_T:
            case SpecialTypeDto.System_Collections_Generic_IReadOnlyList_T:
            case SpecialTypeDto.System_Collections_Generic_IReadOnlyCollection_T:
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a display string for the type (without global:: prefix).
    /// This is pre-computed and stored in DisplayName for performance.
    /// </summary>
    public static string GetDisplayString(this TypeInfoDto type)
    {
        return type.DisplayName;
    }

    /// <summary>
    /// Gets a suggested instance variable name for the type.
    /// This is pre-computed and stored in InstanceVariableName.
    /// </summary>
    public static string GetInstanceName(this TypeInfoDto type)
    {
        return type.InstanceVariableName;
    }

    /// <summary>
    /// Gets a cached variable name with prefix based on type's display name hash.
    /// Used for generating unique variable names for formatters and serializers.
    /// </summary>
    public static string GetCachedVariableName(this TypeInfoDto type, string prefix)
    {
        var typeDisplayName = type.DisplayName;
        var hash = typeDisplayName.GetLegacyNonRandomizedHashCode();
        var hexString = ((uint)hash).ToString("X8");
        return $"{prefix}_{hexString}";
    }

    /// <summary>
    /// Determines if a type is a primitive type (int, float, bool, etc.).
    /// </summary>
    public static bool IsPrimitive(this TypeInfoDto type)
    {
        return type.SpecialType switch
        {
            SpecialTypeDto.System_Boolean or
            SpecialTypeDto.System_Char or
            SpecialTypeDto.System_SByte or
            SpecialTypeDto.System_Byte or
            SpecialTypeDto.System_Int16 or
            SpecialTypeDto.System_UInt16 or
            SpecialTypeDto.System_Int32 or
            SpecialTypeDto.System_UInt32 or
            SpecialTypeDto.System_Int64 or
            SpecialTypeDto.System_UInt64 or
            SpecialTypeDto.System_Single or
            SpecialTypeDto.System_Double or
            SpecialTypeDto.System_Decimal or
            SpecialTypeDto.System_IntPtr or
            SpecialTypeDto.System_UIntPtr => true,
            _ => false
        };
    }

    /// <summary>
    /// Determines if a type is a string type.
    /// </summary>
    public static bool IsString(this TypeInfoDto type)
    {
        return type.SpecialType == SpecialTypeDto.System_String;
    }

    /// <summary>
    /// Determines if a type is an enum type.
    /// </summary>
    public static bool IsEnum(this TypeInfoDto type)
    {
        return type.Kind == TypeKindDto.Enum;
    }
}
