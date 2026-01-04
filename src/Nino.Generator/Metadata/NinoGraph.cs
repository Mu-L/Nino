using System.Collections.Generic;
using System.Linq;

namespace Nino.Generator.Metadata;

/// <summary>
/// Immutable DTO representing the complete type hierarchy graph for Nino serialization.
/// Converted from mutable class to readonly record struct for value-based equality and incremental caching.
/// All construction logic moved to NinoGraphBuilder in the pipeline phase.
/// </summary>
public readonly record struct NinoGraph
{
    /// <summary>
    /// Maps each type to its base types (parent classes and interfaces).
    /// Uses NinoType as key with TypeId-based equality.
    /// </summary>
    public Dictionary<NinoType, List<NinoType>> BaseTypes { get; init; }

    /// <summary>
    /// Maps each type to its derived types (subtypes).
    /// Inverse of BaseTypes relationship.
    /// </summary>
    public Dictionary<NinoType, List<NinoType>> SubTypes { get; init; }

    /// <summary>
    /// Top-level types with no base types (root types in the hierarchy).
    /// </summary>
    public HashSet<NinoType> TopTypes { get; init; }

    /// <summary>
    /// Types that contain circular references (direct or transitive).
    /// Computed by CircularTypeDetector during graph building.
    /// </summary>
    public HashSet<NinoType> CircularTypes { get; init; }

    /// <summary>
    /// String-based type lookup map (DisplayName -> NinoType).
    /// Used for GetDisplayString() lookups during code generation.
    /// </summary>
    public Dictionary<string, NinoType> TypeMap { get; init; }

    /// <summary>
    /// String representation for debugging.
    /// </summary>
    public override string ToString()
    {
        var lines = new List<string>();

        // Base Types
        lines.Add("Base Types:");
        foreach (var kvp in (BaseTypes ?? new()).Where(t => t.Value?.Count > 0))
        {
            var key = kvp.Key;
            var value = kvp.Value;
            lines.Add(
                $"{key.TypeInfo.DisplayName} -> {string.Join(", ", value.Select(x => x.TypeInfo.DisplayName))}");
        }

        lines.Add("");

        // Sub Types
        lines.Add("Sub Types:");
        foreach (var kvp in (SubTypes ?? new()).Where(t => t.Value?.Count > 0))
        {
            var key = kvp.Key;
            var value = kvp.Value;
            lines.Add(
                $"{key.TypeInfo.DisplayName} -> {string.Join(", ", value.Select(x => x.TypeInfo.DisplayName))}");
        }

        lines.Add("");

        // Top Types
        lines.Add("Top Types:");
        var topTypes = (TopTypes ?? new())
            .Where(t => t.Members.Length > 0 && !t.TypeInfo.IsUnmanagedType)
            .Select(x => x.TypeInfo.DisplayName);
        lines.Add(string.Join("\n", topTypes));

        lines.Add("");

        // Circular Types
        lines.Add("Circular Types:");
        var circularTypes = (CircularTypes ?? new())
            .Where(t => t.Members.Length > 0)
            .Select(x => x.TypeInfo.DisplayName);
        lines.Add(string.Join("\n", circularTypes));

        return string.Join("\n", lines);
    }
}
