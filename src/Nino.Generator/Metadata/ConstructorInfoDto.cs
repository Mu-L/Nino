namespace Nino.Generator.Metadata;

/// <summary>
/// Immutable DTO representing constructor metadata for deserialization code generation.
/// Extracted from IMethodSymbol during symbol extraction phase.
/// </summary>
public readonly record struct ConstructorInfoDto
{
    /// <summary>
    /// Constructor parameters (name and type).
    /// </summary>
    public EquatableArray<ConstructorParameterDto> Parameters { get; init; }

    /// <summary>
    /// True if this constructor has [NinoConstructor] attribute.
    /// </summary>
    public bool HasNinoConstructorAttribute { get; init; }

    /// <summary>
    /// Custom parameter order from [NinoConstructor(nameof(a), nameof(b), ...)] if specified.
    /// If null, use default parameter order.
    /// </summary>
    public EquatableArray<string>? NinoConstructorParameterNames { get; init; }

    /// <summary>
    /// Display string for debugging (e.g., "MyType(int x, string y)").
    /// </summary>
    public string DisplayString { get; init; }

    /// <summary>
    /// True if this is the primary constructor of a record type.
    /// </summary>
    public bool IsPrimaryConstructor { get; init; }

    /// <summary>
    /// True if this is a regular constructor (vs static factory method).
    /// Most constructors are true, but some types use static factory methods.
    /// </summary>
    public bool IsConstructor { get; init; }

    /// <summary>
    /// Method name (only used for static factory methods, null for regular constructors).
    /// </summary>
    public string? MethodName { get; init; }
}

/// <summary>
/// DTO representing a constructor parameter.
/// </summary>
public readonly record struct ConstructorParameterDto
{
    /// <summary>
    /// Parameter name.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Parameter type information.
    /// </summary>
    public TypeInfoDto Type { get; init; }
}
