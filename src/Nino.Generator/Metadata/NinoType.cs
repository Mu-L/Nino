using System.Collections.Generic;
using System.Linq;

namespace Nino.Generator.Metadata;

/// <summary>
/// Immutable DTO representing a type with Nino serialization metadata.
/// Converted from mutable class to readonly record struct for value-based equality and incremental caching.
/// All construction logic moved to pipeline (SymbolDataExtractor, NinoGraphBuilder).
/// </summary>
public readonly record struct NinoType
{
    /// <summary>
    /// Complete type information.
    /// Replaces the old ITypeSymbol TypeSymbol property.
    /// </summary>
    public TypeInfoDto TypeInfo { get; init; }

    /// <summary>
    /// Serializable members (fields and properties).
    /// Replaces ImmutableList with EquatableArray for value-based equality.
    /// </summary>
    public EquatableArray<NinoMember> Members { get; init; }

    /// <summary>
    /// Parent type IDs (base class and interfaces).
    /// Stores TypeIds instead of NinoType references to avoid circular dependencies.
    /// Replaces ImmutableList&lt;NinoType&gt; Parents.
    /// </summary>
    public EquatableArray<int> ParentTypeIds { get; init; }

    /// <summary>
    /// Custom serializer method from external assembly (if any).
    /// Example: "ExternalAssembly.Serializer"
    /// </summary>
    public string CustomSerializer { get; init; }

    /// <summary>
    /// Custom deserializer method from external assembly (if any).
    /// Example: "ExternalAssembly.Deserializer"
    /// </summary>
    public string CustomDeserializer { get; init; }

    /// <summary>
    /// Reference deserialization method name.
    /// </summary>
    public string RefDeserializationMethod { get; init; }

    /// <summary>
    /// Available constructors for deserialization.
    /// Extracted during symbol extraction phase for use in deserialization code generation.
    /// </summary>
    public EquatableArray<ConstructorInfoDto> Constructors { get; init; }

    /// <summary>
    /// True if this type is polymorphic (has derived types or is marked as polymorphic).
    /// Computed during graph building phase.
    /// </summary>
    public bool IsPolymorphic { get; init; }

    /// <summary>
    /// True if this type contains circular references (direct or transitive).
    /// Computed by CircularTypeDetector during graph building.
    /// </summary>
    public bool IsCircular { get; init; }

    /// <summary>
    /// Hierarchy level (0 for base types, increments with inheritance depth).
    /// Computed during graph building phase.
    /// </summary>
    public int HierarchyLevel { get; init; }

    /// <summary>
    /// Groups members by primitivity for optimized serialization.
    /// Unmanaged members can be serialized in bulk, while managed members need individual handling.
    /// Yields groups of up to 16 unmanaged members, or single managed members.
    /// </summary>
    public IEnumerable<List<NinoMember>> GroupByPrimitivity()
    {
        List<NinoMember> unmanagedGroup = new();
        foreach (var member in Members)
        {
            // Check if member is unmanaged and doesn't have custom formatter
            // Note: Nullable<T> is not serialized as unmanaged even though it might be
            if (member.Type.IsUnmanagedType &&
                !member.HasCustomFormatter() &&
                !member.Type.IsNullableValueType)
            {
                unmanagedGroup.Add(member);
            }
            else
            {
                // If any unmanaged members were accumulated, yield them first
                if (unmanagedGroup.Count > 0)
                {
                    yield return unmanagedGroup;
                    unmanagedGroup = new List<NinoMember>();
                }

                // Yield the managed member as its own group
                yield return new List<NinoMember> { member };
            }

            // One group can contain at most 16 members for unsafe accessor limits
            if (unmanagedGroup.Count >= 16)
            {
                yield return unmanagedGroup;
                unmanagedGroup = new List<NinoMember>();
            }
        }

        // Yield any remaining unmanaged members
        if (unmanagedGroup.Count > 0)
        {
            yield return unmanagedGroup;
        }
    }

    /// <summary>
    /// Determines if this type is polymorphic (has parents or is inherently polymorphic).
    /// </summary>
    public bool IsPolyMorphic()
    {
        if (ParentTypeIds.Length > 0)
            return true;

        return TypeInfo?.IsPolymorphicType() ?? false;
    }

    /// <summary>
    /// Custom equality based on TypeId only.
    /// This allows Dictionary&lt;NinoType, ...&gt; to work with value-based equality.
    /// Two NinoTypes are equal if they represent the same type (same TypeId).
    /// </summary>
    public bool Equals(NinoType other) => TypeInfo?.TypeId == other.TypeInfo?.TypeId;

    /// <summary>
    /// Hash code based on TypeId for efficient dictionary lookups.
    /// </summary>
    public override int GetHashCode() => TypeInfo?.TypeId ?? 0;

    /// <summary>
    /// String representation for debugging.
    /// </summary>
    public override string ToString()
    {
        var lines = new List<string>
        {
            $"Type: {TypeInfo?.DisplayName ?? "null"}"
        };

        if (!string.IsNullOrEmpty(CustomSerializer))
        {
            lines.Add($"CustomSerializer: {CustomSerializer}");
        }

        if (!string.IsNullOrEmpty(CustomDeserializer))
        {
            lines.Add($"CustomDeserializer: {CustomDeserializer}");
        }

        if (!string.IsNullOrEmpty(RefDeserializationMethod))
        {
            lines.Add($"RefDeserializationMethod: {RefDeserializationMethod}");
        }

        if (IsPolymorphic)
        {
            lines.Add("IsPolymorphic: true");
        }

        if (IsCircular)
        {
            lines.Add("IsCircular: true");
        }

        if (ParentTypeIds.Length > 0)
        {
            lines.Add($"Parents: {ParentTypeIds.Length} parent(s)");
        }

        if (Members.Length > 0)
        {
            lines.Add("Members:");
            foreach (var member in Members)
            {
                lines.Add($"  {member}");
            }
        }

        return string.Join("\n", lines);
    }
}
