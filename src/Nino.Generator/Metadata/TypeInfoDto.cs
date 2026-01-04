namespace Nino.Generator.Metadata;

/// <summary>
/// Immutable DTO representing all required type information for code generation.
/// This replaces direct ITypeSymbol usage in the metadata layer to enable proper incremental caching.
/// All data is extracted once from ITypeSymbol via SymbolDataExtractor and stored as immutable records.
/// </summary>
public sealed record TypeInfoDto
{
    // ===== Type Identity =====

    /// <summary>
    /// Fully qualified name (e.g., "global::System.Collections.Generic.List&lt;int&gt;").
    /// </summary>
    public string FullyQualifiedName { get; init; } = string.Empty;

    /// <summary>
    /// Assembly qualified name for cross-assembly references.
    /// </summary>
    public string AssemblyQualifiedName { get; init; } = string.Empty;

    /// <summary>
    /// Deterministic hash of FullyQualifiedName for fast lookups and equality comparisons.
    /// Used as the primary identity mechanism in dictionaries and collections.
    /// </summary>
    public int TypeId { get; init; }

    // ===== Type Classification =====

    /// <summary>
    /// The kind of type (class, struct, interface, enum, etc.).
    /// </summary>
    public TypeKindDto Kind { get; init; }

    /// <summary>
    /// Special type classification for built-in .NET types.
    /// </summary>
    public SpecialTypeDto SpecialType { get; init; }

    /// <summary>
    /// Accessibility level (public, internal, private, etc.).
    /// </summary>
    public AccessibilityDto Accessibility { get; init; }

    // ===== Type Characteristics =====

    /// <summary>
    /// True if the type is a value type (struct, enum, primitive).
    /// </summary>
    public bool IsValueType { get; init; }

    /// <summary>
    /// True if the type is a reference type (class, interface, delegate).
    /// </summary>
    public bool IsReferenceType { get; init; }

    /// <summary>
    /// True if the type is unmanaged (contains no managed references).
    /// </summary>
    public bool IsUnmanagedType { get; init; }

    /// <summary>
    /// True if the type is a ref struct (stack-only type).
    /// </summary>
    public bool IsRefLikeType { get; init; }

    /// <summary>
    /// True if the type is a record (class or struct).
    /// </summary>
    public bool IsRecord { get; init; }

    /// <summary>
    /// True if the type is abstract.
    /// </summary>
    public bool IsAbstract { get; init; }

    /// <summary>
    /// True if the type is sealed.
    /// </summary>
    public bool IsSealed { get; init; }

    /// <summary>
    /// True if the type is static.
    /// </summary>
    public bool IsStatic { get; init; }

    /// <summary>
    /// True if the type is a generic type (constructed or definition).
    /// </summary>
    public bool IsGenericType { get; init; }

    // ===== Generic Type Information =====

    /// <summary>
    /// Type arguments for constructed generic types (e.g., [int] for List&lt;int&gt;).
    /// Empty for non-generic types.
    /// </summary>
    public EquatableArray<TypeInfoDto> TypeArguments { get; init; }

    /// <summary>
    /// Original generic definition (e.g., "List&lt;T&gt;" for List&lt;int&gt;).
    /// Null for non-generic types.
    /// </summary>
    public string? GenericOriginalDefinition { get; init; }

    /// <summary>
    /// True if this is a generic type definition (e.g., List&lt;T&gt; vs List&lt;int&gt;).
    /// </summary>
    public bool IsGenericTypeDefinition { get; init; }

    // ===== Array Information =====

    /// <summary>
    /// Array rank (0 for non-arrays, 1 for T[], 2 for T[,], etc.).
    /// </summary>
    public int ArrayRank { get; init; }

    /// <summary>
    /// Element type for arrays (null for non-arrays).
    /// </summary>
    public TypeInfoDto? ArrayElementType { get; init; }

    // ===== Nullable Information =====

    /// <summary>
    /// True if this is a Nullable&lt;T&gt; value type.
    /// </summary>
    public bool IsNullableValueType { get; init; }

    /// <summary>
    /// Underlying type for Nullable&lt;T&gt; (null if not nullable).
    /// </summary>
    public TypeInfoDto? NullableUnderlyingType { get; init; }

    // ===== Tuple Information =====

    /// <summary>
    /// True if this is a tuple type (ValueTuple or Tuple).
    /// </summary>
    public bool IsTupleType { get; init; }

    /// <summary>
    /// Tuple element information (null for non-tuples).
    /// </summary>
    public EquatableArray<TupleElementDto>? TupleElements { get; init; }

    // ===== Containment =====

    /// <summary>
    /// Containing namespace (e.g., "System.Collections.Generic").
    /// </summary>
    public string ContainingNamespace { get; init; } = string.Empty;

    /// <summary>
    /// Containing assembly name.
    /// </summary>
    public string ContainingAssemblyName { get; init; } = string.Empty;

    // ===== Display Names (Pre-computed for Code Generation) =====

    /// <summary>
    /// Display name for code generation (without 'global::' prefix).
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Suggested variable name for instances of this type (e.g., "list" for List&lt;T&gt;).
    /// </summary>
    public string InstanceVariableName { get; init; } = string.Empty;

    /// <summary>
    /// Simple type name without namespace (e.g., "List", "Dictionary", "ValueTuple").
    /// For generic types, includes type parameters (e.g., "List&lt;T&gt;").
    /// </summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// DTO for tuple element metadata.
/// </summary>
public sealed record TupleElementDto
{
    /// <summary>
    /// Element type.
    /// </summary>
    public TypeInfoDto Type { get; init; } = null!;

    /// <summary>
    /// Element name (e.g., "Item1" or custom name).
    /// </summary>
    public string Name { get; init; } = string.Empty;
}
