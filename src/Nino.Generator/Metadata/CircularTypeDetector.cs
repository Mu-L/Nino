using System.Collections.Generic;
using System.Linq;

namespace Nino.Generator.Metadata;

/// <summary>
/// Detects circular type references without using Compilation.HasImplicitConversion.
/// Replaces the circular detection logic from old NinoGraph.cs (lines 79-126).
///
/// A type is circular if it contains (directly or transitively) a member whose type:
/// - Is the same type (direct self-reference: class Node { Node next; })
/// - Is a parent type (parent reference: class Derived : Base { Base parent; })
/// - Contains the type in generics (generic container: class Node { List&lt;Node&gt; children; })
/// - Is itself circular (transitive: class A { B b; } class B { A a; })
/// </summary>
public static class CircularTypeDetector
{
    /// <summary>
    /// Detects all circular types in the collection.
    /// Value types are always skipped (can't have true circular references in managed memory).
    /// </summary>
    public static HashSet<NinoType> Detect(
        EquatableArray<NinoType> allTypes,
        Dictionary<NinoType, List<NinoType>> baseTypes)
    {
        var circularTypes = new HashSet<NinoType>();

        foreach (var ninoType in allTypes)
        {
            // Skip value types (can't be truly circular in managed memory)
            if (ninoType.TypeInfo.IsValueType)
                continue;

            // Check if this type contains circular references
            if (IsCircular(ninoType, ninoType.TypeInfo.TypeId, allTypes, baseTypes, new HashSet<int>()))
            {
                circularTypes.Add(ninoType);
            }
        }

        return circularTypes;
    }

    /// <summary>
    /// Recursively checks if a type contains circular references.
    /// </summary>
    /// <param name="type">The type to check</param>
    /// <param name="originalTypeId">The TypeId we're checking for circularity</param>
    /// <param name="allTypes">All NinoTypes in the graph</param>
    /// <param name="baseTypes">Base type relationships</param>
    /// <param name="visited">Visited types to prevent infinite recursion</param>
    /// <returns>True if the type is circular</returns>
    private static bool IsCircular(
        NinoType type,
        int originalTypeId,
        EquatableArray<NinoType> allTypes,
        Dictionary<NinoType, List<NinoType>> baseTypes,
        HashSet<int> visited)
    {
        // Prevent infinite recursion
        if (visited.Contains(type.TypeInfo.TypeId))
            return false;

        visited.Add(type.TypeInfo.TypeId);

        // Check each member
        foreach (var member in type.Members)
        {
            // Skip unmanaged types (can't create cycles)
            if (member.Type.IsUnmanagedType)
                continue;

            // Check if member type is related to original type
            if (IsTypeRelated(member.Type, originalTypeId, allTypes, baseTypes))
                return true;

            // Check if member type contains the original type (transitive check)
            // This handles cases like: class A { B b; } class B { A a; }
            var memberNinoType = FindTypeById(allTypes, member.Type.TypeId);
            if (!memberNinoType.Equals(default(NinoType)))
            {
                if (IsCircular(memberNinoType, originalTypeId, allTypes, baseTypes, visited))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a member type is related to the original type.
    /// A type is related if:
    /// 1. Direct self-reference (member.TypeId == originalTypeId)
    /// 2. Parent reference (member is a parent of original type)
    /// 3. Generic type arguments relate (List&lt;Node&gt; in Node)
    /// 4. Array element type relates (Node[] in Node)
    /// 5. Nullable underlying type relates (Node? in Node)
    /// </summary>
    private static bool IsTypeRelated(
        TypeInfoDto memberType,
        int originalTypeId,
        EquatableArray<NinoType> allTypes,
        Dictionary<NinoType, List<NinoType>> baseTypes)
    {
        // Direct self-reference
        if (memberType.TypeId == originalTypeId)
            return true;

        // Check if member type is a parent of original type
        var originalType = FindTypeById(allTypes, originalTypeId);
        if (!originalType.Equals(default(NinoType)))
        {
            if (baseTypes.TryGetValue(originalType, out var bases))
            {
                // Check if any base type has the same TypeId as member type
                if (bases.Any(b => b.TypeInfo.TypeId == memberType.TypeId))
                    return true;
            }
        }

        // Check generic type arguments (e.g., List<Node> in Node)
        if (memberType.IsGenericType && memberType.TypeArguments.Length > 0)
        {
            foreach (var typeArg in memberType.TypeArguments)
            {
                if (IsTypeRelated(typeArg, originalTypeId, allTypes, baseTypes))
                    return true;
            }
        }

        // Check array element type (e.g., Node[] in Node)
        if (memberType.ArrayRank > 0 && memberType.ArrayElementType.HasValue)
        {
            if (IsTypeRelated(memberType.ArrayElementType.Value, originalTypeId, allTypes, baseTypes))
                return true;
        }

        // Check nullable underlying type (e.g., Node? in Node for value types)
        if (memberType.IsNullableValueType && memberType.NullableUnderlyingType.HasValue)
        {
            if (IsTypeRelated(memberType.NullableUnderlyingType.Value, originalTypeId, allTypes, baseTypes))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Finds a NinoType by TypeId in the collection.
    /// Returns default(NinoType) if not found.
    /// </summary>
    private static NinoType FindTypeById(EquatableArray<NinoType> types, int typeId)
    {
        foreach (var type in types)
        {
            if (type.TypeInfo.TypeId == typeId)
            {
                return type;
            }
        }

        return default;
    }
}
