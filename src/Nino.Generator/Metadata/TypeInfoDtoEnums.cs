namespace Nino.Generator.Metadata;

/// <summary>
/// DTO representation of TypeKind for value-based equality.
/// Maps from Microsoft.CodeAnalysis.TypeKind.
/// </summary>
public enum TypeKindDto
{
    Unknown,
    Array,
    Class,
    Delegate,
    Dynamic,
    Enum,
    Error,
    Interface,
    Module,
    Pointer,
    Struct,
    Structure,
    TypeParameter,
    Submission,
    FunctionPointer
}

/// <summary>
/// DTO representation of SpecialType for value-based equality.
/// Maps from Microsoft.CodeAnalysis.SpecialType.
/// </summary>
public enum SpecialTypeDto
{
    None,
    System_Object,
    System_Enum,
    System_MulticastDelegate,
    System_Delegate,
    System_ValueType,
    System_Void,
    System_Boolean,
    System_Char,
    System_SByte,
    System_Byte,
    System_Int16,
    System_UInt16,
    System_Int32,
    System_UInt32,
    System_Int64,
    System_UInt64,
    System_Decimal,
    System_Single,
    System_Double,
    System_String,
    System_IntPtr,
    System_UIntPtr,
    System_Array,
    System_Collections_IEnumerable,
    System_Collections_Generic_IEnumerable_T,
    System_Collections_Generic_IList_T,
    System_Collections_Generic_ICollection_T,
    System_Collections_IEnumerator,
    System_Collections_Generic_IEnumerator_T,
    System_Collections_Generic_IReadOnlyList_T,
    System_Collections_Generic_IReadOnlyCollection_T,
    System_Nullable_T,
    System_DateTime,
    System_Runtime_CompilerServices_IsVolatile,
    System_IDisposable,
    System_TypedReference,
    System_ArgIterator,
    System_RuntimeArgumentHandle,
    System_RuntimeFieldHandle,
    System_RuntimeMethodHandle,
    System_RuntimeTypeHandle,
    System_IAsyncResult,
    System_AsyncCallback,
    System_Runtime_CompilerServices_RuntimeFeature
}

/// <summary>
/// DTO representation of Accessibility for value-based equality.
/// Maps from Microsoft.CodeAnalysis.Accessibility.
/// </summary>
public enum AccessibilityDto
{
    NotApplicable,
    Private,
    ProtectedAndInternal,
    Protected,
    Internal,
    ProtectedOrInternal,
    Public
}

/// <summary>
/// Nino-specific type classification for code generation.
/// </summary>
public enum NinoTypeKind
{
    /// <summary>Type is serialized in boxed form (reference type).</summary>
    Boxed,

    /// <summary>Type is unmanaged and serialized directly.</summary>
    Unmanaged,

    /// <summary>Type is a custom NinoType with generated serialization.</summary>
    NinoType,

    /// <summary>Type is a built-in type with special handling (arrays, collections, etc.).</summary>
    BuiltIn,

    /// <summary>Type cannot be serialized by Nino.</summary>
    Invalid
}
