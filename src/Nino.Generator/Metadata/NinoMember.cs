namespace Nino.Generator.Metadata;

/// <summary>
/// Immutable DTO representing a serializable member (field or property) of a NinoType.
/// Converted from mutable class to readonly record struct for value-based equality and incremental caching.
/// </summary>
public readonly record struct NinoMember
{
    /// <summary>
    /// Member name (field or property name).
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Type information for this member.
    /// Replaces the old ITypeSymbol Type property.
    /// </summary>
    public TypeInfoDto Type { get; init; }

    /// <summary>
    /// True if this member is a constructor parameter.
    /// </summary>
    public bool IsCtorParameter { get; init; }

    /// <summary>
    /// True if this member is private.
    /// </summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// True if this member is a property (false for fields).
    /// </summary>
    public bool IsProperty { get; init; }

    /// <summary>
    /// True if this member is a UTF-8 string (special serialization).
    /// </summary>
    public bool IsUtf8String { get; init; }

    /// <summary>
    /// True if this member is static.
    /// </summary>
    public bool IsStatic { get; init; }

    /// <summary>
    /// True if this member is readonly.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Custom formatter type for this member (null if no custom formatter).
    /// Replaces the old CustomFormatterType() method.
    /// This data is extracted during the pipeline phase.
    /// </summary>
    public TypeInfoDto? CustomFormatterType { get; init; }

    /// <summary>
    /// Determines if this member has a custom formatter.
    /// </summary>
    public bool HasCustomFormatter() => CustomFormatterType != null;

    /// <summary>
    /// String representation for debugging.
    /// </summary>
    public override string ToString()
    {
        return
            $"{Type.DisplayName} {Name} " +
            $"[Ctor: {IsCtorParameter}, " +
            $"Private: {IsPrivate}, " +
            $"Property: {IsProperty}, " +
            $"Utf8String: {IsUtf8String}, " +
            $"Static: {IsStatic}, " +
            $"ReadOnly: {IsReadOnly}]";
    }
}
